using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;
using Syncfusion.DocIORenderer;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using Syncfusion.Pdf.Interactive;
using Syncfusion.Drawing;
using AccessFormServer.Services;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Service for converting Word documents to PDF with form fields created during conversion
    /// This preserves JavaScript and conditional logic while adding AI-detected fields
    /// </summary>
    public class WordToPdfWithFieldsService
    {
        private readonly ILogger<WordToPdfWithFieldsService> _logger;
        private readonly FormFieldCreationService _fieldCreationService;
        private readonly AnthropicService _anthropicService;

        public WordToPdfWithFieldsService(
            ILogger<WordToPdfWithFieldsService> logger,
            FormFieldCreationService fieldCreationService,
            AnthropicService anthropicService)
        {
            _logger = logger;
            _fieldCreationService = fieldCreationService;
            _anthropicService = anthropicService;
        }

        /// <summary>
        /// Converts Word to PDF with AI-detected form fields while preserving existing controls and scripts
        /// </summary>
        public async Task<byte[]> ConvertWordToPdfWithFields(
            byte[] wordBytes, 
            string fileName,
            bool preserveExistingFields = true)
        {
            _logger.LogInformation($"Starting Word to PDF conversion with AI field detection for {fileName}");

            // Step 1: Extract text from Word for Claude analysis
            string extractedText = ExtractTextFromWord(wordBytes);
            _logger.LogInformation($"Extracted {extractedText.Length} characters from Word document");

            // Step 2: Send to Claude for field detection
            List<AnthropicService.FieldAnalysisResult> detectedFields = null;
            try
            {
                var aiAnalysis = await _anthropicService.AnalyzeFormFieldsAsync(extractedText);
                detectedFields = _anthropicService.ParseAnalysisResult(aiAnalysis);
                _logger.LogInformation($"Claude detected {detectedFields?.Count ?? 0} form fields");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get field detection from Claude");
                detectedFields = new List<AnthropicService.FieldAnalysisResult>();
            }

            // Step 3: Convert Word to PDF with special settings to preserve form controls
            byte[] pdfBytes;
            using (var inputStream = new MemoryStream(wordBytes))
            using (var wordDoc = new WordDocument(inputStream, FormatType.Docx))
            {
                // Configure renderer to preserve form fields and create accessible PDF
                using (var renderer = new DocIORenderer())
                {
                    // Enable settings that preserve form controls
                    renderer.Settings.PreserveFormFields = true;
                    renderer.Settings.AutoTag = true; // For accessibility
                    renderer.Settings.EmbedFonts = true;
                    renderer.Settings.EmbedCompleteFonts = false;
                    
                    // Convert to PDF document (not loaded document)
                    using (var pdfDocument = renderer.ConvertToPDF(wordDoc))
                    {
                        // Step 4: Add AI-detected fields to the PDF
                        if (detectedFields != null && detectedFields.Count > 0)
                        {
                            AddDetectedFieldsToPdf(pdfDocument, detectedFields);
                        }

                        // Step 5: Apply document-level accessibility settings
                        ApplyAccessibilitySettings(pdfDocument, fileName);

                        // Save the PDF
                        using (var outputStream = new MemoryStream())
                        {
                            pdfDocument.Save(outputStream);
                            pdfBytes = outputStream.ToArray();
                        }
                    }
                }
            }

            _logger.LogInformation($"Conversion complete. PDF size: {pdfBytes.Length} bytes");
            return pdfBytes;
        }

        private string ExtractTextFromWord(byte[] wordBytes)
        {
            try
            {
                using (var stream = new MemoryStream(wordBytes))
                using (var wordDoc = new WordDocument(stream, FormatType.Docx))
                {
                    // Get text from all sections and paragraphs
                    var text = wordDoc.GetText();
                    return text;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract text from Word document");
                return string.Empty;
            }
        }

        private void AddDetectedFieldsToPdf(
            PdfDocument pdfDocument,
            List<AnthropicService.FieldAnalysisResult> detectedFields)
        {
            _logger.LogInformation($"Adding {detectedFields.Count} AI-detected fields to PDF");

            // Get list of existing field names to avoid duplicates
            var existingFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (pdfDocument.Form != null && pdfDocument.Form.Fields != null)
            {
                foreach (PdfField field in pdfDocument.Form.Fields)
                {
                    if (!string.IsNullOrEmpty(field.Name))
                    {
                        existingFieldNames.Add(field.Name);
                    }
                }
                _logger.LogInformation($"Found {existingFieldNames.Count} existing fields in PDF");
            }

            // Add new fields detected by Claude that don't already exist
            var newFields = detectedFields.Where(df => 
                !string.IsNullOrEmpty(df.FieldName) && 
                !existingFieldNames.Contains(df.FieldName)).ToList();

            if (newFields.Count > 0)
            {
                _logger.LogInformation($"Adding {newFields.Count} new AI-detected fields that don't already exist");
                _fieldCreationService.CreateFormFieldsInNewDocument(pdfDocument, newFields);
            }
            else
            {
                _logger.LogInformation("All detected fields already exist in the document");
            }
        }

        private void ApplyAccessibilitySettings(PdfDocument pdfDocument, string fileName)
        {
            _logger.LogInformation("Applying accessibility settings...");

            try
            {
                // Set document metadata
                pdfDocument.DocumentInformation.Title = Path.GetFileNameWithoutExtension(fileName);
                pdfDocument.DocumentInformation.Language = "en-US";
                pdfDocument.DocumentInformation.Keywords = "form, accessible, Section 508, WCAG";
                pdfDocument.DocumentInformation.Subject = "Accessible Form Document";
                
                // Custom metadata for compliance tracking
                pdfDocument.DocumentInformation.CustomMetadata["AccessibilityStandard"] = "WCAG 2.1 AA";
                pdfDocument.DocumentInformation.CustomMetadata["Section508"] = "Compliant";
                pdfDocument.DocumentInformation.CustomMetadata["ProcessedDate"] = DateTime.Now.ToString("yyyy-MM-dd");
                pdfDocument.DocumentInformation.CustomMetadata["AIEnhanced"] = "true";
                
                // Enable auto-tagging for accessibility (already set in renderer)
                pdfDocument.AutoTag = true;
                
                // Set form fields tab order
                if (pdfDocument.Form != null && pdfDocument.Form.Fields.Count > 0)
                {
                    int tabIndex = 1;
                    foreach (PdfField field in pdfDocument.Form.Fields)
                    {
                        if (field.TabIndex == 0)
                        {
                            field.TabIndex = tabIndex++;
                        }
                        
                        // Add validation for common field types based on name
                        if (field is PdfTextBoxField textField)
                        {
                            AddFieldValidation(textField);
                        }
                    }
                    _logger.LogInformation($"Set tab order for {pdfDocument.Form.Fields.Count} form fields");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying accessibility settings");
            }
        }

        private void AddFieldValidation(PdfTextBoxField textField)
        {
            var fieldName = textField.Name?.ToLower() ?? "";

            try
            {
                // Add format validation for common field types
                if (fieldName.Contains("date"))
                {
                    // Add date format validation
                    var formatAction = new PdfJavaScriptAction(
                        "var re = /^(0[1-9]|1[0-2])\\/(0[1-9]|[12][0-9]|3[01])\\/(19|20)\\d{2}$/;" +
                        "if (!re.test(event.value) && event.value != '') {" +
                        "  app.alert('Please enter date in MM/DD/YYYY format');" +
                        "  event.rc = false;" +
                        "}");
                    textField.Actions.LostFocus = formatAction;
                    _logger.LogInformation($"Added date validation to field '{textField.Name}'");
                }
                else if (fieldName.Contains("phone"))
                {
                    // Add phone format validation
                    var formatAction = new PdfJavaScriptAction(
                        "var re = /^\\(?([0-9]{3})\\)?[-. ]?([0-9]{3})[-. ]?([0-9]{4})$/;" +
                        "if (!re.test(event.value) && event.value != '') {" +
                        "  app.alert('Please enter a valid phone number');" +
                        "  event.rc = false;" +
                        "}");
                    textField.Actions.LostFocus = formatAction;
                    _logger.LogInformation($"Added phone validation to field '{textField.Name}'");
                }
                else if (fieldName.Contains("email"))
                {
                    // Add email validation
                    var formatAction = new PdfJavaScriptAction(
                        "var re = /^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$/;" +
                        "if (!re.test(event.value) && event.value != '') {" +
                        "  app.alert('Please enter a valid email address');" +
                        "  event.rc = false;" +
                        "}");
                    textField.Actions.LostFocus = formatAction;
                    _logger.LogInformation($"Added email validation to field '{textField.Name}'");
                }
                else if (fieldName.Contains("ssn"))
                {
                    // Add SSN format validation and masking
                    textField.Password = true; // Mask the field
                    var formatAction = new PdfJavaScriptAction(
                        "var re = /^\\d{3}-\\d{2}-\\d{4}$/;" +
                        "if (!re.test(event.value) && event.value != '') {" +
                        "  app.alert('Please enter SSN in XXX-XX-XXXX format');" +
                        "  event.rc = false;" +
                        "}");
                    textField.Actions.LostFocus = formatAction;
                    _logger.LogInformation($"Added SSN validation and masking to field '{textField.Name}'");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not add validation to field '{textField.Name}': {ex.Message}");
            }
        }
    }
}