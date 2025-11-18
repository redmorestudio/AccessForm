using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.Layout;
using WordToPdfConverter.Models.Logical;
using WordToPdfConverter.Models.Remediation;
using WordToPdfConverter.Services.Remediation.Models;
using WordToPdfConverter.Services.Remediation.Structure;

namespace WordToPdfConverter.Services.Pdf;

/// <summary>
/// Phase 6b Pipeline Integration: Default implementation of ITaggedPdfFinalizer.
///
/// Orchestrates the final tagged PDF generation by:
/// 1. Enforcing LayoutPlan requirement for Phase 6b
/// 2. Preventing secondary rebuilds via context flags
/// 3. Delegating to ITextPdfStructureWriter for structure + MCID work
/// 4. Setting execution flags to write-protect the PDF
/// </summary>
public sealed class TaggedPdfFinalizer : ITaggedPdfFinalizer
{
    private readonly ILogger<TaggedPdfFinalizer> _logger;
    private readonly IPdfStructureWriter _structureWriter;
    private readonly IConfiguration _configuration;
    private readonly RemediationJobContext _jobContext;

    public TaggedPdfFinalizer(
        ILogger<TaggedPdfFinalizer> logger,
        IPdfStructureWriter structureWriter,
        IConfiguration configuration,
        RemediationJobContext jobContext)
    {
        _logger = logger;
        _structureWriter = structureWriter;
        _configuration = configuration;
        _jobContext = jobContext;
    }

    public byte[] FinalizeTaggedPdf(
        byte[] originalPdf,
        LogicalDocument logical,
        StructureTree structure,
        PageLayoutPlan layoutPlan,
        StructureRebuildContext context)
    {
        try
        {
            _logger.LogInformation("[FINALIZER] Starting final tagged PDF generation");

            // PHASE 6E: Read MCID settings from job context instead of IConfiguration
            var options = _jobContext.Options ?? new RemediationOptions();
            var enableMcidLinking = options.EnableMcidLinking;
            var enableMcidContentRewrite = options.EnableMcidContentRewrite;

            _logger.LogInformation(
                "[FINALIZER] Using MCID settings: linking={Link}, rewrite={Rewrite}",
                enableMcidLinking,
                enableMcidContentRewrite);

            // Guard 1: Check if rebuild already executed
            if (context.StructureRebuildExecuted)
            {
                _logger.LogWarning(
                    "[FINALIZER] Structure rebuild already executed. Skipping to prevent overwriting MCID markers. " +
                    "Passing through input PDF unchanged.");
                return originalPdf; // PHASE 6F: Pass through input unchanged
            }

            // Guard 2: Check if MCID content rewrite already executed
            if (context.McidContentRewriteExecuted)
            {
                _logger.LogWarning(
                    "[FINALIZER] MCID content rewrite already executed. Skipping to prevent overwriting BDC/EMC markers. " +
                    "Passing through input PDF unchanged.");
                return originalPdf; // PHASE 6F: Pass through input unchanged
            }

            // Enforcement: Require LayoutPlan for Phase 6b
            if (enableMcidContentRewrite && layoutPlan == null)
            {
                var errorMsg =
                    "[FINALIZER] MCID content rewrite is enabled but LayoutPlan is null. " +
                    "Phase 6b requires geometric ordering from LayoutPlan. " +
                    "Final rebuild must include LayoutPlan.";
                _logger.LogError(errorMsg);
                throw new InvalidOperationException(errorMsg);
            }

            // Log configuration
            _logger.LogInformation(
                "[FINALIZER] Configuration: " +
                $"EnableMcidLinking={enableMcidLinking}, " +
                $"EnableMcidContentRewrite={enableMcidContentRewrite}, " +
                $"LayoutPlan={(layoutPlan != null ? "Present" : "Null")}");

            // Delegate to structure writer for actual rebuild
            _logger.LogInformation("[FINALIZER] Delegating to IPdfStructureWriter for structure rebuild + MCID work");
            var result = _structureWriter.Rewrite(originalPdf, structure, context);

            // Set execution flags to write-protect the PDF
            context.StructureRebuildExecuted = true;
            context.McidContentRewriteExecuted = enableMcidContentRewrite;

            _logger.LogInformation(
                "[FINALIZER] Final tagged PDF generation complete. " +
                $"Context flags set: StructureRebuildExecuted=true, McidContentRewriteExecuted={enableMcidContentRewrite}");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FINALIZER] Failed to finalize tagged PDF");

            // On error, do NOT set execution flags - allow retry if orchestrator wants
            return originalPdf;
        }
    }
}
