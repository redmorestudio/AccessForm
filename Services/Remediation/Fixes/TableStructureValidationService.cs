using System;
using System.Collections.Generic;
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
    /// Validates and fixes table structure to ensure proper HTML-like hierarchy.
    /// Handles 7.2 violations where TR is not in Table and ensures Table -> TR -> TH/TD pattern.
    /// </summary>
    public class TableStructureValidationService : IRemediationService
    {
        private readonly ILogger<TableStructureValidationService> _logger;

        public string ServiceName => "Table Structure Validation Service";
        public ViolationCategory TargetCategory => ViolationCategory.TableAndList;
        public int Priority => 7; // Important for proper table structure
        public bool IsRequired => false; // Only run when table violations detected

        public TableStructureValidationService(ILogger<TableStructureValidationService> logger)
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
                _logger.LogInformation("[TABLE-STRUCTURE] Starting table structure validation");

                using var ms = new MemoryStream(pdfBytes);
                using var outputMs = new MemoryStream();
                using var pdfDoc = new PdfDocument(new PdfReader(ms), new PdfWriter(outputMs));

                if (!pdfDoc.IsTagged())
                {
                    _logger.LogWarning("[TABLE-STRUCTURE] Document is not tagged");
                    result.Success = true;
                    return result;
                }

                var fixedCount = 0;
                var rootTag = pdfDoc.GetStructTreeRoot();

                // Phase 1-4: Iterate through root's children to fix various issues
                var kids = rootTag.GetKids();
                if (kids != null)
                {
                    foreach (var kid in kids)
                    {
                        if (kid is PdfStructElem elem)
                        {
                            // Phase 1: Find orphaned table rows (TR not in Table)
                            fixedCount += await FixOrphanedTableRows(elem);

                            // Phase 2: Fix improper table nesting (ensure Table -> TR -> TH/TD)
                            fixedCount += await FixTableNesting(elem);

                            // Phase 3: Validate and fix table sections (THead, TBody, TFoot)
                            fixedCount += await FixTableSections(elem);

                            // Phase 4: Ensure all TH/TD are in TR
                            fixedCount += await FixOrphanedCells(elem);
                        }
                    }
                }

                pdfDoc.Close();

                if (fixedCount > 0)
                {
                    result.OutputPdf = outputMs.ToArray();
                    result.Success = true;
                    result.ChangesMade = true;
                    result.IssuesFixed = fixedCount;
                    _logger.LogInformation($"[TABLE-STRUCTURE] Fixed {fixedCount} table structure issues");
                }
                else
                {
                    _logger.LogInformation("[TABLE-STRUCTURE] All tables properly structured");
                    result.Success = true;
                }

                stopwatch.Stop();
                _logger.LogInformation($"[TABLE-STRUCTURE] Completed in {stopwatch.ElapsedMilliseconds}ms");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TABLE-STRUCTURE] Failed to validate table structure");
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private async Task<int> FixOrphanedTableRows(PdfStructElem element)
        {
            var fixedCount = 0;

            try
            {
                var role = element.GetRole();
                if (role != null && (role.GetValue() == "TR"))
                {
                    // Check if parent is a Table
                    var parent = element.GetParent() as PdfStructElem;
                    if (parent != null)
                    {
                        var parentRole = parent.GetRole();
                        if (parentRole == null ||
                            (!IsTableElement(parentRole) && !IsTableSectionElement(parentRole)))
                        {
                            _logger.LogInformation("[TABLE-STRUCTURE] Found orphaned TR not in Table");

                            // Find or create nearest table
                            var table = await FindOrCreateNearestTable(parent);
                            if (table != null)
                            {
                                // Move TR to table
                                parent.RemoveKid(element);
                                table.AddKid(element);
                                fixedCount++;
                                _logger.LogInformation("[TABLE-STRUCTURE] Moved TR into Table");
                            }
                        }
                    }
                }

                // Recursively check children
                var children = element.GetKids();
                if (children != null)
                {
                    foreach (var child in children.ToList())
                    {
                        if (child is PdfStructElem childElem)
                        {
                            fixedCount += await FixOrphanedTableRows(childElem);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[TABLE-STRUCTURE] Error fixing orphaned rows: {ex.Message}");
            }

            return await Task.FromResult(fixedCount);
        }

        private async Task<int> FixTableNesting(PdfStructElem element)
        {
            var fixedCount = 0;

            try
            {
                var role = element.GetRole();
                if (role != null && IsTableElement(role))
                {
                    _logger.LogDebug("[TABLE-STRUCTURE] Checking table nesting");

                    // Check immediate children
                    var children = element.GetKids();
                    if (children != null)
                    {
                        var childrenToMove = new List<PdfStructElem>();

                        foreach (var child in children)
                        {
                            if (child is PdfStructElem childElem)
                            {
                                var childRole = childElem.GetRole();
                                if (childRole != null)
                                {
                                    var childRoleValue = childRole.GetValue();

                                    // TH/TD should not be direct children of Table
                                    if (childRoleValue == "TH" || childRoleValue == "TD")
                                    {
                                        childrenToMove.Add(childElem);
                                    }
                                    // Non-table elements in table
                                    else if (!IsValidTableChild(childRoleValue))
                                    {
                                        _logger.LogWarning($"[TABLE-STRUCTURE] Found invalid child '{childRoleValue}' in Table");
                                        // Could wrap in TR or remove based on context
                                    }
                                }
                            }
                        }

                        // Move TH/TD into new TR
                        if (childrenToMove.Count > 0)
                        {
                            _logger.LogInformation($"[TABLE-STRUCTURE] Found {childrenToMove.Count} cells not in TR");

                            // Get document reference from element
                            var doc = element.GetPdfObject().GetIndirectReference()?.GetDocument();

                            var newTR = new PdfStructElem(doc, PdfName.TR);
                            element.AddKid(newTR);

                            foreach (var child in childrenToMove)
                            {
                                element.RemoveKid(child);
                                newTR.AddKid(child);
                            }

                            fixedCount++;
                            _logger.LogInformation("[TABLE-STRUCTURE] Wrapped orphaned cells in TR");
                        }
                    }
                }

                // Recursively check children
                var kids = element.GetKids();
                if (kids != null)
                {
                    foreach (var kid in kids.ToList())
                    {
                        if (kid is PdfStructElem childElem)
                        {
                            fixedCount += await FixTableNesting(childElem);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[TABLE-STRUCTURE] Error fixing table nesting: {ex.Message}");
            }

            return await Task.FromResult(fixedCount);
        }

        private async Task<int> FixTableSections(PdfStructElem element)
        {
            var fixedCount = 0;

            try
            {
                var role = element.GetRole();
                if (role != null && IsTableElement(role))
                {
                    // Check if table has proper sections
                    var children = element.GetKids();
                    if (children != null)
                    {
                        var hasProperSections = false;
                        var looseTRs = new List<PdfStructElem>();

                        foreach (var child in children)
                        {
                            if (child is PdfStructElem childElem)
                            {
                                var childRole = childElem.GetRole();
                                if (childRole != null)
                                {
                                    var childRoleValue = childRole.GetValue();

                                    if (IsTableSectionElement(childRole))
                                    {
                                        hasProperSections = true;
                                    }
                                    else if (childRoleValue == "TR")
                                    {
                                        looseTRs.Add(childElem);
                                    }
                                }
                            }
                        }

                        // If table has sections but also loose TRs, move TRs into TBody
                        if (hasProperSections && looseTRs.Count > 0)
                        {
                            _logger.LogInformation($"[TABLE-STRUCTURE] Found {looseTRs.Count} loose TRs in table with sections");

                            // Find or create TBody
                            var tbody = await FindOrCreateTableSection(element, "TBody");

                            foreach (var tr in looseTRs)
                            {
                                element.RemoveKid(tr);
                                tbody.AddKid(tr);
                                fixedCount++;
                            }

                            _logger.LogInformation("[TABLE-STRUCTURE] Moved loose TRs into TBody");
                        }
                    }
                }

                // Recursively check children
                var kids = element.GetKids();
                if (kids != null)
                {
                    foreach (var kid in kids.ToList())
                    {
                        if (kid is PdfStructElem childElem)
                        {
                            fixedCount += await FixTableSections(childElem);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[TABLE-STRUCTURE] Error fixing table sections: {ex.Message}");
            }

            return await Task.FromResult(fixedCount);
        }

        private async Task<int> FixOrphanedCells(PdfStructElem element)
        {
            var fixedCount = 0;

            try
            {
                var role = element.GetRole();
                if (role != null && (role.GetValue() == "TH" || role.GetValue() == "TD"))
                {
                    // Check if parent is TR
                    var parent = element.GetParent() as PdfStructElem;
                    if (parent != null)
                    {
                        var parentRole = parent.GetRole();
                        if (parentRole == null || parentRole.GetValue() != "TR")
                        {
                            _logger.LogInformation($"[TABLE-STRUCTURE] Found {role.GetValue()} not in TR");

                            // Try to find or create appropriate TR
                            var tr = await FindOrCreateNearestTR(parent);
                            if (tr != null)
                            {
                                parent.RemoveKid(element);
                                tr.AddKid(element);
                                fixedCount++;
                                _logger.LogInformation($"[TABLE-STRUCTURE] Moved {role.GetValue()} into TR");
                            }
                        }
                    }
                }

                // Recursively check children
                var children = element.GetKids();
                if (children != null)
                {
                    foreach (var child in children.ToList())
                    {
                        if (child is PdfStructElem childElem)
                        {
                            fixedCount += await FixOrphanedCells(childElem);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[TABLE-STRUCTURE] Error fixing orphaned cells: {ex.Message}");
            }

            return await Task.FromResult(fixedCount);
        }

        private async Task<PdfStructElem> FindOrCreateNearestTable(PdfStructElem element)
        {
            // Look for table in ancestors
            var current = element;
            while (current != null)
            {
                var role = current.GetRole();
                if (role != null && IsTableElement(role))
                {
                    return current;
                }
                current = current.GetParent() as PdfStructElem;
            }

            // No table found, create one
            _logger.LogInformation("[TABLE-STRUCTURE] Creating new Table for orphaned TR");
            var doc = element.GetPdfObject().GetIndirectReference()?.GetDocument();
            var table = new PdfStructElem(doc, PdfName.Table);
            var parentElem = element.GetParent() as PdfStructElem;
            parentElem?.AddKid(table);

            return await Task.FromResult(table);
        }

        private async Task<PdfStructElem> FindOrCreateNearestTR(PdfStructElem element)
        {
            // Look for TR in ancestors
            var current = element;
            while (current != null)
            {
                var role = current.GetRole();
                if (role != null && role.GetValue() == "TR")
                {
                    return current;
                }
                current = current.GetParent() as PdfStructElem;
            }

            // Look for Table to add TR to
            current = element;
            while (current != null)
            {
                var role = current.GetRole();
                if (role != null && IsTableElement(role))
                {
                    // Create new TR in this table
                    var doc = current.GetPdfObject().GetIndirectReference()?.GetDocument();
                    var tr = new PdfStructElem(doc, PdfName.TR);
                    current.AddKid(tr);
                    return tr;
                }
                current = current.GetParent() as PdfStructElem;
            }

            return await Task.FromResult<PdfStructElem>(null);
        }

        private async Task<PdfStructElem> FindOrCreateTableSection(PdfStructElem table, string sectionType)
        {
            // Look for existing section
            var children = table.GetKids();
            if (children != null)
            {
                foreach (var child in children)
                {
                    if (child is PdfStructElem childElem)
                    {
                        var role = childElem.GetRole();
                        if (role != null && role.GetValue() == sectionType)
                        {
                            return childElem;
                        }
                    }
                }
            }

            // Create new section
            _logger.LogInformation($"[TABLE-STRUCTURE] Creating {sectionType} section");
            var doc = table.GetPdfObject().GetIndirectReference()?.GetDocument();
            var section = new PdfStructElem(doc, new PdfName(sectionType));
            table.AddKid(section);

            return await Task.FromResult(section);
        }

        private bool IsTableElement(PdfName role)
        {
            var roleValue = role.GetValue();
            return roleValue == "Table" || roleValue == "TABLE";
        }

        private bool IsTableSectionElement(PdfName role)
        {
            var roleValue = role.GetValue();
            return roleValue == "THead" || roleValue == "THEAD" ||
                   roleValue == "TBody" || roleValue == "TBODY" ||
                   roleValue == "TFoot" || roleValue == "TFOOT";
        }

        private bool IsValidTableChild(string roleValue)
        {
            return roleValue == "TR" ||
                   roleValue == "THead" || roleValue == "THEAD" ||
                   roleValue == "TBody" || roleValue == "TBODY" ||
                   roleValue == "TFoot" || roleValue == "TFOOT" ||
                   roleValue == "Caption" || roleValue == "CAPTION";
        }
    }
}
