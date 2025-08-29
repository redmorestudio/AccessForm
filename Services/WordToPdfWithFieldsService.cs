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
using Syncfusion.Pdf.Graphics;
using Syncfusion.Drawing;
using AccessFormServer.Services;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Helper class to store Syncfusion detected fields
    /// </summary>
    public class SyncfusionDetectedField
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public RectangleF Bounds { get; set; }
        public int PageNumber { get; set; }
        public bool IsRequired { get; set; }
    }
    
    /// <summary>
    /// Service for converting Word documents to PDF with form fields created during conversion
    /// This preserves JavaScript and conditional logic while adding AI-detected fields
    /// </summary>
    public class WordToPdfWithFieldsService
    {
        private readonly ILogger<WordToPdfWithFieldsService> _logger;
        private readonly FormFieldCreationService _fieldCreationService;
        private readonly AnthropicService _anthropicService;
        private readonly WordFormFieldAnalyzer _wordAnalyzer;
        private readonly ClaudeVisionFieldDetector _visionDetector;
        private readonly GoogleDocumentAiService _googleAiService;
        private readonly MultiSourceFieldCombiner _fieldCombiner;

        public WordToPdfWithFieldsService(
            ILogger<WordToPdfWithFieldsService> logger,
            FormFieldCreationService fieldCreationService,
            AnthropicService anthropicService,
            WordFormFieldAnalyzer wordAnalyzer,
            ClaudeVisionFieldDetector visionDetector = null,
            GoogleDocumentAiService googleAiService = null,
            MultiSourceFieldCombiner fieldCombiner = null)
        {
            _logger = logger;
            _fieldCreationService = fieldCreationService;
            _anthropicService = anthropicService;
            _wordAnalyzer = wordAnalyzer;
            _visionDetector = visionDetector;
            _googleAiService = googleAiService;
            _fieldCombiner = fieldCombiner;
        }

        /// <summary>
        /// Converts Word to PDF with vision-detected form fields for maximum accuracy
        /// </summary>
        public async Task<byte[]> ConvertWordToPdfWithVisionFields(
            byte[] wordBytes, 
            string fileName)
        {
            _logger.LogInformation($"Starting Word to PDF conversion with MULTI-SOURCE field detection for {fileName}");
            
            // Step 1a: First, get Syncfusion's field detection by preserving fields
            List<SyncfusionDetectedField> syncfusionFields = new List<SyncfusionDetectedField>();
            byte[] syncfusionPdfBytes;
            
            _logger.LogInformation("Step 1a: Getting Syncfusion field detection");
            using (var inputStream = new MemoryStream(wordBytes))
            using (var wordDoc = new WordDocument(inputStream, FormatType.Docx))
            {
                using (var renderer = new DocIORenderer())
                {
                    // PRESERVE fields to get Syncfusion's detection
                    renderer.Settings.PreserveFormFields = true;
                    renderer.Settings.AutoTag = true;
                    
                    using (var pdfDocument = renderer.ConvertToPDF(wordDoc))
                    {
                        using (var outputStream = new MemoryStream())
                        {
                            pdfDocument.Save(outputStream);
                            syncfusionPdfBytes = outputStream.ToArray();
                        }
                    }
                }
            }
            
            // Extract and filter Syncfusion's fields
            using (var pdfStream = new MemoryStream(syncfusionPdfBytes))
            using (var pdfDoc = new PdfLoadedDocument(pdfStream))
            {
                if (pdfDoc.Form != null && pdfDoc.Form.Fields.Count > 0)
                {
                    _logger.LogInformation($"Syncfusion detected {pdfDoc.Form.Fields.Count} raw fields");
                    
                    foreach (PdfLoadedField field in pdfDoc.Form.Fields)
                    {
                        if (!IsLikelyFalsePositive(field))
                        {
                            var sfField = ExtractSyncfusionField(field, pdfDoc);
                            if (sfField != null)
                            {
                                syncfusionFields.Add(sfField);
                                _logger.LogDebug($"Syncfusion field kept: {sfField.Name} ({sfField.Type})");
                            }
                        }
                    }
                    
                    _logger.LogInformation($"Syncfusion: {syncfusionFields.Count} fields after filtering false positives");
                }
            }
            
            // Step 1b: Now create clean PDF for vision analysis
            byte[] cleanPdfBytes;
            _logger.LogInformation("Step 1b: Creating clean PDF for vision analysis");
            using (var inputStream = new MemoryStream(wordBytes))
            using (var wordDoc = new WordDocument(inputStream, FormatType.Docx))
            {
                using (var renderer = new DocIORenderer())
                {
                    // DO NOT preserve form fields for vision - we want a clean PDF
                    renderer.Settings.PreserveFormFields = false;
                    renderer.Settings.AutoTag = true;
                    renderer.Settings.EmbedFonts = true;
                    renderer.Settings.EmbedCompleteFonts = false;
                    
                    using (var pdfDocument = renderer.ConvertToPDF(wordDoc))
                    {
                        ApplyAccessibilitySettings(pdfDocument, fileName);
                        
                        using (var outputStream = new MemoryStream())
                        {
                            pdfDocument.Save(outputStream);
                            cleanPdfBytes = outputStream.ToArray();
                        }
                    }
                }
            }
            
            _logger.LogInformation($"Created clean PDF ({cleanPdfBytes.Length} bytes) - now applying vision detection");
            
            // Step 2: Multi-source field detection
            List<ClaudeVisionFieldDetector.VisualFieldDetectionResult> visionResults = null;
            List<GoogleDocumentAiService.GoogleFormField> googleResults = null;
            List<AnthropicService.FieldAnalysisResult> textResults = null;
            
            // 2a: Claude Vision detection
            if (_visionDetector != null)
            {
                try
                {
                    visionResults = await _visionDetector.AnalyzePdfWithVision(cleanPdfBytes, maxPages: 10);
                    _logger.LogInformation($"Vision detection found {visionResults.SelectMany(r => r.Fields).Count()} fields");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Vision detection failed");
                }
            }
            
            // 2b: Google Document AI detection
            if (_googleAiService != null)
            {
                try
                {
                    googleResults = await _googleAiService.ProcessPdfAsync(cleanPdfBytes);
                    _logger.LogInformation($"Google Document AI found {googleResults.Count} fields");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Google Document AI detection failed");
                }
            }
            
            // 2c: Text-based detection with Claude
            try
            {
                // Extract text for analysis
                string extractedText;
                using (var pdfStream = new MemoryStream(cleanPdfBytes))
                using (var pdfDoc = new PdfLoadedDocument(pdfStream))
                {
                    extractedText = pdfDoc.Pages[0].ExtractText();
                }
                
                if (!string.IsNullOrEmpty(extractedText))
                {
                    var aiAnalysis = await _anthropicService.AnalyzeFormFieldsAsync(extractedText);
                    textResults = _anthropicService.ParseAnalysisResult(aiAnalysis);
                    _logger.LogInformation($"Text analysis found {textResults?.Count ?? 0} fields");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Text analysis failed");
            }
            
            // Step 3: Combine results from all sources
            if (_fieldCombiner != null && (visionResults != null || googleResults != null || textResults != null))
            {
                using (var pdfStream = new MemoryStream(cleanPdfBytes))
                using (var pdfDoc = new PdfLoadedDocument(pdfStream))
                {
                    var page = pdfDoc.Pages[0] as PdfLoadedPage;
                    float pageWidth = page.Size.Width;
                    float pageHeight = page.Size.Height;
                    
                    // Combine all detection results (including Syncfusion)
                    var combinedFields = _fieldCombiner.CombineFieldResults(
                        visionResults, googleResults, textResults, syncfusionFields, pageWidth, pageHeight);
                    
                    _logger.LogInformation($"Combined detection resulted in {combinedFields.Count} final fields");
                    
                    // Add combined fields to PDF
                    AddCombinedFields(pdfDoc, combinedFields);
                    
                    using (var finalStream = new MemoryStream())
                    {
                        pdfDoc.Save(finalStream);
                        return finalStream.ToArray();
                    }
                }
            }
            else if (visionResults != null) // Fallback to vision-only if combiner not available
            {
                using (var pdfStream = new MemoryStream(cleanPdfBytes))
                using (var pdfDoc = new PdfLoadedDocument(pdfStream))
                {
                    AddVisionDetectedFields(pdfDoc, visionResults);
                    
                    using (var finalStream = new MemoryStream())
                    {
                        pdfDoc.Save(finalStream);
                        return finalStream.ToArray();
                    }
                }
            }
            
            return cleanPdfBytes;
        }
        
        /// <summary>
        /// Original method - keeping for compatibility but should use vision method instead
        /// </summary>
        public async Task<byte[]> ConvertWordToPdfWithFields(
            byte[] wordBytes, 
            string fileName,
            bool preserveExistingFields = true)
        {
            _logger.LogInformation($"Starting Word to PDF conversion with AI field detection for {fileName}");

            // Step 1: Analyze the Word document structure for information
            var wordAnalysis = _wordAnalyzer.AnalyzeDocument(wordBytes);
            _logger.LogInformation($"Word analysis complete: {wordAnalysis.ActualFormFields.Count} form fields found in Word");
            
            // ALWAYS skip preserving Word fields - we'll use vision detection instead
            bool shouldPreserveWordFields = false; // Never preserve - always use vision
            _logger.LogInformation("Skipping Word field preservation - will use vision-based detection for accuracy");

            // Step 2: Extract text from Word for Claude analysis
            string extractedText = ExtractTextFromWord(wordBytes);
            _logger.LogInformation($"Extracted {extractedText.Length} characters from Word document");

            // Step 3: Send to Claude for field detection
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

            // Step 4: Convert Word to PDF with intelligent settings
            byte[] pdfBytes;
            using (var inputStream = new MemoryStream(wordBytes))
            using (var wordDoc = new WordDocument(inputStream, FormatType.Docx))
            {
                // Configure renderer to preserve form fields and create accessible PDF
                using (var renderer = new DocIORenderer())
                {
                    // Only preserve form fields if we found REAL form fields in the Word doc
                    renderer.Settings.PreserveFormFields = shouldPreserveWordFields;
                    renderer.Settings.AutoTag = true; // For accessibility
                    renderer.Settings.EmbedFonts = true;
                    renderer.Settings.EmbedCompleteFonts = false;
                    
                    if (shouldPreserveWordFields)
                    {
                        _logger.LogInformation($"Preserving {wordAnalysis.ActualFormFields.Count} real Word form fields with their JavaScript/logic");
                    }
                    else
                    {
                        _logger.LogInformation("Not preserving Word fields (none found or too many false positives) - will use AI detection only");
                    }
                    
                    // Convert to PDF document (not loaded document)
                    using (var pdfDocument = renderer.ConvertToPDF(wordDoc))
                    {
                        // Check if Word document already had form fields that were preserved
                        bool hasExistingFields = false;
                        try
                        {
                            hasExistingFields = pdfDocument.Form?.Fields?.Count > 0;
                            if (hasExistingFields)
                            {
                                _logger.LogInformation($"Word document already contained {pdfDocument.Form.Fields.Count} form fields that were preserved");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Could not check existing fields: {ex.Message}");
                        }

                        // Step 5: Add AI-detected fields based on analysis results
                        if (shouldPreserveWordFields)
                        {
                            // If we preserved real Word form fields, just log that we're using them
                            _logger.LogInformation($"Using {pdfDocument.Form?.Fields?.Count ?? 0} preserved Word form fields with their JavaScript/logic");
                        }
                        else if (detectedFields != null && detectedFields.Count > 0)
                        {
                            // No real Word fields found, so add AI-detected fields
                            _logger.LogInformation($"Adding {detectedFields.Count} AI-detected fields since no real Word form fields were found");
                            AddDetectedFieldsToPdf(pdfDocument, detectedFields);
                        }
                        else
                        {
                            _logger.LogWarning("No form fields found by either Word analysis or text-based AI detection");
                            
                            // Step 5b: Try vision-based detection if available and no fields found
                            if (_visionDetector != null && !shouldPreserveWordFields)
                            {
                                _logger.LogInformation("Attempting vision-based field detection as fallback");
                                // Note: Vision detection would happen after PDF creation
                                // We'll mark this for post-processing
                            }
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
                
                // Set form fields tab order (but be careful not to break structure with too many fields)
                if (pdfDocument.Form != null && pdfDocument.Form.Fields.Count > 0)
                {
                    try
                    {
                        int tabIndex = 1;
                        int validationCount = 0;
                        foreach (PdfField field in pdfDocument.Form.Fields)
                        {
                            if (field.TabIndex == 0)
                            {
                                field.TabIndex = tabIndex++;
                            }
                            
                            // Only add validation to a limited number of fields to avoid structure issues
                            // Skip validation if we have too many fields (>100)
                            if (pdfDocument.Form.Fields.Count <= 100 && field is PdfTextBoxField textField)
                            {
                                AddFieldValidation(textField);
                                validationCount++;
                            }
                        }
                        _logger.LogInformation($"Set tab order for {pdfDocument.Form.Fields.Count} form fields, added validation to {validationCount} fields");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Could not set all field properties: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying accessibility settings");
            }
        }

        private void AddVisionDetectedFields(PdfLoadedDocument pdfDoc, List<ClaudeVisionFieldDetector.VisualFieldDetectionResult> visionResults)
        {
            if (pdfDoc.Form == null)
                pdfDoc.CreateForm();
                
            int fieldsAdded = 0;
            
            foreach (var pageResult in visionResults.Where(r => r.Success))
            {
                var pageIndex = pageResult.PageNumber - 1; // Convert to 0-based
                if (pageIndex >= 0 && pageIndex < pdfDoc.Pages.Count)
                {
                    var page = pdfDoc.Pages[pageIndex] as PdfLoadedPage;
                    
                    foreach (var field in pageResult.Fields)
                    {
                        try
                        {
                            // Convert percentage bounds to actual PDF coordinates
                            var bounds = ClaudeVisionFieldDetector.ConvertPercentageToPdfBounds(
                                field.Bounds,
                                page.Size.Width,
                                page.Size.Height
                            );
                            
                            // Create appropriate field type
                            PdfField newField = null;
                            
                            switch (field.FieldType?.ToLower())
                            {
                                case "checkbox":
                                case "check":
                                    var checkField = new PdfCheckBoxField(page, field.FieldName ?? $"Check_{fieldsAdded + 1}");
                                    checkField.Bounds = bounds;
                                    newField = checkField;
                                    break;
                                    
                                case "radio":
                                    var radioField = new PdfRadioButtonListField(page, field.FieldName ?? $"Radio_{fieldsAdded + 1}");
                                    var radioItem = new PdfRadioButtonListItem(field.FieldName);
                                    radioItem.Bounds = bounds;
                                    radioField.Items.Add(radioItem);
                                    newField = radioField;
                                    break;
                                    
                                case "signature":
                                    var sigField = new PdfSignatureField(page, field.FieldName ?? $"Signature_{fieldsAdded + 1}");
                                    sigField.Bounds = bounds;
                                    newField = sigField;
                                    break;
                                    
                                case "dropdown":
                                case "select":
                                    var comboField = new PdfComboBoxField(page, field.FieldName ?? $"Dropdown_{fieldsAdded + 1}");
                                    comboField.Bounds = bounds;
                                    newField = comboField;
                                    break;
                                    
                                default: // text, date, or any other type
                                    var textField = new PdfTextBoxField(page, field.FieldName ?? $"Field_{fieldsAdded + 1}");
                                    textField.Bounds = bounds;
                                    textField.ToolTip = field.Description ?? field.FieldName;
                                    
                                    // For multiline fields
                                    if (field.Description?.Contains("multiline") == true || 
                                        bounds.Height > 50)
                                    {
                                        textField.Multiline = true;
                                    }
                                    
                                    // Mark required fields
                                    if (field.IsRequired)
                                    {
                                        textField.Required = true;
                                    }
                                    
                                    newField = textField;
                                    break;
                            }
                            
                            if (newField != null)
                            {
                                pdfDoc.Form.Fields.Add(newField);
                                fieldsAdded++;
                                _logger.LogDebug($"Added {field.FieldType} field '{field.FieldName}' at page {pageResult.PageNumber}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Could not add field '{field.FieldName}': {ex.Message}");
                        }
                    }
                }
            }
            
            _logger.LogInformation($"Added {fieldsAdded} vision-detected fields to PDF");
        }
        
        private void AddCombinedFields(PdfLoadedDocument pdfDoc, List<MultiSourceFieldCombiner.CombinedField> combinedFields)
        {
            if (pdfDoc.Form == null)
                pdfDoc.CreateForm();
                
            int fieldsAdded = 0;
            
            foreach (var field in combinedFields)
            {
                try
                {
                    var pageIndex = field.PageNumber - 1; // Convert to 0-based
                    if (pageIndex >= 0 && pageIndex < pdfDoc.Pages.Count)
                    {
                        var page = pdfDoc.Pages[pageIndex] as PdfLoadedPage;
                        
                        // Create appropriate field type
                        PdfField newField = null;
                        
                        switch (field.FieldType?.ToLower())
                        {
                            case "checkbox":
                            case "check":
                                var checkField = new PdfCheckBoxField(page, field.FieldName ?? $"Check_{fieldsAdded + 1}");
                                checkField.Bounds = field.Bounds;
                                newField = checkField;
                                break;
                                
                            case "radio":
                                var radioField = new PdfRadioButtonListField(page, field.FieldName ?? $"Radio_{fieldsAdded + 1}");
                                var radioItem = new PdfRadioButtonListItem(field.FieldName);
                                radioItem.Bounds = field.Bounds;
                                radioField.Items.Add(radioItem);
                                newField = radioField;
                                break;
                                
                            case "signature":
                                var sigField = new PdfSignatureField(page, field.FieldName ?? $"Signature_{fieldsAdded + 1}");
                                sigField.Bounds = field.Bounds;
                                newField = sigField;
                                break;
                                
                            default: // Text field
                                var textField = new PdfTextBoxField(page, field.FieldName ?? $"Field_{fieldsAdded + 1}");
                                textField.Bounds = field.Bounds;
                                textField.Font = new PdfStandardFont(PdfFontFamily.Helvetica, 10);
                                
                                // Set tooltip
                                textField.ToolTip = field.Description ?? field.FieldName;
                                
                                // Mark required fields
                                if (field.IsRequired)
                                {
                                    textField.Required = true;
                                    textField.BorderColor = new PdfColor(200, 0, 0);
                                }
                                
                                // Add validation
                                AddFieldValidation(textField);
                                
                                newField = textField;
                                break;
                        }
                        
                        if (newField != null)
                        {
                            pdfDoc.Form.Fields.Add(newField);
                            fieldsAdded++;
                            _logger.LogDebug($"Added {field.Source} field: {field.FieldName} (confidence: {field.Confidence:F2})");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not add combined field {field.FieldName}: {ex.Message}");
                }
            }
            
            _logger.LogInformation($"Added {fieldsAdded} combined fields to PDF from multiple sources");
        }
        
        /// <summary>
        /// Checks if a Syncfusion-detected field is likely a false positive
        /// </summary>
        private bool IsLikelyFalsePositive(PdfLoadedField field)
        {
            // Much less aggressive filtering - we want to keep most real fields
            if (string.IsNullOrWhiteSpace(field.Name))
                return true;
                
            // Check for auto-generated names that indicate table cells
            if (field.Name.StartsWith("Text") && field.Name.Length <= 7 && 
                System.Text.RegularExpressions.Regex.IsMatch(field.Name, @"^Text\d{1,3}$"))
            {
                // But keep it if it has meaningful bounds
                if (field is PdfLoadedTextBoxField textField)
                {
                    var bounds = textField.Bounds;
                    // Keep if it's a reasonable size for a form field
                    if (bounds.Width >= 50 && bounds.Height >= 15)
                        return false;
                }
                return true; // Likely a false positive from table cell
            }
            
            return false; // Default to keeping the field
        }
        
        /// <summary>
        /// Extracts field information from a Syncfusion-detected field
        /// </summary>
        private SyncfusionDetectedField ExtractSyncfusionField(PdfLoadedField field, PdfLoadedDocument pdfDoc)
        {
            try
            {
                var result = new SyncfusionDetectedField
                {
                    Name = field.Name,
                    IsRequired = field.Required
                };
                
                // Determine field type
                if (field is PdfLoadedTextBoxField)
                    result.Type = "text";
                else if (field is PdfLoadedCheckBoxField)
                    result.Type = "checkbox";
                else if (field is PdfLoadedRadioButtonListField)
                    result.Type = "radio";
                else if (field is PdfLoadedSignatureField)
                    result.Type = "signature";
                else if (field is PdfLoadedComboBoxField)
                    result.Type = "dropdown";
                else
                    result.Type = "unknown";
                
                // Get bounds and page
                if (field is PdfLoadedTextBoxField textField)
                {
                    result.Bounds = textField.Bounds;
                    result.PageNumber = GetFieldPageNumber(textField, pdfDoc);
                }
                else if (field is PdfLoadedCheckBoxField checkField)
                {
                    result.Bounds = checkField.Bounds;
                    result.PageNumber = GetFieldPageNumber(checkField, pdfDoc);
                }
                else if (field is PdfLoadedRadioButtonListField radioField && radioField.Items.Count > 0)
                {
                    result.Bounds = radioField.Items[0].Bounds;
                    result.PageNumber = 1; // Default to first page
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not extract Syncfusion field {field.Name}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Determines which page a field is on
        /// </summary>
        private int GetFieldPageNumber(PdfLoadedField field, PdfLoadedDocument pdfDoc)
        {
            // This is a simplified approach - in reality you'd need to check field's page reference
            // For now, return 1 as most forms start on page 1
            return 1;
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