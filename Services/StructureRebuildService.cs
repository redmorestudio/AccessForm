using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models;
using WordToPdfConverter.Models.Logical;
using WordToPdfConverter.Models.Remediation;
using WordToPdfConverter.Services.Enrichment;
using WordToPdfConverter.Services.Layout;
using WordToPdfConverter.Services.Pdf;
using WordToPdfConverter.Services.Remediation.Structure;

namespace WordToPdfConverter.Services;

/// <summary>
/// Orchestrates the complete structure rebuild pipeline:
/// 1. Receive LogicalDocument (from upstream AI analysis)
/// 2. Run enrichers (figures, forms)
/// 3. Build StructureTree
/// 4. Call layout engine to create PageLayoutPlan
/// 5. Build StructureRebuildContext with ImageCache and LayoutPlan
/// 6. Call PDF writer with context
///
/// This service is the ONLY place that invokes the layout engine.
/// </summary>
public sealed class StructureRebuildService
{
    private readonly ILogger<StructureRebuildService> _logger;
    private readonly IPageLayoutEngine _layoutEngine;
    private readonly ITaggedPdfFinalizer _finalizer;
    private readonly FigureDetectionEnricher? _figureEnricher;
    private readonly RemediationJobContext _jobContext;

    public StructureRebuildService(
        ILogger<StructureRebuildService> logger,
        IPageLayoutEngine layoutEngine,
        ITaggedPdfFinalizer finalizer,
        RemediationJobContext jobContext,
        FigureDetectionEnricher? figureEnricher = null)
    {
        _logger = logger;
        _layoutEngine = layoutEngine;
        _finalizer = finalizer;
        _jobContext = jobContext;
        _figureEnricher = figureEnricher;
    }

    /// <summary>
    /// Rebuilds a PDF with proper structure and reading order.
    /// </summary>
    /// <param name="logicalDocument">The logical document model from AI analysis</param>
    /// <param name="originalPdfBytes">Original PDF bytes for image extraction</param>
    /// <param name="fileName">Filename for logging purposes</param>
    /// <returns>Remediated PDF bytes with proper structure and reading order</returns>
    public async Task<byte[]> RebuildAsync(
        LogicalDocument logicalDocument,
        byte[] originalPdfBytes,
        string fileName = "document.pdf")
    {
        // PHASE 6E: Read MCID settings from job context
        var options = _jobContext.Options ?? new WordToPdfConverter.Services.Remediation.Models.RemediationOptions();

        _logger.LogInformation(
            $"[STRUCTURE-REBUILD] Starting structure rebuild for {fileName}");
        _logger.LogInformation(
            "[STRUCTURE-REBUILD] MCID linking={McidLinking}, MCID content rewrite={McidRewrite}",
            options.EnableMcidLinking,
            options.EnableMcidContentRewrite);

        try
        {
            // Step 1: Run enrichers
            var imageCache = new Dictionary<string, ExtractedImageData>();

            if (_figureEnricher != null)
            {
                _logger.LogInformation("[STRUCTURE-REBUILD] Running figure enrichment");
                imageCache = await _figureEnricher.EnrichAsync(
                    logicalDocument,
                    originalPdfBytes,
                    fileName);
                _logger.LogInformation(
                    $"[STRUCTURE-REBUILD] Figure enrichment complete: {imageCache.Count} images cached");
            }

            // TODO: Add form field enricher when ready
            // if (_formEnricher != null) { ... }

            // Step 2: Build StructureTree
            _logger.LogInformation("[STRUCTURE-REBUILD] Building structure tree");
            var structureTree = StructureTreeBuilder.Build(logicalDocument);
            _logger.LogInformation(
                $"[STRUCTURE-REBUILD] Structure tree built with {structureTree.Nodes.Count} root nodes");

            // Step 3: Call layout engine to create PageLayoutPlan
            _logger.LogInformation("[STRUCTURE-REBUILD] Building layout plan");
            var layoutPlan = _layoutEngine.BuildLayoutPlan(structureTree);
            _logger.LogInformation(
                $"[STRUCTURE-REBUILD] Layout plan built: " +
                $"{layoutPlan.Pages.Count} pages, " +
                $"{layoutPlan.Pages.Sum(p => p.Instructions.Count)} instructions");

            // Step 4: Use shared job context (Phase 6b Pipeline Integration)
            // Update the shared context with image cache and layout plan
            var context = _jobContext.StructureContext;
            context.ImageCache = imageCache;
            context.LayoutPlan = layoutPlan;

            // Check if rebuild already executed (should never happen, but log if it does)
            if (context.StructureRebuildExecuted)
            {
                _logger.LogWarning(
                    "[STRUCTURE-REBUILD] Structure rebuild already executed. " +
                    "This should not be called multiple times per remediation job.");
            }

            // Step 5: Call tagged PDF finalizer (Phase 6b Pipeline Integration)
            // This orchestrates the final structure rebuild, MCID assignment, and content marking
            // The finalizer will set context.StructureRebuildExecuted and context.McidContentRewriteExecuted
            _logger.LogInformation("[STRUCTURE-REBUILD] Finalizing tagged PDF structure");
            var remediatedPdf = _finalizer.FinalizeTaggedPdf(
                originalPdfBytes,
                logicalDocument,
                structureTree,
                layoutPlan,
                context);
            _logger.LogInformation(
                $"[STRUCTURE-REBUILD] Structure rebuild complete: " +
                $"{remediatedPdf.Length} bytes, " +
                $"StructureRebuildExecuted={context.StructureRebuildExecuted}, " +
                $"McidContentRewriteExecuted={context.McidContentRewriteExecuted}");

            // DEBUG: Check if MCID markers are present in returned PDF
            if (context.McidContentRewriteExecuted)
            {
                var bdcCount = System.Text.Encoding.ASCII.GetString(remediatedPdf).Split(new[] { "BDC" }, StringSplitOptions.None).Length - 1;
                var emcCount = System.Text.Encoding.ASCII.GetString(remediatedPdf).Split(new[] { "EMC" }, StringSplitOptions.None).Length - 1;
                _logger.LogInformation($"[STRUCTURE-REBUILD-DEBUG] PDF has {bdcCount} BDC and {emcCount} EMC markers");
            }

            return remediatedPdf;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                $"[STRUCTURE-REBUILD] Failed to rebuild structure for {fileName}");
            throw;
        }
    }
}
