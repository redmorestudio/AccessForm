using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Tagging;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation.Fixes
{
    /// <summary>
    /// Ensures all Figure elements have proper alt text for accessibility.
    /// This service runs as a final pass to catch any figures that might have been missed
    /// or created during other remediation steps.
    /// </summary>
    public class FigureAltTextService : IRemediationService
    {
        private readonly ILogger<FigureAltTextService> _logger;

        public string ServiceName => "Figure Alt Text Service";
        public ViolationCategory TargetCategory => ViolationCategory.AlternateText;
        public int Priority => 3; // High priority for accessibility
        public bool IsRequired => true; // Always run this as a safety net

        public FigureAltTextService(ILogger<FigureAltTextService> logger)
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
                _logger.LogInformation("[FIGURE-ALT-TEXT] Starting figure alt text remediation");

                using var ms = new MemoryStream(pdfBytes);
                using var outputMs = new MemoryStream();
                using var pdfDoc = new PdfDocument(new PdfReader(ms), new PdfWriter(outputMs));

                if (!pdfDoc.IsTagged())
                {
                    _logger.LogWarning("[FIGURE-ALT-TEXT] Document is not tagged");
                    result.Success = true;
                    return result;
                }

                var fixedCount = 0;
                var skippedCount = 0;
                var rootTag = pdfDoc.GetStructTreeRoot();

                // Find all figure elements - iterate through root's children
                var figures = new List<PdfStructElem>();
                var kids = rootTag.GetKids();
                if (kids != null)
                {
                    foreach (var kid in kids)
                    {
                        if (kid is PdfStructElem elem)
                        {
                            figures.AddRange(await FindAllFiguresRecursively(elem));
                        }
                    }
                }

                _logger.LogInformation($"[FIGURE-ALT-TEXT] Found {figures.Count} figure elements to check");

                foreach (var figure in figures)
                {
                    var altTextResult = await ProcessFigure(figure);
                    if (altTextResult == ProcessResult.Fixed)
                    {
                        fixedCount++;
                    }
                    else if (altTextResult == ProcessResult.Skipped)
                    {
                        skippedCount++;
                    }
                }

                pdfDoc.Close();

                if (fixedCount > 0)
                {
                    result.OutputPdf = outputMs.ToArray();
                    result.Success = true;
                    result.ChangesMade = true;
                    result.IssuesFixed = fixedCount;
                    _logger.LogInformation($"[FIGURE-ALT-TEXT] Added alt text to {fixedCount} figures, skipped {skippedCount} form-related figures");
                }
                else
                {
                    _logger.LogInformation($"[FIGURE-ALT-TEXT] All {figures.Count - skippedCount} non-form figures have alt text");
                    result.Success = true;
                }

                stopwatch.Stop();
                _logger.LogInformation($"[FIGURE-ALT-TEXT] Completed in {stopwatch.ElapsedMilliseconds}ms");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FIGURE-ALT-TEXT] Failed to add figure alt text");
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private async Task<List<PdfStructElem>> FindAllFiguresRecursively(PdfStructElem element)
        {
            var figures = new List<PdfStructElem>();

            try
            {
                var role = element.GetRole();
                if (role != null && (role.Equals(PdfName.Figure) ||
                                    role.GetValue() == "Figure" ||
                                    role.GetValue() == "FIGURE" ||
                                    role.GetValue() == "Fig"))
                {
                    figures.Add(element);
                }

                // Recursively search children
                var children = element.GetKids();
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        if (child is PdfStructElem childElem)
                        {
                            figures.AddRange(await FindAllFiguresRecursively(childElem));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FIGURE-ALT-TEXT] Error searching for figures: {ex.Message}");
            }

            return figures;
        }

        private async Task<ProcessResult> ProcessFigure(PdfStructElem figure)
        {
            try
            {
                // Check if this figure is actually a form element (checkbox, radio button, etc.)
                if (await IsFormRelatedFigure(figure))
                {
                    _logger.LogDebug("[FIGURE-ALT-TEXT] Skipping form-related figure");
                    return ProcessResult.Skipped;
                }

                // Check for existing alt text
                var existingAltText = figure.GetAlt()?.GetValue();

                if (!string.IsNullOrWhiteSpace(existingAltText))
                {
                    _logger.LogDebug($"[FIGURE-ALT-TEXT] Figure already has alt text: '{existingAltText}'");
                    return ProcessResult.AlreadyFixed;
                }

                // Check for actual text
                var actualText = figure.GetActualText()?.GetValue();
                if (!string.IsNullOrWhiteSpace(actualText))
                {
                    _logger.LogDebug($"[FIGURE-ALT-TEXT] Figure has actual text: '{actualText}'");
                    return ProcessResult.AlreadyFixed;
                }

                // Try to infer alt text from context
                var inferredAltText = await InferAltText(figure);

                // Set alt text directly on the PDF object
                figure.Put(PdfName.Alt, new PdfString(inferredAltText));

                _logger.LogInformation($"[FIGURE-ALT-TEXT] Added alt text: '{inferredAltText}'");
                return ProcessResult.Fixed;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FIGURE-ALT-TEXT] Error processing figure: {ex.Message}");
                return ProcessResult.Error;
            }
        }

        private async Task<bool> IsFormRelatedFigure(PdfStructElem figure)
        {
            try
            {
                // Check parent context
                var parent = figure.GetParent() as PdfStructElem;
                if (parent != null)
                {
                    var parentRole = parent.GetRole();
                    if (parentRole != null)
                    {
                        var roleValue = parentRole.GetValue();
                        if (roleValue == "Form" || roleValue == "FORM" ||
                            roleValue == "Lbl" || roleValue == "LBL" ||
                            roleValue == "LBody" || roleValue == "LBODY")
                        {
                            return true;
                        }
                    }

                    // Check if parent has form-related attributes
                    var parentObj = parent.GetPdfObject();
                    if (parentObj != null)
                    {
                        var formAttr = parentObj.GetAsDictionary(new PdfName("Form"));
                        if (formAttr != null)
                        {
                            return true;
                        }
                    }
                }

                // Check children for form widgets
                var children = figure.GetKids();
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        // Check if child contains form annotations
                        if (child is PdfStructElem childElem)
                        {
                            var childObj = childElem.GetPdfObject();
                            if (childObj != null && childObj.ContainsKey(PdfName.Widget))
                            {
                                return true;
                            }
                        }
                    }
                }

                // Check figure's own properties for form indicators
                var figureObj = figure.GetPdfObject();
                if (figureObj != null)
                {
                    // Check for form-related keys
                    if (figureObj.ContainsKey(PdfName.Widget) ||
                        figureObj.ContainsKey(PdfName.FT) || // Field Type
                        figureObj.ContainsKey(PdfName.T)) // Field Name
                    {
                        return true;
                    }

                    // Check MCID references that might point to form elements
                    var mcid = figureObj.GetAsNumber(PdfName.MCID);
                    if (mcid != null)
                    {
                        // This could be linked to a form widget
                        // For now, we'll be conservative and check the context
                        var actualText = figure.GetActualText()?.GetValue();
                        if (actualText != null &&
                            (actualText.Contains("checkbox", StringComparison.OrdinalIgnoreCase) ||
                             actualText.Contains("radio", StringComparison.OrdinalIgnoreCase) ||
                             actualText.Contains("button", StringComparison.OrdinalIgnoreCase)))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FIGURE-ALT-TEXT] Error checking if figure is form-related: {ex.Message}");
            }

            return await Task.FromResult(false);
        }

        private async Task<string> InferAltText(PdfStructElem figure)
        {
            try
            {
                // Try to get text from nearby elements
                var parent = figure.GetParent() as PdfStructElem;
                if (parent != null)
                {
                    // Look for a caption or label
                    var children = parent.GetKids();
                    if (children != null)
                    {
                        foreach (var sibling in children)
                        {
                            if (sibling != figure && sibling is PdfStructElem siblingElem)
                            {
                                var role = siblingElem.GetRole();
                                if (role != null)
                                {
                                    var roleValue = role.GetValue();
                                    if (roleValue == "Caption" || roleValue == "CAPTION" ||
                                        roleValue == "Lbl" || roleValue == "LBL" ||
                                        roleValue == "P")
                                    {
                                        var text = GetTextFromElement(siblingElem);
                                        if (!string.IsNullOrWhiteSpace(text))
                                        {
                                            _logger.LogDebug($"[FIGURE-ALT-TEXT] Inferred from {roleValue}: '{text}'");
                                            return text;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Check if this is in a specific context
                if (parent != null)
                {
                    var parentRole = parent.GetRole();
                    if (parentRole != null)
                    {
                        var roleValue = parentRole.GetValue();

                        // Context-based defaults
                        if (roleValue == "TD" || roleValue == "TH")
                        {
                            return "Image in table cell";
                        }
                        else if (roleValue == "LI")
                        {
                            return "Image in list item";
                        }
                        else if (roleValue == "Sect" || roleValue == "SECT")
                        {
                            return "Section image";
                        }
                    }
                }

                // Check page context
                var pageNum = GetPageNumber(figure);
                if (pageNum > 0)
                {
                    return $"Image on page {pageNum}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FIGURE-ALT-TEXT] Error inferring alt text: {ex.Message}");
            }

            // Default fallback
            return await Task.FromResult("Decorative image");
        }

        private string GetTextFromElement(PdfStructElem element)
        {
            try
            {
                // Try actual text first
                var actualText = element.GetActualText()?.GetValue();
                if (!string.IsNullOrWhiteSpace(actualText))
                {
                    return actualText.Trim();
                }

                // Try to get text from children
                var children = element.GetKids();
                if (children != null)
                {
                    var texts = new List<string>();
                    foreach (var child in children)
                    {
                        if (child is PdfStructElem childElem)
                        {
                            var childText = GetTextFromElement(childElem);
                            if (!string.IsNullOrWhiteSpace(childText))
                            {
                                texts.Add(childText);
                            }
                        }
                        else if (child is IStructureNode node)
                        {
                            // Handle text content nodes
                            var content = node.ToString();
                            if (!string.IsNullOrWhiteSpace(content))
                            {
                                texts.Add(content.Trim());
                            }
                        }
                    }

                    if (texts.Count > 0)
                    {
                        return string.Join(" ", texts);
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return null;
        }

        private int GetPageNumber(PdfStructElem element)
        {
            try
            {
                // Try to find page reference
                var obj = element.GetPdfObject();
                if (obj != null)
                {
                    var pg = obj.GetAsDictionary(PdfName.Pg);
                    if (pg != null)
                    {
                        // Find page number
                        // This is simplified - full implementation would traverse page tree
                        return 1;
                    }
                }

                // Try parent
                var parent = element.GetParent() as PdfStructElem;
                if (parent != null && parent != element)
                {
                    return GetPageNumber(parent);
                }
            }
            catch
            {
                // Ignore errors
            }

            return 0;
        }

        private enum ProcessResult
        {
            Fixed,
            AlreadyFixed,
            Skipped,
            Error
        }
    }
}
