using System;
using System.Collections.Generic;
using WordToPdfConverter.Services.Remediation.Models;
using static WordToPdfConverter.Services.Remediation.Api.RemediationSessionManager;

namespace WordToPdfConverter.Services.Remediation.Api
{
    /// <summary>
    /// API request/response models for remediation endpoints
    /// </summary>

    public class StartRemediationRequest
    {
        public int? MaxIterations { get; set; }
        public int? MaxDurationMinutes { get; set; }
        public double? AcceptableComplianceScore { get; set; }
        public bool? DetectStagnation { get; set; }
        public bool? StopOnRegression { get; set; }
        public bool? SaveBestPdf { get; set; }
        public string BestPdfOutputPath { get; set; }
    }

    public class RemediationStatusResponse
    {
        public string SessionId { get; set; }
        public string Status { get; set; } // Starting, Running, Completed, Failed, Cancelled
        public string FileName { get; set; }
        public DateTime StartTime { get; set; }
        public TimeSpan? ElapsedTime { get; set; }

        // Current iteration info (if running)
        public int? CurrentIteration { get; set; }
        public int? MaxIterations { get; set; }
        public int? CurrentViolations { get; set; }
        public int? InitialViolations { get; set; }
        public double? ComplianceScore { get; set; }
        public string ExitReason { get; set; }

        // Best PDF info
        public int? BestIterationNumber { get; set; }
        public double? BestComplianceScore { get; set; }
        public int? BestViolationCount { get; set; }

        // Progress details
        public List<IterationDetail> IterationHistory { get; set; } = new();

        // Completion info (if completed)
        public bool? IsCompliant { get; set; }
        public bool? Success { get; set; }
        public string ErrorMessage { get; set; }

        // GPT fallback info
        public int? GptAttempts { get; set; }
        public bool? GptStagnationDetected { get; set; }
    }

    public class IterationDetail
    {
        public int IterationNumber { get; set; }
        public int ViolationCount { get; set; }
        public double ComplianceScore { get; set; }
        public int FixesApplied { get; set; }
        public List<string> PhasesExecuted { get; set; } = new();
        public TimeSpan Duration { get; set; }
    }

    public class RemediationResultResponse
    {
        public bool Success { get; set; }
        public string SessionId { get; set; }
        public string FileName { get; set; }

        // Summary
        public RemediationSummary Summary { get; set; }

        // PDF data
        public string BestPdfBase64 { get; set; }
        public string FinalPdfBase64 { get; set; }
        public int BestPdfSize { get; set; }
        public int FinalPdfSize { get; set; }

        // Detailed results
        public string ExitReason { get; set; }
        public bool IsCompliant { get; set; }
        public int TotalIterations { get; set; }
        public TimeSpan TotalDuration { get; set; }

        // Violations
        public List<ViolationInfo> RemainingViolations { get; set; } = new();

        // Best PDF info
        public int BestIterationNumber { get; set; }
        public double BestComplianceScore { get; set; }
        public int BestViolationCount { get; set; }
    }

    public class RemediationSummary
    {
        public int TotalIterations { get; set; }
        public int InitialViolations { get; set; }
        public int FinalViolations { get; set; }
        public int ViolationsFixed { get; set; }
        public double InitialCompliance { get; set; }
        public double FinalCompliance { get; set; }
        public double ComplianceImprovement { get; set; }
        public bool IsCompliant { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public class ViolationInfo
    {
        public string RuleId { get; set; }
        public string Description { get; set; }
        public string Context { get; set; }
        public int? PageNumber { get; set; }
        public string Category { get; set; }
    }

    public class UpdateRemediationOptionsRequest
    {
        public int? MaxIterations { get; set; }
        public double? AcceptableComplianceScore { get; set; }
        public bool? StopOnRegression { get; set; }
    }

    public class RemediationHealthResponse
    {
        public int ActiveSessions { get; set; }
        public int TotalSessions { get; set; }
        public string Status { get; set; }
        public DateTime ServerTime { get; set; }
    }
}
