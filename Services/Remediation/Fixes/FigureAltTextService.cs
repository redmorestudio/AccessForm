using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Xobject;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Remediation.Models;
using AccessFormServer.Services;

namespace WordToPdfConverter.Services.Remediation.Fixes
{
    /// <summary>
    /// Ensures all Figure elements have proper alt text for accessibility.
    /// Uses Claude Vision API to generate descriptive alt text for images.
    /// </summary>
    public class FigureAltTextService : IRemediationService
    {
        private readonly ILogger<FigureAltTextService> _logger;
        private readonly AnthropicService _anthropicService;

        public string ServiceName => "Figure Alt Text Service";
        public ViolationCategory TargetCategory => ViolationCategory.AlternateText;
        public int Priority => 3; // High priority for accessibility
        public bool IsRequired => true; // Always run this as a safety net

        public FigureAltTextService(
            ILogger<FigureAltTextService> logger,
            AnthropicService anthropicService)
        {
            _logger = logger;
            _anthropicService = anthropicService;
        }

        // Store PDF document reference and bytes for image extraction
        private PdfDocument _currentPdfDoc;
        private byte[] _currentPdfBytes;

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

                // Store references for image extraction
                _currentPdfDoc = pdfDoc;
                _currentPdfBytes = pdfBytes;

                if (!pdfDoc.IsTagged())
                {
                    _logger.LogWarning("[FIGURE-ALT-TEXT] Document is not tagged");
                    result.Success = true;
                    return result;
                }

                var fixedCount = 0;
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
                    // Log figure details for debugging
                    var figureObj = figure.GetPdfObject();
                    var objNum = figureObj?.GetIndirectReference()?.GetObjNumber() ?? -1;
                    _logger.LogInformation($"[FIGURE-ALT-TEXT] Processing figure object {objNum}");

                    var altTextResult = await ProcessFigure(figure);
                    if (altTextResult == ProcessResult.Fixed)
                    {
                        fixedCount++;
                        _logger.LogInformation($"[FIGURE-ALT-TEXT] ✅ Fixed figure object {objNum}");
                    }
                    else if (altTextResult == ProcessResult.AlreadyFixed)
                    {
                        _logger.LogInformation($"[FIGURE-ALT-TEXT] ✓ Figure object {objNum} already has alt text");
                    }
                }

                _currentPdfDoc = null;
                pdfDoc.Close();

