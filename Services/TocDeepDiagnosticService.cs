using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.Extensions.Logging;
using System.Text;

namespace AccessFormServer.Services;

/// <summary>
/// Deep diagnostic service to understand the exact TOC structure from Word
/// </summary>
public class TocDeepDiagnosticService
{
    private readonly ILogger<TocDeepDiagnosticService> _logger;

    public TocDeepDiagnosticService(ILogger<TocDeepDiagnosticService> logger)
    {
        _logger = logger;
    }

    public async Task<string> AnalyzeTocStructureAsync(byte[] pdfBytes)
    {
        var report = new StringBuilder();
        report.AppendLine("===== DEEP TOC STRUCTURE ANALYSIS =====\n");

        try
        {
            using var inputMs = new MemoryStream(pdfBytes);
            using var pdfDoc = new PdfDocument(new PdfReader(inputMs));

            if (!pdfDoc.IsTagged())
            {
                report.AppendLine("ERROR: Document is not tagged");
                return report.ToString();
            }

            var structTreeRoot = pdfDoc.GetStructTreeRoot();

            // First, find ALL TOCI elements
            report.AppendLine("=== SEARCHING FOR TOCI ELEMENTS ===");
            var tociElements = new List<PdfStructElem>();

            // Process root's children
            var rootKids = structTreeRoot.GetKids();
            if (rootKids != null)
            {
                foreach (var kid in rootKids)
                {
                    if (kid is PdfStructElem elem)
                    {
                        FindAllTociElements(elem, tociElements);
                    }
                }
            }

            report.AppendLine($"Found {tociElements.Count} TOCI elements\n");

            // Analyze first few TOCI elements in detail
            int tociCount = 0;
            foreach (var toci in tociElements.Take(3))
            {
                tociCount++;
                report.AppendLine($"=== TOCI #{tociCount} DETAILED STRUCTURE ===");
                AnalyzeTociElement(toci, report, pdfDoc);
                report.AppendLine();
            }

            // Now look for link annotations on page 2 (ToC page)
            report.AppendLine("=== PAGE 2 LINK ANNOTATIONS ===");
            var page2 = pdfDoc.GetPage(2);
            var annotations = page2.GetAnnotations();
            var linkAnnotations = annotations
                .Where(a => a is PdfLinkAnnotation)
                .Cast<PdfLinkAnnotation>()
                .ToList();

            report.AppendLine($"Found {linkAnnotations.Count} link annotations on page 2");

            // Check first few annotations
            for (int i = 0; i < Math.Min(3, linkAnnotations.Count); i++)
            {
                var link = linkAnnotations[i];
                report.AppendLine($"\nLink Annotation #{i + 1}:");

                // Check StructParent
                var structParent = link.GetPdfObject().GetAsNumber(PdfName.StructParent);
                report.AppendLine($"  StructParent: {(structParent != null ? structParent.IntValue().ToString() : "NONE")}");

                // Check Contents (alt text)
                var contents = link.GetContents();
                report.AppendLine($"  Contents: {(contents != null ? $"'{contents.GetValue()}'" : "NONE")}");

                // Check destination
                var action = link.GetAction();
                if (action != null)
                {
                    var actionType = action.GetAsName(PdfName.S);
                    report.AppendLine($"  Action Type: {actionType}");

                    if (PdfName.GoTo.Equals(actionType))
                    {
                        var dest = action.Get(PdfName.D);
                        report.AppendLine($"  Destination: {dest}");
                    }
                }

                // Try to find corresponding structure element
                if (structParent != null)
                {
                    var structElem = FindStructureElementByParentTree(pdfDoc, structParent.IntValue());
                    if (structElem != null)
                    {
                        report.AppendLine($"  → Connected to structure element: {structElem.GetRole()}");
                    }
                    else
                    {
                        report.AppendLine($"  → NO structure element found for StructParent {structParent.IntValue()}");
                    }
                }
            }

            // Extract actual text from TOCI elements
            report.AppendLine("\n=== EXTRACTING TEXT FROM TOCI ELEMENTS ===");
            foreach (var toci in tociElements.Take(3))
            {
                var text = ExtractTextFromStructElement(toci, pdfDoc);
                report.AppendLine($"TOCI text: '{text}'");
            }
        }
        catch (Exception ex)
        {
            report.AppendLine($"ERROR: {ex.Message}");
            report.AppendLine($"Stack: {ex.StackTrace}");
        }

        var result = report.ToString();
        _logger.LogInformation(result);
        return result;
    }

    private void FindAllTociElements(PdfStructElem elem, List<PdfStructElem> tociElements)
    {
        var role = elem.GetRole()?.GetValue();

        if (role == "TOCI" || role == "TOC")
        {
            // Check if this is a nested TOC that should be TOCI
            var parentRole = GetParentRole(elem);
            if (parentRole == "TOC" && role == "TOC")
            {
                // This is a nested TOC, treat as TOCI
                tociElements.Add(elem);
            }
            else if (role == "TOCI")
            {
                tociElements.Add(elem);
            }
        }

        var kids = elem.GetKids();
        if (kids != null)
        {
            foreach (var kid in kids)
            {
                if (kid is PdfStructElem childElem)
                {
                    FindAllTociElements(childElem, tociElements);
                }
            }
        }
    }

    private string GetParentRole(PdfStructElem elem)
    {
        try
        {
            var parentObj = elem.GetPdfObject().Get(PdfName.P);
            if (parentObj is PdfDictionary parentDict)
            {
                var roleObj = parentDict.Get(PdfName.S);
                if (roleObj is PdfName roleName)
                {
                    return roleName.GetValue();
                }
            }
        }
        catch { }
        return "";
    }

