using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.Pdf.Tagutils;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation.Fixes
{
    /// <summary>
    /// Fixes 7.18.4-2: If the Form element omits a Role attribute (Table 348),
    /// it shall have only one child: an object reference (14.7.4.3) identifying
    /// the widget annotation per ISO 32000-1:2008, 14.8.4.5, Table 340.
    ///
    /// Solution: Add Role="Form" attribute to Form structure elements that are missing it.
    /// </summary>
    public class FormRoleAttributeFixService : IRemediationService
    {
        private readonly ILogger<FormRoleAttributeFixService> _logger;

        public string ServiceName => "Form Role Attribute Fix";
        public ViolationCategory TargetCategory => ViolationCategory.FormFields;
        public int Priority => 5; // Critical priority for form accessibility
        public bool IsRequired => false; // Only run when form violations detected

        public FormRoleAttributeFixService(ILogger<FormRoleAttributeFixService> logger)
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
                _logger.LogInformation("[FORM-ROLE-FIX] Starting Form Role attribute remediation");

                using var ms = new MemoryStream(pdfBytes);
                using var outputMs = new MemoryStream();
                using var pdfDoc = new PdfDocument(new PdfReader(ms), new PdfWriter(outputMs));

                if (!pdfDoc.IsTagged())
                {
                    _logger.LogWarning("[FORM-ROLE-FIX] Document is not tagged");
                    result.Success = true;
                    return result;
                }

                var fixedCount = 0;
                var rootTag = pdfDoc.GetStructTreeRoot();
                if (rootTag == null)
                {
                    _logger.LogWarning("[FORM-ROLE-FIX] No structure tree root found");
                    result.Success = true;
                    return result;
                }

                // Traverse structure tree to find Form elements
                fixedCount = FixFormRoleAttributes(rootTag);

                pdfDoc.Close();

                if (fixedCount > 0)
                {
                    result.OutputPdf = outputMs.ToArray();
                    result.Success = true;
                    result.ChangesMade = true;
                    result.IssuesFixed = fixedCount;
                    _logger.LogInformation($"[FORM-ROLE-FIX] Added Role attribute to {fixedCount} Form elements");
                }
                else
                {
                    _logger.LogInformation("[FORM-ROLE-FIX] All Form elements have proper Role attributes");
                    result.Success = true;
                }

                stopwatch.Stop();
                _logger.LogInformation($"[FORM-ROLE-FIX] Completed in {stopwatch.ElapsedMilliseconds}ms");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FORM-ROLE-FIX] Failed to fix Form Role attributes");
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private int FixFormRoleAttributes(PdfStructTreeRoot root)
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
                        fixedCount += ProcessStructElement(elem);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FORM-ROLE-FIX] Error processing structure tree");
            }

            return fixedCount;
        }

        private int ProcessStructElement(PdfStructElem element)
        {
            var fixedCount = 0;

            try
            {
                var role = element.GetRole();

                // Check if this is a Form structure element
                if (role != null && role.GetValue() == "Form")
                {
                    var elemDict = element.GetPdfObject();

                    // Check if it has a Role attribute in RoleMap
                    if (!elemDict.ContainsKey(PdfName.A))
                    {
                        // Form element without attributes - check if it needs Role
                        // Per 7.18.4-2: Form without Role attribute must have only one child (OBJR)
                        var kids = element.GetKids();

                        if (kids != null && (kids.Count > 1 || (kids.Count == 1 && !IsOnlyChildOBJR(kids))))
                        {
                            // Violation: Form without Role has multiple children or non-OBJR child
                            // Fix: Add Role="Form" attribute
                            _logger.LogInformation($"[FORM-ROLE-FIX] Adding Role attribute to Form element");

                            // Create attributes dictionary with Role
                            var attrDict = new PdfDictionary();
                            attrDict.Put(PdfName.O, new PdfName("PrintField"));
                            attrDict.Put(new PdfName("Role"), new PdfName("Form"));

                            elemDict.Put(PdfName.A, attrDict);
                            elemDict.SetModified();

                            fixedCount++;
                        }
                    }
                }

                // Recursively process children
                var children = element.GetKids();
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        if (child is PdfStructElem childElem)
                        {
                            fixedCount += ProcessStructElement(childElem);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FORM-ROLE-FIX] Error processing structure element");
            }

            return fixedCount;
        }

        private bool IsOnlyChildOBJR(System.Collections.Generic.IList<IStructureNode> kids)
        {
            try
            {
                if (kids == null || kids.Count != 1)
                    return false;

                var firstKid = kids[0];
                if (firstKid is PdfObjRef)
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
