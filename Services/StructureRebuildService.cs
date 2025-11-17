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
    private readonly IPdfStructureWriter _writer;
    private readonly FigureDetectionEnricher? _figureEnricher;

    public StructureRebuildService(
        ILogger<StructureRebuildService> logger,
        IPageLayoutEngine layoutEngine,
        IPdfStructureWriter writer,
        FigureDetectionEnricher? figureEnricher = null)
    {
        _logger = logger;
        _layoutEngine = layoutEngine;
        _writer = writer;
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
        _logger.LogInformation(
            $"[STRUCTURE-REBUILD] Starting structure rebuild for {fileName}");

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

            // Step 4: Build StructureRebuildContext
            var context = new StructureRebuildContext
            {
                ImageCache = imageCache,
                LayoutPlan = layoutPlan
            };

            // Step 5: Call PDF writer
            _logger.LogInformation("[STRUCTURE-REBUILD] Writing PDF structure");
            var remediatedPdf = _writer.Rewrite(originalPdfBytes, structureTree, context);
            _logger.LogInformation(
                $"[STRUCTURE-REBUILD] Structure rebuild complete: " +
                $"{remediatedPdf.Length} bytes");

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
