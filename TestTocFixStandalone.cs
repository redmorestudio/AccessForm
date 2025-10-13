using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Tagging;
using Microsoft.Extensions.Logging;

class TestTocFixStandalone
{
    static async Task Main()
    {
        // Read original PDF
        var inputPath = "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/TWC Forms/eBily/ORIGINAL-foster-youth-services-guide-twc.pdf";

        Console.WriteLine($"Reading PDF from: {inputPath}");
        var pdfBytes = await File.ReadAllBytesAsync(inputPath);

        // Analyze TOC structure
        Console.WriteLine("\n===== ANALYZING TOC STRUCTURE =====");
        AnalyzeTocStructure(pdfBytes);
    }

    static void AnalyzeTocStructure(byte[] pdfBytes)
    {
        try
        {
            using var inputMs = new MemoryStream(pdfBytes);
            using var pdfDoc = new PdfDocument(new PdfReader(inputMs));

            if (!pdfDoc.IsTagged())
            {
                Console.WriteLine("ERROR: Document is not tagged");
                return;
            }

            var structTreeRoot = pdfDoc.GetStructTreeRoot();

            // First, find ALL TOCI elements
            Console.WriteLine("\n=== SEARCHING FOR TOCI ELEMENTS ===");
            var tociElements = new List<PdfStructElem>();

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

            Console.WriteLine($"Found {tociElements.Count} TOCI elements");

            // Look for Reference elements
            Console.WriteLine("\n=== SEARCHING FOR REFERENCE ELEMENTS ===");
            int referenceCount = 0;
            int referenceInToci = 0;

            foreach (var toci in tociElements)
            {
                var kids = toci.GetKids();
                if (kids != null)
                {
                    foreach (var kid in kids)
                    {
                        if (kid is PdfStructElem childElem)
                        {
                            var role = childElem.GetRole()?.GetValue();
                            if (role == "Reference")
                            {
                                referenceInToci++;
                                Console.WriteLine($"  Found Reference element in TOCI");

                                // Check if it has Link parent/sibling
                                var parent = GetParentElement(childElem, structTreeRoot);
                                if (parent != null)
                                {
                                    Console.WriteLine($"    Parent role: {parent.GetRole()?.GetValue()}");
                                }

                                // Check children
                                var refKids = childElem.GetKids();
                                if (refKids != null)
                                {
                                    Console.WriteLine($"    Has {refKids.Count} children");
                                }
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"\nTotal Reference elements in TOCI: {referenceInToci}");

            // Check page 2 annotations
            Console.WriteLine("\n=== PAGE 2 LINK ANNOTATIONS ===");
            var page2 = pdfDoc.GetPage(2);
            var annotations = page2.GetAnnotations();
            var linkAnnotations = annotations
                .Where(a => a is PdfLinkAnnotation)
                .Cast<PdfLinkAnnotation>()
                .ToList();

            Console.WriteLine($"Found {linkAnnotations.Count} link annotations on page 2");

            // Check parent tree
            var parentTreeObj = structTreeRoot.GetPdfObject().GetAsDictionary(PdfName.ParentTree);
            if (parentTreeObj != null)
            {
                var numsArray = parentTreeObj.GetAsArray(PdfName.Nums);
                if (numsArray != null)
                {
                    Console.WriteLine($"Parent tree has {numsArray.Size()/2} entries");

                    // Check first few entries
                    for (int i = 0; i < Math.Min(6, numsArray.Size()); i += 2)
                    {
                        var index = numsArray.GetAsNumber(i);
                        var elem = numsArray.Get(i + 1);
                        if (elem is PdfDictionary dict)
                        {
                            var elemStruct = new PdfStructElem(dict);
                            Console.WriteLine($"  StructParent {index?.IntValue()}: {elemStruct.GetRole()?.GetValue()}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");
        }
    }

    static void FindAllTociElements(PdfStructElem elem, List<PdfStructElem> tociElements)
    {
        var role = elem.GetRole()?.GetValue();

        if (role == "TOCI" || role == "TOC")
        {
            var parentRole = GetParentRole(elem);
            if (parentRole == "TOC" && role == "TOC")
            {
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

    static string GetParentRole(PdfStructElem elem)
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

    static PdfStructElem? GetParentElement(PdfStructElem elem, PdfStructTreeRoot root)
    {
        try
        {
            var parentObj = elem.GetPdfObject().Get(PdfName.P);
            if (parentObj is PdfDictionary parentDict)
            {
                if (parentDict.Equals(root.GetPdfObject()))
                {
                    return null;
                }
                return new PdfStructElem(parentDict);
            }
        }
        catch { }
        return null;
    }
}