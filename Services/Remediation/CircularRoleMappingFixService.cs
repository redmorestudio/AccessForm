using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using iText.Kernel.Pdf;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation
{
    /// <summary>
    /// Fixes circular role mappings where a structure type is mapped to itself
    /// Example: Form -> Form, P -> P, etc.
    /// These are invalid and should be removed from the RoleMap
    /// </summary>
    public class CircularRoleMappingFixService : IRemediationService
    {
        private readonly ILogger<CircularRoleMappingFixService> _logger;

        public string ServiceName => "Circular Role Mapping Fix";
        public ViolationCategory TargetCategory => ViolationCategory.Structure;
        public int Priority => 100; // Low priority - reactive (triggered by validation feedback)
        public bool IsRequired => false; // Only run when circular mapping violation detected

        public CircularRoleMappingFixService(ILogger<CircularRoleMappingFixService> logger)
        {
            _logger = logger;
        }

        public async Task<ServiceResult> RemediateAsync(byte[] pdfBytes)
        {
            var result = new ServiceResult
            {
                Success = false,
                OutputPdf = pdfBytes
            };

            try
            {
                _logger.LogInformation("[CIRCULAR-ROLE-FIX] Starting circular role mapping fix");

                using var ms = new MemoryStream(pdfBytes);
                using var outputMs = new MemoryStream();
                using var pdfDoc = new PdfDocument(new PdfReader(ms), new PdfWriter(outputMs));

                var catalog = pdfDoc.GetCatalog().GetPdfObject();
                var structTreeRoot = catalog.GetAsDictionary(iText.Kernel.Pdf.PdfName.StructTreeRoot);

                if (structTreeRoot == null)
                {
                    _logger.LogInformation("[CIRCULAR-ROLE-FIX] No structure tree found");
                    result.Success = true;
                    return result;
                }

                var roleMap = structTreeRoot.GetAsDictionary(iText.Kernel.Pdf.PdfName.RoleMap);
                if (roleMap == null)
                {
                    _logger.LogInformation("[CIRCULAR-ROLE-FIX] No RoleMap found");
                    result.Success = true;
                    return result;
                }

                var fixedCount = 0;
                var toRemove = new List<iText.Kernel.Pdf.PdfName>();

                // Find all circular mappings (where key == value)
                foreach (var key in roleMap.KeySet())
                {
                    var value = roleMap.Get(key);

                    if (value is iText.Kernel.Pdf.PdfName valueName)
                    {
                        if (key.Equals(valueName))
                        {
                            _logger.LogInformation($"[CIRCULAR-ROLE-FIX] Found circular mapping: {key} -> {valueName}");
                            toRemove.Add(key);
                            fixedCount++;
                        }
                    }
                }

                // Remove circular mappings
                foreach (var key in toRemove)
                {
                    roleMap.Remove(key);
                    _logger.LogInformation($"[CIRCULAR-ROLE-FIX] Removed circular mapping: {key}");
                }

                // If RoleMap is now empty, remove it entirely
                if (roleMap.Size() == 0)
                {
                    structTreeRoot.Remove(iText.Kernel.Pdf.PdfName.RoleMap);
                    _logger.LogInformation("[CIRCULAR-ROLE-FIX] Removed empty RoleMap");
                }

                pdfDoc.Close();

                if (fixedCount > 0)
                {
                    result.OutputPdf = outputMs.ToArray();
                    result.Success = true;
                    result.ChangesMade = true;
                    result.IssuesFixed = fixedCount;
                    _logger.LogInformation($"[CIRCULAR-ROLE-FIX] Fixed {fixedCount} circular role mapping(s)");
                }
                else
                {
                    _logger.LogInformation("[CIRCULAR-ROLE-FIX] No circular mappings found");
                    result.Success = true;
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CIRCULAR-ROLE-FIX] Failed to fix circular role mappings");
                result.ErrorMessage = ex.Message;
                return result;
            }
        }
    }
}
