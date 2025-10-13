using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Tagging;
using Microsoft.Extensions.Logging;

namespace AccessFormServer.Services;

/// <summary>
/// Enhanced service to fix TOC link structure by ensuring all link annotations
/// are properly nested inside Link structure elements per PDF/UA requirements
/// </summary>
public class TocLinkFixServiceEnhanced
{
    private readonly ILogger<TocLinkFixServiceEnhanced> _logger;

    public TocLinkFixServiceEnhanced(ILogger<TocLinkFixServiceEnhanced> logger)
    {
        _logger = logger;
    }

    public async Task<RemediationResult> FixTocLinksAsync(byte[] pdfBytes)
    {
        var result = new RemediationResult();

        try
        {
            _logger.LogInformation("===== ENHANCED TOC LINK FIX SERVICE =====");

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
            int orphanedAnnotations = 0;

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

            // Second pass: Comprehensive link annotation fix
            FixAllLinkAnnotations(pdfDoc, ref fixedLinks, ref addedAltText, ref orphanedAnnotations);

            // Third pass: Validate and fix remaining TOCI structure issues
            if (rootKids != null)
            {
                foreach (var kid in rootKids)
                {
                    if (kid is PdfStructElem elem)
                    {
                        ValidateAndFixTociStructure(elem, pdfDoc, ref fixedLinks);
                    }
                }
            }

            pdfDoc.Close();
            result.FixedPdf = outputMs.ToArray();
            result.Success = true;
            result.FixedTocElements = fixedTocStructure;
            result.FixedLinks = fixedLinks;

            _logger.LogInformation($"===== ENHANCED REMEDIATION COMPLETE =====");
            _logger.LogInformation($"Fixed TOC structure elements: {fixedTocStructure}");
            _logger.LogInformation($"Fixed link elements: {fixedLinks}");
            _logger.LogInformation($"Added alt text: {addedAltText}");
            _logger.LogInformation($"Fixed orphaned annotations: {orphanedAnnotations}");
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
    /// Comprehensive fix for all link annotations in the document
    /// </summary>
    private void FixAllLinkAnnotations(PdfDocument pdfDoc, ref int fixedLinks, ref int addedAltText, ref int orphanedAnnotations)
    {
        _logger.LogInformation("=== COMPREHENSIVE LINK ANNOTATION FIX ===");

        var structTreeRoot = pdfDoc.GetStructTreeRoot();
        var parentTreeObj = structTreeRoot.GetPdfObject().GetAsDictionary(PdfName.ParentTree);

        if (parentTreeObj == null)
        {
            _logger.LogWarning("No parent tree found - creating one");
            parentTreeObj = new PdfDictionary();
            parentTreeObj.Put(PdfName.Nums, new PdfArray());
            structTreeRoot.GetPdfObject().Put(PdfName.ParentTree, parentTreeObj);
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
                bool annotationFixed = false;
                var annotObj = linkAnnot.GetPdfObject();
                var structParent = annotObj.GetAsNumber(PdfName.StructParent);

                if (structParent == null)
                {
                    // Orphaned annotation - create StructParent
                    _logger.LogWarning($"Link annotation missing StructParent on page {pageNum} - creating one");

                    // Generate new StructParent index
                    var newStructParentIndex = GetNextStructParentIndex(parentTreeObj);
                    annotObj.Put(PdfName.StructParent, new PdfNumber(newStructParentIndex));
                    structParent = annotObj.GetAsNumber(PdfName.StructParent);
                    orphanedAnnotations++;
                }

                _logger.LogInformation($"Processing link with StructParent={structParent.IntValue()}");

                // Find or create the structure element for this annotation
                var structElem = FindStructureElementInParentTree(parentTreeObj, structParent.IntValue());

                if (structElem == null)
                {
                    // No structure element exists - create complete Link structure
                    _logger.LogWarning($"No structure element for StructParent {structParent.IntValue()} - creating Link structure");

                    structElem = CreateLinkStructureForOrphanedAnnotation(
                        linkAnnot, pageNum, pdfDoc, structTreeRoot, parentTreeObj, structParent.IntValue());
                    annotationFixed = true;
                    fixedLinks++;
                }
                else
                {
                    var role = structElem.GetRole()?.GetValue();
                    _logger.LogInformation($"  Current role: {role}");

                    if (role == "Reference" || role == "Span" || role == "P")
                    {
                        // Wrap in Link element
                        annotationFixed = WrapInLinkElement(
                            structElem, linkAnnot, pageNum, pdfDoc,
                            structTreeRoot, parentTreeObj, structParent.IntValue(),
                            ref fixedLinks);
                    }
                    else if (role == "Link")
                    {
                        // Already a Link - ensure it has proper structure
                        annotationFixed = EnsureLinkStructureComplete(
                            structElem, linkAnnot, pageNum, pdfDoc, ref addedAltText);
                    }
                    else if (role == "OBJR")
                    {
                        // OBJR without parent Link - needs fixing
                        var parent = GetParentElement(structElem, structTreeRoot);
                        if (parent != null && parent.GetRole()?.GetValue() != "Link")
                        {
                            _logger.LogWarning($"OBJR not under Link element - fixing structure");
                            annotationFixed = CreateProperLinkStructure(
                                structElem, linkAnnot, pageNum, pdfDoc,
                                parent, parentTreeObj, structParent.IntValue(),
                                ref fixedLinks);
                        }
                    }
                }

                // Ensure annotation has Contents (alt text)
                if (linkAnnot.GetContents() == null || string.IsNullOrEmpty(linkAnnot.GetContents().GetValue()))
                {
                    var altText = GenerateAltTextFromAnnotation(linkAnnot, pageNum, pdfDoc);
                    linkAnnot.SetContents(new PdfString(altText));
                    addedAltText++;
                    _logger.LogInformation($"Added Contents to annotation: '{altText}'");
                }

                if (annotationFixed)
                {
                    _logger.LogInformation($"✓ Fixed link annotation on page {pageNum}");
                }
            }
        }
    }

    private bool WrapInLinkElement(
        PdfStructElem structElem, PdfLinkAnnotation linkAnnot, int pageNum,
        PdfDocument pdfDoc, PdfStructTreeRoot structTreeRoot, PdfDictionary parentTreeObj,
        int structParentIndex, ref int fixedLinks)
    {
        try
        {
            var parent = GetParentElement(structElem, structTreeRoot);

            if (parent == null)
            {
                _logger.LogWarning("Cannot wrap element - no parent found");
                return false;
            }

            _logger.LogInformation($"Wrapping {structElem.GetRole()?.GetValue()} in Link element");

            // Create Link element
            var linkDict = new PdfDictionary();
            linkDict.Put(PdfName.Type, PdfName.StructElem);
            linkDict.Put(PdfName.S, PdfName.Link);
            linkDict.Put(PdfName.P, parent.GetPdfObject());

            var linkElem = new PdfStructElem(linkDict);
            linkElem.MakeIndirect(pdfDoc);

            // Add alt text
            var altText = ExtractTextFromElement(structElem);
            if (string.IsNullOrEmpty(altText))
            {
                altText = GenerateAltTextFromAnnotation(linkAnnot, pageNum, pdfDoc);
            }
            linkElem.Put(PdfName.Alt, new PdfString(altText));

            // Create OBJR element
            var objrDict = new PdfDictionary();
            objrDict.Put(PdfName.Type, PdfName.OBJR);
            objrDict.Put(PdfName.Obj, linkAnnot.GetPdfObject().GetIndirectReference());
            objrDict.Put(PdfName.P, linkElem.GetPdfObject());

            var objrElem = new PdfStructElem(objrDict);
            objrElem.MakeIndirect(pdfDoc);

            // Build new structure: Link -> [OBJR, original element]
            linkElem.AddKid(objrElem);

            // Move original element under Link
            parent.RemoveKid(structElem);
            structElem.Put(PdfName.P, linkElem.GetPdfObject());
            linkElem.AddKid(structElem);

            // Add Link to parent
            parent.AddKid(linkElem);

            // Update parent tree
            UpdateParentTreeEntry(parentTreeObj, structParentIndex, linkElem);

            fixedLinks++;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to wrap in Link element: {ex.Message}");
            return false;
        }
    }

    private PdfStructElem CreateLinkStructureForOrphanedAnnotation(
        PdfLinkAnnotation linkAnnot, int pageNum, PdfDocument pdfDoc,
        PdfStructTreeRoot structTreeRoot, PdfDictionary parentTreeObj, int structParentIndex)
    {
        try
        {
            // Find appropriate parent (TOCI, P, or root)
            PdfStructElem parent = FindAppropriateParentForLink(pdfDoc, pageNum, structTreeRoot);

            // Create Link element
            var linkDict = new PdfDictionary();
            linkDict.Put(PdfName.Type, PdfName.StructElem);
            linkDict.Put(PdfName.S, PdfName.Link);
            linkDict.Put(PdfName.P, parent.GetPdfObject());

            var linkElem = new PdfStructElem(linkDict);
            linkElem.MakeIndirect(pdfDoc);

            // Add alt text
            var altText = GenerateAltTextFromAnnotation(linkAnnot, pageNum, pdfDoc);
            linkElem.Put(PdfName.Alt, new PdfString(altText));

            // Create OBJR element
            var objrDict = new PdfDictionary();
            objrDict.Put(PdfName.Type, PdfName.OBJR);
            objrDict.Put(PdfName.Obj, linkAnnot.GetPdfObject().GetIndirectReference());
            objrDict.Put(PdfName.P, linkElem.GetPdfObject());

            var objrElem = new PdfStructElem(objrDict);
            objrElem.MakeIndirect(pdfDoc);

            // Add OBJR to Link
            linkElem.AddKid(objrElem);

            // Add Link to parent
            parent.AddKid(linkElem);

            // Add to parent tree
            AddToParentTree(parentTreeObj, structParentIndex, linkElem);

            _logger.LogInformation($"Created complete Link structure for orphaned annotation");
            return linkElem;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to create Link structure: {ex.Message}");
            throw;
        }
    }

    private bool CreateProperLinkStructure(
        PdfStructElem objrElem, PdfLinkAnnotation linkAnnot, int pageNum,
        PdfDocument pdfDoc, PdfStructElem parent, PdfDictionary parentTreeObj,
        int structParentIndex, ref int fixedLinks)
    {
        try
        {
            // Create Link element between parent and OBJR
            var linkDict = new PdfDictionary();
            linkDict.Put(PdfName.Type, PdfName.StructElem);
            linkDict.Put(PdfName.S, PdfName.Link);
            linkDict.Put(PdfName.P, parent.GetPdfObject());

            var linkElem = new PdfStructElem(linkDict);
            linkElem.MakeIndirect(pdfDoc);

            // Add alt text
            var altText = GenerateAltTextFromAnnotation(linkAnnot, pageNum, pdfDoc);
            linkElem.Put(PdfName.Alt, new PdfString(altText));

            // Move OBJR under Link
            parent.RemoveKid(objrElem);
            objrElem.Put(PdfName.P, linkElem.GetPdfObject());
            linkElem.AddKid(objrElem);

            // Add Link to parent
            parent.AddKid(linkElem);

            // Update parent tree
            UpdateParentTreeEntry(parentTreeObj, structParentIndex, linkElem);

            fixedLinks++;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to create proper Link structure: {ex.Message}");
            return false;
        }
    }

    private bool EnsureLinkStructureComplete(
        PdfStructElem linkElem, PdfLinkAnnotation linkAnnot,
        int pageNum, PdfDocument pdfDoc, ref int addedAltText)
    {
        bool modified = false;

        // Ensure alt text
        var alt = linkElem.GetAlt();
        if (alt == null || string.IsNullOrEmpty(alt.GetValue()))
        {
            var altText = GenerateAltTextFromAnnotation(linkAnnot, pageNum, pdfDoc);
            linkElem.Put(PdfName.Alt, new PdfString(altText));
            addedAltText++;
            modified = true;
            _logger.LogInformation($"Added alt text to Link: '{altText}'");
        }

        // Check for OBJR child
        var kids = linkElem.GetKids();
        bool hasObjr = false;

        if (kids != null)
        {
            foreach (var kid in kids)
            {
                if (kid is PdfStructElem childElem)
                {
                    var role = childElem.GetRole()?.GetValue();
                    if (role == "OBJR")
                    {
                        hasObjr = true;
                        break;
                    }
                }
            }
        }

        // Add OBJR if missing
        if (!hasObjr)
        {
            _logger.LogWarning("Link element missing OBJR child - adding");

            var objrDict = new PdfDictionary();
            objrDict.Put(PdfName.Type, PdfName.OBJR);
            objrDict.Put(PdfName.Obj, linkAnnot.GetPdfObject().GetIndirectReference());
            objrDict.Put(PdfName.P, linkElem.GetPdfObject());

            var objrElem = new PdfStructElem(objrDict);
            objrElem.MakeIndirect(pdfDoc);

            linkElem.AddKid(0, objrElem); // Add as first child
            modified = true;
        }

        return modified;
    }

    private void ValidateAndFixTociStructure(PdfStructElem elem, PdfDocument pdfDoc, ref int fixedLinks)
    {
        var role = elem.GetRole()?.GetValue();

        if (role == "TOCI")
        {
            var kids = elem.GetKids();
            if (kids != null)
            {
                // Look for any Reference elements not under Link
                foreach (var kid in kids.ToList()) // ToList to avoid modification during iteration
                {
                    if (kid is PdfStructElem childElem)
                    {
                        var childRole = childElem.GetRole()?.GetValue();
                        if (childRole == "Reference")
                        {
                            _logger.LogWarning($"Found Reference not under Link in TOCI - needs fixing");
                            // This should have been fixed in the previous pass,
                            // but log it for debugging
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
                    ValidateAndFixTociStructure(childElem, pdfDoc, ref fixedLinks);
                }
            }
        }
    }

    // Helper methods

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

    private void AddToParentTree(PdfDictionary parentTreeDict, int structParentIndex, PdfStructElem elem)
    {
        try
        {
            var numsArray = parentTreeDict.GetAsArray(PdfName.Nums);
            if (numsArray == null)
            {
                numsArray = new PdfArray();
                parentTreeDict.Put(PdfName.Nums, numsArray);
            }

            // Add new entry
            numsArray.Add(new PdfNumber(structParentIndex));
            numsArray.Add(elem.GetPdfObject());

            _logger.LogInformation($"Added parent tree entry {structParentIndex}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error adding to parent tree: {ex.Message}");
        }
    }

    private int GetNextStructParentIndex(PdfDictionary parentTreeDict)
    {
        try
        {
            var numsArray = parentTreeDict.GetAsArray(PdfName.Nums);
            if (numsArray == null || numsArray.Size() == 0) return 0;

            int maxIndex = -1;
            for (int i = 0; i < numsArray.Size(); i += 2)
            {
                var indexObj = numsArray.GetAsNumber(i);
                if (indexObj != null && indexObj.IntValue() > maxIndex)
                {
                    maxIndex = indexObj.IntValue();
                }
            }

            return maxIndex + 1;
        }
        catch
        {
            return 0;
        }
    }

    private PdfStructElem FindAppropriateParentForLink(PdfDocument pdfDoc, int pageNum, PdfStructTreeRoot root)
    {
        // Try to find a TOCI element on this page
        var tociParent = FindTociForPage(root, pageNum);
        if (tociParent != null) return tociParent;

        // Otherwise use root - cast to PdfStructElem
        return new PdfStructElem(root.GetPdfObject());
    }

    private PdfStructElem? FindTociForPage(PdfStructTreeRoot root, int pageNum)
    {
        // Simplified - would need to implement proper page-to-structure mapping
        // For now, return null to use root
        return null;
    }

    private PdfStructElem? GetParentElement(PdfStructElem elem, PdfStructTreeRoot root)
    {
        try
        {
            var parentObj = elem.GetPdfObject().Get(PdfName.P);
            if (parentObj is PdfDictionary parentDict)
            {
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

    private string ExtractTextFromElement(PdfStructElem elem)
    {
        try
        {
            // Try Alt text
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