    private void AnalyzeTociElement(PdfStructElem toci, StringBuilder report, PdfDocument pdfDoc)
    {
        var kids = toci.GetKids();
        if (kids == null)
        {
            report.AppendLine("  No children");
            return;
        }

        report.AppendLine($"  Children count: {kids.Count}");

        // Analyze each child in detail
        for (int i = 0; i < kids.Count; i++)
        {
            report.AppendLine($"  Child [{i}]:");

            if (kids[i] is PdfStructElem childElem)
            {
                AnalyzeStructElement(childElem, report, "    ", pdfDoc, 2);
            }
            else if (kids[i] is PdfMcr mcr)
            {
                report.AppendLine($"    Type: MCR (Marked Content Reference)");
                report.AppendLine($"    MCID: {mcr.GetMcid()}");
                report.AppendLine($"    Page: {mcr.GetPageObject()}");
            }
            else
            {
                report.AppendLine($"    Type: {kids[i].GetType().Name}");
            }
        }
    }

    private void AnalyzeStructElement(PdfStructElem elem, StringBuilder report, string indent, PdfDocument pdfDoc, int maxDepth)
    {
        if (maxDepth <= 0) return;

        var role = elem.GetRole()?.GetValue() ?? "null";
        report.AppendLine($"{indent}Role: {role}");

        // Check for Alt text
        var alt = elem.GetAlt();
        if (alt != null)
        {
            report.AppendLine($"{indent}Alt: '{alt.GetValue()}'");
        }

        // Check for ActualText
        var actualText = elem.GetActualText();
        if (actualText != null)
        {
            report.AppendLine($"{indent}ActualText: '{actualText.GetValue()}'");
        }

        // Check for OBJR reference
        if (role == "OBJR" || role == "Link-OBJR")
        {
            var obj = elem.GetPdfObject().Get(PdfName.Obj);
            if (obj != null)
            {
                report.AppendLine($"{indent}OBJR references: {obj}");

                // Try to resolve the reference
                if (obj is PdfIndirectReference indRef)
                {
                    var refObj = indRef.GetRefersTo();
                    if (refObj is PdfDictionary refDict)
                    {
                        var type = refDict.GetAsName(PdfName.Type);
                        var subtype = refDict.GetAsName(PdfName.Subtype);
                        report.AppendLine($"{indent}  → Type: {type}, Subtype: {subtype}");

                        if (subtype != null && subtype.Equals(PdfName.Link))
                        {
                            // This is a link annotation
                            var contents = refDict.GetAsString(PdfName.Contents);
                            report.AppendLine($"{indent}  → Link Contents: {(contents != null ? contents.GetValue() : "NONE")}");
                        }
                    }
                }
            }
            else
            {
                report.AppendLine($"{indent}OBJR has NO object reference!");
            }
        }

        // Check children
        var kids = elem.GetKids();
        if (kids != null && kids.Count > 0)
        {
            report.AppendLine($"{indent}Children: {kids.Count}");
            foreach (var kid in kids)
            {
                if (kid is PdfStructElem childElem)
                {
                    AnalyzeStructElement(childElem, report, indent + "  ", pdfDoc, maxDepth - 1);
                }
                else if (kid is PdfMcr mcr)
                {
                    report.AppendLine($"{indent}  MCR: MCID={mcr.GetMcid()}");
                }
            }
        }
    }

    private PdfStructElem? FindStructureElementByParentTree(PdfDocument pdfDoc, int structParentIndex)
    {
        try
        {
            var structTreeRoot = pdfDoc.GetStructTreeRoot();

            // Get the parent tree from the structure tree root's PdfObject
            var parentTreeDict = structTreeRoot.GetPdfObject().GetAsDictionary(PdfName.ParentTree);
            if (parentTreeDict == null) return null;

            // Get the Nums array from the parent tree
            var numsArray = parentTreeDict.GetAsArray(PdfName.Nums);
            if (numsArray == null) return null;

            // Search for the struct parent index
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
        catch { }

        return null;
    }

    private string ExtractTextFromStructElement(PdfStructElem elem, PdfDocument pdfDoc)
    {
        var text = new StringBuilder();

        // Try Alt text first
        var alt = elem.GetAlt();
        if (alt != null && !string.IsNullOrEmpty(alt.GetValue()))
        {
            return alt.GetValue();
        }

        // Try ActualText
        var actualText = elem.GetActualText();
        if (actualText != null && !string.IsNullOrEmpty(actualText.GetValue()))
        {
            return actualText.GetValue();
        }

        // Try to extract from MCR
        var kids = elem.GetKids();
        if (kids != null)
        {
            foreach (var kid in kids)
            {
                if (kid is PdfMcr mcr)
                {
                    try
                    {
                        var pageNum = pdfDoc.GetPageNumber(mcr.GetPageObject());
                        var page = pdfDoc.GetPage(pageNum);

                        // This is simplified - would need proper text extraction
                        var strategy = new SimpleTextExtractionStrategy();
                        var pageText = PdfTextExtractor.GetTextFromPage(page, strategy);

                        // In reality, we'd need to extract text specifically from the MCID
                        text.Append($"[Page {pageNum} content]");
                    }
                    catch { }
                }
                else if (kid is PdfStructElem childElem)
                {
                    text.Append(ExtractTextFromStructElement(childElem, pdfDoc));
                }
            }
        }

        return text.ToString();
    }
}