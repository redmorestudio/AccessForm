using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.Pdf.Tagutils;
using Microsoft.Extensions.Logging;

namespace AccessFormServer.Services;

/// <summary>
/// Service for fixing link annotation accessibility issues WITHOUT recreating content
/// Addresses PAC errors by adding Link structure elements and alt text to existing annotations
/// </summary>
public class LinkAnnotationFixService
{
    private readonly ILogger<LinkAnnotationFixService> _logger;

    public LinkAnnotationFixService(ILogger<LinkAnnotationFixService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Fix link annotations in place by adding structure elements and alt text
    /// </summary>
    public async Task<LinkFixResult> FixLinkAnnotationsAsync(byte[] pdfBytes)
    {
        var result = new LinkFixResult();

        try
        {
            _logger.LogInformation("===== LINK ANNOTATION FIX SERVICE (IN-PLACE) =====");

            using var inputMs = new MemoryStream(pdfBytes);
            using var outputMs = new MemoryStream();
            using var pdfDoc = new PdfDocument(new PdfReader(inputMs), new PdfWriter(outputMs));

            // Ensure PDF is tagged
            if (!pdfDoc.IsTagged())
            {
                pdfDoc.SetTagged();
            }

            var tagStructure = pdfDoc.GetTagStructureContext();
            int linksFixed = 0;
            int altTextAdded = 0;

            // Process each page
            for (int pageNum = 1; pageNum <= pdfDoc.GetNumberOfPages(); pageNum++)
            {
                var page = pdfDoc.GetPage(pageNum);
                var annotations = page.GetAnnotations();

                var linkAnnotations = annotations
                    .Where(a => a is PdfLinkAnnotation)
                    .Cast<PdfLinkAnnotation>()
                    .ToList();

                if (linkAnnotations.Any())
                {
                    _logger.LogInformation($"Page {pageNum}: Found {linkAnnotations.Count} link annotations");
                }

                foreach (var linkAnnot in linkAnnotations)
                {
                    try
                    {
                        // Add alt text to annotation if missing
                        var contents = linkAnnot.GetContents();
                        if (contents == null || string.IsNullOrEmpty(contents.GetValue()))
                        {
                            var altText = GenerateAltText(linkAnnot, pageNum, pdfDoc);
                            linkAnnot.SetContents(new PdfString(altText));
                            altTextAdded++;
                            _logger.LogInformation($"  Added alt text: '{altText}'");
                        }

                        // Create Link structure element for this annotation
                        bool created = CreateLinkStructureForAnnotation(
                            tagStructure,
                            linkAnnot,
                            page,
                            pageNum);

                        if (created)
                        {
                            linksFixed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to fix link on page {pageNum}: {ex.Message}");
                        result.Warnings.Add($"Page {pageNum}: {ex.Message}");
                    }
                }
            }

            result.LinksFixed = linksFixed;
            result.AltTextAdded = altTextAdded;

            // Save
            pdfDoc.Close();
            result.FixedPdf = outputMs.ToArray();
            result.Success = true;

            _logger.LogInformation($"===== FIX COMPLETE =====");
            _logger.LogInformation($"Links with structure created: {linksFixed}");
            _logger.LogInformation($"Alt text added: {altTextAdded}");

            if (result.Warnings.Any())
            {
                _logger.LogWarning($"Warnings: {result.Warnings.Count}");
                foreach (var warning in result.Warnings)
                {
                    _logger.LogWarning($"  - {warning}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error fixing link annotations: {ex.Message}");
            _logger.LogError($"Stack trace: {ex.StackTrace}");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.FixedPdf = pdfBytes;
        }

        return result;
    }

    /// <summary>
    /// Fix existing Link structure by adding alt text to OBJR elements
    /// The PDF from Word already has Link → OBJR structure, we just need to add the alt text
    /// </summary>
    private bool CreateLinkStructureForAnnotation(
        TagStructureContext tagStructure,
        PdfLinkAnnotation annotation,
        PdfPage page,
        int pageNum)
    {
        try
        {
            var pdfDoc = page.GetDocument();
            var altText = GenerateAltText(annotation, pageNum, pdfDoc);

            // 1. Add /Contents to the annotation itself
            var contents = annotation.GetContents();
            if (contents == null || string.IsNullOrEmpty(contents.GetValue()))
            {
                annotation.SetContents(new PdfString(altText));
                _logger.LogInformation($"    Set annotation /Contents: '{altText}'");
            }

            // 2. Find the Link structure element that contains an OBJR pointing to this annotation
            var linkElem = FindLinkElementForAnnotation(pdfDoc, annotation, page);
            if (linkElem != null)
            {
                // Add /Alt to the Link element
                var existingAlt = linkElem.GetAlt();
                if (existingAlt == null || string.IsNullOrEmpty(existingAlt.GetValue()))
                {
                    linkElem.Put(PdfName.Alt, new PdfString(altText));
                    _logger.LogInformation($"    Set Link element /Alt: '{altText}'");
                }

                // Find and update the OBJR child
                var kids = linkElem.GetKids();
                if (kids != null)
                {
                    foreach (var kid in kids)
                    {
                        if (kid is PdfStructElem objrElem)
                        {
                            var role = objrElem.GetRole();
                            if (role != null && role.GetValue() == "OBJR")
                            {
                                // Add /Alt to the OBJR element
                                var objrAlt = objrElem.GetAlt();
                                if (objrAlt == null || string.IsNullOrEmpty(objrAlt.GetValue()))
                                {
                                    objrElem.Put(PdfName.Alt, new PdfString(altText));
                                    _logger.LogInformation($"    Set OBJR element /Alt: '{altText}'");
                                }
                            }
                        }
                    }
                }

                _logger.LogInformation($"    ✓ Fixed Link structure on page {pageNum}");
                return true;
            }
            else
            {
                _logger.LogWarning($"    Could not find Link structure element for annotation on page {pageNum}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to fix link structure: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Find the Link structure element that contains an OBJR pointing to this annotation
    /// </summary>
    private PdfStructElem? FindLinkElementForAnnotation(PdfDocument pdfDoc, PdfLinkAnnotation annotation, PdfPage page)
    {
        try
        {
            var structTreeRoot = pdfDoc.GetStructTreeRoot();
            var rootKids = structTreeRoot.GetKids();

            if (rootKids != null)
            {
                foreach (var kid in rootKids)
                {
                    if (kid is PdfStructElem elem)
                    {
                        var result = TraverseForLinkElement(elem, annotation, page);
                        if (result != null)
                        {
                            return result;
                        }
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error finding Link element: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Recursively traverse structure tree to find Link element with OBJR pointing to annotation
    /// </summary>
    private PdfStructElem? TraverseForLinkElement(PdfStructElem elem, PdfLinkAnnotation annotation, PdfPage page)
    {
        try
        {
            var role = elem.GetRole();

            // If this is a Link element, check if it has an OBJR child pointing to our annotation
            if (role != null && role.GetValue() == "Link")
            {
                var kids = elem.GetKids();
                if (kids != null)
                {
                    foreach (var kid in kids)
                    {
                        if (kid is PdfStructElem objrElem)
                        {
                            var objrRole = objrElem.GetRole();
                            if (objrRole != null && objrRole.GetValue() == "OBJR")
                            {
                                // Check if this OBJR points to our annotation
                                var obj = objrElem.GetPdfObject().Get(PdfName.Obj);
                                var pg = objrElem.GetPdfObject().Get(PdfName.Pg);

                                if (obj != null && obj.Equals(annotation.GetPdfObject()) &&
                                    pg != null && pg.Equals(page.GetPdfObject()))
                                {
                                    return elem; // Found it!
                                }
                            }
                        }
                    }
                }
            }

            // Recursively check children
            var children = elem.GetKids();
            if (children != null)
            {
                foreach (var child in children)
                {
                    if (child is PdfStructElem childElem)
                    {
                        var result = TraverseForLinkElement(childElem, annotation, page);
                        if (result != null)
                        {
                            return result;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error traversing structure tree: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Generate alt text for a link annotation
    /// </summary>
    private string GenerateAltText(PdfLinkAnnotation annotation, int pageNum, PdfDocument pdfDoc)
    {
        try
        {
            var action = annotation.GetAction();
            if (action == null)
            {
                return $"Link on page {pageNum}";
            }

            var actionType = action.GetAsName(PdfName.S);
            if (PdfName.GoTo.Equals(actionType))
            {
                var dest = action.Get(PdfName.D);
                if (dest is PdfArray destArray && destArray.Size() > 0)
                {
                    // Try to determine destination page
                    var pageRef = destArray.Get(0);

                    if (pageRef is PdfDictionary pageDict)
                    {
                        for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                        {
                            if (pdfDoc.GetPage(i).GetPdfObject().Equals(pageDict))
                            {
                                return $"Link to page {i}";
                            }
                        }
                    }
                }
                return "Internal link";
            }
            else if (PdfName.URI.Equals(actionType))
            {
                var uri = action.GetAsString(PdfName.URI);
                return $"External link to {uri?.GetValue() ?? "URL"}";
            }

            return $"Link on page {pageNum}";
        }
        catch
        {
            return $"Link on page {pageNum}";
        }
    }
}
