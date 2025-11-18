using System;
using System.Collections.Generic;

namespace WordToPdfConverter.Services.Remediation.Models
{
    /// <summary>
    /// Configuration options for closed-loop remediation
    /// </summary>
    public class RemediationOptions
    {
        // Exit conditions
        public int MaxIterations { get; set; } = 10;
        public TimeSpan MaxDuration { get; set; } = TimeSpan.FromMinutes(30);
        public double? AcceptableComplianceScore { get; set; } = null;  // e.g., 0.95 for 95%

        // Execution options
        public bool ValidateBetweenPhases { get; set; } = true;
        public bool StopOnRegression { get; set; } = true;
        public bool DetectStagnation { get; set; } = true;
        public int StagnationThreshold { get; set; } = 2; // iterations without improvement

        // Service configuration
        public Dictionary<string, bool> EnabledServices { get; set; } = new();
        public Dictionary<string, object> ServiceOptions { get; set; } = new();

        /// <summary>
        /// Controls when/if Aspose Cloud font optimization runs.
        /// - "PreStructureOnly" (default): Aspose runs in preflight, before structure rebuild/MCID work
        /// - "Disabled": Aspose is not used at all
        /// - "PostStructureAllowed": Aspose can run after structure (incompatible with MCID content rewrite)
        /// </summary>
        public string AsposeOptimizationMode { get; set; } = "PreStructureOnly";

        /// <summary>
        /// Controls when/if Artifact Fix (unmarked XObject wrapping) runs.
        /// - "PreStructureOnly" (default): ARTIFACT-FIX runs in preflight, before structure rebuild/MCID work
        /// - "PostStructureMcidAware": Reserved for future MCID-aware implementation (not yet available)
        /// </summary>
        public string ArtifactFixMode { get; set; } = "PreStructureOnly";

        /// <summary>
        /// Phase 6E: Enable MCID (Marked Content ID) linking between structure tree and page content.
        /// When true, structure elements will be assigned MCIDs that reference page content.
        /// </summary>
        public bool EnableMcidLinking { get; set; } = true;

        /// <summary>
        /// Phase 6E: Enable MCID content stream rewriting (Phase 6b).
        /// When true, page content streams will be rewritten to insert BDC/EMC operators
        /// that mark content with MCIDs, linking visual content to the structure tree.
        /// Requires EnableMcidLinking to also be true.
        /// </summary>
        public bool EnableMcidContentRewrite { get; set; } = true;

        // Reporting
        public bool SaveIntermediatePdfs { get; set; } = false;
        public string IntermediateOutputPath { get; set; } = "./remediation-temp";
        public bool SaveBestPdf { get; set; } = true;  // Always save best PDF by default
        public string BestPdfOutputPath { get; set; } = "./remediation-best";
        public bool ReturnBestPdf { get; set; } = true;  // Return best PDF instead of current in final result
        public bool GenerateDetailedReport { get; set; } = true;

        // Progress tracking
        public string ProgressSessionId { get; set; } = null;

        // Logging
        public string FileName { get; set; } = null; // Original filename for logging

        /// <summary>
        /// Default production-ready options
        /// </summary>
        public static RemediationOptions Production => new RemediationOptions
        {
            MaxIterations = 5,
            MaxDuration = TimeSpan.FromMinutes(15),
            AcceptableComplianceScore = null,  // Don't stop until 100% compliant
            ValidateBetweenPhases = true,
            DetectStagnation = true,
            StagnationThreshold = 2,
            GenerateDetailedReport = true
        };

        /// <summary>
        /// Aggressive options for maximum compliance
        /// </summary>
        public static RemediationOptions Aggressive => new RemediationOptions
        {
            MaxIterations = 10,
            MaxDuration = TimeSpan.FromMinutes(30),
            AcceptableComplianceScore = null,  // Don't stop until perfect
            ValidateBetweenPhases = true,
            DetectStagnation = true,
            StagnationThreshold = 3,
            GenerateDetailedReport = true
        };
    }
}
