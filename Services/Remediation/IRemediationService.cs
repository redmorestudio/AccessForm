using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WordToPdfConverter.Services.Remediation
{
    /// <summary>
    /// Common interface for all remediation services in the closed-loop system
    /// </summary>
    public interface IRemediationService
    {
        /// <summary>
        /// Execute remediation on the provided PDF
        /// </summary>
        Task<ServiceResult> RemediateAsync(byte[] pdfBytes);

        /// <summary>
        /// Human-readable name of the service
        /// </summary>
        string ServiceName { get; }

        /// <summary>
        /// Category of violations this service targets
        /// </summary>
        ViolationCategory TargetCategory { get; }

        /// <summary>
        /// Execution priority (lower number = higher priority)
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Whether this service is required for the phase to succeed
        /// </summary>
        bool IsRequired { get; }
    }

    /// <summary>
    /// Result of a remediation service execution
    /// </summary>
    public class ServiceResult
    {
        public bool Success { get; set; }
        public byte[] OutputPdf { get; set; }
        public bool ChangesMade { get; set; }
        public int IssuesFound { get; set; }
        public int IssuesFixed { get; set; }
        public string ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Categories of violations for remediation
    /// </summary>
    public enum ViolationCategory
    {
        StructureRebuild, // AI-powered complete structure rebuild (Phase 0)
        Structure,        // Tagged structure issues (7.1)
        Metadata,         // Title, language, PDF/UA identifier (6.1, 6.2)
        Content,          // Tagged content, artifacts (7.3)
        Whitespace,       // Whitespace tagging issues
        FormFields,       // Form field accessibility
        Links,            // Link annotations
        Annotations,      // Other annotations
        Fonts,            // Font embedding/encoding
        Language,         // Natural language specification
        TableAndList,     // Table structure and list issues (7.2)
        AlternateText,    // Alternative text for images and figures
        Unknown
    }
}
