using System;
using System.IO;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Tagging;

class QuickTocDiagnostic
{
    static void Main(string[] args)
    {
        var pdfPath = args.Length > 0 ? args[0]
            : "TWC Forms/eBily/foster-youth-services-guide-twc.pdf";

        Console.WriteLine("===== QUICK TOC DIAGNOSTIC =====\n");

        try
        {
            using var pdfDoc = new PdfDocument(new PdfReader(pdfPath));

            // Check if tagged
            Console.WriteLine($"PDF is tagged: {pdfDoc.IsTagged()}\n");

            // Check page 2 (TOC page) link annotations
            var page2 = pdfDoc.GetPage(2);
            var annotations = page2.GetAnnotations();
            var linkAnnotations = annotations
                .Where(a => a is PdfLinkAnnotation)
                .Cast<PdfLinkAnnotation>()
                .ToList();

            Console.WriteLine($"Page 2 has {linkAnnotations.Count} link annotations\n");

            // Check first few link annotations for structure
            int problemCount = 0;
            for (int i = 0; i < Math.Min(5, linkAnnotations.Count); i++)
            {
                var link = linkAnnotations[i];
                var annotObj = link.GetPdfObject();

                // Check for StructParent
                var structParent = annotObj.GetAsNumber(PdfName.StructParent);

                Console.WriteLine($"Link #{i + 1}:");
                Console.WriteLine($"  Has StructParent: {(structParent != null ? "YES - " + structParent.IntValue() : "NO (PROBLEM!)")}");

                // Check for Contents (alt text)
                var contents = link.GetContents();
                Console.WriteLine($"  Has Contents/Alt: {(contents != null && !string.IsNullOrEmpty(contents.GetValue()) ? "YES" : "NO")}");

                if (structParent == null)
                {
                    Console.WriteLine("  ⚠️ ORPHANED ANNOTATION - Not connected to structure!");
                    problemCount++;
                }
                else
                {
                    // Try to find what it's connected to in the structure tree
                    var structTreeRoot = pdfDoc.GetStructTreeRoot();
                    var parentTree = structTreeRoot.GetPdfObject().GetAsDictionary(PdfName.ParentTree);

                    if (parentTree != null)
                    {
                        var numsArray = parentTree.GetAsArray(PdfName.Nums);
                        if (numsArray != null)
                        {
                            for (int j = 0; j < numsArray.Size(); j += 2)
                            {
                                var indexObj = numsArray.GetAsNumber(j);
                                if (indexObj != null && indexObj.IntValue() == structParent.IntValue())
                                {
                                    var structElemObj = numsArray.Get(j + 1);
                                    if (structElemObj is PdfDictionary dict)
                                    {
                                        var role = dict.GetAsName(PdfName.S);
                                        Console.WriteLine($"  Connected to: {role?.GetValue() ?? "UNKNOWN"}");

                                        if (role != null && role.GetValue() != "Link")
                                        {
                                            Console.WriteLine($"  ⚠️ PROBLEM: Not connected to Link element!");
                                            problemCount++;
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
                Console.WriteLine();
            }

            Console.WriteLine($"\n===== SUMMARY =====");
            Console.WriteLine($"Problems found in first {Math.Min(5, linkAnnotations.Count)} links: {problemCount}");
            Console.WriteLine($"Total links on TOC page: {linkAnnotations.Count}");

            if (problemCount > 0)
            {
                Console.WriteLine("\n⚠️ The PDF has link annotations that are NOT properly nested in Link structure elements.");
                Console.WriteLine("This is what's causing the PAC errors.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}