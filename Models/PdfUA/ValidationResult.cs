using System;
using System.Collections.Generic;

namespace WordToPdfConverter.Models.PdfUA
{
    /// <summary>
    /// Result of PDF/UA validation from veraPDF
    /// </summary>
    public class ValidationResult
    {
        public Guid ValidationId { get; set; } = Guid.NewGuid();
        public DateTime ValidationDate { get; set; } = DateTime.UtcNow;
        public string FilePath { get; set; }
        public long FileSizeBytes { get; set; }
        public ValidationStatus Status { get; set; }
        public ValidationSummary Summary { get; set; } = new();
        public List<PdfUAViolation> Violations { get; set; } = new();
        public TimeSpan ProcessingTime { get; set; }
        public string RawVeraPdfOutput { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class ValidationSummary
    {
        public bool IsCompliant { get; set; }
        public string ProfileName { get; set; } = "PDF/UA-1";
        public string Statement { get; set; }
        public int TotalChecks { get; set; }
        public int PassedChecks { get; set; }
        public int FailedChecks { get; set; }
        public int WarningChecks { get; set; }
        public double ComplianceScore => TotalChecks > 0
            ? (double)PassedChecks / TotalChecks * 100.0
            : 0.0;
    }

    public class PdfUAViolation
    {
        public string RuleId { get; set; }
        public string Specification { get; set; } = "ISO 14289-1:2014";
        public string Clause { get; set; }
        public int TestNumber { get; set; }
        public ViolationStatus Status { get; set; }
        public ViolationSeverity Severity { get; set; }
        public string Description { get; set; }
        public ViolationLocation Location { get; set; } = new();
        public string Context { get; set; }
        public string ErrorMessage { get; set; }

        // Remediation hints
        public string RemediationHint { get; set; }
        public List<string> SuggestedServices { get; set; } = new();
        public bool CanAutoRemediate { get; set; }
    }

    public class ViolationLocation
    {
        public int? PageNumber { get; set; }
        public string ObjectId { get; set; }
        public string XPath { get; set; }
        public string ContextDescription { get; set; }
    }

    public enum ValidationStatus
    {
        NotStarted,
        Running,
        Completed,
        Failed,
        TimedOut
    }

    public enum ViolationStatus
    {
        Failed,
        Passed,
        Warning,
        NotApplicable
    }

    public enum ViolationSeverity
    {
        Critical,  // Prevents accessibility
        Error,     // Major accessibility issue
        Warning,   // Minor issue or best practice
        Info       // Informational only
    }
}
