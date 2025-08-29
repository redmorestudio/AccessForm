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
using WordToPdfConverter.Models;
using PDFtoImage;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Configurable field detection service that allows choosing which detection methods to use
    /// </summary>
    public class ConfigurableFieldDetectionService
    {
        private readonly ILogger<ConfigurableFieldDetectionService> _logger;
        private readonly FormFieldCreationService _fieldCreationService;
        private readonly AnthropicService _anthropicService;
        private readonly WordFormFieldAnalyzer _wordAnalyzer;
        private readonly ClaudeVisionFieldDetector _visionDetector;
        private readonly GoogleDocumentAiService _googleAiService;
        private readonly ClaudeBoundingBoxValidator _boundingBoxValidator;
        private readonly NLPLabelGenerator _nlpGenerator;
        private int _fieldCounter = 0;

        public ConfigurableFieldDetectionService(
            ILogger<ConfigurableFieldDetectionService> logger,
            FormFieldCreationService fieldCreationService,
            AnthropicService anthropicService,
            WordFormFieldAnalyzer wordAnalyzer,
            ClaudeVisionFieldDetector visionDetector,
            GoogleDocumentAiService googleAiService,
            ClaudeBoundingBoxValidator boundingBoxValidator,
            NLPLabelGenerator nlpGenerator)
        {
            _logger = logger;
            _fieldCreationService = fieldCreationService;
            _anthropicService = anthropicService;
            _wordAnalyzer = wordAnalyzer;
            _visionDetector = visionDetector;
            _googleAiService = googleAiService;
            _boundingBoxValidator = boundingBoxValidator;
            _nlpGenerator = nlpGenerator;
        }

        /// <summary>
        /// Convert Word to PDF with configurable field detection
        /// </summary>
        public async Task<(byte[] pdfBytes, List<FieldDetectionResult> fields)> ConvertWithConfig(
            byte[] wordBytes, 
            string fileName, 
            FieldDetectionConfig config)
        {
            _logger.LogInformation($"Starting configurable conversion for {fileName}");
            _logger.LogInformation($"Config: Syncfusion={config.Services.UseSyncfusion}, Google={config.Services.UseGoogle}, " +
                                  $"ClaudeVision={config.Services.UseClaudeVision}, ClaudeValidation={config.Services.UseClaudeValidation}");
            
            var detectedFields = new List<FieldDetectionResult>();
            byte[] pdfBytes = null;
            
            if (config.Mode == ProcessingMode.Sequential || config.Mode == ProcessingMode.SyncfusionWithValidation)
            {
                // PHASE 1: Get base detections from Syncfusion and/or Google
                _logger.LogInformation("PHASE 1: Getting base field detections");
                
                if (config.Services.UseSyncfusion)
                {
                    var syncfusionResult = await DetectWithSyncfusion(wordBytes, fileName);
                    pdfBytes = syncfusionResult.pdfBytes;
                    detectedFields.AddRange(syncfusionResult.fields);
                    _logger.LogInformation($"Syncfusion detected {syncfusionResult.fields.Count} fields");
                }
                else
                {
                    // Create clean PDF without field preservation
                    pdfBytes = await CreateCleanPdf(wordBytes);
                }
                
                if (config.Services.UseGoogle && _googleAiService != null)
                {
                    var googleFields = await DetectWithGoogle(pdfBytes);
                    detectedFields.AddRange(googleFields);
                    _logger.LogInformation($"Google detected {googleFields.Count} fields");
                }
                
                // PHASE 2: Use Claude for validation and correction if requested
                if (config.Services.UseClaudeValidation && detectedFields.Any())
                {
                    _logger.LogInformation("PHASE 2: Validating fields with Claude");
                    detectedFields = await ValidateFieldsWithClaude(pdfBytes, detectedFields);
                }
                
                // PHASE 3: Add Claude Vision detection if requested
                if (config.Services.UseClaudeVision && _visionDetector != null)
                {
                    // If we have Syncfusion fields, use Claude for labeling only
                    if (config.Services.UseSyncfusion && detectedFields.Any(f => f.Source == "Syncfusion"))
                    {
                        _logger.LogInformation("PHASE 3: Using Claude Vision for intelligent field labeling");
                        detectedFields = await EnhanceFieldsWithClaudeLabels(pdfBytes, detectedFields);
                    }
                    else
                    {
                        // Otherwise do full Claude Vision detection
                        _logger.LogInformation("PHASE 3: Claude Vision detection");
                        var visionFields = await DetectWithClaudeVision(pdfBytes);
                        
                        // Merge vision fields, avoiding duplicates
                        foreach (var visionField in visionFields)
                        {
                            if (!HasDuplicateField(detectedFields, visionField))
                            {
                                detectedFields.Add(visionField);
                            }
                        }
                    }
                }
            }
            else
            {
                // Simultaneous mode - run everything at once (original behavior)
                await RunSimultaneousDetection(wordBytes, fileName, config, detectedFields);
            }
            
            // Remove false positives
            if (!config.DebugMode)
            {
                detectedFields = detectedFields.Where(f => f.IsValid).ToList();
            }
            
            // Create final PDF with detected fields
            pdfBytes = await CreatePdfWithFields(pdfBytes, detectedFields, config);
            
            _logger.LogInformation($"Final result: {detectedFields.Count} fields detected");
            
            return (pdfBytes, detectedFields);
        }

        private async Task<(byte[] pdfBytes, List<FieldDetectionResult> fields)> DetectWithSyncfusion(
            byte[] wordBytes, string fileName)
        {
            var fields = new List<FieldDetectionResult>();
            byte[] pdfBytes;
            
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
                            pdfBytes = outputStream.ToArray();
                        }
                    }
                }
            }
            
            // Extract Syncfusion's detected fields
            using (var pdfStream = new MemoryStream(pdfBytes))
            using (var pdfDoc = new PdfLoadedDocument(pdfStream))
            {
                if (pdfDoc.Form?.Fields != null)
                {
                    for (int i = 0; i < pdfDoc.Form.Fields.Count; i++)
                    {
                        var fieldObj = pdfDoc.Form.Fields[i];
                        if (!(fieldObj is PdfLoadedField field))
                            continue;
                        
                        // Skip suspicious empty fields (false positives)
                        if (IsSuspiciousFieldLoaded(field))
                            continue;
                        
                        var shortId = $"SF{++_fieldCounter}";
                        var bounds = GetLoadedFieldBounds(field);
                        var pageNum = 1;
                        if (field.Page is PdfLoadedPage page)
                        {
                            // Find page index
                            for (int p = 0; p < pdfDoc.Pages.Count; p++)
                            {
                                if (pdfDoc.Pages[p] == page)
                                {
                                    pageNum = p + 1;
                                    break;
                                }
                            }
                        }
                        
                        var detectedField = new FieldDetectionResult
                        {
                            ShortId = shortId,
                            FieldName = CleanFieldName(field.Name),
                            FieldType = DetermineFieldTypeLoaded(field),
                            X = bounds.X,
                            Y = bounds.Y,
                            Width = bounds.Width,
                            Height = bounds.Height,
                            PageNumber = pageNum,
                            Source = "Syncfusion",
                            Confidence = 0.85f,
                            IsValid = true
                        };
                        
                        detectedField.DebugInfo["OriginalName"] = field.Name;
                        detectedField.DebugInfo["FieldClass"] = field.GetType().Name;
                        
                        fields.Add(detectedField);
                    }
                }
            }
            
            return (pdfBytes, fields);
        }

        private RectangleF GetLoadedFieldBounds(PdfLoadedField field)
        {
            // Different field types have bounds differently
            if (field is PdfLoadedTextBoxField textField)
                return textField.Bounds;
            if (field is PdfLoadedCheckBoxField checkField)
                return checkField.Bounds;
            if (field is PdfLoadedRadioButtonListField radioField)
                return radioField.Bounds;
            if (field is PdfLoadedComboBoxField comboField)
                return comboField.Bounds;
            if (field is PdfLoadedListBoxField listField)
                return listField.Bounds;
            if (field is PdfLoadedSignatureField sigField)
                return sigField.Bounds;
            
            // Default bounds if we can't determine
            return new RectangleF(0, 0, 100, 20);
        }

        private bool IsSuspiciousFieldLoaded(PdfLoadedField field)
        {
            var name = field.Name;
            var bounds = GetLoadedFieldBounds(field);
            
            // Be more liberal - only filter out really obvious false positives
            // Since we have Claude validation to catch false positives later
            
            // Only filter out extremely small fields that are clearly artifacts
            if (bounds.Width < 5 || bounds.Height < 5)
            {
                _logger.LogDebug($"Filtering tiny field: {name} ({bounds.Width}x{bounds.Height})");
                return true;
            }
            
            // Keep fields even if they have generic names - Claude will label them better
            // Don't filter based on name anymore since we'll get better names from Claude
            
            return false;
        }

        private string DetermineFieldTypeLoaded(PdfLoadedField field)
        {
            if (field is PdfLoadedCheckBoxField)
                return "checkbox";
            if (field is PdfLoadedRadioButtonListField)
                return "radio";
            if (field is PdfLoadedComboBoxField)
                return "dropdown";
            if (field is PdfLoadedListBoxField)
                return "listbox";
            if (field is PdfLoadedSignatureField)
                return "signature";
                
            // Use the comprehensive field type detector for text fields
            return FieldTypeDetector.DetectFieldType(field.Name, null, null);
        }

        private string CleanFieldName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "Field";
                
            // Remove common prefixes
            name = name.Replace("Textformfield", "")
                      .Replace("formfield", "")
                      .Replace("_", " ")
                      .Trim();
            
            // If it's just a hash, make it more readable
            if (name.Length > 10 && !name.Contains(" "))
            {
                name = "Field";
            }
            
            return name;
        }

        private async Task<List<FieldDetectionResult>> DetectWithGoogle(byte[] pdfBytes)
        {
            var fields = new List<FieldDetectionResult>();
            
            var googleFields = await _googleAiService.ProcessPdfAsync(pdfBytes);
            
            foreach (var gField in googleFields)
            {
                var shortId = $"G{++_fieldCounter}";
                fields.Add(new FieldDetectionResult
                {
                    ShortId = shortId,
                    FieldName = gField.FieldName,
                    FieldType = gField.FieldType,
                    X = gField.Bounds?.X ?? 0,
                    Y = gField.Bounds?.Y ?? 0,
                    Width = gField.Bounds?.Width ?? 100,
                    Height = gField.Bounds?.Height ?? 20,
                    PageNumber = gField.PageNumber,
                    Source = "Google",
                    Confidence = gField.Confidence,
                    IsValid = true
                });
            }
            
            return fields;
        }

        private async Task<List<FieldDetectionResult>> DetectWithClaudeVision(byte[] pdfBytes)
        {
            var fields = new List<FieldDetectionResult>();
            
            var visionResults = await _visionDetector.AnalyzePdfWithVision(pdfBytes, 5);
            
            foreach (var page in visionResults)
            {
                foreach (var vField in page.Fields)
                {
                    var shortId = $"CV{++_fieldCounter}";
                    
                    // Check if bounds are actually populated
                    if (vField.Bounds == null)
                    {
                        _logger.LogWarning($"Claude Vision field '{vField.FieldName}' has null bounds - skipping coordinate extraction");
                        continue;
                    }
                    
                    _logger.LogDebug($"Claude field '{vField.FieldName}' bounds: X={vField.Bounds.X:F1}, Y={vField.Bounds.Y:F1}, W={vField.Bounds.Width:F1}, H={vField.Bounds.Height:F1}");
                    
                    fields.Add(new FieldDetectionResult
                    {
                        ShortId = shortId,
                        FieldName = vField.FieldName,
                        FieldType = vField.FieldType,
                        X = vField.Bounds.X,
                        Y = vField.Bounds.Y,
                        Width = vField.Bounds.Width,
                        Height = vField.Bounds.Height,
                        PageNumber = page.PageNumber,
                        Source = "ClaudeVision",
                        Confidence = 0.9f,
                        IsValid = true,
                        ValidationNotes = vField.Description
                    });
                }
            }
            
            return fields;
        }

        private async Task<List<FieldDetectionResult>> ValidateFieldsWithClaude(
            byte[] pdfBytes, List<FieldDetectionResult> fields)
        {
            // Group fields by page for validation
            var fieldsByPage = fields.GroupBy(f => f.PageNumber);
            
            foreach (var pageGroup in fieldsByPage)
            {
                var pageNum = pageGroup.Key;
                var pageFields = pageGroup.ToList();
                
                // Convert PDF page to image for validation
                using var pdfStream = new MemoryStream(pdfBytes);
                var pageImage = await ConvertPdfPageToImage(pdfStream, pageNum - 1);
                
                if (pageImage != null)
                {
                    // Get page dimensions
                    using var pdfDoc = new PdfLoadedDocument(pdfStream);
                    var page = pdfDoc.Pages[pageNum - 1];
                    
                    // Validate and correct the fields
                    await _boundingBoxValidator.ValidateAndCorrectBoundingBoxes(
                        pageImage, pageFields, pageNum, page.Size.Width, page.Size.Height);
                }
            }
            
            return fields;
        }

        private async Task<byte[]> ConvertPdfPageToImage(Stream pdfStream, int pageIndex)
        {
            try
            {
                // Reset stream position
                pdfStream.Position = 0;
                
                // Convert to byte array for PDFtoImage
                byte[] pdfBytes;
                using (var ms = new MemoryStream())
                {
                    await pdfStream.CopyToAsync(ms);
                    pdfBytes = ms.ToArray();
                }
                
                var options = new PDFtoImage.RenderOptions
                {
                    Dpi = 150,
                    WithAnnotations = true,
                    WithFormFill = true,
                    AntiAliasing = PDFtoImage.PdfAntiAliasing.All
                };
                
                using var bitmap = PDFtoImage.Conversion.ToImage(pdfBytes, pageIndex, null, options);
                
                // Convert bitmap to PNG byte array
                using var imageMs = new MemoryStream();
                using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
                data.SaveTo(imageMs);
                return imageMs.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to convert PDF page {pageIndex} to image");
                return null;
            }
        }

        private bool HasDuplicateField(List<FieldDetectionResult> fields, FieldDetectionResult newField)
        {
            return fields.Any(f => 
                f.PageNumber == newField.PageNumber &&
                Math.Abs(f.X - newField.X) < 10 &&
                Math.Abs(f.Y - newField.Y) < 10);
        }

        private async Task<byte[]> CreateCleanPdf(byte[] wordBytes)
        {
            using var inputStream = new MemoryStream(wordBytes);
            using var wordDoc = new WordDocument(inputStream, FormatType.Docx);
            using var renderer = new DocIORenderer();
            
            renderer.Settings.PreserveFormFields = false; // Don't preserve fields
            renderer.Settings.AutoTag = true;
            
            using var pdfDocument = renderer.ConvertToPDF(wordDoc);
            using var outputStream = new MemoryStream();
            
            pdfDocument.Save(outputStream);
            return outputStream.ToArray();
        }

        private async Task<byte[]> CreatePdfWithFields(
            byte[] pdfBytes, List<FieldDetectionResult> fields, FieldDetectionConfig config)
        {
            using var pdfStream = new MemoryStream(pdfBytes);
            using var pdfDoc = new PdfLoadedDocument(pdfStream);
            
            // Enable tagging for PDF/UA compliance
            pdfDoc.DocumentInformation.Title = pdfDoc.DocumentInformation.Title ?? "Accessible Form";
            
            // Remove any existing fields if needed
            if (pdfDoc.Form?.Fields != null && pdfDoc.Form.Fields.Count > 0)
            {
                pdfDoc.Form.Fields.Clear();
            }
            
            // Initialize form if needed
            if (pdfDoc.Form == null)
            {
                // Form will be created automatically when adding first field
            }
            
            // Add detected fields
            foreach (var field in fields.Where(f => f.IsValid))
            {
                var bounds = new RectangleF(field.X, field.Y, field.Width, field.Height);
                
                // Special handling for checkboxes - they should be small squares
                if (field.FieldType.ToLower() == "checkbox")
                {
                    // Checkboxes should be small, typically 12x12 to 15x15
                    const float CHECKBOX_SIZE = 12f;
                    bounds.Width = CHECKBOX_SIZE;
                    bounds.Height = CHECKBOX_SIZE;
                    // Optionally adjust Y position to center in original bounds
                    // bounds.Y = field.Y + (field.Height - CHECKBOX_SIZE) / 2;
                }
                else
                {
                    // Make text fields more appropriately sized
                    // Increase width significantly for text input fields
                    if (bounds.Width < 150)
                    {
                        // For fields like name, email, address - make them wider
                        bounds.Width = Math.Max(150, bounds.Width * 2);
                    }
                    
                    // Ensure minimum height for text fields
                    if (bounds.Height < 20)
                    {
                        bounds.Height = 20;
                    }
                }
                
                // Add the field based on type
                PdfField pdfField = null;
                string tooltip = GetFieldTooltip(field);
                
                switch (field.FieldType.ToLower())
                {
                    case "checkbox":
                        var checkField = new PdfCheckBoxField(pdfDoc.Pages[field.PageNumber - 1], 
                            field.FieldName ?? field.ShortId);
                        checkField.Bounds = bounds;
                        checkField.ToolTip = tooltip;
                        pdfField = checkField;
                        break;
                        
                    case "radio":
                        var radioField = new PdfRadioButtonListField(pdfDoc.Pages[field.PageNumber - 1],
                            field.FieldName ?? field.ShortId);
                        // Radio button list needs items, add a default one
                        var radioItem = new PdfRadioButtonListItem("Option1");
                        radioItem.Bounds = bounds;
                        radioField.Items.Add(radioItem);
                        radioField.ToolTip = tooltip;
                        pdfField = radioField;
                        break;
                        
                    case "signature":
                        var sigField = new PdfSignatureField(pdfDoc.Pages[field.PageNumber - 1],
                            field.FieldName ?? field.ShortId);
                        sigField.Bounds = bounds;
                        // Signature fields may not support tooltips directly
                        pdfField = sigField;
                        break;
                        
                    default:
                        // Create text field for all text-based types
                        var textField = new PdfTextBoxField(pdfDoc.Pages[field.PageNumber - 1],
                            field.FieldName ?? field.ShortId);
                        textField.Bounds = bounds;
                        
                        // Apply field-type specific formatting and validation
                        ApplyFieldTypeFormatting(textField, field.FieldType);
                        
                        // Set tooltip
                        textField.ToolTip = tooltip;
                        
                        pdfField = textField;
                        break;
                }
                
                if (pdfField != null)
                {
                    // Add debug info to tooltip if in debug mode
                    if (config.ShowFieldIds)
                    {
                        var currentTooltip = GetFieldTooltipFromPdfField(pdfField);
                        SetFieldTooltip(pdfField, $"[{field.ShortId}] {field.Source} | Type: {field.FieldType} | {currentTooltip}");
                    }
                    
                    pdfDoc.Form.Fields.Add(pdfField);
                }
            }
            
            // Log field count for debugging
            _logger.LogInformation($"Added {pdfDoc.Form?.Fields?.Count ?? 0} fields to PDF (from {fields.Count(f => f.IsValid)} valid detected fields)");
            
            using var outputStream = new MemoryStream();
            pdfDoc.Save(outputStream);
            return outputStream.ToArray();
        }
        
        private void ApplyFieldTypeFormatting(PdfTextBoxField textField, string fieldType)
        {
            switch (fieldType.ToLower())
            {
                case "date":
                    textField.MaxLength = 10; // MM/DD/YYYY
                    // Could add JavaScript format validation here
                    break;
                    
                case "phone":
                    textField.MaxLength = 14; // (XXX) XXX-XXXX
                    break;
                    
                case "email":
                    textField.MaxLength = 100;
                    break;
                    
                case "ssn":
                    textField.MaxLength = 11; // XXX-XX-XXXX
                    textField.Password = false; // Don't hide SSN
                    break;
                    
                case "ein":
                    textField.MaxLength = 10; // XX-XXXXXXX
                    break;
                    
                case "zip":
                case "zipcode":
                    textField.MaxLength = 10; // XXXXX-XXXX
                    break;
                    
                case "number":
                case "integer":
                    // Could add numeric validation
                    break;
            }
        }
        
        private string GetFieldTooltip(FieldDetectionResult field)
        {
            // Try to generate intelligent tooltip with NLP generator
            if (_nlpGenerator != null)
            {
                try
                {
                    var context = new FieldContext
                    {
                        UseAI = false, // Keep it fast for real-time use
                        IsRequired = field.IsValid
                    };
                    
                    var labelResult = _nlpGenerator.GenerateLabelsAsync(field.FieldName, field.FieldType, context).Result;
                    
                    if (labelResult.Success && labelResult.Tooltip != null)
                    {
                        var tooltipText = labelResult.Tooltip.Primary;
                        
                        // Add format hint if available
                        if (!string.IsNullOrEmpty(labelResult.Tooltip.FormatHint))
                        {
                            tooltipText += $" (Example: {labelResult.Tooltip.FormatHint})";
                        }
                        
                        return tooltipText;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to generate NLP tooltip for field {field.FieldName}");
                }
            }
            
            // Fallback to simple tooltip generation
            var mask = FieldTypeDetector.GetFieldMask(field.FieldType);
            if (!string.IsNullOrEmpty(mask))
            {
                return $"Enter {field.FieldName} (Format: {mask})";
            }
            
            return $"Enter {field.FieldName}";
        }
        
        private string GetFieldTooltipFromPdfField(PdfField field)
        {
            if (field is PdfTextBoxField textField)
                return textField.ToolTip ?? "";
            else if (field is PdfCheckBoxField checkField)
                return checkField.ToolTip ?? "";
            else if (field is PdfRadioButtonListField radioField)
                return radioField.ToolTip ?? "";
            // Signature fields don't have ToolTip property
            
            return "";
        }

        
        private void SetFieldTooltip(PdfField field, string tooltip)
        {
            if (field is PdfTextBoxField textField)
                textField.ToolTip = tooltip;
            else if (field is PdfCheckBoxField checkField)
                checkField.ToolTip = tooltip;
            else if (field is PdfRadioButtonListField radioField)
                radioField.ToolTip = tooltip;
            // Signature fields don't have ToolTip property
        }

        private async Task RunSimultaneousDetection(
            byte[] wordBytes, string fileName, FieldDetectionConfig config, List<FieldDetectionResult> detectedFields)
        {
            // Original simultaneous detection logic
            // (implement if needed for backward compatibility)
            _logger.LogInformation("Running simultaneous detection (legacy mode)");
        }

        /// <summary>
        /// Enhance existing fields with Claude Vision labels
        /// </summary>
        private async Task<List<FieldDetectionResult>> EnhanceFieldsWithClaudeLabels(
            byte[] pdfBytes, List<FieldDetectionResult> existingFields)
        {
            _logger.LogInformation($"Enhancing {existingFields.Count} fields with Claude labels");
            
            // For now, just call the vision detector to get labels
            // In the future, we could send field positions to Claude for targeted labeling
            var visionResults = await _visionDetector.AnalyzePdfWithVision(pdfBytes, 5);
            
            // Extract all Claude-detected fields
            var claudeFields = new List<FieldDetectionResult>();
            foreach (var page in visionResults)
            {
                foreach (var vField in page.Fields)
                {
                    // Even if bounds are null/zero, we can still use the labels
                    claudeFields.Add(new FieldDetectionResult
                    {
                        ShortId = $"CV_temp",
                        FieldName = vField.FieldName,
                        FieldType = vField.FieldType,
                        X = vField.Bounds?.X ?? 0,
                        Y = vField.Bounds?.Y ?? 0,
                        Width = vField.Bounds?.Width ?? 100,
                        Height = vField.Bounds?.Height ?? 20,
                        PageNumber = page.PageNumber,
                        Source = "ClaudeVision",
                        Confidence = 0.9f,
                        IsValid = true
                    });
                }
            }
            
            // Now match by field index/order since coordinates aren't reliable
            var enhancedFields = new List<FieldDetectionResult>();
            var sfFieldsByPage = existingFields.Where(f => f.Source == "Syncfusion")
                                              .GroupBy(f => f.PageNumber)
                                              .OrderBy(g => g.Key);
            var cvFieldsByPage = claudeFields.GroupBy(f => f.PageNumber)
                                            .OrderBy(g => g.Key);
            
            foreach (var sfPageGroup in sfFieldsByPage)
            {
                var pageNum = sfPageGroup.Key;
                var sfFields = sfPageGroup.OrderBy(f => f.Y).ThenBy(f => f.X).ToList();
                
                // Find corresponding Claude fields for this page
                var cvPageGroup = cvFieldsByPage.FirstOrDefault(g => g.Key == pageNum);
                var cvFields = cvPageGroup?.OrderBy(f => f.FieldName).ToList() ?? new List<FieldDetectionResult>();
                
                // Separate checkboxes and text fields for better matching
                var sfCheckboxes = sfFields.Where(f => f.FieldType.ToLower() == "checkbox").ToList();
                var sfTextFields = sfFields.Where(f => f.FieldType.ToLower() != "checkbox").ToList();
                var cvCheckboxLabels = cvFields.Where(f => f.FieldType?.ToLower() == "checkbox").ToList();
                var cvTextLabels = cvFields.Where(f => f.FieldType?.ToLower() != "checkbox").ToList();
                
                _logger.LogInformation($"Page {pageNum}: {sfFields.Count} Syncfusion fields ({sfCheckboxes.Count} checkboxes, {sfTextFields.Count} text), {cvFields.Count} Claude labels");
                
                // Process text fields first
                int textIndex = 0;
                foreach (var sfField in sfTextFields)
                {
                    FieldDetectionResult bestMatch = null;
                    
                    // Match text fields with text labels
                    if (textIndex < cvTextLabels.Count)
                    {
                        bestMatch = cvTextLabels[textIndex];
                        textIndex++;
                    }
                    else if (cvTextLabels.Any())
                    {
                        // If we run out, use the last available label
                        bestMatch = cvTextLabels.Last();
                    }
                    
                    if (bestMatch != null)
                    {
                        // Create enhanced field with intelligent type detection
                        var detectedType = FieldTypeDetector.DetectFieldType(bestMatch.FieldName, null, bestMatch.FieldType);
                        
                        // Validate the type conversion is compatible
                        var compatibleType = FieldTypeCompatibilityChecker.GetCompatibleType(sfField.FieldType, detectedType);
                        if (compatibleType != detectedType)
                        {
                            _logger.LogWarning($"Prevented invalid conversion for {sfField.ShortId}: {sfField.FieldType} → {detectedType}, using {compatibleType} instead");
                        }
                        
                        var enhanced = new FieldDetectionResult
                        {
                            ShortId = sfField.ShortId,
                            FieldName = bestMatch.FieldName,  // Use Claude's label
                            FieldType = compatibleType,  // Use compatible type
                            X = sfField.X,
                            Y = sfField.Y,
                            Width = sfField.Width,
                            Height = sfField.Height,
                            PageNumber = sfField.PageNumber,
                            Source = "Syncfusion+Claude",
                            Confidence = Math.Max(sfField.Confidence, bestMatch.Confidence),
                            IsValid = sfField.IsValid
                        };
                        enhancedFields.Add(enhanced);
                        _logger.LogDebug($"Enhanced field {sfField.ShortId}: '{sfField.FieldName}' → '{bestMatch.FieldName}' (type: {detectedType}, Claude suggested: {bestMatch.FieldType})");
                    }
                    else
                    {
                        enhancedFields.Add(sfField);
                    }
                }
                
                // Process checkboxes - keep them as checkboxes but try to get labels
                int checkboxIndex = 0;
                foreach (var sfCheckbox in sfCheckboxes)
                {
                    FieldDetectionResult bestLabel = null;
                    
                    // Try to match with Claude checkbox labels
                    if (checkboxIndex < cvCheckboxLabels.Count)
                    {
                        bestLabel = cvCheckboxLabels[checkboxIndex];
                        checkboxIndex++;
                    }
                    else if (cvCheckboxLabels.Any())
                    {
                        // Use a generic checkbox label if we run out
                        bestLabel = cvCheckboxLabels.Last();
                    }
                    
                    if (bestLabel != null)
                    {
                        // Keep it as checkbox but use Claude's label
                        var enhanced = new FieldDetectionResult
                        {
                            ShortId = sfCheckbox.ShortId,
                            FieldName = bestLabel.FieldName,     // Claude's label
                            FieldType = "checkbox",               // ALWAYS keep as checkbox
                            X = sfCheckbox.X,
                            Y = sfCheckbox.Y,
                            Width = sfCheckbox.Width,
                            Height = sfCheckbox.Height,
                            PageNumber = sfCheckbox.PageNumber,
                            Source = "Syncfusion+Claude",
                            Confidence = sfCheckbox.Confidence,
                            IsValid = sfCheckbox.IsValid
                        };
                        enhancedFields.Add(enhanced);
                        _logger.LogDebug($"Enhanced checkbox {sfCheckbox.ShortId}: label → '{bestLabel.FieldName}'");
                    }
                    else
                    {
                        // No label found, keep original checkbox
                        enhancedFields.Add(sfCheckbox);
                    }
                }
            }
            
            // Keep non-Syncfusion fields as-is
            enhancedFields.AddRange(existingFields.Where(f => f.Source != "Syncfusion"));
            
            _logger.LogInformation($"Field enhancement complete: {existingFields.Count} → {enhancedFields.Count} fields");
            
            return enhancedFields;
        }

        /// <summary>
        /// Smart matching: Use Syncfusion's accurate positions with Claude's intelligent labels
        /// </summary>
        private List<FieldDetectionResult> PerformSmartMatching(
            List<FieldDetectionResult> syncfusionFields, 
            List<FieldDetectionResult> claudeFields)
        {
            var matchedFields = new List<FieldDetectionResult>();
            var unmatchedSyncfusion = new List<FieldDetectionResult>(syncfusionFields.Where(f => f.Source == "Syncfusion"));
            var unmatchedClaude = new List<FieldDetectionResult>(claudeFields);
            
            // Distance threshold for matching (in pixels)
            const float MATCH_THRESHOLD = 50f; // Adjust based on testing
            
            foreach (var sfField in syncfusionFields.Where(f => f.Source == "Syncfusion"))
            {
                // Find closest Claude field on the same page
                FieldDetectionResult bestMatch = null;
                float bestDistance = float.MaxValue;
                
                foreach (var claudeField in claudeFields.Where(c => c.PageNumber == sfField.PageNumber))
                {
                    // Calculate distance between field centers
                    float centerX1 = sfField.X + sfField.Width / 2;
                    float centerY1 = sfField.Y + sfField.Height / 2;
                    float centerX2 = claudeField.X + claudeField.Width / 2;
                    float centerY2 = claudeField.Y + claudeField.Height / 2;
                    
                    float distance = (float)Math.Sqrt(
                        Math.Pow(centerX1 - centerX2, 2) + 
                        Math.Pow(centerY1 - centerY2, 2));
                    
                    if (distance < bestDistance && distance < MATCH_THRESHOLD)
                    {
                        bestDistance = distance;
                        bestMatch = claudeField;
                    }
                }
                
                if (bestMatch != null)
                {
                    // Create enhanced field: Syncfusion position + Claude intelligence
                    var enhancedField = new FieldDetectionResult
                    {
                        ShortId = sfField.ShortId,
                        FieldName = bestMatch.FieldName, // Use Claude's better name
                        FieldType = bestMatch.FieldType, // Use Claude's better type detection
                        X = sfField.X,                   // Use Syncfusion's position
                        Y = sfField.Y,
                        Width = sfField.Width,
                        Height = sfField.Height,
                        PageNumber = sfField.PageNumber,
                        Source = "Syncfusion+Claude",
                        Confidence = Math.Max(sfField.Confidence, bestMatch.Confidence),
                        IsValid = sfField.IsValid,
                        // ValidationRules would go here if the property existed
                        DebugInfo = new Dictionary<string, object>
                        {
                            ["SyncfusionName"] = sfField.FieldName,
                            ["ClaudeName"] = bestMatch.FieldName,
                            ["MatchDistance"] = bestDistance,
                            ["SyncfusionType"] = sfField.FieldType,
                            ["ClaudeType"] = bestMatch.FieldType
                        }
                    };
                    
                    matchedFields.Add(enhancedField);
                    unmatchedSyncfusion.Remove(sfField);
                    unmatchedClaude.Remove(bestMatch);
                    
                    _logger.LogInformation($"Matched {sfField.ShortId}: '{sfField.FieldName}' → '{bestMatch.FieldName}' (distance: {bestDistance:F1}px)");
                }
                else
                {
                    // No match found, keep Syncfusion field as-is but try to improve its type
                    var improvedField = sfField;
                    
                    // Try to detect better field type from the name
                    var detectedType = FieldTypeDetector.DetectFieldType(sfField.FieldName, null, null);
                    if (detectedType != "text" && detectedType != sfField.FieldType)
                    {
                        improvedField.FieldType = detectedType;
                        _logger.LogInformation($"Improved type for {sfField.ShortId}: {sfField.FieldType} → {detectedType}");
                    }
                    
                    matchedFields.Add(improvedField);
                }
            }
            
            // Add any unmatched Claude fields that might be additional fields Syncfusion missed
            // But be more conservative - only add if they have high confidence and don't overlap with existing
            foreach (var claudeField in unmatchedClaude)
            {
                // Check if this Claude field overlaps with any matched field
                bool overlaps = matchedFields.Any(m => 
                    m.PageNumber == claudeField.PageNumber &&
                    Math.Abs(m.X - claudeField.X) < 20 && 
                    Math.Abs(m.Y - claudeField.Y) < 20);
                
                if (!overlaps && claudeField.Confidence > 0.7f)
                {
                    claudeField.ShortId = $"CV{++_fieldCounter}";
                    claudeField.Source = "Claude-Additional";
                    matchedFields.Add(claudeField);
                    _logger.LogInformation($"Added additional Claude field: {claudeField.FieldName} at ({claudeField.X}, {claudeField.Y})");
                }
                else if (overlaps)
                {
                    _logger.LogDebug($"Skipping overlapping Claude field: {claudeField.FieldName} at ({claudeField.X}, {claudeField.Y})");
                }
            }
            
            // Also keep non-Syncfusion fields (like Google fields if any)
            matchedFields.AddRange(syncfusionFields.Where(f => f.Source != "Syncfusion"));
            
            // Log field breakdown for debugging
            var fieldsBySource = matchedFields.GroupBy(f => f.Source)
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToList();
            _logger.LogInformation($"Smart matching complete: {syncfusionFields.Count} Syncfusion + {claudeFields.Count} Claude → {matchedFields.Count} matched fields");
            _logger.LogInformation($"Fields by source: {string.Join(", ", fieldsBySource)}");
            
            return matchedFields;
        }
    }
}