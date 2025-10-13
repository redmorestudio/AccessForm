using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Tagging;
using Microsoft.Extensions.Logging;

namespace AccessFormServer.Services;

/// <summary>
/// Service to fix TOC link structure by creating proper Link elements for annotations
/// </summary>
public class TocLinkFixService
{
    private readonly ILogger<TocLinkFixService> _logger;

    public TocLinkFixService(ILogger<TocLinkFixService> logger)
    {
        _logger = logger;
    }

    public async Task<RemediationResult> FixTocLinksAsync(byte[] pdfBytes)
    {
        var result = new RemediationResult();

        try
        {
            _logger.LogInformation("===== TOC LINK FIX SERVICE =====");

            using var inputMs = new MemoryStream(pdfBytes);
            using var outputMs = new MemoryStream();
            using var pdfDoc = new PdfDocument(new PdfReader(inputMs), new PdfWriter(outputMs));

            if (!pdfDoc.IsTagged())
            {
                pdfDoc.SetTagged();
            }

            var structTreeRoot = pdfDoc.GetStructTreeRoot();
            int fixedTocStructure = 0;
            int fixedLinks = 0;
            int addedAltText = 0;

            // First pass: Fix nested TOC elements to TOCI
            var rootKids = structTreeRoot.GetKids();
            if (rootKids != null)
            {
                foreach (var kid in rootKids)
                {
                    if (kid is PdfStructElem elem)
                    {
                        FixNestedTocElements(elem, ref fixedTocStructure);
                    }
                }
            }

            // Second pass: Fix link structure for all annotations
            FixLinkAnnotationStructure(pdfDoc, ref fixedLinks, ref addedAltText);

            // Third pass: Ensure all TOCI elements have proper structure
            if (rootKids != null)
            {
                foreach (var kid in rootKids)
                {
                    if (kid is PdfStructElem elem)
                    {
                        FixTociStructure(elem, pdfDoc, ref fixedLinks);
                    }
                }
            }

            pdfDoc.Close();
            result.FixedPdf = outputMs.ToArray();
            result.Success = true;
            result.FixedTocElements = fixedTocStructure;
            result.FixedLinks = fixedLinks;

            _logger.LogInformation($"===== REMEDIATION COMPLETE =====");
            _logger.LogInformation($"Fixed TOC structure elements: {fixedTocStructure}");
            _logger.LogInformation($"Fixed link elements: {fixedLinks}");
            _logger.LogInformation($"Added alt text: {addedAltText}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error fixing TOC links: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.FixedPdf = pdfBytes;
        }

        return result;
    }

    /// <summary>
    /// Fix nested TOC elements by converting them to TOCI
    /// </summary>
    private void FixNestedTocElements(PdfStructElem elem, ref int fixedCount)
    {
        var role = elem.GetRole()?.GetValue();

        if (role == "TOC")
        {
            var kids = elem.GetKids();
            if (kids != null)
            {
                foreach (var kid in kids)
                {
                    if (kid is PdfStructElem childElem)
                    {
                        var childRole = childElem.GetRole()?.GetValue();
                        if (childRole == "TOC")
                        {
                            // Change nested TOC to TOCI
                            childElem.Put(PdfName.S, new PdfName("TOCI"));
                            fixedCount++;
                            _logger.LogInformation("Converted nested TOC to TOCI");
                        }
                    }
                }
            }
        }

        // Recursively process children
        var children = elem.GetKids();
        if (children != null)
        {
            foreach (var child in children)
            {
                if (child is PdfStructElem childElem)
                {
                    FixNestedTocElements(childElem, ref fixedCount);
                }
            }
        }
    }

    /// <summary>
    /// Fix link annotation structure by ensuring they're properly connected to Link elements
    /// </summary>
    private void FixLinkAnnotationStructure(PdfDocument pdfDoc, ref int fixedLinks, ref int addedAltText)
    {
        _logger.LogInformation("=== FIXING LINK ANNOTATION STRUCTURE ===");

        var structTreeRoot = pdfDoc.GetStructTreeRoot();
        var parentTreeObj = structTreeRoot.GetPdfObject().GetAsDictionary(PdfName.ParentTree);

        if (parentTreeObj == null)
        {
            _logger.LogWarning("No parent tree found");
            return;
        }

        // Process each page
        for (int pageNum = 1; pageNum <= pdfDoc.GetNumberOfPages(); pageNum++)
        {
            var page = pdfDoc.GetPage(pageNum);
            var annotations = page.GetAnnotations();

            var linkAnnotations = annotations
                .Where(a => a is PdfLinkAnnotation)
                .Cast<PdfLinkAnnotation>()
                .ToList();

            if (!linkAnnotations.Any()) continue;

            _logger.LogInformation($"Processing {linkAnnotations.Count} link annotations on page {pageNum}");

            foreach (var linkAnnot in linkAnnotations)
            {
                var annotObj = linkAnnot.GetPdfObject();
                var structParent = annotObj.GetAsNumber(PdfName.StructParent);

                if (structParent == null)
                {
                    _logger.LogWarning("Link annotation missing StructParent");
                    continue;
                }

                _logger.LogInformation($"Processing link with StructParent={structParent.IntValue()}");

                // Find the structure element for this annotation
                var structElem = FindStructureElementInParentTree(parentTreeObj, structParent.IntValue());

                if (structElem != null)
                {
                    var role = structElem.GetRole()?.GetValue();
                    _logger.LogInformation($"  Current role: {role}");

                    if (role == "Reference")
                    {
                        // We need to wrap this in a Link element
                        var parent = GetParentElement(structElem, structTreeRoot);

                        if (parent != null && parent.GetRole()?.GetValue() == "TOCI")
                        {
                            _logger.LogInformation("  Found Reference in TOCI, creating Link wrapper");

                            try
                            {
                                // Create a new Link element using the parent's document
                                var linkDict = new PdfDictionary();
                                linkDict.Put(PdfName.Type, PdfName.StructElem);
                                linkDict.Put(PdfName.S, PdfName.Link);
                                linkDict.Put(PdfName.P, parent.GetPdfObject()); // Set parent

                                var linkElem = new PdfStructElem(linkDict);
                                linkElem.MakeIndirect(pdfDoc);

                                // Add alt text to Link element
                                var altText = ExtractTextFromReference(structElem);
                                if (string.IsNullOrEmpty(altText))
                                {
                                    altText = GenerateAltTextFromAnnotation(linkAnnot, pageNum, pdfDoc);
                                }
                                linkElem.Put(PdfName.Alt, new PdfString(altText));

                                // Create OBJR element to reference the annotation
                                var objrDict = new PdfDictionary();
                                objrDict.Put(PdfName.Type, PdfName.OBJR);
                                objrDict.Put(PdfName.Obj, annotObj.GetIndirectReference());
                                objrDict.Put(PdfName.P, linkElem.GetPdfObject()); // Set parent to Link

                                var objrElem = new PdfStructElem(objrDict);
                                objrElem.MakeIndirect(pdfDoc);

                                // Add OBJR as child of Link
                                linkElem.AddKid(objrElem);

                                // Move Reference as child of Link
                                parent.RemoveKid(structElem);

                                // Update Reference's parent to Link
                                structElem.Put(PdfName.P, linkElem.GetPdfObject());
                                linkElem.AddKid(structElem);

                                // Add Link to parent
                                parent.AddKid(linkElem);

                                // Update the parent tree to point to Link instead of Reference
                                UpdateParentTreeEntry(parentTreeObj, structParent.IntValue(), linkElem);

                                fixedLinks++;
                                _logger.LogInformation($"  Created Link element with alt text: '{altText}'");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"  Failed to create Link wrapper: {ex.Message}");
                            }
                        }
                    }
                    else if (role == "Link")
                    {
                        // Already a Link element, just ensure it has alt text
                        var alt = structElem.GetAlt();
                        if (alt == null || string.IsNullOrEmpty(alt.GetValue()))
                        {
                            var altText = GenerateAltTextFromAnnotation(linkAnnot, pageNum, pdfDoc);
                            structElem.Put(PdfName.Alt, new PdfString(altText));
                            addedAltText++;
                            _logger.LogInformation($"  Added alt text to existing Link: '{altText}'");
                        }
                    }
                }

                // Also add Contents to the annotation itself
                if (linkAnnot.GetContents() == null || string.IsNullOrEmpty(linkAnnot.GetContents().GetValue()))
                {
                    var altText = GenerateAltTextFromAnnotation(linkAnnot, pageNum, pdfDoc);
                    linkAnnot.SetContents(new PdfString(altText));
                    addedAltText++;
                }
            }
        }
    }

