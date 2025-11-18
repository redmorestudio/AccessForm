using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AccessFormServer.Services;
using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation;

/// <summary>
/// Preflight service that runs Aspose font optimization BEFORE any structure rebuild or MCID work.
/// This ensures that font fixes don't destroy MCID markers added later in the pipeline.
/// </summary>
public class PdfPreflightService : IPdfPreflightService
{
    private readonly ILogger<PdfPreflightService> _logger;
    private readonly AsposePdfCloudService _asposeCloud;

    public PdfPreflightService(
        ILogger<PdfPreflightService> logger,
        AsposePdfCloudService asposeCloud)
    {
        _logger = logger;
        _asposeCloud = asposeCloud;
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

            // Run Aspose font optimization
            _logger.LogInformation("[PREFLIGHT] Running Aspose Cloud font optimization...");
            var optimizedPdf = await _asposeCloud.OptimizePdfWithFontEmbeddingAsync(inputPdf);

            if (optimizedPdf != null && optimizedPdf.Length > 0)
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    $"[PREFLIGHT] ✓ Font optimization complete: {inputPdf.Length:N0} → {optimizedPdf.Length:N0} bytes ({stopwatch.ElapsedMilliseconds}ms)");
                return optimizedPdf;
            }
            else
            {
                _logger.LogWarning("[PREFLIGHT] Aspose optimization returned no output - using original PDF");
                return inputPdf;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[PREFLIGHT] Aspose optimization failed: {ex.Message}");
            _logger.LogWarning("[PREFLIGHT] Continuing with original PDF despite preflight failure");
            return inputPdf;
        }
    }
}
