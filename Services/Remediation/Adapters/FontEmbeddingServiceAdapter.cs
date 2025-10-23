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
    /// </summary>
    public class FontEmbeddingServiceAdapter : IRemediationService
    {
        private readonly ILogger<FontEmbeddingServiceAdapter> _logger;
        private readonly AsposePdfService _asposePdfService;

        public string ServiceName => "Font Embedding";
        public ViolationCategory TargetCategory => ViolationCategory.Fonts;
        public int Priority => 6;
        public bool IsRequired => true; // Fonts MUST be embedded

        public FontEmbeddingServiceAdapter(
            ILogger<FontEmbeddingServiceAdapter> logger,
            AsposePdfService asposePdfService)
        {
            _logger = logger;
            _asposePdfService = asposePdfService;
        }

        public async Task<ServiceResult> RemediateAsync(byte[] pdfBytes)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new ServiceResult { Success = true };

            try
            {
                _logger.LogInformation($"[{ServiceName}] Embedding all fonts for PDF ({pdfBytes.Length} bytes)");

                // Use Aspose.PDF to embed all fonts
                var fixedBytes = await _asposePdfService.ConvertFontsAsync(pdfBytes);

                if (fixedBytes != null && fixedBytes.Length > 0)
                {
                    result.OutputPdf = fixedBytes;
                    result.ChangesMade = (fixedBytes.Length != pdfBytes.Length);
                    result.Success = true;

                    _logger.LogInformation(
                        $"[{ServiceName}] ✓ Embedded fonts: {pdfBytes.Length} → {fixedBytes.Length} bytes");
                }
                else
                {
                    result.Success = false;
                    result.ChangesMade = false;
                    _logger.LogWarning($"[{ServiceName}] Font embedding returned no output");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[{ServiceName}] Exception during font embedding");
                result.Success = false;
                result.ChangesMade = false;
            }

            stopwatch.Stop();
            return result;
        }
    }
}
