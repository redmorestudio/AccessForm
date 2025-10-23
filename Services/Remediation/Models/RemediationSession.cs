using System;
using System.Collections.Generic;
using System.Diagnostics;
using WordToPdfConverter.Models.PdfUA;

namespace WordToPdfConverter.Services.Remediation.Models
{
    /// <summary>
    /// Maintains state throughout a remediation session
    /// </summary>
    public class RemediationSession
    {
        public Guid SessionId { get; set; } = Guid.NewGuid();
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public RemediationOptions Options { get; set; }

        // Current state
        public byte[] CurrentPdf { get; set; }
        public byte[] OriginalPdf { get; set; }
        public int IterationCount { get; set; } = 0;
        public bool IsComplete { get; set; } = false;
        public bool IsCompliant { get; set; } = false;
        public ExitReason ExitReason { get; set; }

        // History tracking
        public List<IterationSnapshot> History { get; set; } = new();
        public List<PhaseResult> AllPhaseResults { get; set; } = new();
        public ValidationResult InitialValidation { get; set; }
        public ValidationResult CurrentValidation { get; set; }

        // Metrics
        public RemediationMetrics Metrics { get; set; } = new();

        // Computed properties
        public TimeSpan ElapsedTime => DateTime.UtcNow - StartTime;
        public bool IsFinalIteration { get; set; }

        public RemediationSession(RemediationOptions options)
        {
            Options = options;
        }

        public void NextIteration(ExecutionResult result)
        {
            IterationCount++;
            CurrentPdf = result.OutputPdf;
            AllPhaseResults.AddRange(result.Phases);
        }

        public void Complete(ValidationResult finalValidation)
        {
            IsComplete = true;
            IsCompliant = finalValidation.Summary.IsCompliant;
            CurrentValidation = finalValidation;
        }
    }

    /// <summary>
    /// Snapshot of state at each iteration
    /// </summary>
    public class IterationSnapshot
    {
        public int IterationNumber { get; set; }
        public DateTime Timestamp { get; set; }
        public int ViolationCount { get; set; }
        public double ComplianceScore { get; set; }
        public Dictionary<ViolationCategory, int> ViolationsByCategory { get; set; } = new();
        public List<string> PhasesExecuted { get; set; } = new();
        public int FixesApplied { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Metrics tracked throughout remediation
    /// </summary>
    public class RemediationMetrics
    {
        public double ViolationReductionRate { get; set; }
        public double ComplianceImprovement { get; set; }
        public double AverageFixRate { get; set; }
        public double EfficiencyScore { get; set; }
    }

    /// <summary>
    /// Reasons why remediation exited
    /// </summary>
    public enum ExitReason
    {
        NotSet,
        Success,
        MaxIterationsReached,
        NoProgress,
        Regression,
        Timeout,
        ThresholdMet,
        UserCancelled,
        FatalError
    }

    /// <summary>
    /// Result of executing all phases in an iteration
    /// </summary>
    public class ExecutionResult
    {
        public bool Success { get; set; }
        public byte[] OutputPdf { get; set; }
        public List<PhaseResult> Phases { get; set; } = new();
        public string ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
        public ValidationResult Validation { get; set; }
    }

    /// <summary>
    /// Result of executing a single phase
    /// </summary>
    public class PhaseResult
    {
        public string PhaseName { get; set; }
        public bool Success { get; set; }
        public byte[] OutputPdf { get; set; }
        public int Iterations { get; set; }
        public List<ServiceResult> ServiceResults { get; set; } = new();
        public string ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
    }
}
