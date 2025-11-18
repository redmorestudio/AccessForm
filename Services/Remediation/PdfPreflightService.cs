using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AccessFormServer.Services;
using WordToPdfConverter.Services.Remediation.Models;
using WordToPdfConverter.Services.Remediation.Fixes;

namespace WordToPdfConverter.Services.Remediation;

/// <summary>
/// Preflight service that runs content-stream mutations BEFORE structure rebuild or MCID work.
/// This ensures that operations like font fixes and artifact tagging don't destroy MCID markers.
/// Phase 6C: Aspose font optimization
/// Phase 6D: Artifact fix (unmarked XObject wrapping)
/// </summary>
public class PdfPreflightService : IPdfPreflightService
{
    private readonly ILogger<PdfPreflightService> _logger;
    private readonly AsposePdfCloudService _asposeCloud;
    private readonly ArtifactTaggedContentFixService _artifactFix;

    public PdfPreflightService(
        ILogger<PdfPreflightService> logger,
        AsposePdfCloudService asposeCloud,
        ArtifactTaggedContentFixService artifactFix)
    {
        _logger = logger;
        _asposeCloud = asposeCloud;
        _artifactFix = artifactFix;
    }

    public async Task<byte[]> RunPreflightAsync(byte[] inputPdf, RemediationOptions options)
    {
        _logger.LogInformation("[PREFLIGHT] Starting PDF preflight operations");
        _logger.LogInformation($"[PREFLIGHT] Input size: {inputPdf.Length:N0} bytes");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Check if Aspose is configured and enabled
            if (!_asposeCloud.IsConfigured)
            {
                _logger.LogWarning("[PREFLIGHT] Aspose Cloud is not configured - skipping font optimization");
                return inputPdf;
            }

            if (options.AsposeOptimizationMode == "Disabled")
            {
                _logger.LogInformation("[PREFLIGHT] Aspose optimization disabled in settings - skipping");
                return inputPdf;
            }

            if (options.AsposeOptimizationMode != "PreStructureOnly")
            {
                _logger.LogWarning(
                    $"[PREFLIGHT] Unexpected AsposeOptimizationMode: {options.AsposeOptimizationMode}. " +
                    "Expected 'PreStructureOnly' or 'Disabled'. Proceeding with preflight.");
            }

            byte[] pdf = inputPdf;

            // Step 1: Run Aspose font optimization (Phase 6C)
            _logger.LogInformation("[PREFLIGHT] Step 1: Aspose Cloud font optimization...");
            var optimizedPdf = await _asposeCloud.OptimizePdfWithFontEmbeddingAsync(pdf);

            if (optimizedPdf != null && optimizedPdf.Length > 0)
            {
                _logger.LogInformation(
                    $"[PREFLIGHT] ✓ Font optimization complete: {pdf.Length:N0} → {optimizedPdf.Length:N0} bytes");
                pdf = optimizedPdf;
            }
            else
            {
                _logger.LogWarning("[PREFLIGHT] Aspose optimization returned no output - continuing with original");
            }

            // Step 2: Run Artifact Fix (Phase 6D)
            if (options.ArtifactFixMode == "PreStructureOnly")
            {
                _logger.LogInformation("[PREFLIGHT] Step 2: Artifact Fix (unmarked XObject wrapping)...");
                var artifactResult = await _artifactFix.RemediateAsync(pdf);

                if (artifactResult.Success && artifactResult.OutputPdf != null && artifactResult.OutputPdf.Length > 0)
                {
                    _logger.LogInformation(
                        $"[PREFLIGHT] ✓ Artifact fix complete: {pdf.Length:N0} → {artifactResult.OutputPdf.Length:N0} bytes, {artifactResult.IssuesFixed} issues fixed");
                    pdf = artifactResult.OutputPdf;
                }
                else
                {
                    _logger.LogWarning("[PREFLIGHT] Artifact fix returned no output or failed - continuing");
                }
            }
            else
            {
                _logger.LogInformation($"[PREFLIGHT] Artifact fix skipped (mode: {options.ArtifactFixMode})");
            }

            stopwatch.Stop();
            _logger.LogInformation($"[PREFLIGHT] ✓ Preflight complete: {inputPdf.Length:N0} → {pdf.Length:N0} bytes ({stopwatch.ElapsedMilliseconds}ms)");
            return pdf;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[PREFLIGHT] Aspose optimization failed: {ex.Message}");
            _logger.LogWarning("[PREFLIGHT] Continuing with original PDF despite preflight failure");
            return inputPdf;
        }
    }
}
