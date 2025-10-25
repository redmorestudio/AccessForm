using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.Pdf.Tagutils;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation.Fixes
{
    /// <summary>
    /// Fixes 7.1-1 and 7.1-2 violations related to artifacts and tagged content.
    /// 7.1-1: Content marked as Artifact should not be present inside tagged content
    /// 7.1-2: Tagged content should not be present inside artifact designation
    /// These are two sides of the same coin and must be handled together to avoid ping-ponging.
    /// </summary>
    public class ArtifactTaggedContentFixService : IRemediationService
    {
        private readonly ILogger<ArtifactTaggedContentFixService> _logger;

        public string ServiceName => "Artifact/Tagged Content Fix";
        public ViolationCategory TargetCategory => ViolationCategory.Structure;
        public int Priority => 10; // High priority - structural issues should be fixed early
        public bool IsRequired => false; // Only run when these specific violations are detected

        public ArtifactTaggedContentFixService(ILogger<ArtifactTaggedContentFixService> logger)
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
                _logger.LogInformation("[ARTIFACT-FIX] Starting artifact/tagged content remediation");

                using var ms = new MemoryStream(pdfBytes);
                using var outputMs = new MemoryStream();
                using var pdfDoc = new PdfDocument(new PdfReader(ms), new PdfWriter(outputMs));

                if (!pdfDoc.IsTagged())
                {
                    _logger.LogWarning("[ARTIFACT-FIX] Document is not tagged");
                    result.Success = true;
                    return result;
                }

                var fixedCount = 0;
                var rootTag = pdfDoc.GetStructTreeRoot();

                // Iterate through root's children
                var kids = rootTag.GetKids();
                if (kids != null)
                {
                    foreach (var kid in kids)
                    {
                        if (kid is PdfStructElem elem)
                        {
                            // Phase 1: Move tagged content out of artifacts
                            fixedCount += await FixTaggedContentInArtifacts(elem);

                            // Phase 2: Convert non-semantic content to artifacts
                            fixedCount += await ConvertNonSemanticContentToArtifacts(elem);

                            // Phase 3: Clean up whitespace in artifacts
                            fixedCount += await RemoveWhitespaceInArtifacts(elem);
                        }
                    }
                }

                pdfDoc.Close();

                if (fixedCount > 0)
                {
                    result.OutputPdf = outputMs.ToArray();
                    result.Success = true;
                    result.ChangesMade = true;
                    result.IssuesFixed = fixedCount;
                    _logger.LogInformation($"[ARTIFACT-FIX] Fixed {fixedCount} artifact/tagged content issues");
                }
                else
                {
                    _logger.LogInformation("[ARTIFACT-FIX] No artifact/tagged content issues found");
                    result.Success = true;
                }

                stopwatch.Stop();
                _logger.LogInformation($"[ARTIFACT-FIX] Completed in {stopwatch.ElapsedMilliseconds}ms");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ARTIFACT-FIX] Failed to fix artifact/tagged content issues");
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private async Task<int> FixTaggedContentInArtifacts(PdfStructElem element)
        {
            var fixedCount = 0;

            try
            {
                // Check if this element is marked as artifact
                var pdfObject = element.GetPdfObject();
                if (pdfObject != null && IsArtifact(pdfObject))
                {
                    // Check for any tagged content inside
                    var kids = element.GetKids();
                    if (kids != null && kids.Count > 0)
                    {
                        _logger.LogInformation($"[ARTIFACT-FIX] Found tagged content inside artifact, moving out");

                        // Move tagged content outside of artifact
                        foreach (var kid in kids.ToList())
                        {
                            if (kid is PdfStructElem childElem)
                            {
                                // Move to parent if possible
                                var parent = element.GetParent() as PdfStructElem;
                                if (parent != null)
                                {
                                    parent.AddKid(childElem);
                                    fixedCount++;
                                }
                            }
                        }
                    }
                }

                // Recursively check children
                var children = element.GetKids();
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        if (child is PdfStructElem childElem)
                        {
                            fixedCount += await FixTaggedContentInArtifacts(childElem);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ARTIFACT-FIX] Error processing element: {ex.Message}");
            }

            return await Task.FromResult(fixedCount);
        }

        private async Task<int> ConvertNonSemanticContentToArtifacts(PdfStructElem element)
        {
            var fixedCount = 0;

            try
            {
                // List of tag types that are typically non-semantic
                var nonSemanticTags = new HashSet<string>
                {
                    "NonStruct", "Private", "Artifact"
                };

                var role = element.GetRole()?.GetValue();
                if (role != null && ShouldBeArtifact(role))
                {
                    _logger.LogInformation($"[ARTIFACT-FIX] Converting {role} to artifact");

                    // Mark as artifact
                    var pdfObject = element.GetPdfObject();
                    if (pdfObject != null)
                    {
                        pdfObject.Put(PdfName.Type, new PdfName("MCR"));
                        pdfObject.Put(new PdfName("Artifact"), PdfBoolean.TRUE);
                        fixedCount++;
                    }
                }

                // Recursively check children
                var children = element.GetKids();
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        if (child is PdfStructElem childElem)
                        {
                            fixedCount += await ConvertNonSemanticContentToArtifacts(childElem);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ARTIFACT-FIX] Error converting to artifact: {ex.Message}");
            }

            return await Task.FromResult(fixedCount);
        }

        private async Task<int> RemoveWhitespaceInArtifacts(PdfStructElem element)
        {
            var fixedCount = 0;

            try
            {
                // Check if this is an artifact containing only whitespace
                if (IsArtifact(element.GetPdfObject()))
                {
                    var content = GetTextContent(element);
                    if (!string.IsNullOrEmpty(content) && string.IsNullOrWhiteSpace(content))
                    {
                        _logger.LogInformation("[ARTIFACT-FIX] Removing whitespace artifact");

                        // Remove this element
                        var parent = element.GetParent() as PdfStructElem;
                        if (parent != null)
                        {
                            parent.RemoveKid(element);
                            fixedCount++;
                        }
                    }
                }

                // Recursively check children
                var children = element.GetKids();
                if (children != null)
                {
                    foreach (var child in children.ToList()) // ToList to avoid modification during iteration
                    {
                        if (child is PdfStructElem childElem)
                        {
                            fixedCount += await RemoveWhitespaceInArtifacts(childElem);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ARTIFACT-FIX] Error removing whitespace: {ex.Message}");
            }

            return await Task.FromResult(fixedCount);
        }

        private bool IsArtifact(PdfDictionary pdfObject)
        {
            if (pdfObject == null) return false;

            // Check if marked as artifact
            var artifact = pdfObject.GetAsBoolean(new PdfName("Artifact"));
            if (artifact != null && artifact.GetValue()) return true;

            // Check if type is MCR (Marked Content Reference) with artifact property
            var type = pdfObject.GetAsName(PdfName.Type);
            if (type != null && type.GetValue() == "MCR")
            {
                var props = pdfObject.GetAsDictionary(new PdfName("Properties"));
                if (props != null && props.ContainsKey(new PdfName("Artifact")))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ShouldBeArtifact(string tagName)
        {
            // Determine if this tag type should be converted to artifact
            var artifactCandidates = new HashSet<string>
            {
                "NonStruct", "Private", "Background", "Pagination",
                "Layout", "Page", "Watermark", "Redaction"
            };

            return artifactCandidates.Contains(tagName);
        }

        private string GetTextContent(PdfStructElem element)
        {
            try
            {
                // Extract text content from the element using GetActualText
                var actualText = element.GetActualText();
                if (actualText != null)
                {
                    return actualText.GetValue();
                }

                // Try to get content from children
                var kids = element.GetKids();
                if (kids != null)
                {
                    var textContent = "";
                    foreach (var kid in kids)
                    {
                        if (kid is IStructureNode node)
                        {
                            // Handle text content nodes
                            var kidContent = node.ToString();
                            if (!string.IsNullOrEmpty(kidContent))
                            {
                                textContent += kidContent;
                            }
                        }
                    }
                    return textContent;
                }
            }
            catch
            {
                // Ignore errors in text extraction
            }

            return string.Empty;
        }
    }
}
