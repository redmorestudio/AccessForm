using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation.Fixes
{
    /// <summary>
    /// Fixes 7.1-3: Content shall be marked as Artifact or tagged as real content.
    /// This service wraps unmarked XObject references in /Artifact BMC...EMC blocks.
    /// </summary>
    public class UnmarkedXObjectContentFixService : IRemediationService
    {
        private readonly ILogger<UnmarkedXObjectContentFixService> _logger;

        public string ServiceName => "Unmarked XObject Content Fix";
        public ViolationCategory TargetCategory => ViolationCategory.Content;
        public int Priority => 3; // Medium priority
        public bool IsRequired => true; // Always run to catch unmarked content

        public UnmarkedXObjectContentFixService(ILogger<UnmarkedXObjectContentFixService> logger)
        {
            _logger = logger;
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
                _logger.LogInformation("[UNMARKED-XOBJECT-FIX] Starting unmarked XObject content remediation");

                using var ms = new MemoryStream(pdfBytes);
                using var outputMs = new MemoryStream();
                using var pdfDoc = new PdfDocument(new PdfReader(ms), new PdfWriter(outputMs));

                var fixedCount = 0;

                // Process each page
                for (int pageNum = 1; pageNum <= pdfDoc.GetNumberOfPages(); pageNum++)
                {
                    var page = pdfDoc.GetPage(pageNum);
                    fixedCount += await FixUnmarkedXObjectsOnPage(page, pageNum);
                }

                pdfDoc.Close();

                if (fixedCount > 0)
                {
                    result.OutputPdf = outputMs.ToArray();
                    result.Success = true;
                    result.ChangesMade = true;
                    result.IssuesFixed = fixedCount;
                    _logger.LogInformation($"[UNMARKED-XOBJECT-FIX] Fixed {fixedCount} unmarked XObject references");
                }
                else
                {
                    _logger.LogInformation("[UNMARKED-XOBJECT-FIX] No unmarked XObject content found");
                    result.Success = true;
                }

                stopwatch.Stop();
                _logger.LogInformation($"[UNMARKED-XOBJECT-FIX] Completed in {stopwatch.ElapsedMilliseconds}ms");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UNMARKED-XOBJECT-FIX] Failed to fix unmarked XObject content");
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private async Task<int> FixUnmarkedXObjectsOnPage(PdfPage page, int pageNum)
        {
            var fixedCount = 0;

            try
            {
                // Get the page content bytes (iText combines all content streams)
                var contentBytes = page.GetContentBytes();
                if (contentBytes == null || contentBytes.Length == 0)
                {
                    return 0;
                }

                var contentString = System.Text.Encoding.UTF8.GetString(contentBytes);

                // Check if page has XObject references
                if (!contentString.Contains(" Do"))
                {
                    _logger.LogInformation($"[UNMARKED-XOBJECT-FIX] Page {pageNum} has no XObject references");
                    return 0;
                }

                // Parse content to identify individual content blocks
                var lines = contentString.Split('\n');
                var modifiedContent = new System.Text.StringBuilder();
                var inMarkedContent = false;
                var currentBlock = new System.Text.StringBuilder();
                var hasUnmarkedXObject = false;

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();

                    // Track marked content blocks
                    if (trimmed.Contains("BMC") || trimmed.Contains("BDC"))
                    {
                        inMarkedContent = true;
                        if (hasUnmarkedXObject && currentBlock.Length > 0)
                        {
                            // Wrap previous unmarked block
                            modifiedContent.AppendLine("/Artifact BMC");
                            modifiedContent.Append(currentBlock.ToString());
                            modifiedContent.AppendLine("EMC");
                            currentBlock.Clear();
                            hasUnmarkedXObject = false;
                            fixedCount++;
                        }
                        modifiedContent.AppendLine(line);
                    }
                    else if (trimmed.Contains("EMC"))
                    {
                        inMarkedContent = false;
                        modifiedContent.AppendLine(line);
                    }
                    else if (!inMarkedContent && trimmed.Contains(" Do"))
                    {
                        // Found unmarked XObject reference
                        hasUnmarkedXObject = true;
                        currentBlock.AppendLine(line);
                        _logger.LogInformation($"[UNMARKED-XOBJECT-FIX] Page {pageNum}: Found unmarked XObject");
                    }
                    else
                    {
                        if (hasUnmarkedXObject)
                        {
                            currentBlock.AppendLine(line);
                        }
                        else
                        {
                            modifiedContent.AppendLine(line);
                        }
                    }
                }

                // Handle any remaining unmarked content
                if (hasUnmarkedXObject && currentBlock.Length > 0)
                {
                    modifiedContent.AppendLine("/Artifact BMC");
                    modifiedContent.Append(currentBlock.ToString());
                    modifiedContent.AppendLine("EMC");
                    fixedCount++;
                }

                if (fixedCount > 0)
                {
                    // Replace the page content
                    page.Put(PdfName.Contents, new PdfStream(System.Text.Encoding.UTF8.GetBytes(modifiedContent.ToString())));
                    page.SetModified();
                    _logger.LogInformation($"[UNMARKED-XOBJECT-FIX] Page {pageNum}: Wrapped {fixedCount} unmarked XObject blocks");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"[UNMARKED-XOBJECT-FIX] Error processing page {pageNum}");
            }

            return await Task.FromResult(fixedCount);
        }

        private List<string> FindXObjectReferences(string content)
        {
            var references = new List<string>();

            // Find all patterns like "/FmX Do" or "/ImX Do"
            var lines = content.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.EndsWith(" Do"))
                {
                    // Extract the XObject name (e.g., "/Fm0")
                    var parts = trimmed.Split(' ');
                    if (parts.Length >= 2)
                    {
                        var xobjectName = parts[parts.Length - 2];
                        if (xobjectName.StartsWith("/"))
                        {
                            references.Add(xobjectName);
                        }
                    }
                }
            }

            return references;
        }

        private string WrapXObjectsInArtifactMarkers(string content, List<string> xobjectReferences)
        {
            var result = new StringBuilder();
            var lines = content.Split('\n');

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // Check if this line contains an XObject Do operator
                var isXObjectLine = false;
                foreach (var xobject in xobjectReferences)
                {
                    if (trimmed.Contains(xobject) && trimmed.EndsWith(" Do"))
                    {
                        isXObjectLine = true;
                        break;
                    }
                }

                if (isXObjectLine)
                {
                    // Wrap the XObject reference in Artifact markers
                    result.AppendLine("/Artifact BMC");
                    result.AppendLine(line);
                    result.AppendLine("EMC");
                }
                else
                {
                    result.AppendLine(line);
                }
            }

            return result.ToString();
        }
    }
}
