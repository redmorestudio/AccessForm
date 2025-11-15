using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Aspose.Pdf.Cloud.Sdk.Api;
using Aspose.Pdf.Cloud.Sdk.Model;

namespace AccessFormServer.Services
{
    /// <summary>
    /// Aspose.PDF Cloud service for font embedding and PDF operations
    /// Uses cloud API to avoid macOS GDI+ issues with local Aspose.PDF library
    /// </summary>
    public class AsposePdfCloudService
    {
        private readonly ILogger<AsposePdfCloudService> _logger;
        private readonly PdfApi _pdfApi;
        private readonly bool _isConfigured;
        private readonly string _appSid;
        private readonly string _appKey;

        public AsposePdfCloudService(
            ILogger<AsposePdfCloudService> logger,
            IConfiguration configuration)
        {
            _logger = logger;

            // Load configuration
            _appSid = configuration["AsposePdfCloud:AppSid"];
            _appKey = configuration["AsposePdfCloud:AppKey"];

            _isConfigured = !string.IsNullOrEmpty(_appSid) && !string.IsNullOrEmpty(_appKey);

            if (_isConfigured)
            {
                try
                {
                    _pdfApi = new PdfApi(_appKey, _appSid);
                    _logger.LogInformation("✅ Aspose PDF Cloud service initialized successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize Aspose PDF Cloud API");
                    _isConfigured = false;
                }
            }
            else
            {
                _logger.LogWarning("⚠️ Aspose PDF Cloud credentials not configured");
            }
        }

        /// <summary>
        /// Optimizes PDF with font embedding using cloud API
        /// </summary>
        public async Task<byte[]> OptimizePdfWithFontEmbeddingAsync(byte[] pdfBytes)
        {
            if (!_isConfigured)
            {
                throw new InvalidOperationException("Aspose PDF Cloud service is not configured");
            }

            try
            {
                _logger.LogInformation($"[ASPOSE-CLOUD] Starting cloud font embedding for {pdfBytes.Length} byte PDF");

                // Generate unique filename
                var fileName = $"remediation_{Guid.NewGuid()}.pdf";

                // Step 1: Upload PDF to cloud storage
                _logger.LogInformation($"[ASPOSE-CLOUD] Uploading PDF to cloud: {fileName}");
                using (var stream = new MemoryStream(pdfBytes))
                {
                    var uploadResponse = await _pdfApi.UploadFileAsync(fileName, stream);
                    _logger.LogInformation($"[ASPOSE-CLOUD] Upload complete: {uploadResponse?.Uploaded?.Count ?? 0} files");
                }

                // Step 2: Optimize PDF with font embedding
                _logger.LogInformation($"[ASPOSE-CLOUD] Optimizing PDF with font embedding");
                var optimizeOptions = new OptimizeOptions
                {
                    AllowReusePageContent = false,
                    CompressImages = false,
                    ImageQuality = 100,
                    LinkDuplcateStreams = false,
                    RemoveUnusedObjects = false,
                    RemoveUnusedStreams = false,
                    UnembedFonts = false,  // CRITICAL: Keep fonts embedded
                    SubsetFonts = false     // Don't subset fonts
                };

                var optimizeResponse = await _pdfApi.PostOptimizeDocumentAsync(fileName, optimizeOptions);
                _logger.LogInformation($"[ASPOSE-CLOUD] Optimization complete: {optimizeResponse?.Status}");

                // Step 3: Convert to PDF/A to force font embedding
                _logger.LogInformation($"[ASPOSE-CLOUD] Converting to PDF/A-1b to force font embedding");

                var outputFileName = $"optimized_{fileName}";
                var convertResponse = await _pdfApi.PutPdfInStorageToPdfAAsync(
                    fileName,
                    outputFileName,
                    type: "PDFA1B");

                _logger.LogInformation($"[ASPOSE-CLOUD] PDF/A conversion complete");

                // Step 4: Download optimized PDF
                _logger.LogInformation($"[ASPOSE-CLOUD] Downloading optimized PDF: {outputFileName}");
                var downloadStream = await _pdfApi.DownloadFileAsync(outputFileName);

                if (downloadStream == null)
                {
                    throw new Exception("Downloaded PDF stream is null");
                }

                // Convert stream to byte array
                byte[] downloadResponse;
                using (var memStream = new MemoryStream())
                {
                    await downloadStream.CopyToAsync(memStream);
                    downloadResponse = memStream.ToArray();
                }

                if (downloadResponse.Length == 0)
                {
                    throw new Exception("Downloaded PDF is empty");
                }

                _logger.LogInformation($"[ASPOSE-CLOUD] ✓ Success: {pdfBytes.Length} → {downloadResponse.Length} bytes");

                // Step 5: Cleanup cloud storage
                try
                {
                    await _pdfApi.DeleteFileAsync(fileName);
                    await _pdfApi.DeleteFileAsync(outputFileName);
                    _logger.LogInformation($"[ASPOSE-CLOUD] Cleaned up temporary files");
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning($"[ASPOSE-CLOUD] Cleanup failed (non-critical): {cleanupEx.Message}");
                }

                return downloadResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[ASPOSE-CLOUD] Font embedding failed: {ex.Message}");
                throw new InvalidOperationException($"Aspose Cloud font embedding failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Check if service is configured and ready
        /// </summary>
        public bool IsConfigured => _isConfigured;
    }
}
