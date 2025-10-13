using System;
using System.IO;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Tagging;

class ForceTocFix
{
    static void Main(string[] args)
    {
        var inputPath = args.Length > 0 ? args[0]
            : "TWC Forms/eBily/foster-youth-services-guide-twc.pdf";
        var outputPath = args.Length > 1 ? args[1]
            : "TWC Forms/eBily/foster-youth-FORCE-FIXED.pdf";

        Console.WriteLine("===== FORCE TOC FIX =====\n");

        try
        {
            using var inputMs = new MemoryStream(File.ReadAllBytes(inputPath));
            using var outputMs = new MemoryStream();
            using var pdfDoc = new PdfDocument(new PdfReader(inputMs), new PdfWriter(outputMs));

            if (!pdfDoc.IsTagged())
            {
                pdfDoc.SetTagged();
            }

            var structTreeRoot = pdfDoc.GetStructTreeRoot();
            var parentTreeObj = structTreeRoot.GetPdfObject().GetAsDictionary(PdfName.ParentTree);

            if (parentTreeObj == null)
            {
                Console.WriteLine("No parent tree - creating one");
                parentTreeObj = new PdfDictionary();
                parentTreeObj.Put(PdfName.Nums, new PdfArray());
                structTreeRoot.GetPdfObject().Put(PdfName.ParentTree, parentTreeObj);
            }

            int fixedCount = 0;

            // Process page 2 (TOC page)
            var page2 = pdfDoc.GetPage(2);
            var annotations = page2.GetAnnotations();
            var linkAnnotations = annotations
                .Where(a => a is PdfLinkAnnotation)
                .Cast<PdfLinkAnnotation>()
                .ToList();

            Console.WriteLine($"Found {linkAnnotations.Count} link annotations on page 2\n");

            foreach (var linkAnnot in linkAnnotations)
            {
                var annotObj = linkAnnot.GetPdfObject();
                var structParent = annotObj.GetAsNumber(PdfName.StructParent);

                if (structParent != null)
                {
                    var structElem = FindStructureElementInParentTree(parentTreeObj, structParent.IntValue());

                    if (structElem != null)
                    {
                        var role = structElem.GetRole()?.GetValue();
                        Console.WriteLine($"Link {structParent.IntValue()} connected to: {role}");

                        // FORCE FIX: If it's Reference, wrap it in Link regardless of parent
                        if (role == "Reference")
                        {
                            Console.WriteLine("  -> FORCING Link wrapper creation");

                            try
                            {
                                // Create Link element
                                var linkDict = new PdfDictionary();
                                linkDict.Put(PdfName.Type, PdfName.StructElem);
                                linkDict.Put(PdfName.S, PdfName.Link);

                                var linkElem = new PdfStructElem(linkDict);
                                linkElem.MakeIndirect(pdfDoc);

                                // Add alt text
                                var altText = $"Go to page {GetDestinationPage(linkAnnot, pdfDoc)}";
                                linkElem.Put(PdfName.Alt, new PdfString(altText));

                                // Create OBJR element
                                var objrDict = new PdfDictionary();
                                objrDict.Put(PdfName.Type, PdfName.OBJR);
                                objrDict.Put(PdfName.Obj, annotObj.GetIndirectReference());
                                objrDict.Put(PdfName.P, linkElem.GetPdfObject());

                                var objrElem = new PdfStructElem(objrDict);
                                objrElem.MakeIndirect(pdfDoc);

                                // Add OBJR to Link
                                linkElem.AddKid(objrElem);

                                // Move Reference under Link
                                structElem.Put(PdfName.P, linkElem.GetPdfObject());
                                linkElem.AddKid(structElem);

                                // Update parent tree to point to Link instead of Reference
                                UpdateParentTreeEntry(parentTreeObj, structParent.IntValue(), linkElem);

                                // Add alt text to annotation itself
                                linkAnnot.SetContents(new PdfString(altText));

                                fixedCount++;
                                Console.WriteLine($"  -> FIXED! Added Link wrapper with alt text: '{altText}'");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"  -> ERROR: {ex.Message}");
                            }
                        }
                    }
                }
            }

            pdfDoc.Close();
            File.WriteAllBytes(outputPath, outputMs.ToArray());

            Console.WriteLine($"\n===== COMPLETE =====");
            Console.WriteLine($"Fixed {fixedCount} of {linkAnnotations.Count} links");
            Console.WriteLine($"Saved to: {outputPath}");

            // Verify the fix
            Console.WriteLine("\n===== VERIFYING FIX =====");
            VerifyFix(outputPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    static void VerifyFix(string pdfPath)
    {
        using var pdfDoc = new PdfDocument(new PdfReader(pdfPath));
        var page2 = pdfDoc.GetPage(2);
        var annotations = page2.GetAnnotations();
        var linkAnnotations = annotations
            .Where(a => a is PdfLinkAnnotation)
            .Cast<PdfLinkAnnotation>()
            .ToList();

        int problemCount = 0;
        var structTreeRoot = pdfDoc.GetStructTreeRoot();
        var parentTreeObj = structTreeRoot.GetPdfObject().GetAsDictionary(PdfName.ParentTree);

        foreach (var linkAnnot in linkAnnotations)
        {
            var annotObj = linkAnnot.GetPdfObject();
            var structParent = annotObj.GetAsNumber(PdfName.StructParent);

            if (structParent != null && parentTreeObj != null)
            {
                var structElem = FindStructureElementInParentTree(parentTreeObj, structParent.IntValue());
                if (structElem != null)
                {
                    var role = structElem.GetRole()?.GetValue();
                    if (role != "Link")
                    {
                        problemCount++;
                    }
                }
            }
        }

        if (problemCount == 0)
        {
            Console.WriteLine("✅ ALL LINKS ARE NOW PROPERLY NESTED IN LINK ELEMENTS!");
        }
        else
        {
            Console.WriteLine($"⚠️ Still have {problemCount} links not in Link elements");
        }
    }

    static PdfStructElem? FindStructureElementInParentTree(PdfDictionary parentTreeDict, int structParentIndex)
    {
        try
        {
            var numsArray = parentTreeDict.GetAsArray(PdfName.Nums);
            if (numsArray == null) return null;

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

    static void UpdateParentTreeEntry(PdfDictionary parentTreeDict, int structParentIndex, PdfStructElem newElem)
    {
        try
        {
            var numsArray = parentTreeDict.GetAsArray(PdfName.Nums);
            if (numsArray == null) return;

            for (int i = 0; i < numsArray.Size(); i += 2)
            {
                var indexObj = numsArray.GetAsNumber(i);
                if (indexObj != null && indexObj.IntValue() == structParentIndex)
                {
                    numsArray.Set(i + 1, newElem.GetPdfObject());
                    break;
                }
            }
        }
        catch { }
    }

    static int GetDestinationPage(PdfLinkAnnotation annotation, PdfDocument pdfDoc)
    {
        try
        {
            var action = annotation.GetAction();
            if (action != null)
            {
                var actionType = action.GetAsName(PdfName.S);
                if (PdfName.GoTo.Equals(actionType))
                {
                    var dest = action.Get(PdfName.D);
                    if (dest is PdfArray destArray && destArray.Size() > 0)
                    {
                        var pageRef = destArray.Get(0);
                        if (pageRef is PdfIndirectReference indirectRef)
                        {
                            for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                            {
                                var pageObj = pdfDoc.GetPage(i).GetPdfObject();
                                if (pageObj.GetIndirectReference() != null &&
                                    pageObj.GetIndirectReference().Equals(indirectRef))
                                {
                                    return i;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }
        return 0;
    }
}