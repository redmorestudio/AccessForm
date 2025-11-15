using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Tagging;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation.Fixes
{
    /// <summary>
    /// Fixes 7.18.4-2: Form elements with 0 children that don't have a Role attribute.
    /// Solution: Remove empty Form structure elements from the structure tree.
    /// </summary>
    public class EmptyFormElementRemovalService : IRemediationService
    {
        private readonly ILogger<EmptyFormElementRemovalService> _logger;

        public string ServiceName => "Empty Form Element Removal";
        public ViolationCategory TargetCategory => ViolationCategory.FormFields;
        public int Priority => 6; // Run after other form fixes
        public bool IsRequired => true; // Always run to catch empty Form elements

        public EmptyFormElementRemovalService(ILogger<EmptyFormElementRemovalService> logger)
        {
            _logger = logger;
        }

        public async Task<ServiceResult> RemediateAsync(byte[] pdfBytes)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new ServiceResult
            {
                Success = false,
                OutputPdf = pdfBytes
            };

            try
            {
                _logger.LogInformation("[EMPTY-FORM-FIX] Starting empty Form element removal");

                using var ms = new MemoryStream(pdfBytes);
                using var outputMs = new MemoryStream();
                using var pdfDoc = new PdfDocument(new PdfReader(ms), new PdfWriter(outputMs));

                if (!pdfDoc.IsTagged())
                {
                    _logger.LogWarning("[EMPTY-FORM-FIX] Document is not tagged");
                    result.Success = true;
                    return result;
                }

                var fixedCount = 0;
                var rootTag = pdfDoc.GetStructTreeRoot();
                if (rootTag == null)
                {
                    _logger.LogWarning("[EMPTY-FORM-FIX] No structure tree root found");
                    result.Success = true;
                    return result;
                }

                // Traverse structure tree to find and remove empty Form elements
                fixedCount = RemoveEmptyFormElements(rootTag);

                pdfDoc.Close();

                if (fixedCount > 0)
                {
                    result.OutputPdf = outputMs.ToArray();
                    result.Success = true;
                    result.ChangesMade = true;
                    result.IssuesFixed = fixedCount;
                    _logger.LogInformation($"[EMPTY-FORM-FIX] Removed {fixedCount} empty Form elements");
                }
                else
                {
                    _logger.LogInformation("[EMPTY-FORM-FIX] No empty Form elements found");
                    result.Success = true;
                }

                stopwatch.Stop();
                _logger.LogInformation($"[EMPTY-FORM-FIX] Completed in {stopwatch.ElapsedMilliseconds}ms");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EMPTY-FORM-FIX] Failed to remove empty Form elements");
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private int RemoveEmptyFormElements(PdfStructTreeRoot root)
        {
            var fixedCount = 0;

            try
            {
                // Get all kids from the root
                var kids = root.GetKids();
                if (kids == null || kids.Count == 0)
                    return 0;

                foreach (var kid in kids)
                {
                    if (kid is PdfStructElem elem)
                    {
                        fixedCount += ProcessStructElement(elem, null);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EMPTY-FORM-FIX] Error processing structure tree");
            }

            return fixedCount;
        }

        private int ProcessStructElement(PdfStructElem element, PdfStructElem parent)
        {
            var fixedCount = 0;

            try
            {
                var role = element.GetRole();

                // Check if this is a Form structure element
                if (role != null && role.GetValue() == "Form")
                {
                    var elemDict = element.GetPdfObject();

                    // Check if it has a Role attribute
                    var hasRole = elemDict.ContainsKey(PdfName.A) &&
                                  elemDict.GetAsDictionary(PdfName.A) != null &&
                                  elemDict.GetAsDictionary(PdfName.A).ContainsKey(new PdfName("Role"));

                    // Count children
                    var kids = element.GetKids();
                    var hasChildren = kids != null && kids.Count > 0;

                    // Violation: Form without Role and without children (or with 0 children)
                    if (!hasRole && !hasChildren)
                    {
                        _logger.LogInformation($"[EMPTY-FORM-FIX] Found empty Form element without Role attribute");

                        // Remove this element from its parent
                        if (parent != null)
                        {
                            try
                            {
                                var parentKids = parent.GetKids();
                                if (parentKids != null)
                                {
                                    // Find the index of this element in the parent's kids
                                    for (int i = 0; i < parentKids.Count; i++)
                                    {
                                        if (parentKids[i] == element)
                                        {
                                            parent.RemoveKid(i);
                                            _logger.LogInformation($"[EMPTY-FORM-FIX] Removed empty Form element at index {i}");
                                            fixedCount++;
                                            return fixedCount; // Don't process children since we removed this element
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "[EMPTY-FORM-FIX] Error removing Form element from parent");
                            }
                        }
                        else
                        {
                            _logger.LogWarning("[EMPTY-FORM-FIX] Cannot remove Form element without parent reference");
                        }
                    }
                }

                // Recursively process children
                var children = element.GetKids();
                if (children != null)
                {
                    // Process in reverse order to avoid index shifting issues when removing elements
                    for (int i = children.Count - 1; i >= 0; i--)
                    {
                        if (children[i] is PdfStructElem childElem)
                        {
                            fixedCount += ProcessStructElement(childElem, element);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[EMPTY-FORM-FIX] Error processing structure element");
            }

            return fixedCount;
        }
    }
}
