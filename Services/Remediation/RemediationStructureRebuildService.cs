using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.Logical;
using WordToPdfConverter.Models.Remediation;
using WordToPdfConverter.Services.Analysis;
using WordToPdfConverter.Services.Pdf;
using WordToPdfConverter.Services.Remediation.Structure;

namespace WordToPdfConverter.Services.Remediation;

/// <summary>
/// Remediation service that rebuilds PDF tag structure based on AI-derived logical layout.
/// This service uses AI vision models to analyze the visual layout of a PDF, constructs a
/// semantic document model, and then rebuilds the tag tree from scratch for better accessibility.
/// Particularly useful for PDFs from design tools (Illustrator, InDesign) that have poor structure.
/// NOTE: This is the REMEDIATION version. The production version is at Services/StructureRebuildService.cs
/// </summary>
public sealed class RemediationStructureRebuildService : IRemediationService
{
    private readonly ILogger<RemediationStructureRebuildService> _logger;
    private readonly ILogicalLayoutAnalysisService _layout;
    private readonly FormFieldEnrichmentService _formFieldEnrichment;
    private readonly StructureTreeCleaner _cleaner;
    private readonly IPdfStructureWriter _writer;
    private readonly ProcessingProgressService? _progressService;
    private readonly RemediationJobContext _jobContext;

    public string ServiceName => "AI Structure Rebuild";
    public ViolationCategory TargetCategory => ViolationCategory.Structure;
    public int Priority => 2; // Run early, after whitespace cleanup, before other structure fixes
    public bool IsRequired => false; // Optional - can be disabled if needed

    public RemediationStructureRebuildService(
        ILogger<RemediationStructureRebuildService> logger,
        ILogicalLayoutAnalysisService layout,
        FormFieldEnrichmentService formFieldEnrichment,
        StructureTreeCleaner cleaner,
        IPdfStructureWriter writer,
        RemediationJobContext jobContext,
        ProcessingProgressService? progressService = null)
    {
        _logger = logger;
        _layout = layout;
        _formFieldEnrichment = formFieldEnrichment;
        _cleaner = cleaner;
        _writer = writer;
        _jobContext = jobContext;
        _progressService = progressService;
    }

    public async Task<ServiceResult> RemediateAsync(byte[] pdfBytes)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new ServiceResult
        {
            Success = false,
            OutputPdf = pdfBytes
        };

        try
        {
            _logger.LogInformation("[AI-STRUCTURE-REBUILD] Starting AI-driven structure rebuild");

            // Step 1: Run AI layout analysis
            _logger.LogInformation("[AI-STRUCTURE-REBUILD] Analyzing PDF layout with AI...");

            var logical = await _layout.AnalyzeAsync(
                pdfBytes,
                new LogicalLayoutOptions(),
                default);

            _logger.LogInformation(
                $"[AI-STRUCTURE-REBUILD] Analysis complete: {logical.Pages.Count} pages, " +
                $"{CountBlocks(logical)} total blocks");

            // Step 1.5: Enrich with form fields from PDF
            _logger.LogInformation("[AI-STRUCTURE-REBUILD] Enriching document with form fields...");
            logical = _formFieldEnrichment.EnrichWithFormFields(logical, pdfBytes);

            _logger.LogInformation(
                $"[AI-STRUCTURE-REBUILD] Enrichment complete: {CountBlocks(logical)} total blocks " +
                "(including form fields)");

            // Step 2: Check if we should rebuild
            if (!ShouldRebuild(logical))
            {
                _logger.LogInformation("[AI-STRUCTURE-REBUILD] Skipping rebuild (not needed)");
                result.Success = true;
                result.ChangesMade = false;
                return result;
            }

            // Step 3: Build semantic structure tree
            _logger.LogInformation("[AI-STRUCTURE-REBUILD] Building semantic structure tree...");
            var structure = StructureTreeBuilder.Build(logical);

            _logger.LogInformation(
                $"[AI-STRUCTURE-REBUILD] Structure tree built with {structure.Nodes.Count} root nodes");

            // Step 3.5: Clean structure tree (artifact detection + table semantics)
            _logger.LogInformation("[AI-STRUCTURE-REBUILD] Cleaning structure tree (artifacts & table headers)...");
            structure = _cleaner.Clean(structure);

            _logger.LogInformation("[AI-STRUCTURE-REBUILD] Structure tree cleaning complete");

            // Step 4: Write new PDF tags
            // Phase 6b Pipeline Integration: Use shared context from job
            // If structure rebuild already occurred with MCID content rewrite, the guard will skip this
            _logger.LogInformation("[AI-STRUCTURE-REBUILD] Rewriting PDF tag structure...");
            var context = _jobContext.StructureContext;
            var updated = _writer.Rewrite(pdfBytes, structure, context);

            result.OutputPdf = updated;
            result.Success = true;
            result.ChangesMade = true;
            result.IssuesFixed = 1; // Track that we rebuilt the structure

            _logger.LogInformation("[AI-STRUCTURE-REBUILD] Structure rebuild completed successfully");

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI-STRUCTURE-REBUILD] Failed to rebuild structure");
            result.ErrorMessage = ex.Message;
            result.Success = false;
            result.OutputPdf = pdfBytes; // Return original on failure

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;

            return result;
        }
    }

    /// <summary>
    /// Determines whether we should rebuild the structure for this document.
    /// Initial heuristic: always rebuild.
    /// Future: Could check for Illustrator/InDesign creator metadata, structure quality scores, etc.
    /// </summary>
    private static bool ShouldRebuild(LogicalDocument logical)
    {
        // For now, always rebuild if we got meaningful content from AI
        // Could add more sophisticated heuristics later:
        // - Check if PDF was created by Illustrator/InDesign
        // - Check if existing structure is poor quality
        // - Check if document has enough blocks to warrant rebuild

        if (logical.Pages.Count == 0)
        {
            return false; // No pages to rebuild
        }

        var blockCount = CountBlocks(logical);
        if (blockCount == 0)
        {
            return false; // No blocks detected
        }

        // TODO: Add more sophisticated decision logic here
        // For now, always rebuild if we have content
        return true;
    }

    /// <summary>
    /// Counts total blocks across all pages for logging.
    /// </summary>
    private static int CountBlocks(LogicalDocument doc)
    {
        int count = 0;
        foreach (var page in doc.Pages)
        {
            count += page.Blocks.Count;
        }
        return count;
    }
}