    /// <summary>
    /// Fix TOCI structure to ensure proper Link elements
    /// </summary>
    private void FixTociStructure(PdfStructElem elem, PdfDocument pdfDoc, ref int fixedLinks)
    {
        var role = elem.GetRole()?.GetValue();

        if (role == "TOCI")
        {
            var kids = elem.GetKids();
            if (kids != null)
            {
                // Check if we have Reference elements without Link wrappers
                var hasReference = false;
                var hasLink = false;

                foreach (var kid in kids)
                {
                    if (kid is PdfStructElem childElem)
                    {
                        var childRole = childElem.GetRole()?.GetValue();
                        if (childRole == "Reference") hasReference = true;
                        if (childRole == "Link") hasLink = true;
                    }
                }

                if (hasReference && !hasLink)
                {
                    _logger.LogInformation("TOCI has Reference but no Link - needs fixing");
                    // This case should be handled by FixLinkAnnotationStructure
                }
            }
        }

        // Recursively process children
        var children = elem.GetKids();
        if (children != null)
        {
            foreach (var child in children)
            {
                if (child is PdfStructElem childElem)
                {
                    FixTociStructure(childElem, pdfDoc, ref fixedLinks);
                }
            }
        }
    }

    private PdfStructElem? FindStructureElementInParentTree(PdfDictionary parentTreeDict, int structParentIndex)
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
        catch (Exception ex)
        {
            _logger.LogWarning($"Error finding structure element: {ex.Message}");
        }

