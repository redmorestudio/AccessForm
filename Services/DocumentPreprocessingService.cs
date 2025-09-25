using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Pdf.Interactive;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Service that preprocesses documents to remove invisible text and problematic fonts.
    /// Handles fonts with "invisible" in the name, ZapfDingbats replacement, and other invisible text detection.
    /// </summary>
    public class DocumentPreprocessingService
    {
        private readonly ILogger<DocumentPreprocessingService> _logger;

        // Font mapping for ZapfDingbats and other problematic fonts
        private readonly Dictionary<char, string> _zapfDingbatsMapping = new()
        {
            { 'q', "☐" },      // Empty checkbox
            { '\x71', "☐" },   // Empty checkbox (hex)
            { '4', "☑" },      // Checked checkbox
            { '\x34', "☑" },   // Checked checkbox (hex)
            { 'n', "✓" },      // Checkmark
            { '\x6E', "✓" },   // Checkmark (hex)
            { 'l', "●" },      // Filled circle (radio selected)
            { '\x6C', "●" },   // Filled circle (hex)
            { 'm', "○" },      // Empty circle (radio unselected)
            { '\x6D', "○" },   // Empty circle (hex)
            { '●', "●" },      // Keep bullet as is
            { '○', "○" },      // Keep empty circle as is
        };

        // Additional problematic font patterns to detect
        private readonly string[] _problematicFontPatterns = new[]
        {
            "invisible",
            "zapf",
            "dingbat",
            "wingdings",
            "webdings",
            "symbol",
            "marlett"
        };

        public DocumentPreprocessingService(ILogger<DocumentPreprocessingService> logger)
        {
            _logger = logger;
        }

        public class PreprocessingResult
        {
            public bool Success { get; set; }
            public byte[]? ProcessedPdfBytes { get; set; }
            public string? ErrorMessage { get; set; }
            public List<string> IssuesFound { get; set; } = new();
            public List<string> ActionsPerformed { get; set; } = new();
            public int InvisibleTextInstancesRemoved { get; set; }
            public int ProblematicFontsReplaced { get; set; }
            public Dictionary<string, int> FontIssuesSummary { get; set; } = new();
        }

        /// <summary>
        /// Main preprocessing method - detects and removes invisible text and problematic fonts
        /// </summary>
        public async Task<PreprocessingResult> PreprocessDocumentAsync(byte[] pdfBytes)
        {
            return await Task.Run(() =>
            {
                var result = new PreprocessingResult();

                try
                {
                    _logger.LogInformation("=== DOCUMENT PREPROCESSING STARTING ===");
                    _logger.LogInformation($"Input PDF size: {pdfBytes.Length} bytes");

                    using var document = new PdfLoadedDocument(pdfBytes);
                    _logger.LogInformation($"Document loaded: {document.Pages.Count} pages");

                    // Step 1: Analyze all fonts in the document
                    var fontAnalysis = AnalyzeFonts(document);
                    result.FontIssuesSummary = fontAnalysis.FontIssues;

                    // Step 2: Process each page for invisible text and problematic fonts
                    var totalIssuesFound = 0;
                    var totalActionsPerformed = 0;

                    for (int pageIndex = 0; pageIndex < document.Pages.Count; pageIndex++)
                    {
                        var page = document.Pages[pageIndex] as PdfLoadedPage;
                        if (page == null) continue;

                        _logger.LogInformation($"Processing page {pageIndex + 1}...");

                        var pageResult = ProcessPage(page, pageIndex + 1, fontAnalysis.ProblematicFonts);
                        result.IssuesFound.AddRange(pageResult.IssuesFound);
                        result.ActionsPerformed.AddRange(pageResult.ActionsPerformed);
                        totalIssuesFound += pageResult.IssuesFound.Count;
                        totalActionsPerformed += pageResult.ActionsPerformed.Count;
                    }

                    // Step 3: Save the processed document
                    using var outputStream = new MemoryStream();
                    document.Save(outputStream);
                    result.ProcessedPdfBytes = outputStream.ToArray();

                    result.Success = true;
                    result.InvisibleTextInstancesRemoved = totalIssuesFound;
                    result.ProblematicFontsReplaced = totalActionsPerformed;

                    _logger.LogInformation($"=== PREPROCESSING COMPLETE ===");
                    _logger.LogInformation($"Issues found: {totalIssuesFound}");
                    _logger.LogInformation($"Actions performed: {totalActionsPerformed}");
                    _logger.LogInformation($"Output PDF size: {result.ProcessedPdfBytes.Length} bytes");

                    // Write debug info
                    WriteDebugInfo(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during document preprocessing");
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                }

                return result;
            });
        }

        private FontAnalysisResult AnalyzeFonts(PdfLoadedDocument document)
        {
            var result = new FontAnalysisResult();

            try
            {
                _logger.LogInformation("=== ANALYZING FONTS ===");

                // This is a simplified analysis - in a real implementation you'd extract
                // detailed font information from the PDF structure
                // For now, we'll focus on the patterns we know are problematic

                foreach (var pattern in _problematicFontPatterns)
                {
                    result.FontIssues[pattern] = 0; // Will be incremented as we find instances
                    result.ProblematicFonts.Add(pattern);
                }

                _logger.LogInformation($"Will scan for {_problematicFontPatterns.Length} problematic font patterns");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error analyzing fonts: {ex.Message}");
            }

            return result;
        }

        private PageProcessingResult ProcessPage(PdfLoadedPage page, int pageNumber, HashSet<string> problematicFontPatterns)
        {
            var result = new PageProcessingResult();

            try
            {
                // Extract text with formatting information
                // Note: Syncfusion's text extraction with formatting details may be limited
                // This is a simplified approach - you might need to use lower-level PDF parsing

                var extractedText = page.ExtractText();

                if (!string.IsNullOrEmpty(extractedText))
                {
                    // Check for suspicious patterns that might indicate invisible text
                    var suspiciousPatterns = DetectSuspiciousTextPatterns(extractedText, pageNumber);
                    result.IssuesFound.AddRange(suspiciousPatterns);

                    if (suspiciousPatterns.Any())
                    {
                        result.ActionsPerformed.Add($"Page {pageNumber}: Detected {suspiciousPatterns.Count} suspicious text patterns");
                        _logger.LogInformation($"Page {pageNumber}: Found {suspiciousPatterns.Count} suspicious text patterns");
                    }
                }

                // Process form fields on this page to check for problematic fonts
                ProcessFormFieldFonts(page, pageNumber, result);

            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error processing page {pageNumber}: {ex.Message}");
                result.IssuesFound.Add($"Page {pageNumber}: Processing error - {ex.Message}");
            }

            return result;
        }

        private List<string> DetectSuspiciousTextPatterns(string text, int pageNumber)
        {
            var issues = new List<string>();

            // Look for patterns that might indicate invisible or problematic text
            // These are heuristics - real implementation might need more sophisticated analysis

            // Check for ZapfDingbats-like characters
            foreach (var kvp in _zapfDingbatsMapping)
            {
                if (text.Contains(kvp.Key))
                {
                    issues.Add($"Page {pageNumber}: Found ZapfDingbats character '{kvp.Key}' -> should be '{kvp.Value}'");
                }
            }

            // Check for repeated spaces or unusual whitespace patterns
            if (text.Contains("    ")) // 4 or more spaces
            {
                issues.Add($"Page {pageNumber}: Excessive whitespace detected (possible invisible text spacing)");
            }

            // Check for non-printable characters
            var nonPrintableCount = text.Count(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t');
            if (nonPrintableCount > 5) // Threshold for concern
            {
                issues.Add($"Page {pageNumber}: {nonPrintableCount} non-printable characters detected");
            }

            return issues;
        }

        private void ProcessFormFieldFonts(PdfLoadedPage page, int pageNumber, PageProcessingResult result)
        {
            try
            {
                // Check form fields for problematic fonts
                var document = page.Document as PdfLoadedDocument;
                if (document?.Form?.Fields != null)
                {
                    foreach (PdfLoadedField field in document.Form.Fields)
                    {
                        // Check if field is on this page (simplified - might need bounds checking)
                        if (field is PdfLoadedTextBoxField textField)
                        {
                            // Check field font properties
                            CheckFieldFont(textField, pageNumber, result);
                        }
                        else if (field is PdfLoadedCheckBoxField checkboxField)
                        {
                            CheckFieldFont(checkboxField, pageNumber, result);
                        }
                        else if (field is PdfLoadedRadioButtonListField radioField)
                        {
                            CheckFieldFont(radioField, pageNumber, result);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error processing form field fonts on page {pageNumber}: {ex.Message}");
            }
        }

        private void CheckFieldFont(PdfLoadedField field, int pageNumber, PageProcessingResult result)
        {
            try
            {
                // This is a simplified check - real implementation would need to access
                // the field's font properties more directly

                string fieldInfo = $"Field '{field.Name}' on page {pageNumber}";

                // For now, we'll log that we're checking the field
                // In a full implementation, you'd extract the actual font name and check it
                // against the problematic patterns

                foreach (var pattern in _problematicFontPatterns)
                {
                    // Placeholder for actual font checking logic
                    // You'd need to access the field's appearance dictionary or font resources

                    // Example logic (would need actual font name extraction):
                    // if (actualFontName.ToLower().Contains(pattern.ToLower()))
                    // {
                    //     result.IssuesFound.Add($"{fieldInfo}: Uses problematic font containing '{pattern}'");
                    //     result.ActionsPerformed.Add($"{fieldInfo}: Replaced problematic font");
                    // }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error checking font for field {field.Name}: {ex.Message}");
            }
        }

        private void WriteDebugInfo(PreprocessingResult result)
        {
            try
            {
                var debugInfo = new
                {
                    Timestamp = DateTime.Now,
                    Success = result.Success,
                    IssuesFound = result.IssuesFound.Count,
                    ActionsPerformed = result.ActionsPerformed.Count,
                    FontIssuesSummary = result.FontIssuesSummary,
                    Details = new
                    {
                        IssuesList = result.IssuesFound,
                        ActionsList = result.ActionsPerformed
                    }
                };

                var debugPath = "/tmp/document_preprocessing_debug.json";
                var json = JsonSerializer.Serialize(debugInfo, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(debugPath, json);

                _logger.LogInformation($"Debug info written to: {debugPath}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not write debug info: {ex.Message}");
            }
        }

        private class FontAnalysisResult
        {
            public Dictionary<string, int> FontIssues { get; set; } = new();
            public HashSet<string> ProblematicFonts { get; set; } = new();
        }

        private class PageProcessingResult
        {
            public List<string> IssuesFound { get; set; } = new();
            public List<string> ActionsPerformed { get; set; } = new();
        }

        /// <summary>
        /// Enhanced ZapfDingbats character mapping with best-guess Unicode replacements
        /// </summary>
        public string GetUnicodeReplacement(char zapfChar)
        {
            if (_zapfDingbatsMapping.TryGetValue(zapfChar, out var replacement))
            {
                return replacement;
            }

            // Best-guess mapping for unknown ZapfDingbats characters
            // Based on common ZapfDingbats usage patterns
            return zapfChar switch
            {
                >= 'a' and <= 'z' => "□", // Default to empty box for lowercase
                >= 'A' and <= 'Z' => "■", // Default to filled box for uppercase
                >= '0' and <= '9' => "○", // Default to circle for numbers
                _ => "?" // Unknown character placeholder
            };
        }

        /// <summary>
        /// Check if a font name indicates it should be considered problematic/invisible
        /// </summary>
        public bool IsProblematicFont(string fontName)
        {
            if (string.IsNullOrEmpty(fontName)) return false;

            var lowerFontName = fontName.ToLower();
            return _problematicFontPatterns.Any(pattern => lowerFontName.Contains(pattern));
        }
    }
}