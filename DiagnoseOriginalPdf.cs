using System;
using System.IO;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Tagging;

class DiagnoseOriginalPdf
{
    static void Main()
    {
        var pdfPath = "TWC Forms/eBily/foster-youth-services-guide-twc.pdf";

        Console.WriteLine("===== ORIGINAL PDF STRUCTURE ANALYSIS =====");

        using (var reader = new PdfReader(pdfPath))
        using (var pdfDoc = new PdfDocument(reader))
        {
            Console.WriteLine($"Document is tagged: {pdfDoc.IsTagged()}");
            Console.WriteLine($"Total pages: {pdfDoc.GetNumberOfPages()}");

            // Analyze page 2 (ToC page)
            var page2 = pdfDoc.GetPage(2);
            var annotations = page2.GetAnnotations();
            var linkAnnotations = annotations
                .Where(a => a is PdfLinkAnnotation)
                .Cast<PdfLinkAnnotation>()
                .ToList();

            Console.WriteLine($"\nPage 2 (ToC) has {linkAnnotations.Count} link annotations");

            // Analyze structure tree
            if (pdfDoc.IsTagged())
            {
                var structTreeRoot = pdfDoc.GetStructTreeRoot();
                Console.WriteLine("\n=== STRUCTURE TREE ROOT ===");
                AnalyzeStructure(structTreeRoot.GetKids(), "", 0, 3);
            }

            // Check for StructParent on annotations
            Console.WriteLine("\n=== ANNOTATION STRUCTPARENT CHECK ===");
            foreach (var link in linkAnnotations.Take(3))
            {
                var structParent = link.GetPdfObject().GetAsNumber(PdfName.StructParent);
                Console.WriteLine($"Link has StructParent: {structParent != null} (value: {structParent?.IntValue()})");
            }
        }
    }

    static void AnalyzeStructure(IList<IStructureNode> kids, string indent, int currentDepth, int maxDepth)
    {
        if (kids == null || currentDepth >= maxDepth) return;

        foreach (var kid in kids)
        {
            if (kid is PdfStructElem elem)
            {
                var role = elem.GetRole()?.GetValue() ?? "null";
                var alt = elem.GetAlt()?.GetValue() ?? "";

                Console.Write($"{indent}{role}");
                if (!string.IsNullOrEmpty(alt))
                {
                    Console.Write($" [Alt: {alt.Substring(0, Math.Min(30, alt.Length))}...]");
                }

                // Check for OBJR children
                var childKids = elem.GetKids();
                if (childKids != null)
                {
                    var objrCount = childKids.Count(k => k is PdfStructElem e && e.GetRole()?.GetValue() == "OBJR");
                    if (objrCount > 0)
                    {
                        Console.Write($" ({objrCount} OBJR children)");
                    }
                }

                Console.WriteLine();

                AnalyzeStructure(elem.GetKids(), indent + "  ", currentDepth + 1, maxDepth);
            }
        }
    }
}