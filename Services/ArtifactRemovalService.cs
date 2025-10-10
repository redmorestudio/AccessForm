using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tagged;
using Microsoft.Extensions.Logging;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Service to remove artifact markers from text content in PDFs using Aspose.PDF.
    /// Fixes "Tagged content present inside an artifact" errors where
    /// PassportPDF incorrectly marks form field labels as decorative artifacts.
    /// </summary>
    public class ArtifactRemovalService
    {
        private readonly ILogger<ArtifactRemovalService> _logger;

        public ArtifactRemovalService(ILogger<ArtifactRemovalService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Remove artifact markers from text content while preserving actual decorative artifacts.
        /// Uses Aspose.PDF to manipulate the tagged structure tree.
        /// </summary>
        public async Task<byte[]> RemoveTextArtifactsAsync(byte[] pdfBytes)
        {
            try
            {
                _logger.LogInformation("Starting Aspose artifact removal from text content...");

                using var pdfStream = new MemoryStream(pdfBytes);
                using var document = new Document(pdfStream);

                int artifactsProcessed = 0;

                // Iterate through all pages
                foreach (Page page in document.Pages)
                {
                    var artifacts = page.Artifacts;
                    _logger.LogDebug($"Page {page.Number}: Found {artifacts.Count} artifacts");

                    // Collect indices of artifacts to remove (we must remove by index from back to front)
                    var indicesToRemove = new System.Collections.Generic.List<int>();

                    for (int i = 0; i < artifacts.Count; i++)
                    {
                        var artifact = artifacts[i];

                        // Check if this artifact contains text
                        // Artifacts with text should be regular tagged content, not artifacts
                        var text = artifact.Text;

                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            _logger.LogInformation($"Page {page.Number}: Found text artifact: '{text.Substring(0, Math.Min(50, text.Length))}...'");
                            indicesToRemove.Add(i);
                            artifactsProcessed++;
                        }
                    }

                    // Remove text artifacts (from end to start so indices don't shift)
                    for (int i = indicesToRemove.Count - 1; i >= 0; i--)
                    {
                        artifacts.Delete(indicesToRemove[i]);
                    }

                    if (indicesToRemove.Count > 0)
                    {
                        _logger.LogInformation($"Page {page.Number}: Removed {indicesToRemove.Count} text artifacts");
                    }
                }

                _logger.LogInformation($"Total text artifacts removed: {artifactsProcessed}");

                // Save to memory stream
                using var outputStream = new MemoryStream();
                document.Save(outputStream);

                return outputStream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove artifacts from text using Aspose");
                return pdfBytes; // Return original on error
            }
        }
    }
}