        return null;
    }

    private void UpdateParentTreeEntry(PdfDictionary parentTreeDict, int structParentIndex, PdfStructElem newElem)
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
                    // Update the entry to point to the new element
                    numsArray.Set(i + 1, newElem.GetPdfObject());
                    _logger.LogInformation($"Updated parent tree entry {structParentIndex}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error updating parent tree: {ex.Message}");
        }
    }

    private PdfStructElem? GetParentElement(PdfStructElem elem, PdfStructTreeRoot root)
    {
        try
        {
            // Try to get parent from the element's dictionary
            var parentObj = elem.GetPdfObject().Get(PdfName.P);
            if (parentObj is PdfDictionary parentDict)
            {
                // Check if it's the root
                if (parentDict.Equals(root.GetPdfObject()))
                {
                    return null; // Parent is root
                }
                return new PdfStructElem(parentDict);
            }
        }
        catch { }

        return null;
    }

    private string ExtractTextFromReference(PdfStructElem referenceElem)
    {
        try
        {
            // Try Alt text
            var alt = referenceElem.GetAlt();
            if (alt != null && !string.IsNullOrEmpty(alt.GetValue()))
            {
                return alt.GetValue();
            }

            // Try ActualText
            var actualText = referenceElem.GetActualText();
            if (actualText != null && !string.IsNullOrEmpty(actualText.GetValue()))
            {
                return actualText.GetValue();
            }

            // TODO: Could extract from MCR if needed

            return "";
        }
        catch
        {
            return "";
        }
    }

    private string GenerateAltTextFromAnnotation(PdfLinkAnnotation annotation, int pageNum, PdfDocument pdfDoc)
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
                    var pageRef = destArray.Get(0);
                    if (pageRef is PdfIndirectReference indirectRef)
                    {
                        for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                        {
                            var pageObj = pdfDoc.GetPage(i).GetPdfObject();
                            if (pageObj.GetIndirectReference() != null &&
                                pageObj.GetIndirectReference().Equals(indirectRef))
                            {
                                return $"Go to page {i}";
                            }
                        }
                    }
                }
                return "Internal navigation link";
            }
            else if (PdfName.URI.Equals(actionType))
            {
                var uri = action.GetAsString(PdfName.URI);
                return $"External link to {uri?.GetValue() ?? "website"}";
            }

            return $"Link on page {pageNum}";
        }
        catch
        {
            return $"Link on page {pageNum}";
        }
    }
}