using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Analyzes Word documents to identify REAL form fields vs formatting elements
    /// This helps distinguish between actual interactive fields and visual elements
    /// </summary>
    public class WordFormFieldAnalyzer
    {
        private readonly ILogger<WordFormFieldAnalyzer> _logger;

        public WordFormFieldAnalyzer(ILogger<WordFormFieldAnalyzer> logger)
        {
            _logger = logger;
        }

        public class WordDocumentAnalysis
        {
            public int TotalSections { get; set; }
            public int TotalParagraphs { get; set; }
            public int TotalTables { get; set; }
            public int TotalTextBoxes { get; set; }
            public int TotalShapes { get; set; }
            
            // These are the REAL form fields
            public List<RealFormField> ActualFormFields { get; set; } = new();
            public List<ContentControlInfo> ContentControls { get; set; } = new();
            
            // These might be misinterpreted as fields
            public List<TableCellInfo> TableCells { get; set; } = new();
            public List<TextBoxInfo> TextBoxes { get; set; } = new();
            
            public string Summary { get; set; }
            public bool HasMacros { get; set; }
        }

        public class RealFormField
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public string DefaultValue { get; set; }
            public int SectionIndex { get; set; }
            public int ParagraphIndex { get; set; }
        }

        public class ContentControlInfo
        {
            public string Type { get; set; }
            public string Content { get; set; }
        }

        public class TableCellInfo
        {
            public int TableIndex { get; set; }
            public int RowIndex { get; set; }
            public int CellIndex { get; set; }
            public bool IsEmpty { get; set; }
            public string Content { get; set; }
        }

        public class TextBoxInfo
        {
            public int Index { get; set; }
            public string Content { get; set; }
        }

        /// <summary>
        /// Performs a comprehensive analysis of the Word document structure
        /// </summary>
        public WordDocumentAnalysis AnalyzeDocument(byte[] wordBytes)
        {
            var analysis = new WordDocumentAnalysis();
            
            try
            {
                using (var stream = new MemoryStream(wordBytes))
                using (var wordDoc = new WordDocument(stream, FormatType.Docx))
                {
                    _logger.LogInformation("=== WORD DOCUMENT STRUCTURE ANALYSIS ===");
                    
                    // Count basic structures
                    analysis.TotalSections = wordDoc.Sections.Count;
                    _logger.LogInformation($"Sections: {analysis.TotalSections}");
                    
                    // Check for VBA macros
                    analysis.HasMacros = wordDoc.HasMacros;
                    if (analysis.HasMacros)
                    {
                        _logger.LogWarning("⚠️ Document contains VBA macros - these may include form logic!");
                    }
                    
                    // Analyze each section
                    for (int sectionIdx = 0; sectionIdx < wordDoc.Sections.Count; sectionIdx++)
                    {
                        var section = wordDoc.Sections[sectionIdx];
                        AnalyzeSection(section, sectionIdx, analysis);
                    }
                    
                    // Generate summary
                    analysis.Summary = GenerateSummary(analysis);
                    _logger.LogInformation(analysis.Summary);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze Word document");
                analysis.Summary = $"Analysis failed: {ex.Message}";
            }
            
            return analysis;
        }

        private void AnalyzeSection(WSection section, int sectionIdx, WordDocumentAnalysis analysis)
        {
            _logger.LogInformation($"\n--- Section {sectionIdx + 1} ---");
            
            // Count paragraphs
            int paragraphCount = section.Paragraphs.Count;
            analysis.TotalParagraphs += paragraphCount;
            _logger.LogInformation($"Paragraphs: {paragraphCount}");
            
            // Analyze tables
            int tableCount = section.Tables.Count;
            analysis.TotalTables += tableCount;
            if (tableCount > 0)
            {
                _logger.LogInformation($"Tables: {tableCount}");
                AnalyzeTables(section.Tables, analysis);
            }
            
            // Look for form fields and other entities in paragraphs
            int paragraphIdx = 0;
            foreach (WParagraph para in section.Paragraphs)
            {
                AnalyzeParagraph(para, sectionIdx, paragraphIdx++, analysis);
            }
        }

        private void AnalyzeParagraph(WParagraph para, int sectionIdx, int paragraphIdx, WordDocumentAnalysis analysis)
        {
            try
            {
                // Check each child entity in the paragraph
                foreach (var entity in para.ChildEntities)
                {
                    string entityType = entity.GetType().Name;
                    
                    // Check for form fields
                    if (entity is WFormField formField)
                    {
                        var realField = new RealFormField
                        {
                            Name = formField.Name ?? $"Field_{analysis.ActualFormFields.Count + 1}",
                            Type = GetFormFieldType(formField),
                            SectionIndex = sectionIdx,
                            ParagraphIndex = paragraphIdx
                        };
                        
                        // Get type-specific info
                        if (formField is WTextFormField textField)
                        {
                            realField.DefaultValue = textField.DefaultText;
                            realField.Type = $"Text (MaxLength: {textField.MaximumLength})";
                            _logger.LogInformation($"🎯 Found TEXT form field: {realField.Name}");
                        }
                        else if (formField is WCheckBox checkBox)
                        {
                            realField.DefaultValue = checkBox.Checked ? "Checked" : "Unchecked";
                            realField.Type = "CheckBox";
                            _logger.LogInformation($"☑️ Found CHECKBOX form field: {realField.Name}");
                        }
                        else if (formField is WDropDownFormField dropdown)
                        {
                            realField.Type = $"Dropdown ({dropdown.DropDownItems.Count} items)";
                            _logger.LogInformation($"📋 Found DROPDOWN form field: {realField.Name}");
                        }
                        else
                        {
                            _logger.LogInformation($"🎯 Found form field: {realField.Name} ({entityType})");
                        }
                        
                        analysis.ActualFormFields.Add(realField);
                    }
                    // Check for text boxes
                    else if (entity is WTextBox textBox)
                    {
                        var info = new TextBoxInfo
                        {
                            Index = analysis.TotalTextBoxes++,
                            Content = ExtractTextBoxContent(textBox)
                        };
                        
                        analysis.TextBoxes.Add(info);
                        _logger.LogInformation($"📦 Found text box: {info.Content?.Substring(0, Math.Min(30, info.Content?.Length ?? 0))}");
                    }
                    // Check for shapes
                    else if (entityType.Contains("Shape"))
                    {
                        analysis.TotalShapes++;
                        _logger.LogInformation($"🔷 Found shape: {entityType}");
                    }
                    // Check for content controls
                    else if (entity is IInlineContentControl)
                    {
                        var controlInfo = new ContentControlInfo
                        {
                            Type = entityType
                        };
                        
                        analysis.ContentControls.Add(controlInfo);
                        _logger.LogInformation($"📋 Found content control: {controlInfo.Type}");
                    }
                    // Log other entity types for debugging
                    else if (entityType != "WTextRange" && entityType != "Break")
                    {
                        _logger.LogDebug($"  Found entity: {entityType}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not fully analyze paragraph {paragraphIdx}: {ex.Message}");
            }
        }

        private void AnalyzeTables(IWTableCollection tables, WordDocumentAnalysis analysis)
        {
            int tableIdx = 0;
            foreach (WTable table in tables)
            {
                int emptyCells = 0;
                int totalCells = 0;
                
                for (int rowIdx = 0; rowIdx < table.Rows.Count; rowIdx++)
                {
                    var row = table.Rows[rowIdx];
                    for (int cellIdx = 0; cellIdx < row.Cells.Count; cellIdx++)
                    {
                        var cell = row.Cells[cellIdx];
                        totalCells++;
                        string cellText = ExtractCellText(cell);
                        
                        // Track empty cells that might be interpreted as form fields
                        if (string.IsNullOrWhiteSpace(cellText))
                        {
                            emptyCells++;
                            analysis.TableCells.Add(new TableCellInfo
                            {
                                TableIndex = tableIdx,
                                RowIndex = rowIdx,
                                CellIndex = cellIdx,
                                IsEmpty = true
                            });
                        }
                        
                        // Also check for form fields within table cells
                        foreach (WParagraph para in cell.Paragraphs)
                        {
                            foreach (var entity in para.ChildEntities)
                            {
                                if (entity is WFormField formField)
                                {
                                    _logger.LogInformation($"🎯 Found form field IN TABLE: {formField.Name}");
                                    // Add if not already found
                                    if (!analysis.ActualFormFields.Any(f => f.Name == formField.Name))
                                    {
                                        analysis.ActualFormFields.Add(new RealFormField
                                        {
                                            Name = formField.Name ?? $"TableField_{analysis.ActualFormFields.Count + 1}",
                                            Type = $"{GetFormFieldType(formField)} (in table)",
                                            SectionIndex = -1,
                                            ParagraphIndex = -1
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
                
                _logger.LogInformation($"  Table {tableIdx + 1}: {table.Rows.Count} rows, {emptyCells}/{totalCells} empty cells");
                if (emptyCells > 10)
                {
                    _logger.LogWarning($"    ⚠️ Table has {emptyCells} empty cells - likely source of false positives!");
                }
                
                tableIdx++;
            }
        }

        private string GetFormFieldType(WFormField field)
        {
            if (field is WTextFormField) return "TextBox";
            if (field is WCheckBox) return "CheckBox";
            if (field is WDropDownFormField) return "DropDown";
            return field.GetType().Name.Replace("W", "").Replace("FormField", "");
        }

        private string ExtractTextBoxContent(WTextBox textBox)
        {
            try
            {
                var body = textBox.TextBoxBody;
                if (body != null && body.Paragraphs.Count > 0)
                {
                    return body.Paragraphs[0].Text;
                }
            }
            catch { }
            return "";
        }

        private string ExtractCellText(WTableCell cell)
        {
            try
            {
                if (cell.Paragraphs.Count > 0)
                {
                    var texts = new List<string>();
                    foreach (WParagraph para in cell.Paragraphs)
                    {
                        if (!string.IsNullOrWhiteSpace(para.Text))
                        {
                            texts.Add(para.Text.Trim());
                        }
                    }
                    return string.Join(" ", texts);
                }
            }
            catch { }
            return "";
        }

        private string GenerateSummary(WordDocumentAnalysis analysis)
        {
            var summary = "\n=== ANALYSIS SUMMARY ===\n";
            summary += $"✅ REAL Form Fields Found: {analysis.ActualFormFields.Count}\n";
            
            if (analysis.ActualFormFields.Count > 0)
            {
                summary += "\nActual form fields:\n";
                foreach (var field in analysis.ActualFormFields)
                {
                    summary += $"  - {field.Name} ({field.Type})";
                    if (!string.IsNullOrEmpty(field.DefaultValue))
                    {
                        summary += $" [Default: {field.DefaultValue}]";
                    }
                    summary += "\n";
                }
            }
            else
            {
                summary += "🔍 NO REAL FORM FIELDS FOUND!\n";
            }
            
            summary += $"\n📊 Document Structure:\n";
            summary += $"  - Sections: {analysis.TotalSections}\n";
            summary += $"  - Paragraphs: {analysis.TotalParagraphs}\n";
            summary += $"  - Tables: {analysis.TotalTables}\n";
            summary += $"  - Text Boxes: {analysis.TotalTextBoxes}\n";
            summary += $"  - Shapes: {analysis.TotalShapes}\n";
            summary += $"  - Content Controls: {analysis.ContentControls.Count}\n";
            
            if (analysis.HasMacros)
            {
                summary += "\n⚠️ DOCUMENT HAS VBA MACROS\n";
            }
            
            summary += $"\n⚠️ Potential False Positives When Converting:\n";
            int emptyTableCells = analysis.TableCells.Count(tc => tc.IsEmpty);
            summary += $"  - Empty table cells: {emptyTableCells}";
            if (emptyTableCells > 50)
            {
                summary += " (MAJOR SOURCE OF FALSE POSITIVES!)";
            }
            summary += "\n";
            summary += $"  - Text boxes: {analysis.TextBoxes.Count}\n";
            
            if (analysis.ActualFormFields.Count == 0 && emptyTableCells > 0)
            {
                summary += "\n🔍 RECOMMENDATION:\n";
                summary += "This document has NO real form fields but many table cells.\n";
                summary += "The tables are likely being misinterpreted as form fields.\n";
                summary += "Consider using visual detection (Claude Vision) to identify actual fillable areas.\n";
            }
            
            return summary;
        }
    }
}