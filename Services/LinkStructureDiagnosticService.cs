using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.Pdf.Tagutils;
using Microsoft.Extensions.Logging;

namespace AccessFormServer.Services;

/// <summary>
/// Diagnostic service to examine link annotation structure connections
/// </summary>
public class LinkStructureDiagnosticService
{
    private readonly ILogger<LinkStructureDiagnosticService> _logger;

    public LinkStructureDiagnosticService(ILogger<LinkStructureDiagnosticService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Diagnose link annotation structure issues
    /// </summary>
    public async Task<DiagnosticResult> DiagnoseLinkStructureAsync(byte[] pdfBytes)
    {
        var result = new DiagnosticResult();

        try
        {
            _logger.LogInformation("===== LINK STRUCTURE DIAGNOSTIC =====");

            using var inputMs = new MemoryStream(pdfBytes);
            using var pdfDoc = new PdfDocument(new PdfReader(inputMs));

            _logger.LogInformation($"Document is tagged: {pdfDoc.IsTagged()}");

            if (!pdfDoc.IsTagged())
            {
                result.Issues.Add("Document is not tagged");
                return result;
            }

            var tagStructure = pdfDoc.GetTagStructureContext();
            int totalLinks = 0;
            int linksWithStructParent = 0;
            int linksWithoutStructParent = 0;
            int linksWithContents = 0;
            int linksWithoutContents = 0;

            // Examine each page
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
                    _logger.LogInformation($"\n=== PAGE {pageNum} ===");
                    _logger.LogInformation($"Found {linkAnnotations.Count} link annotations");

                    foreach (var linkAnnot in linkAnnotations)
                    {
                        totalLinks++;
                        var annotObj = linkAnnot.GetPdfObject();

                        // Check for StructParent
                        var structParent = annotObj.GetAsNumber(PdfName.StructParent);
                        if (structParent != null)
                        {
                            linksWithStructParent++;
                            _logger.LogInformation($"  Link {totalLinks}: HAS StructParent = {structParent.IntValue()}");

                            // Try to find corresponding structure element
                            var structElem = FindStructureElementByStructParent(pdfDoc, structParent.IntValue());
                            if (structElem != null)
                            {
                                var role = structElem.GetRole();
                                _logger.LogInformation($"    → Linked to structure element with role: {role}");

                                // Check if it's a Link element
                                if (role != null && role.GetValue() == "Link")
                                {
                                    _logger.LogInformation($"    ✓ Correctly linked to Link element");
                                }
                                else
                                {
                                    _logger.LogWarning($"    ✗ Linked to {role?.GetValue() ?? "unknown"} element (expected Link)");
                                    result.Issues.Add($"Page {pageNum}: Annotation linked to {role?.GetValue() ?? "unknown"} instead of Link");
                                }
                            }
                            else
                            {
                                _logger.LogWarning($"    ✗ StructParent {structParent.IntValue()} not found in parent tree");
                                result.Issues.Add($"Page {pageNum}: StructParent {structParent.IntValue()} has no corresponding structure element");
                            }
                        }
                        else
                        {
                            linksWithoutStructParent++;
                            _logger.LogWarning($"  Link {totalLinks}: NO StructParent");
                            result.Issues.Add($"Page {pageNum}: Link annotation missing StructParent");
                        }

                        // Check for Contents (alt text)
                        var contents = linkAnnot.GetContents();
                        if (contents != null && !string.IsNullOrEmpty(contents.GetValue()))
                        {
                            linksWithContents++;
                            _logger.LogInformation($"    Contents (alt text): '{contents.GetValue()}'");
                        }
                        else
                        {
                            linksWithoutContents++;
                            _logger.LogWarning($"    NO Contents (alt text)");
                            result.Issues.Add($"Page {pageNum}: Link annotation missing alt text");
                        }
                    }
                }
            }

            // Summary
            _logger.LogInformation("\n===== SUMMARY =====");
            _logger.LogInformation($"Total link annotations: {totalLinks}");
            _logger.LogInformation($"Links WITH StructParent: {linksWithStructParent}");
            _logger.LogInformation($"Links WITHOUT StructParent: {linksWithoutStructParent}");
            _logger.LogInformation($"Links WITH alt text: {linksWithContents}");
            _logger.LogInformation($"Links WITHOUT alt text: {linksWithoutContents}");
            _logger.LogInformation($"Total issues found: {result.Issues.Count}");

            result.TotalLinks = totalLinks;
            result.LinksWithStructParent = linksWithStructParent;
            result.LinksWithoutStructParent = linksWithoutStructParent;
            result.LinksWithContents = linksWithContents;
            result.LinksWithoutContents = linksWithoutContents;

            // Examine structure tree for Link elements
            _logger.LogInformation("\n=== STRUCTURE TREE ANALYSIS ===");
            var linkElements = FindAllLinkElements(pdfDoc);
            _logger.LogInformation($"Found {linkElements.Count} Link structure elements");

            foreach (var linkElem in linkElements)
            {
                var hasObjr = HasObjrChild(linkElem);
                var altText = linkElem.GetAlt();
                _logger.LogInformation($"  Link element:");
                _logger.LogInformation($"    Has OBJR child: {hasObjr}");
                _logger.LogInformation($"    Alt text: {(altText != null ? $"'{altText.GetValue()}'" : "NONE")}");

                if (!hasObjr)
                {
                    result.Issues.Add("Link structure element missing OBJR child");
                }
            }

            result.LinkStructureElements = linkElements.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error during diagnostic: {ex.Message}");
            _logger.LogError($"Stack trace: {ex.StackTrace}");
            result.Issues.Add($"Diagnostic error: {ex.Message}");
        }

