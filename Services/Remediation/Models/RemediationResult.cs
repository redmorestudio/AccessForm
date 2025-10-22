using System;
using System.Collections.Generic;
using WordToPdfConverter.Models.PdfUA;

namespace WordToPdfConverter.Services.Remediation.Models
{
    /// <summary>
    /// Final result of closed-loop remediation
    /// </summary>
    public class RemediationResult
    {
        public bool Success { get; set; }
        public ExitReason ExitReason { get; set; }
        public RemediationSummary Summary { get; set; } = new();
        public byte[] OutputPdf { get; set; }

        // Detailed information
        public List<PhaseResult> PhaseResults { get; set; } = new();
        public List<IterationSnapshot> IterationHistory { get; set; } = new();
        public RemediationMetrics Metrics { get; set; } = new();
        public List<PdfUAViolation> RemainingViolations { get; set; } = new();
        public List<Recommendation> Recommendations { get; set; } = new();

        // Validation reports
        public ValidationResult InitialValidation { get; set; }
        public ValidationResult FinalValidation { get; set; }
    }

    /// <summary>
    /// High-level summary of remediation
    /// </summary>
    public class RemediationSummary
    {
        public int TotalIterations { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public int InitialViolationCount { get; set; }
        public int FinalViolationCount { get; set; }
        public int ViolationsFixed { get; set; }
        public double ComplianceImprovement { get; set; }
        public bool IsCompliant { get; set; }

        // Computed properties
        public double FixRate => InitialViolationCount > 0
            ? (double)ViolationsFixed / InitialViolationCount * 100.0
            : 0.0;

        public string FormattedDuration =>
            $"{TotalDuration.Minutes:D2}:{TotalDuration.Seconds:D2}";
    }

    /// <summary>
    /// Recommendation for unresolved violations
    /// </summary>
    public class Recommendation
    {
        public RecommendationType Type { get; set; }
        public string Description { get; set; }
        public RecommendationPriority Priority { get; set; }
        public ViolationCategory? Category { get; set; }
        public string SuggestedAction { get; set; }
    }

    public enum RecommendationType
    {
        Validation,
        ManualIntervention,
        NewTooling,
        ProcessImprovement,
        ExternalService
    }

    public enum RecommendationPriority
    {
        Low,
        Medium,
        High,
        Critical
    }
}
