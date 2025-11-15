using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation.Fixes
{
    /// <summary>
    /// Adds AI-generated alt text to link annotations for accessibility
    /// Handles 7.18.1-2 and 7.18.5-2 violations (links need Contents or Alt)
    /// </summary>
    public class LinkAltTextService : IRemediationService
    {
        private readonly ILogger<LinkAltTextService> _logger;
        private readonly AccessFormServer.Services.AnthropicService _anthropic;

        public string ServiceName => "Link Alt Text Service";
        public ViolationCategory TargetCategory => ViolationCategory.Links;
        public int Priority => 6;
        public bool IsRequired => false;

        public LinkAltTextService(
            ILogger<LinkAltTextService> logger,
            AccessFormServer.Services.AnthropicService anthropic)
        {
            _logger = logger;
            _anthropic = anthropic;
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
                _logger.LogInformation("[LINK-ALT-TEXT] Starting link alt text remediation");

                using var ms = new MemoryStream(pdfBytes);
                using var outputMs = new MemoryStream();
                using var pdfDoc = new PdfDocument(new PdfReader(ms), new PdfWriter(outputMs));

                var fixedCount = 0;
                var skippedCount = 0;

                // Process all pages
                for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                {
                    var page = pdfDoc.GetPage(i);
                    var annotations = page.GetAnnotations();

                    if (annotations == null || annotations.Count == 0)
                        continue;

                    foreach (var annot in annotations)
                    {
                        // Check if it's a link annotation
                        if (annot.GetSubtype() != PdfName.Link)
                            continue;

                        var linkAnnot = annot as PdfLinkAnnotation;
                        if (linkAnnot == null)
                            continue;

                        // Check if it already has meaningful Contents
                        // VeraPDF requires BOTH Contents key AND Alt on structure element
                        var pdfDict = linkAnnot.GetPdfObject();
                        var existingContents = pdfDict.GetAsString(PdfName.Contents);
                        var contentsValue = existingContents?.GetValue();
                        bool hasValidContents = !string.IsNullOrWhiteSpace(contentsValue) &&
                                                contentsValue.Trim().Length >= 3; // Require at least 3 meaningful characters

                        string finalAltText = null;
                        bool needsContentsUpdate = false;

                        if (hasValidContents)
                        {
                            // Use existing Contents value
                            finalAltText = contentsValue;
                            _logger.LogDebug($"[LINK-ALT-TEXT] Link already has valid Contents: '{contentsValue}', will ensure structure element Alt is set");
                        }
                        else
                        {
                            // Need to generate new Contents
                            needsContentsUpdate = true;

                            if (existingContents != null)
                            {
                                _logger.LogInformation($"[LINK-ALT-TEXT] Link has invalid/empty Contents ('{contentsValue}'), will replace");
                            }

                            // Extract link information with context
                            var linkText = ExtractLinkText(linkAnnot, page);
                            var linkUrl = ExtractLinkUrl(linkAnnot);
                            var surroundingContext = ExtractSurroundingContext(linkAnnot, page);
                            var linkType = DetermineLinkType(linkAnnot);

                            // Generate alt text using AI with context
                            var altText = await GenerateAltTextAsync(linkText, linkUrl, surroundingContext, linkType);

                            // Determine the alt text to use
                            if (!string.IsNullOrWhiteSpace(altText))
                            {
                                finalAltText = altText;
                            }
                            else
                            {
                                // Fallback: use link text or URL
                                finalAltText = !string.IsNullOrWhiteSpace(linkText) ? linkText : linkUrl;
                            }

                            // If still no text, use generic description
                            if (string.IsNullOrWhiteSpace(finalAltText))
                            {
                                finalAltText = linkUrl != null && linkUrl.Length > 0 ? $"Link to {linkUrl}" : "Link";
                            }
                        }

                        // Update Contents if needed
                        if (needsContentsUpdate && !string.IsNullOrWhiteSpace(finalAltText))
                        {
                            pdfDict.Put(PdfName.Contents, new iText.Kernel.Pdf.PdfString(finalAltText));
                            _logger.LogInformation($"[LINK-ALT-TEXT] Updated Contents: '{finalAltText.Substring(0, Math.Min(50, finalAltText.Length))}'");
                        }

                        // ALWAYS set Alt on the structure element (required by VeraPDF)
                        // Per PDF/UA 7.18.1-2 and 7.18.5-2, both Contents and Alt are needed
                        bool structElemFixed = SetStructureElementAlt(pdfDoc, linkAnnot, finalAltText);

                        if (needsContentsUpdate || structElemFixed)
                        {
                            fixedCount++;
                            _logger.LogInformation($"[LINK-ALT-TEXT] Fixed link: Contents={(needsContentsUpdate ? "updated" : "preserved")}, StructElem={structElemFixed}");
                        }
                        else
                        {
                            skippedCount++;
                            _logger.LogDebug($"[LINK-ALT-TEXT] Skipped link: already compliant");
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
                    _logger.LogInformation($"[LINK-ALT-TEXT] Added alt text to {fixedCount} links, skipped {skippedCount} links that already had alt text");
                }
                else
                {
                    _logger.LogInformation($"[LINK-ALT-TEXT] All {skippedCount} links already have alt text");
                    result.Success = true;
                }

                stopwatch.Stop();
                _logger.LogInformation($"[LINK-ALT-TEXT] Completed in {stopwatch.ElapsedMilliseconds}ms");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LINK-ALT-TEXT] Failed to add link alt text");
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private string ExtractLinkText(PdfLinkAnnotation linkAnnot, PdfPage page)
        {
            try
            {
                var rect = linkAnnot.GetRectangle();
                if (rect == null) return "";

                // Extract all text from page - simplified approach
                var strategy = new LocationTextExtractionStrategy();
                var processor = new PdfCanvasProcessor(strategy);
                processor.ProcessPageContent(page);
                var pageText = strategy.GetResultantText();

                if (string.IsNullOrWhiteSpace(pageText)) return "";

                // Simple heuristic: extract text around link position
                // For production, would need more sophisticated region-based extraction
                var linkText = pageText.Trim();
                if (linkText.Length > 100)
                {
                    linkText = linkText.Substring(0, 100); // Limit for context
                }

                _logger.LogDebug($"[LINK-ALT-TEXT] Extracted page text snippet");
                return linkText;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[LINK-ALT-TEXT] Could not extract link text: {ex.Message}");
            }

            return "";
        }

        private string ExtractSurroundingContext(PdfLinkAnnotation linkAnnot, PdfPage page)
        {
            try
            {
                var rect = linkAnnot.GetRectangle();
                if (rect == null) return "";

                // Extract all text from the page
                var strategy = new LocationTextExtractionStrategy();
                var processor = new PdfCanvasProcessor(strategy);
                processor.ProcessPageContent(page);
                var pageText = strategy.GetResultantText();

                if (string.IsNullOrWhiteSpace(pageText)) return "";

                // For context, provide more surrounding text (up to 200 chars)
                var context = pageText.Trim();
                if (context.Length > 200)
                {
                    context = context.Substring(0, 200);
                }

                _logger.LogDebug($"[LINK-ALT-TEXT] Extracted context: '{context.Substring(0, Math.Min(50, context.Length))}...'");
                return context;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[LINK-ALT-TEXT] Could not extract surrounding context: {ex.Message}");
            }

            return "";
        }

        private string DetermineLinkType(PdfLinkAnnotation linkAnnot)
        {
            try
            {
                var action = linkAnnot.GetAction();
                if (action != null)
                {
                    var actionType = action.Get(PdfName.S);

                    if (actionType == PdfName.URI)
                    {
                        var uri = action.GetAsString(PdfName.URI);
                        if (uri != null)
                        {
                            var url = uri.GetValue();
                            if (url.StartsWith("mailto:")) return "email";
                            if (url.StartsWith("tel:")) return "phone";
                            if (url.StartsWith("http://") || url.StartsWith("https://")) return "external";
                            return "file";
                        }
                    }
                    else if (actionType == PdfName.GoTo || actionType == PdfName.GoToR)
                    {
                        return "internal";
                    }
                }

                // Check for destination (internal link)
                var dest = linkAnnot.GetDestinationObject();
                if (dest != null) return "internal";
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[LINK-ALT-TEXT] Could not determine link type: {ex.Message}");
            }

            return "unknown";
        }

        private string ExtractLinkUrl(PdfLinkAnnotation linkAnnot)
        {
            try
            {
                var action = linkAnnot.GetAction();
                if (action != null)
                {
                    // URI action
                    if (action.Get(PdfName.S) == PdfName.URI)
                    {
                        var uri = action.GetAsString(PdfName.URI);
                        if (uri != null)
                            return uri.GetValue();
                    }
                }

                // Try destination
                var dest = linkAnnot.GetDestinationObject();
                if (dest != null)
                {
                    return dest.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[LINK-ALT-TEXT] Could not extract link URL: {ex.Message}");
            }

            return "";
        }

        /// <summary>
        /// Set Alt attribute on the structure element that encloses this link annotation
        /// This is required by VeraPDF in addition to the Contents key on the annotation
        /// </summary>
        private bool SetStructureElementAlt(PdfDocument pdfDoc, PdfLinkAnnotation linkAnnot, string altText)
        {
            try
            {
                var annotDict = linkAnnot.GetPdfObject();

                // Get the StructParent index from the annotation
                var structParent = annotDict.GetAsNumber(PdfName.StructParent);
                if (structParent == null)
                {
                    _logger.LogDebug("[LINK-ALT-TEXT] Annotation has no StructParent, cannot set structure element Alt");
                    return false;
                }

                int structParentIndex = structParent.IntValue();

                // Get the structure tree root
                var catalog = pdfDoc.GetCatalog().GetPdfObject();
                var structTreeRoot = catalog.GetAsDictionary(PdfName.StructTreeRoot);
                if (structTreeRoot == null)
                {
                    _logger.LogDebug("[LINK-ALT-TEXT] No structure tree root found");
                    return false;
                }

                // Get the ParentTree
                var parentTree = structTreeRoot.GetAsDictionary(PdfName.ParentTree);
                if (parentTree == null)
                {
                    _logger.LogDebug("[LINK-ALT-TEXT] No parent tree found in structure tree root");
                    return false;
                }

                // Navigate the ParentTree to find the structure element
                // The ParentTree is a number tree mapping StructParent indices to structure elements
                var structElem = FindStructureElementByStructParent(parentTree, structParentIndex);
                if (structElem == null)
                {
                    _logger.LogDebug($"[LINK-ALT-TEXT] Could not find structure element for StructParent {structParentIndex}");
                    return false;
                }

                // Set the Alt attribute on the structure element
                structElem.Put(PdfName.Alt, new PdfString(altText));
                _logger.LogDebug($"[LINK-ALT-TEXT] Set Alt attribute on structure element for StructParent {structParentIndex}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[LINK-ALT-TEXT] Failed to set structure element Alt: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Find a structure element in the ParentTree by StructParent index
        /// </summary>
        private PdfDictionary FindStructureElementByStructParent(PdfDictionary parentTree, int structParentIndex)
        {
            try
            {
                // Get the Nums array from the number tree
                var nums = parentTree.GetAsArray(PdfName.Nums);
                if (nums != null)
                {
                    // Nums array format: [key1 value1 key2 value2 ...]
                    for (int i = 0; i < nums.Size(); i += 2)
                    {
                        var keyObj = nums.Get(i);
                        if (keyObj is PdfNumber keyNum && keyNum.IntValue() == structParentIndex)
                        {
                            var valueObj = nums.Get(i + 1);
                            if (valueObj is PdfDictionary dict)
                            {
                                return dict;
                            }
                            else if (valueObj is PdfArray arr && arr.Size() > 0)
                            {
                                // Sometimes the value is an array, take first element
                                var firstElem = arr.Get(0);
                                if (firstElem is PdfDictionary firstDict)
                                {
                                    return firstDict;
                                }
                            }
                        }
                    }
                }

                // If not found in Nums, check Kids array (for large trees)
                var kids = parentTree.GetAsArray(PdfName.Kids);
                if (kids != null)
                {
                    for (int i = 0; i < kids.Size(); i++)
                    {
                        var kid = kids.GetAsDictionary(i);
                        if (kid != null)
                        {
                            var result = FindStructureElementByStructParent(kid, structParentIndex);
                            if (result != null)
                                return result;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[LINK-ALT-TEXT] Error searching ParentTree: {ex.Message}");
            }

            return null;
        }

        private async Task<string> GenerateAltTextAsync(string linkText, string linkUrl, string context, string linkType)
        {
            try
            {
                // Build context-aware prompt
                var sb = new StringBuilder();
                sb.AppendLine("Generate a concise, accessible description for a PDF link.");
                sb.AppendLine("The description should be suitable for screen readers and follow PDF/UA accessibility standards.");
                sb.AppendLine();
                sb.AppendLine($"Link type: {linkType}");
                sb.AppendLine($"Link text: {(string.IsNullOrWhiteSpace(linkText) ? "(no text)" : linkText)}");
                sb.AppendLine($"Link URL: {(string.IsNullOrWhiteSpace(linkUrl) ? "(no destination)" : linkUrl)}");

                if (!string.IsNullOrWhiteSpace(context))
                {
                    sb.AppendLine($"Surrounding context: {context.Substring(0, Math.Min(200, context.Length))}");
                }

                sb.AppendLine();
                sb.AppendLine("Based on the context, generate a short, descriptive alt text (1-2 sentences max).");
                sb.AppendLine("Consider:");
                sb.AppendLine("- What page/section the link navigates to");
                sb.AppendLine("- The purpose of the link in context");
                sb.AppendLine("- For external links, what resource it leads to");
                sb.AppendLine("- For email/phone, make it action-oriented");
                sb.AppendLine();
                sb.AppendLine("Only return the alt text, nothing else.");

                var prompt = sb.ToString();

                // Use AnalyzeFormFieldsAsync as a general-purpose text generation endpoint
                var response = await _anthropic.AnalyzeFormFieldsAsync(prompt);

                var altText = response?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(altText))
                {
                    _logger.LogDebug($"[LINK-ALT-TEXT] AI generated alt text: '{altText.Substring(0, Math.Min(50, altText.Length))}'");
                }

                return altText;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[LINK-ALT-TEXT] AI generation failed: {ex.Message}");
                return "";
            }
        }
    }
}
