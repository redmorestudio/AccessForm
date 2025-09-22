using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Interactive;
using Syncfusion.Pdf.Parsing;

namespace AccessFormServer.Services
{
    /// <summary>
    /// Converts PDF to structured Markdown preserving form layout
    /// </summary>
    public class PdfToMarkdownConverter
    {
        private readonly ILogger<PdfToMarkdownConverter> _logger;
        
        public PdfToMarkdownConverter(ILogger<PdfToMarkdownConverter> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger), "Logger cannot be null");
        }
        
        /// <summary>
        /// Convert PDF to Markdown with form structure preserved
        /// </summary>
        public string ConvertToMarkdown(byte[] pdfBytes)
        {
            try
            {
                // First, try using Marker for better text extraction
                var markerMarkdown = TryConvertWithMarker(pdfBytes);
                if (!string.IsNullOrEmpty(markerMarkdown))
                {
                    _logger.LogInformation("Successfully converted PDF using Marker");
                    return markerMarkdown;
                }
                
                // Fallback to Syncfusion if Marker fails
                _logger.LogInformation("Falling back to Syncfusion PDF extraction");
                var markdown = new StringBuilder();
                
                using (var stream = new MemoryStream(pdfBytes))
                using (var pdfDoc = new PdfLoadedDocument(stream))
                {
                    // Add document metadata
                    markdown.AppendLine("# PDF Form Document");
                    markdown.AppendLine($"**Pages:** {pdfDoc.Pages.Count}");
                    markdown.AppendLine();
                    
                    // Process each page
                    for (int pageIndex = 0; pageIndex < pdfDoc.Pages.Count; pageIndex++)
                    {
                        var page = pdfDoc.Pages[pageIndex] as PdfLoadedPage;
                        
                        markdown.AppendLine($"## Page {pageIndex + 1}");
                        markdown.AppendLine();
                        
                        // Extract text with structure
                        string pageText = ExtractPageTextWithStructure(page);
                        
                        // Process text to identify form structure
                        var lines = pageText.Split('\n');
                        bool inFormSection = false;
                        
                        foreach (var line in lines)
                        {
                            var trimmedLine = line.Trim();
                            
                            // Skip empty lines unless in form section
                            if (string.IsNullOrWhiteSpace(trimmedLine) && !inFormSection)
                                continue;
                            
                            // Detect checkboxes - look for patterns like □, ☐, [ ], ( )
                            if (ContainsCheckbox(trimmedLine))
                            {
                                inFormSection = true;
                                markdown.AppendLine(FormatCheckboxLine(trimmedLine));
                            }
                            // Detect form fields - look for underscores, colons, etc.
                            else if (IsFormField(trimmedLine))
                            {
                                inFormSection = true;
                                markdown.AppendLine(FormatFormField(trimmedLine));
                            }
                            // Detect section headers
                            else if (IsSectionHeader(trimmedLine))
                            {
                                markdown.AppendLine($"### {trimmedLine}");
                                inFormSection = false;
                            }
                            // Regular text
                            else
                            {
                                if (inFormSection && string.IsNullOrWhiteSpace(trimmedLine))
                                {
                                    markdown.AppendLine("___________"); // Indicate blank field
                                }
                                else
                                {
                                    markdown.AppendLine(trimmedLine);
                                }
                            }
                        }
                        
                        markdown.AppendLine();
                        markdown.AppendLine("---");
                        markdown.AppendLine();
                    }
                    
                    // Add form fields section if present
                    if (pdfDoc.Form != null && pdfDoc.Form.Fields.Count > 0)
                    {
                        markdown.AppendLine("## Detected Form Fields");
                        markdown.AppendLine();
                        
                        foreach (PdfLoadedField field in pdfDoc.Form.Fields)
                        {
                            var fieldType = GetFieldTypeString(field);
                            var fieldName = field.Name ?? "Unnamed";
                            
                            markdown.AppendLine($"- **{fieldName}** [{fieldType}]");
                            
                            if (field is PdfLoadedCheckBoxField checkbox)
                            {
                                markdown.AppendLine($"  - [ ] Checkbox");
                            }
                            else if (field is PdfLoadedRadioButtonListField radio)
                            {
                                markdown.AppendLine($"  - ( ) Radio button");
                                foreach (PdfLoadedRadioButtonItem item in radio.Items)
                                {
                                    markdown.AppendLine($"    - ( ) {item.Value}");
                                }
                            }
                            else if (field is PdfLoadedComboBoxField combo)
                            {
                                markdown.AppendLine($"  - Dropdown with options:");
                                foreach (PdfLoadedListFieldItem item in combo.Items)
                                {
                                    markdown.AppendLine($"    - Option");
                                }
                            }
                            else if (field is PdfLoadedTextBoxField textField)
                            {
                                var tooltip = textField.ToolTip ?? "";
                                if (!string.IsNullOrEmpty(tooltip))
                                {
                                    markdown.AppendLine($"  - Tooltip: {tooltip}");
                                }
                                markdown.AppendLine($"  - _________________________");
                            }
                        }
                    }
                }
                
                _logger.LogInformation($"Converted PDF to Markdown: {markdown.Length} characters");
                return markdown.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to convert PDF to Markdown");
                return "# Error\nFailed to convert PDF to Markdown: " + ex.Message;
            }
        }
        
        private string ExtractPageTextWithStructure(PdfLoadedPage page)
        {
            try
            {
                // Extract text preserving layout
                var text = page.ExtractText(true); // true = preserve layout
                
                // If that doesn't work well, try regular extraction
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = page.ExtractText();
                }
                
                return text ?? "";
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to extract text from page: {ex.Message}");
                return "";
            }
        }
        
        private bool ContainsCheckbox(string line)
        {
            // Common checkbox patterns
            string[] checkboxPatterns = { "□", "☐", "☑", "☒", "[ ]", "[x]", "[X]", "( )", "(x)", "(X)" };
            
            foreach (var pattern in checkboxPatterns)
            {
                if (line.Contains(pattern))
                    return true;
            }
            
            // Also check for lines that start with checkbox-like indicators
            if (line.StartsWith("Check ") || line.Contains("Check if") || line.Contains("Select "))
                return true;
            
            return false;
        }
        
        private string FormatCheckboxLine(string line)
        {
            // Replace checkbox symbols with markdown checkbox
            line = line.Replace("□", "- [ ]");
            line = line.Replace("☐", "- [ ]");
            line = line.Replace("☑", "- [x]");
            line = line.Replace("☒", "- [x]");
            
            // If no markdown checkbox yet, add it
            if (!line.StartsWith("- [ ]") && !line.StartsWith("- [x]"))
            {
                line = $"- [ ] {line}";
            }
            
            return line;
        }
        
        private bool IsFormField(string line)
        {
            // Common form field patterns
            if (line.EndsWith(":") && !line.StartsWith("http"))
                return true;
            
            if (line.Contains("____") || line.Contains("...."))
                return true;
            
            // Common field labels
            string[] fieldIndicators = { 
                "Name:", "Date:", "Email:", "Phone:", "Address:", 
                "SSN:", "EIN:", "Case", "Account", "Reference",
                "Signature", "Initial", "Amount", "Number"
            };
            
            foreach (var indicator in fieldIndicators)
            {
                if (line.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            
            return false;
        }
        
        private string FormatFormField(string line)
        {
            // Enhance form field formatting
            if (line.EndsWith(":"))
            {
                return $"**{line}** ___________________________";
            }
            
            // Replace underscores with markdown equivalent
            if (line.Contains("____"))
            {
                line = line.Replace("____", "___________________________");
            }
            
            return $"**{line}**";
        }
        
        private bool IsSectionHeader(string line)
        {
            // Detect section headers (all caps, or specific keywords)
            if (line.Length > 3 && line == line.ToUpper() && !line.Contains("□") && !line.Contains("("))
                return true;
            
            string[] headerKeywords = { "Section", "Part", "Information", "Details", "Authorization" };
            foreach (var keyword in headerKeywords)
            {
                if (line.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            
            return false;
        }
        
        private string GetFieldTypeString(PdfLoadedField field)
        {
            return field switch
            {
                PdfLoadedTextBoxField => "Text",
                PdfLoadedCheckBoxField => "Checkbox",
                PdfLoadedRadioButtonListField => "Radio",
                PdfLoadedComboBoxField => "Dropdown",
                PdfLoadedListBoxField => "Listbox",
                PdfLoadedSignatureField => "Signature",
                _ => "Unknown"
            };
        }
        
        private string TryConvertWithMarker(byte[] pdfBytes)
        {
            try
            {
                // Save PDF to temp file
                var tempPdfPath = Path.Combine(Path.GetTempPath(), $"temp_{Guid.NewGuid()}.pdf");
                File.WriteAllBytes(tempPdfPath, pdfBytes);
                
                try
                {
                    // Run marker_wrapper.py
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "python3",
                        Arguments = $"marker_wrapper.py \"{tempPdfPath}\"",
                        WorkingDirectory = Directory.GetCurrentDirectory(),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    using var process = Process.Start(startInfo);
                    if (process == null)
                    {
                        _logger.LogWarning("Failed to start Marker process");
                        return null;
                    }
                    
                    // Set a timeout of 30 seconds
                    if (!process.WaitForExit(30000))
                    {
                        process.Kill();
                        _logger.LogWarning("Marker process timed out");
                        return null;
                    }
                    
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    
                    if (!string.IsNullOrEmpty(error))
                    {
                        _logger.LogWarning($"Marker stderr: {error}");
                    }
                    
                    if (process.ExitCode != 0)
                    {
                        _logger.LogWarning($"Marker failed with exit code {process.ExitCode}");
                        return null;
                    }
                    
                    // Parse JSON response
                    try
                    {
                        var result = JsonSerializer.Deserialize<MarkerResult>(output);
                        if (result?.success == true && !string.IsNullOrEmpty(result.markdown))
                        {
                            // Log metadata if available
                            if (result.metadata != null)
                            {
                                _logger.LogInformation($"Marker metadata: {JsonSerializer.Serialize(result.metadata)}");
                                
                                // Check if we have form field information
                                if (result.metadata.ContainsKey("form_fields"))
                                {
                                    _logger.LogInformation($"Marker detected form fields: {JsonSerializer.Serialize(result.metadata["form_fields"])}");
                                }
                                
                                // Check for block-level positional information
                                if (result.metadata.ContainsKey("pages"))
                                {
                                    _logger.LogInformation($"Marker found {result.metadata["page_count"]} pages with block information");
                                }
                            }
                            
                            // Enhance the markdown with form field indicators
                            return EnhanceMarkdownWithFormMarkers(result.markdown);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning($"Failed to parse Marker output: {ex.Message}");
                    }
                }
                finally
                {
                    // Clean up temp file
                    if (File.Exists(tempPdfPath))
                    {
                        File.Delete(tempPdfPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Marker conversion failed: {ex.Message}");
            }
            
            return null;
        }
        
        private string EnhanceMarkdownWithFormMarkers(string markdown)
        {
            // Add markers to help identify form fields
            var enhanced = new StringBuilder();
            enhanced.AppendLine("# PDF Form Document (Marker Enhanced)");
            enhanced.AppendLine();
            
            var lines = markdown.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                // Detect form field patterns and highlight them
                if (trimmed.EndsWith(":") && !trimmed.StartsWith("http"))
                {
                    enhanced.AppendLine($"**[FIELD]** {line}");
                }
                else if (trimmed.Contains("___") || trimmed.Contains("..."))
                {
                    enhanced.AppendLine($"**[INPUT]** {line}");
                }
                else if (ContainsCheckbox(trimmed))
                {
                    enhanced.AppendLine($"**[CHECKBOX]** {line}");
                }
                else
                {
                    enhanced.AppendLine(line);
                }
            }
            
            return enhanced.ToString();
        }
        
        private class MarkerResult
        {
            public bool success { get; set; }
            public string markdown { get; set; }
            public Dictionary<string, object> metadata { get; set; }
            public string error { get; set; }
        }
    }
}