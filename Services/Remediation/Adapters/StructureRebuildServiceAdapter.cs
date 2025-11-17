using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.Logical;
using WordToPdfConverter.Services.Analysis;

namespace WordToPdfConverter.Services.Remediation.Adapters
{
    /// <summary>
    /// Adapter that wraps StructureRebuildService to work within the remediation pipeline.
    /// This service uses AI to analyze the PDF structure and rebuild it from scratch with
    /// proper column detection, reading order, and figure enrichment.
    ///
    /// Runs as Phase 0 (before all other remediation phases) to establish a solid foundation.
    /// </summary>
    public sealed class StructureRebuildServiceAdapter : IRemediationService
    {
        private readonly ILogger<StructureRebuildServiceAdapter> _logger;
        private readonly ILogicalLayoutAnalysisService _layoutAnalysisService;
        private readonly WordToPdfConverter.Services.StructureRebuildService _structureRebuildService;

        public StructureRebuildServiceAdapter(
            ILogger<StructureRebuildServiceAdapter> logger,
            ILogicalLayoutAnalysisService layoutAnalysisService,
            WordToPdfConverter.Services.StructureRebuildService structureRebuildService)
        {
            _logger = logger;
            _layoutAnalysisService = layoutAnalysisService;
            _structureRebuildService = structureRebuildService;
        }

        public string ServiceName => "AI Structure Rebuild";
        public ViolationCategory TargetCategory => ViolationCategory.StructureRebuild;
        public int Priority => 1; // Highest priority within Phase 0
        public bool IsRequired => false; // Optional - can be disabled via config

        public async Task<ServiceResult> RemediateAsync(byte[] pdfBytes)
        {
            var sw = Stopwatch.StartNew();
            var result = new ServiceResult
            {
                Success = false,
                OutputPdf = pdfBytes,
                ChangesMade = false,
                IssuesFound = 0,
                IssuesFixed = 0
            };

            try
            {
                _logger.LogInformation("[STRUCTURE-REBUILD-ADAPTER] Starting AI-powered structure rebuild");

                // Step 1: Analyze PDF with AI to get LogicalDocument
                _logger.LogInformation("[STRUCTURE-REBUILD-ADAPTER] Running AI layout analysis...");
                var layoutOptions = new LogicalLayoutOptions
                {
                    IncludeFigures = true,
                    IncludeTables = true,
                    PreferAiHeadings = true,
                    MaxPages = 0 // Analyze all pages
                };

                LogicalDocument logicalDocument;
                try
                {
                    logicalDocument = await _layoutAnalysisService.AnalyzeAsync(
                        pdfBytes,
                        layoutOptions);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[STRUCTURE-REBUILD-ADAPTER] AI analysis failed, skipping structure rebuild");
                    result.ErrorMessage = $"AI analysis failed: {ex.Message}";
                    result.Success = true; // Not a hard failure - just skip this phase
                    result.Duration = sw.Elapsed;
                    return result;
                }

                if (logicalDocument == null || logicalDocument.Pages.Count == 0)
                {
                    _logger.LogWarning(
                        "[STRUCTURE-REBUILD-ADAPTER] No pages analyzed, skipping structure rebuild");
                    result.Success = true; // Not a failure
                    result.Duration = sw.Elapsed;
                    return result;
                }

                _logger.LogInformation(
                    $"[STRUCTURE-REBUILD-ADAPTER] AI analysis complete: " +
                    $"{logicalDocument.Pages.Count} pages, " +
                    $"{CountTotalBlocks(logicalDocument)} blocks");

                // Step 2: Rebuild PDF with proper structure
                _logger.LogInformation("[STRUCTURE-REBUILD-ADAPTER] Rebuilding PDF structure...");
                byte[] rebuiltPdf;
                try
                {
                    rebuiltPdf = await _structureRebuildService.RebuildAsync(
                        logicalDocument,
                        pdfBytes,
                        "document.pdf");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[STRUCTURE-REBUILD-ADAPTER] Structure rebuild failed");
                    result.ErrorMessage = $"Structure rebuild failed: {ex.Message}";
                    result.Success = false;
                    result.Duration = sw.Elapsed;
                    return result;
                }

                // Success!
                _logger.LogInformation(
                    $"[STRUCTURE-REBUILD-ADAPTER] Structure rebuild complete: " +
                    $"{rebuiltPdf.Length:N0} bytes");

                result.Success = true;
                result.OutputPdf = rebuiltPdf;
                result.ChangesMade = true;
                result.IssuesFound = CountTotalBlocks(logicalDocument); // Blocks analyzed
                result.IssuesFixed = CountTotalBlocks(logicalDocument); // Blocks rebuilt
                result.Metadata["pages_analyzed"] = logicalDocument.Pages.Count;
                result.Metadata["blocks_rebuilt"] = CountTotalBlocks(logicalDocument);
                result.Duration = sw.Elapsed;

                _logger.LogInformation(
                    $"[STRUCTURE-REBUILD-ADAPTER] Complete in {sw.Elapsed.TotalSeconds:F1}s");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[STRUCTURE-REBUILD-ADAPTER] Unexpected error");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Duration = sw.Elapsed;
                return result;
            }
        }

        private int CountTotalBlocks(LogicalDocument document)
        {
            var count = 0;
            foreach (var page in document.Pages)
            {
                count += page.Blocks.Count;
            }
            return count;
        }
    }
}