        return result;
    }

    private PdfStructElem? FindStructureElementByStructParent(PdfDocument pdfDoc, int structParentIndex)
    {
        try
        {
            var structTreeRoot = pdfDoc.GetStructTreeRoot();
            var parentTreeDict = structTreeRoot.GetPdfObject().GetAsDictionary(PdfName.ParentTree);

            if (parentTreeDict == null)
            {
                return null;
            }

            var numsArray = parentTreeDict.GetAsArray(PdfName.Nums);
            if (numsArray == null)
            {
                return null;
            }

            // Search for structParentIndex in the nums array
            for (int i = 0; i < numsArray.Size(); i += 2)
            {
                var indexObj = numsArray.GetAsNumber(i);
                if (indexObj != null && indexObj.IntValue() == structParentIndex)
                {
                    var structElemObj = numsArray.Get(i + 1);
                    if (structElemObj is PdfDictionary dict)
                    {
                        return new PdfStructElem(dict);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error finding structure element: {ex.Message}");
        }

        return null;
    }

    private List<PdfStructElem> FindAllLinkElements(PdfDocument pdfDoc)
    {
        var linkElements = new List<PdfStructElem>();

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
                        TraverseStructureTree(elem, linkElements);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error traversing structure tree: {ex.Message}");
        }

        return linkElements;
    }

    private void TraverseStructureTree(PdfStructElem elem, List<PdfStructElem> linkElements)
    {
        try
        {
            var role = elem.GetRole();
            if (role != null && role.GetValue() == "Link")
            {
                linkElements.Add(elem);
            }

            // Traverse children
            var kids = elem.GetKids();
            if (kids != null)
            {
                foreach (var kid in kids)
                {
                    if (kid is PdfStructElem childElem)
                    {
                        TraverseStructureTree(childElem, linkElements);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error traversing element: {ex.Message}");
        }
    }

    private bool HasObjrChild(PdfStructElem elem)
    {
        try
        {
            var kids = elem.GetKids();
            if (kids != null)
            {
                foreach (var kid in kids)
                {
                    if (kid is PdfStructElem childElem)
                    {
                        var role = childElem.GetRole();
                        if (role != null && role.GetValue() == "OBJR")
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error checking OBJR child: {ex.Message}");
        }

        return false;
    }
}

public class DiagnosticResult
{
    public int TotalLinks { get; set; }
    public int LinksWithStructParent { get; set; }
    public int LinksWithoutStructParent { get; set; }
    public int LinksWithContents { get; set; }
    public int LinksWithoutContents { get; set; }
    public int LinkStructureElements { get; set; }
    public List<string> Issues { get; set; } = new();
}
