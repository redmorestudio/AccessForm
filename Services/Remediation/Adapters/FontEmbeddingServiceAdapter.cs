using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Remediation.Models;
using AccessFormServer.Services;

namespace WordToPdfConverter.Services.Remediation.Adapters
{
    /// <summary>
    /// Adapter for font embedding service to ensure all fonts are embedded
    /// Uses Aspose Cloud API to avoid macOS GDI+ issues
    /// </summary>
    public class FontEmbeddingServiceAdapter : IRemediationService
    {
        private readonly ILogger<FontEmbeddingServiceAdapter> _logger;
        private readonly AsposePdfCloudService _cloudService;

        public string ServiceName => "Font Embedding";
        public ViolationCategory TargetCategory => ViolationCategory.Fonts;
        public int Priority => 6;
        public bool IsRequired => true; // Fonts MUST be embedded

        public FontEmbeddingServiceAdapter(
            ILogger<FontEmbeddingServiceAdapter> logger,
            AsposePdfCloudService cloudService)
        {
            _logger = logger;
            _cloudService = cloudService;
        }

        public async Task<ServiceResult> RemediateAsync(byte[] pdfBytes)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new ServiceResult { Success = false, OutputPdf = pdfBytes };

            try
            {
                _logger.LogInformation($"[FONT-SERVICE] Starting font embedding for PDF ({pdfBytes.Length} bytes)");
                _logger.LogInformation($"[FONT-SERVICE] Strategy: Use Aspose Cloud API → Optimize → Convert to PDF/A-1b");

                if (!_cloudService.IsConfigured)
                {
                    throw new InvalidOperationException("Aspose Cloud service is not configured");
                }

                // Use Aspose Cloud API to embed fonts (avoids macOS GDI+ issues)
                var fixedBytes = await _cloudService.OptimizePdfWithFontEmbeddingAsync(pdfBytes);

                if (fixedBytes != null && fixedBytes.Length > 0)
                {
                    result.OutputPdf = fixedBytes;
                    result.ChangesMade = true;
                    result.Success = true;
                    result.IssuesFixed = 1; // Font embedding applied

                    _logger.LogInformation(
                        $"[FONT-SERVICE] ✓ Success: {pdfBytes.Length} → {fixedBytes.Length} bytes ({stopwatch.ElapsedMilliseconds}ms)");
                    _logger.LogInformation($"[FONT-SERVICE] All fonts embedded via Aspose Cloud, converted to PDF/A-1b");
                }
                else
                {
                    result.Success = false;
                    result.ChangesMade = false;
                    result.ErrorMessage = "Font embedding service returned no output";

                    _logger.LogWarning($"[FONT-SERVICE] ⚠ Font embedding returned no output - may need GPT fallback");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[FONT-SERVICE] ⚠ Exception during font embedding: {ex.Message}");
                _logger.LogWarning($"[FONT-SERVICE] Font embedding failed - will fall back to GPT if needed");

                result.Success = false;
                result.ChangesMade = false;
                result.ErrorMessage = $"Font embedding failed: {ex.Message}";
                result.OutputPdf = pdfBytes; // Return original if failed
            }

            stopwatch.Stop();
            _logger.LogInformation($"[FONT-SERVICE] Completed in {stopwatch.ElapsedMilliseconds}ms");

            return result;
        }
    }
}