                if (fixedCount > 0)
                {
                    result.OutputPdf = outputMs.ToArray();
                    result.Success = true;
                    result.ChangesMade = true;
                    result.IssuesFixed = fixedCount;
                    _logger.LogInformation($"[FIGURE-ALT-TEXT] Added alt text to {fixedCount} figures");
                }
                else
                {
                    _logger.LogInformation($"[FIGURE-ALT-TEXT] All {figures.Count} figures have alt text");
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
                // PDF/UA requires ALL figures to have alt text AND bounding box
                var figureObj = figure.GetPdfObject();
                bool needsAltText = false;
                bool needsBBox = false;

                // Check for existing alt text - but also check if it's lazy/generic
                var existingAltText = figure.GetAlt()?.GetValue();
                bool isLazyAltText = false;

                if (!string.IsNullOrWhiteSpace(existingAltText))
                {
                    // Check for lazy/generic patterns
                    var lazyPatterns = new[]
                    {
                        "Image", "image",
                        "Picture", "picture",
                        "Figure", "figure",
                        "Decorative image", "decorative image",
                        "Section image", "section image",
                        "Image in table cell", "Image in list item",
                        "Image on page",
                        "Graphic", "graphic",
                        "Icon", "icon",
                        "Logo", "logo" // Generic "logo" without company name
                    };

                    // Check if alt text is just one of these lazy patterns (exact match or contains only this)
                    var trimmedAlt = existingAltText.Trim();
                    isLazyAltText = lazyPatterns.Any(pattern =>
                        trimmedAlt.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
                        trimmedAlt.StartsWith($"{pattern} on page", StringComparison.OrdinalIgnoreCase) ||
                        trimmedAlt.StartsWith($"Image on page", StringComparison.OrdinalIgnoreCase));

                    if (isLazyAltText)
                    {
                        _logger.LogInformation($"[FIGURE-ALT-TEXT] Detected lazy alt text: '{existingAltText}' - will regenerate");
                    }
                }

                if (string.IsNullOrWhiteSpace(existingAltText) ||
                    existingAltText.Contains("iText.Kernel") ||  // Garbage class names
                    existingAltText.Contains("PdfMcr") ||
                    isLazyAltText)  // Lazy/generic alt text
                {
                    needsAltText = true;
                }

                // Check for existing BBox (bounding box)
                var existingBBox = figureObj?.GetAsArray(PdfName.BBox);
                if (existingBBox == null || existingBBox.Size() != 4)
                {
                    needsBBox = true;
                }

                // If both exist, we're done
                if (!needsAltText && !needsBBox)
                {
                    _logger.LogInformation($"[FIGURE-ALT-TEXT] Figure already has alt text and BBox");
                    return ProcessResult.AlreadyFixed;
                }

                // Add BBox if missing
                if (needsBBox)
                {
                    var bbox = await ExtractBoundingBox(figure);
                    if (bbox != null)
                    {
                        figure.Put(PdfName.BBox, bbox);
                        _logger.LogInformation($"[FIGURE-ALT-TEXT] Added BBox: [{bbox.GetAsNumber(0)}, {bbox.GetAsNumber(1)}, {bbox.GetAsNumber(2)}, {bbox.GetAsNumber(3)}]");
                    }
                    else
                    {
                        // Use default page-size BBox as fallback
                        var defaultBBox = new PdfArray(new float[] { 0, 0, 612, 792 }); // US Letter
                        figure.Put(PdfName.BBox, defaultBBox);
                        _logger.LogInformation($"[FIGURE-ALT-TEXT] Added default BBox (page size)");
                    }
                }

                // Add alt text if missing
                if (needsAltText)
                {
                    // Check for actual text first
                    var actualText = figure.GetActualText()?.GetValue();
                    if (!string.IsNullOrWhiteSpace(actualText))
                    {
                        figure.Put(PdfName.Alt, new PdfString(actualText));
                        _logger.LogInformation($"[FIGURE-ALT-TEXT] Used ActualText as Alt: '{actualText}'");
                    }
                    else
                    {
                        // Try to infer alt text from context (includes Claude Vision)
                        var inferredAltText = await InferAltText(figure);
                        figure.Put(PdfName.Alt, new PdfString(inferredAltText));
                        _logger.LogInformation($"[FIGURE-ALT-TEXT] Added alt text: '{inferredAltText}'");
                    }
                }

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
                // FIRST: Try Claude Vision to generate actual descriptive alt text
                var imageBytes = await ExtractImageFromFigure(figure);
                if (imageBytes != null && imageBytes.Length > 0)
                {
                    _logger.LogInformation($"[FIGURE-ALT-TEXT] Extracted {imageBytes.Length} bytes, sending to Claude Vision");

                    try
                    {
                        var claudeDescription = await _anthropicService.AnalyzeImageForAltText(imageBytes);

                        if (!string.IsNullOrWhiteSpace(claudeDescription))
                        {
                            // Add the auto-generated marker
                            var markedDescription = $"[Auto-generated] {claudeDescription}";
                            _logger.LogInformation($"[FIGURE-ALT-TEXT] ✅ Claude Vision generated: '{markedDescription}'");
                            return markedDescription;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[FIGURE-ALT-TEXT] Claude Vision failed: {ex.Message}");
                    }
                }
                else
                {
                    _logger.LogWarning($"[FIGURE-ALT-TEXT] Could not extract image bytes for Claude Vision");
                }

                // FALLBACK: Try to get text from nearby elements
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
                        // Skip non-PdfStructElem children
                        // They are internal iText objects (like PdfMcrNumber)
                        // and .ToString() returns class names, not content
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

        /// <summary>
        /// Extract bounding box from a Figure element
        /// </summary>
        private async Task<PdfArray> ExtractBoundingBox(PdfStructElem figure)
        {
            try
            {
                var figureObj = figure.GetPdfObject();
                if (figureObj == null)
                    return null;

                // Try to get BBox from the figure's marked content
                var kids = figure.GetKids();
                if (kids != null)
                {
                    foreach (var kid in kids)
                    {
                        if (kid is PdfMcr mcr)
                        {
                            // Try to get the content item's bounding box from the page's content stream
                            var mcid = mcr.GetMcid();
                            var mcrPageDict = mcr.GetPageObject();

                            if (mcrPageDict != null)
                            {
                                // Try to extract bounding box from page content
                                // Get the PdfDocument first to access the page properly
                                var pdfDoc = mcrPageDict.GetIndirectReference()?.GetDocument();
                                if (pdfDoc == null) return null;

                                // Find the page number from the dictionary
                                var pageNum = 1;
                                for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                                {
                                    if (pdfDoc.GetPage(i).GetPdfObject() == mcrPageDict)
                                    {
                                        pageNum = i;
                                        break;
                                    }
                                }

                                var page = pdfDoc.GetPage(pageNum);
                                var contentBytes = page.GetContentBytes();
                                if (contentBytes != null && contentBytes.Length > 0)
                                {
                                    var streamText = System.Text.Encoding.UTF8.GetString(contentBytes);

                                    // Find the marked content block for this MCID
                                    var mcidPattern = $@"/MCID\s+{mcid}.*?>>.*?BDC(.*?)EMC";
                                    var regex = new System.Text.RegularExpressions.Regex(mcidPattern,
                                        System.Text.RegularExpressions.RegexOptions.Singleline);
                                    var match = regex.Match(streamText);

                                    if (match.Success)
                                    {
                                        var content = match.Groups[1].Value;

                                        // Look for image placement (cm transformation matrix)
                                        var cmPattern = @"([0-9.-]+)\s+([0-9.-]+)\s+([0-9.-]+)\s+([0-9.-]+)\s+([0-9.-]+)\s+([0-9.-]+)\s+cm";
                                        var cmMatch = System.Text.RegularExpressions.Regex.Match(content, cmPattern);

                                        if (cmMatch.Success)
                                        {
                                            // Extract transformation matrix values
                                            // [a b c d e f] where e,f are x,y translation and a,d are scale
                                            float scaleX = float.Parse(cmMatch.Groups[1].Value);
                                            float scaleY = float.Parse(cmMatch.Groups[4].Value);
                                            float x = float.Parse(cmMatch.Groups[5].Value);
                                            float y = float.Parse(cmMatch.Groups[6].Value);

                                            // Create bounding box based on transformation
                                            var bbox = new PdfArray(new float[] { x, y, x + Math.Abs(scaleX), y + Math.Abs(scaleY) });
                                            _logger.LogInformation($"[FIGURE-ALT-TEXT] Extracted BBox from transformation matrix: [{x}, {y}, {x + Math.Abs(scaleX)}, {y + Math.Abs(scaleY)}]");
                                            return bbox;
                                        }

                                        // Look for rectangle operations (re operator)
                                        var rePattern = @"([0-9.-]+)\s+([0-9.-]+)\s+([0-9.-]+)\s+([0-9.-]+)\s+re";
                                        var reMatch = System.Text.RegularExpressions.Regex.Match(content, rePattern);

                                        if (reMatch.Success)
                                        {
                                            float x = float.Parse(reMatch.Groups[1].Value);
                                            float y = float.Parse(reMatch.Groups[2].Value);
                                            float width = float.Parse(reMatch.Groups[3].Value);
                                            float height = float.Parse(reMatch.Groups[4].Value);

                                            var bbox = new PdfArray(new float[] { x, y, x + width, y + height });
                                            _logger.LogInformation($"[FIGURE-ALT-TEXT] Extracted BBox from rectangle: [{x}, {y}, {x + width}, {y + height}]");
                                            return bbox;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Try to get image dimensions if this references an image XObject
                var pageDict = figureObj.GetAsDictionary(PdfName.Pg);
                if (pageDict != null)
                {
                    var resources = pageDict.GetAsDictionary(PdfName.Resources);
                    if (resources != null)
                    {
                        var xobjects = resources.GetAsDictionary(PdfName.XObject);
                        if (xobjects != null)
                        {
                            foreach (var entry in xobjects.EntrySet())
                            {
                                var xobj = entry.Value;
                                if (xobj.IsStream())
                                {
                                    var stream = (PdfStream)xobj;
                                    var subtype = stream.GetAsName(PdfName.Subtype);
                                    if (subtype != null && subtype.Equals(PdfName.Image))
                                    {
                                        try
                                        {
                                            var imageXObj = new PdfImageXObject(stream);
                                            var width = imageXObj.GetWidth();
                                            var height = imageXObj.GetHeight();

                                            // Create BBox from image dimensions
                                            // Format: [llx, lly, urx, ury] (lower-left x/y, upper-right x/y)
                                            return new PdfArray(new float[] { 0, 0, width, height });
                                        }
                                        catch
                                        {
                                            continue;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                return await Task.FromResult<PdfArray>(null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FIGURE-ALT-TEXT] Error extracting bounding box: {ex.Message}");
                return await Task.FromResult<PdfArray>(null);
            }
        }

        /// <summary>
        /// Extract image bytes from a Figure element for Claude Vision analysis
        /// First tries direct extraction, then renders the page area if needed
        /// </summary>
        private async Task<byte[]> ExtractImageFromFigure(PdfStructElem figure)
        {
            _logger.LogInformation($"[FIGURE-ALT-TEXT] 🔍 ExtractImageFromFigure called");

            try
            {
                if (_currentPdfDoc == null || _currentPdfBytes == null)
                {
                    _logger.LogWarning($"[FIGURE-ALT-TEXT] No PDF document reference available");
                    return null;
                }

                _logger.LogInformation($"[FIGURE-ALT-TEXT] PDF doc available, bytes: {_currentPdfBytes?.Length ?? 0}");

                // APPROACH 1: Try direct image extraction using Aspose PdfExtractor
                using (var asposeMs = new MemoryStream(_currentPdfBytes))
                using (var asposeDoc = new Aspose.Pdf.Document(asposeMs))
                {
                    var imageExtractor = new Aspose.Pdf.Facades.PdfExtractor();
                    imageExtractor.BindPdf(asposeDoc);

                    // Extract from all pages
                    imageExtractor.StartPage = 1;
                    imageExtractor.EndPage = asposeDoc.Pages.Count;
                    imageExtractor.ExtractImage();

                    if (imageExtractor.HasNextImage())
                    {
                        using var imgMs = new MemoryStream();
                        imageExtractor.GetNextImage(imgMs);
                        var imageBytes = imgMs.ToArray();

                        if (imageBytes.Length > 0)
                        {
                            _logger.LogInformation($"[FIGURE-ALT-TEXT] ✅ Extracted raster image: {imageBytes.Length} bytes");
                            return imageBytes;
                        }
                    }
                }

                // APPROACH 2: Render the page area containing the Figure
                // This handles vector graphics, form XObjects, and other non-raster content
                _logger.LogInformation($"[FIGURE-ALT-TEXT] ⚠️ No raster images found, attempting page rendering instead");

                var figureObj = figure.GetPdfObject();
                var bbox = figureObj?.GetAsArray(PdfName.BBox);

                _logger.LogInformation($"[FIGURE-ALT-TEXT] Figure object: {figureObj != null}, BBox: {bbox != null}");

                // Get the page number for the figure
                int pageNum = 1; // Default to page 1
                var kids = figure.GetKids();
                _logger.LogInformation($"[FIGURE-ALT-TEXT] Kids count: {kids?.Count ?? 0}");

                if (kids != null)
                {
                    foreach (var kid in kids)
                    {
                        _logger.LogInformation($"[FIGURE-ALT-TEXT] Kid type: {kid?.GetType().Name}");

                        if (kid is PdfMcr mcr)
                        {
                            var pageDict = mcr.GetPageObject();
                            _logger.LogInformation($"[FIGURE-ALT-TEXT] MCR found, pageDict: {pageDict != null}");

                            if (pageDict != null && _currentPdfDoc != null)
                            {
                                // Find page number
                                for (int i = 1; i <= _currentPdfDoc.GetNumberOfPages(); i++)
                                {
                                    var page = _currentPdfDoc.GetPage(i);
                                    if (page.GetPdfObject() == pageDict)
                                    {
                                        pageNum = i;
                                        _logger.LogInformation($"[FIGURE-ALT-TEXT] Found figure on page {pageNum}");
                                        break;
                                    }
                                }
                            }
                            break; // Found page reference
                        }
                    }
                }

                _logger.LogInformation($"[FIGURE-ALT-TEXT] Will render page {pageNum}");

                // Render the page using Aspose
                using (var asposeMs = new MemoryStream(_currentPdfBytes))
                using (var asposeDoc = new Aspose.Pdf.Document(asposeMs))
                {
                    if (pageNum > 0 && pageNum <= asposeDoc.Pages.Count)
                    {
                        var page = asposeDoc.Pages[pageNum];

                        // Render full page at high resolution (300 DPI for better quality)
                        var resolution = new Aspose.Pdf.Devices.Resolution(300);
                        var pngDevice = new Aspose.Pdf.Devices.PngDevice(resolution);

                        using var pageImageMs = new MemoryStream();
                        pngDevice.Process(page, pageImageMs);
                        var pageImageBytes = pageImageMs.ToArray();

                        if (pageImageBytes.Length > 0)
                        {
                            // If we have a BBox, crop to just that area
                            if (bbox != null && bbox.Size() == 4)
                            {
                                try
                                {
                                    // BBox format: [llx, lly, urx, ury] in PDF coordinates (72 DPI, bottom-left origin)
                                    var llx = bbox.GetAsNumber(0)?.FloatValue() ?? 0;
                                    var lly = bbox.GetAsNumber(1)?.FloatValue() ?? 0;
                                    var urx = bbox.GetAsNumber(2)?.FloatValue() ?? 0;
                                    var ury = bbox.GetAsNumber(3)?.FloatValue() ?? 0;

                                    // Get page dimensions in PDF coordinates
                                    var pageRect = page.GetPageRect(true);
                                    var pageHeight = pageRect.Height;

                                    // Convert PDF coordinates (bottom-left origin) to image coordinates (top-left origin)
                                    // Scale from 72 DPI (PDF) to 300 DPI (rendered image)
                                    var scale = 300.0f / 72.0f;

                                    var cropX = (int)(llx * scale);
                                    var cropY = (int)((pageHeight - ury) * scale); // Flip Y axis
                                    var cropWidth = (int)((urx - llx) * scale);
                                    var cropHeight = (int)((ury - lly) * scale);

                                    _logger.LogInformation($"[FIGURE-ALT-TEXT] Cropping to BBox: [{llx}, {lly}, {urx}, {ury}] -> [{cropX}, {cropY}, {cropWidth}, {cropHeight}]");

                                    // Crop using SkiaSharp (already in dependencies)
                                    using var inputMs = new MemoryStream(pageImageBytes);
                                    using var inputBitmap = SkiaSharp.SKBitmap.Decode(inputMs);

                                    if (inputBitmap != null)
                                    {
                                        // Ensure crop rect is within bounds
                                        cropX = Math.Max(0, Math.Min(cropX, inputBitmap.Width - 1));
                                        cropY = Math.Max(0, Math.Min(cropY, inputBitmap.Height - 1));
                                        cropWidth = Math.Max(1, Math.Min(cropWidth, inputBitmap.Width - cropX));
                                        cropHeight = Math.Max(1, Math.Min(cropHeight, inputBitmap.Height - cropY));

                                        var cropRect = new SkiaSharp.SKRectI(cropX, cropY, cropX + cropWidth, cropY + cropHeight);
                                        using var croppedBitmap = new SkiaSharp.SKBitmap();

                                        if (inputBitmap.ExtractSubset(croppedBitmap, cropRect))
                                        {
                                            using var outputMs = new MemoryStream();
                                            using var image = SkiaSharp.SKImage.FromBitmap(croppedBitmap);
                                            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
                                            data.SaveTo(outputMs);
                                            var croppedBytes = outputMs.ToArray();

                                            _logger.LogInformation($"[FIGURE-ALT-TEXT] ✅ Rendered and cropped page {pageNum} to BBox: {croppedBytes.Length} bytes");
                                            return croppedBytes;
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning($"[FIGURE-ALT-TEXT] Failed to crop image: {ex.Message}, using full page instead");
                                }
                            }

                            // Fallback: return full page if cropping failed or no BBox
                            _logger.LogInformation($"[FIGURE-ALT-TEXT] ✅ Rendered full page {pageNum} as image: {pageImageBytes.Length} bytes");
                            return pageImageBytes;
                        }
                    }
                }

                _logger.LogWarning($"[FIGURE-ALT-TEXT] Could not extract or render image for Figure");
                return await Task.FromResult<byte[]>(null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FIGURE-ALT-TEXT] Error extracting/rendering image: {ex.Message}");
                return await Task.FromResult<byte[]>(null);
            }
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
