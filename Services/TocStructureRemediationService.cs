using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.Pdf.Tagutils;
using Microsoft.Extensions.Logging;

namespace AccessFormServer.Services;

/// <summary>
/// Service to fix broken Table of Contents structure from Word PDFs
/// Word creates nested TOC elements with orphaned Reference and Link-OBJR siblings
/// This service restructures them into proper PDF/UA compliant Link elements
/// </summary>
public class TocStructureRemediationService
{
    private readonly ILogger<TocStructureRemediationService> _logger;

    public TocStructureRemediationService(ILogger<TocStructureRemediationService> logger)
    {
        _logger = logger;
    }

    public async Task<RemediationResult> RemediateTocStructureAsync(byte[] pdfBytes)
    {
        var result = new RemediationResult();

        try
        {
            _logger.LogInformation("===== TOC STRUCTURE REMEDIATION SERVICE =====");

            using var inputMs = new MemoryStream(pdfBytes);
            using var outputMs = new MemoryStream();
            using var pdfDoc = new PdfDocument(new PdfReader(inputMs), new PdfWriter(outputMs));

            if (!pdfDoc.IsTagged())
            {
                pdfDoc.SetTagged();
            }

            var structTreeRoot = pdfDoc.GetStructTreeRoot();
            int fixedLinks = 0;
            int fixedTocStructure = 0;

            // Find and fix all TOC elements
            var rootKids = structTreeRoot.GetKids();
            if (rootKids != null)
            {
                foreach (var kid in rootKids)
                {
                    if (kid is PdfStructElem elem)
                    {
                        FixTocStructure(elem, ref fixedTocStructure, ref fixedLinks, pdfDoc);
                    }
                }
            }

            // Also ensure all link annotations have alt text
            for (int pageNum = 1; pageNum <= pdfDoc.GetNumberOfPages(); pageNum++)
            {
                var page = pdfDoc.GetPage(pageNum);
                var annotations = page.GetAnnotations();

                var linkAnnotations = annotations
                    .Where(a => a is PdfLinkAnnotation)
                    .Cast<PdfLinkAnnotation>()
                    .ToList();

                foreach (var linkAnnot in linkAnnotations)
                {
                    var contents = linkAnnot.GetContents();
                    if (contents == null || string.IsNullOrEmpty(contents.GetValue()))
                    {
                        var altText = GenerateAltText(linkAnnot, pageNum, pdfDoc);
                        linkAnnot.SetContents(new PdfString(altText));
                        _logger.LogInformation($"Added alt text to annotation: '{altText}'");
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
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error remediating TOC structure: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.FixedPdf = pdfBytes;
        }

        return result;
    }

    /// <summary>
    /// Fix TOC structure by:
    /// 1. Converting nested TOC elements to TOCI
    /// 2. Moving Reference elements into Link elements
    /// 3. Adding alt text to all structure elements
    /// </summary>
    private void FixTocStructure(PdfStructElem elem, ref int fixedTocStructure, ref int fixedLinks, PdfDocument pdfDoc)
    {
        try
        {
            var role = elem.GetRole();

            // If this is a TOC element
            if (role != null && role.GetValue() == "TOC")
            {
                var kids = elem.GetKids();
                if (kids != null)
                {
                    // Process each child
                    for (int i = 0; i < kids.Count; i++)
                    {
                        if (kids[i] is PdfStructElem childElem)
                        {
                            var childRole = childElem.GetRole();

                            // If we find a nested TOC, it should be a TOCI (Table of Contents Item)
                            if (childRole != null && childRole.GetValue() == "TOC")
                            {
                                // Change role to TOCI
                                childElem.Put(PdfName.S, new PdfName("TOCI"));
                                fixedTocStructure++;
                                _logger.LogInformation($"Changed nested TOC to TOCI");

                                // Now look for Reference and Link-OBJR siblings to merge
                                FixTociChildren(childElem, ref fixedLinks);
                            }
                            // Also process existing TOCI elements
                            else if (childRole != null && childRole.GetValue() == "TOCI")
                            {
                                _logger.LogInformation($"Processing existing TOCI element");
                                // Look for Reference and Link-OBJR siblings to merge
                                FixTociChildren(childElem, ref fixedLinks);
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
                        FixTocStructure(childElem, ref fixedTocStructure, ref fixedLinks, pdfDoc);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error fixing TOC structure: {ex.Message}");
        }
    }

    /// <summary>
    /// Fix TOCI children by merging Reference and Link-OBJR elements
    /// </summary>
    private void FixTociChildren(PdfStructElem tociElem, ref int fixedLinks)
    {
        try
        {
            var kids = tociElem.GetKids();
            if (kids == null) return;

            PdfStructElem? referenceElem = null;
            PdfStructElem? linkObjrElem = null;
            string? referenceText = null;

            _logger.LogInformation($"Processing TOCI with {kids.Count} children");

            // Find Reference and Link-OBJR elements
            foreach (var kid in kids)
            {
                if (kid is PdfStructElem elem)
                {
                    var role = elem.GetRole()?.GetValue();
                    _logger.LogInformation($"  Found child with role: {role}");

                    if (role == "Reference")
                    {
                        referenceElem = elem;
                        // Extract text from Reference - try multiple methods
                        referenceText = ExtractTextFromMcid(elem);

                        // Also try to get actual text content
                        if (string.IsNullOrEmpty(referenceText))
                        {
                            // Try to extract from the actual marked content
                            var alt = elem.GetAlt();
                            if (alt != null)
                            {
                                referenceText = alt.GetValue();
                            }
                        }

                        _logger.LogInformation($"    Reference text: '{referenceText}'");
                    }
                    else if (role == "Link" || role == "Link-OBJR")
                    {
                        linkObjrElem = elem;

                        // Check if this element has an OBJR child
                        var linkKids = elem.GetKids();
                        if (linkKids != null)
                        {
                            foreach (var linkKid in linkKids)
                            {
                                if (linkKid is PdfStructElem objr)
                                {
                                    var objrRole = objr.GetRole()?.GetValue();
                                    if (objrRole == "OBJR")
                                    {
                                        _logger.LogInformation($"    Link has OBJR child");
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // If we have both, merge them
            if (referenceElem != null && linkObjrElem != null)
            {
                _logger.LogInformation($"Found Reference and Link-OBJR to merge");

                // Make sure the Link element has proper role
                linkObjrElem.Put(PdfName.S, PdfName.Link);

                // Move Reference as child of Link
                tociElem.RemoveKid(referenceElem);
                linkObjrElem.AddKid(0, referenceElem);

                // Add alt text from reference
                if (!string.IsNullOrEmpty(referenceText))
                {
                    linkObjrElem.Put(PdfName.Alt, new PdfString(referenceText));
                    _logger.LogInformation($"Added alt text to Link: '{referenceText}'");
                }
                else
                {
                    // Use generic alt text if we couldn't extract
                    linkObjrElem.Put(PdfName.Alt, new PdfString("Table of Contents Entry"));
                    _logger.LogInformation($"Added generic alt text to Link");
                }

                fixedLinks++;
            }
            // If we only have a Link-OBJR, add alt text
            else if (linkObjrElem != null)
            {
                _logger.LogInformation($"Found Link-OBJR without Reference");
                linkObjrElem.Put(PdfName.S, PdfName.Link);

                var alt = linkObjrElem.GetAlt();
                if (alt == null || string.IsNullOrEmpty(alt.GetValue()))
                {
                    // Try to get text from Reference if it exists separately
                    if (referenceElem != null && !string.IsNullOrEmpty(referenceText))
                    {
                        linkObjrElem.Put(PdfName.Alt, new PdfString(referenceText));
                        _logger.LogInformation($"Added Reference text as alt: '{referenceText}'");
                    }
                    else
                    {
                        linkObjrElem.Put(PdfName.Alt, new PdfString("Table of Contents Link"));
                        _logger.LogInformation($"Added generic alt text");
                    }
                    fixedLinks++;
                }
            }
            else if (referenceElem != null && linkObjrElem == null)
            {
                _logger.LogInformation($"Found Reference without Link-OBJR - cannot fix");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error fixing TOCI children: {ex.Message}");
        }
    }

    /// <summary>
    /// Extract text from a structure element with MCID
    /// </summary>
    private string ExtractTextFromMcid(PdfStructElem elem)
    {
        try
        {
            // This is simplified - in production you'd extract the actual marked content
            var alt = elem.GetAlt();
            if (alt != null)
            {
                return alt.GetValue();
            }

            // Try to get from actual text
            var actualText = elem.GetActualText();
            if (actualText != null)
            {
                return actualText.GetValue();
            }

            return "Link";
        }
        catch
        {
            return "Link";
        }
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

public class RemediationResult
{
    public bool Success { get; set; }
    public byte[] FixedPdf { get; set; } = Array.Empty<byte>();
    public int FixedTocElements { get; set; }
    public int FixedLinks { get; set; }
    public string? ErrorMessage { get; set; }
}