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
    /// Adds scope attributes to table header (TH) elements to specify whether they are column or row headers.
    /// This is critical for screen readers to properly associate data cells with their headers.
    /// </summary>
    public class TableScopeAttributeFixService : IRemediationService
    {
        private readonly ILogger<TableScopeAttributeFixService> _logger;

        public string ServiceName => "Table Scope Attribute Fix";
        public ViolationCategory TargetCategory => ViolationCategory.TableAndList;
        public int Priority => 6; // Important for table accessibility
        public bool IsRequired => false; // Only run when table violations detected

        public TableScopeAttributeFixService(ILogger<TableScopeAttributeFixService> logger)
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
                _logger.LogInformation("[TABLE-SCOPE-FIX] Starting table scope attribute remediation");

                using var ms = new MemoryStream(pdfBytes);
                using var outputMs = new MemoryStream();
                using var pdfDoc = new PdfDocument(new PdfReader(ms), new PdfWriter(outputMs));

                if (!pdfDoc.IsTagged())
                {
                    _logger.LogWarning("[TABLE-SCOPE-FIX] Document is not tagged");
                    result.Success = true;
                    return result;
                }

                var fixedCount = 0;
                var rootTag = pdfDoc.GetStructTreeRoot();

                // Find all table elements - iterate through root's children
                var tables = new List<PdfStructElem>();
                var kids = rootTag.GetKids();
                if (kids != null)
                {
                    foreach (var kid in kids)
                    {
                        if (kid is PdfStructElem elem)
                        {
                            tables.AddRange(await FindAllTablesRecursively(elem));
                        }
                    }
                }

                _logger.LogInformation($"[TABLE-SCOPE-FIX] Found {tables.Count} tables to process");

                foreach (var table in tables)
                {
                    fixedCount += await ProcessTable(table);
                }

                pdfDoc.Close();

                if (fixedCount > 0)
                {
                    result.OutputPdf = outputMs.ToArray();
                    result.Success = true;
                    result.ChangesMade = true;
                    result.IssuesFixed = fixedCount;
                    _logger.LogInformation($"[TABLE-SCOPE-FIX] Added scope attributes to {fixedCount} table headers");
                }
                else
                {
                    _logger.LogInformation("[TABLE-SCOPE-FIX] All table headers already have scope attributes");
                    result.Success = true;
                }

                stopwatch.Stop();
                _logger.LogInformation($"[TABLE-SCOPE-FIX] Completed in {stopwatch.ElapsedMilliseconds}ms");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TABLE-SCOPE-FIX] Failed to add table scope attributes");
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private async Task<List<PdfStructElem>> FindAllTablesRecursively(PdfStructElem element)
        {
            var tables = new List<PdfStructElem>();

            try
            {
                var role = element.GetRole();
                if (role != null && (role.Equals(PdfName.Table) ||
                                    role.GetValue() == "Table" ||
                                    role.GetValue() == "TABLE"))
                {
                    tables.Add(element);
                }

                // Recursively search children
                var children = element.GetKids();
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        if (child is PdfStructElem childElem)
                        {
                            tables.AddRange(await FindAllTablesRecursively(childElem));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[TABLE-SCOPE-FIX] Error searching for tables: {ex.Message}");
            }

            return tables;
        }

        private async Task<int> ProcessTable(PdfStructElem tableElement)
        {
            var fixedCount = 0;

            try
            {
                _logger.LogDebug("[TABLE-SCOPE-FIX] Processing table structure");

                // Analyze table structure to determine header positions
                var headerInfo = await AnalyzeTableStructure(tableElement);

                // Process all TH elements
                fixedCount += await ProcessTableHeaders(tableElement, headerInfo);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[TABLE-SCOPE-FIX] Error processing table: {ex.Message}");
            }

            return await Task.FromResult(fixedCount);
        }

        private async Task<TableHeaderInfo> AnalyzeTableStructure(PdfStructElem tableElement)
        {
            var info = new TableHeaderInfo();

            try
            {
                // Check for THead, TBody, TFoot sections
                var children = tableElement.GetKids();
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        if (child is PdfStructElem childElem)
                        {
                            var role = childElem.GetRole();
                            if (role != null)
                            {
                                var roleValue = role.GetValue();
                                if (roleValue == "THead" || roleValue == "THEAD")
                                {
                                    info.HasTHead = true;
                                    // Headers in THead are typically column headers
                                    await MarkHeadersInSection(childElem, "Col", info);
                                }
                                else if (roleValue == "TR")
                                {
                                    // Check if first row contains headers
                                    await AnalyzeRow(childElem, info);
                                }
                            }
                        }
                    }
                }

                // If we found headers in first row but no THead, they're likely column headers
                if (!info.HasTHead && info.FirstRowHasHeaders)
                {
                    info.FirstRowScope = "Col";
                }

                // Check for row headers (typically first cell in each row)
                if (info.FirstColumnHasHeaders)
                {
                    info.FirstColumnScope = "Row";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[TABLE-SCOPE-FIX] Error analyzing table structure: {ex.Message}");
            }

            return await Task.FromResult(info);
        }

        private async Task AnalyzeRow(PdfStructElem rowElement, TableHeaderInfo info)
        {
            try
            {
                var cells = rowElement.GetKids();
                if (cells != null && cells.Count > 0)
                {
                    var firstCell = cells[0] as PdfStructElem;
                    if (firstCell != null)
                    {
                        var role = firstCell.GetRole();
                        if (role != null && (role.Equals(PdfName.TH) || role.GetValue() == "TH"))
                        {
                            // First cell is a header
                            if (!info.FirstRowAnalyzed)
                            {
                                info.FirstRowHasHeaders = true;
                                info.FirstRowAnalyzed = true;
                            }
                            else
                            {
                                // Headers in first column of subsequent rows
                                info.FirstColumnHasHeaders = true;
                            }
                        }
                    }

                    // Check if all cells in first row are headers
                    if (!info.FirstRowAnalyzed)
                    {
                        var allHeaders = cells.All(c =>
                        {
                            if (c is PdfStructElem elem)
                            {
                                var r = elem.GetRole();
                                return r != null && (r.Equals(PdfName.TH) || r.GetValue() == "TH");
                            }
                            return false;
                        });

                        if (allHeaders)
                        {
                            info.FirstRowHasHeaders = true;
                        }
                        info.FirstRowAnalyzed = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[TABLE-SCOPE-FIX] Error analyzing row: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private async Task MarkHeadersInSection(PdfStructElem section, string scope, TableHeaderInfo info)
        {
            try
            {
                var children = section.GetKids();
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        if (child is PdfStructElem childElem)
                        {
                            var role = childElem.GetRole();
                            if (role != null && (role.GetValue() == "TR"))
                            {
                                // Process headers in this row
                                var cells = childElem.GetKids();
                                if (cells != null)
                                {
                                    foreach (var cell in cells)
                                    {
                                        if (cell is PdfStructElem cellElem)
                                        {
                                            var cellRole = cellElem.GetRole();
                                            if (cellRole != null && (cellRole.Equals(PdfName.TH) || cellRole.GetValue() == "TH"))
                                            {
                                                info.HeaderScopes[cellElem] = scope;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[TABLE-SCOPE-FIX] Error marking headers in section: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private async Task<int> ProcessTableHeaders(PdfStructElem element, TableHeaderInfo headerInfo)
        {
            var fixedCount = 0;

            try
            {
                var role = element.GetRole();
                if (role != null && (role.Equals(PdfName.TH) || role.GetValue() == "TH"))
                {
                    // Check if scope attribute already exists
                    var pdfObject = element.GetPdfObject();
                    var attributes = pdfObject.GetAsDictionary(PdfName.A);

                    if (attributes == null || !attributes.ContainsKey(new PdfName("Scope")))
                    {
                        // Determine appropriate scope
                        string scope = DetermineScope(element, headerInfo);

                        // Add scope attribute
                        if (attributes == null)
                        {
                            attributes = new PdfDictionary();
                            pdfObject.Put(PdfName.A, attributes);
                        }

                        attributes.Put(new PdfName("Scope"), new PdfName(scope));
                        fixedCount++;

                        _logger.LogDebug($"[TABLE-SCOPE-FIX] Added scope='{scope}' to TH element");
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
                            fixedCount += await ProcessTableHeaders(childElem, headerInfo);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[TABLE-SCOPE-FIX] Error processing headers: {ex.Message}");
            }

            return await Task.FromResult(fixedCount);
        }

        private string DetermineScope(PdfStructElem headerElement, TableHeaderInfo headerInfo)
        {
            // Check if we have specific scope information for this header
            if (headerInfo.HeaderScopes.ContainsKey(headerElement))
            {
                return headerInfo.HeaderScopes[headerElement];
            }

            // Try to determine based on position in table structure
            var parent = headerElement.GetParent() as PdfStructElem;
            if (parent != null)
            {
                var grandParent = parent.GetParent() as PdfStructElem;
                if (grandParent != null)
                {
                    var gpRole = grandParent.GetRole();
                    if (gpRole != null)
                    {
                        var roleValue = gpRole.GetValue();
                        if (roleValue == "THead" || roleValue == "THEAD")
                        {
                            return "Col"; // Headers in THead are typically column headers
                        }
                    }
                }

                // Check if this is the first cell in a row (likely row header)
                var siblings = parent.GetKids();
                if (siblings != null && siblings.Count > 0 && siblings[0] == headerElement)
                {
                    // First cell in row, check if other cells are TD
                    var hasTDSiblings = siblings.Skip(1).Any(s =>
                    {
                        if (s is PdfStructElem elem)
                        {
                            var r = elem.GetRole();
                            return r != null && (r.Equals(PdfName.TD) || r.GetValue() == "TD");
                        }
                        return false;
                    });

                    if (hasTDSiblings)
                    {
                        return "Row"; // First header with TD siblings is a row header
                    }
                }
            }

            // Default to column scope (most common)
            return "Col";
        }

        private class TableHeaderInfo
        {
            public bool HasTHead { get; set; }
            public bool FirstRowHasHeaders { get; set; }
            public bool FirstColumnHasHeaders { get; set; }
            public string FirstRowScope { get; set; } = "Col";
            public string FirstColumnScope { get; set; } = "Row";
            public bool FirstRowAnalyzed { get; set; }
            public Dictionary<PdfStructElem, string> HeaderScopes { get; set; } = new Dictionary<PdfStructElem, string>();
        }
    }
}
