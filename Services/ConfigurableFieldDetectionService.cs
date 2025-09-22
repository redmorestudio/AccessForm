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
        private readonly ILoggerFactory _loggerFactory;
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
            ILoggerFactory loggerFactory,
            FormFieldCreationService fieldCreationService,
            AnthropicService anthropicService,
            WordFormFieldAnalyzer wordAnalyzer,
            ClaudeVisionFieldDetector visionDetector,
            GoogleDocumentAiService googleAiService,
            ClaudeBoundingBoxValidator boundingBoxValidator,
            NLPLabelGenerator nlpGenerator)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
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
            _logger.LogInformation($"===== FIELD DETECTION START: {fileName} =====");
            _logger.LogInformation($"File size: {wordBytes.Length:N0} bytes");
            _logger.LogInformation($"Config: Syncfusion={config.Services.UseSyncfusion}, Google={config.Services.UseGoogle}, " +
                                  $"ClaudeVision={config.Services.UseClaudeVision}, ClaudeValidation={config.Services.UseClaudeValidation}");
            _logger.LogInformation($"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            
            var detectedFields = new List<FieldDetectionResult>();
            byte[] pdfBytes = null;
            
            // Extract text from Word document for better field labeling
            string extractedText = "";
            try
            {
                using (var stream = new MemoryStream(wordBytes))
                using (var wordDoc = new Syncfusion.DocIO.DLS.WordDocument(stream, Syncfusion.DocIO.FormatType.Docx))
                {
                    extractedText = wordDoc.GetText();
                    _logger.LogInformation($"Extracted {extractedText.Length} characters from Word document for analysis");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to extract text from Word: {ex.Message}");
            }
            
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
                        
                        // Convert PDF to Markdown for better structure analysis
                        string pdfMarkdown = "";
                        try
                        {
                            // PdfToMarkdownConverter not available
                            pdfMarkdown = ""; // PdfToMarkdownConverter not available
                            _logger.LogInformation($"Converted PDF to Markdown: {pdfMarkdown.Length} characters");
                            
                            // Log the markdown to a file for debugging
                            var debugPath = "/tmp/markdown_sent_to_claude.md";
                            System.IO.File.WriteAllText(debugPath, pdfMarkdown);
                            _logger.LogInformation($"Wrote markdown to {debugPath} for debugging");
                            
                            // Check if "Date Sent/Delivered" is in the markdown
                            if (pdfMarkdown.Contains("Date Sent") || pdfMarkdown.Contains("Date Delivered"))
                            {
                                _logger.LogInformation("Markdown contains 'Date Sent/Delivered' label");
                            }
                            else
                            {
                                _logger.LogWarning("WARNING: Markdown does NOT contain 'Date Sent/Delivered' label - Claude won't know what to call this field!");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to convert PDF to Markdown: {ex.Message}");
                        }
                        
                        // Use markdown if available, otherwise use extracted text
                        detectedFields = await EnhanceFieldsWithClaudeLabels(pdfBytes, detectedFields, 
                            string.IsNullOrEmpty(pdfMarkdown) ? extractedText : pdfMarkdown);
                    }
                    else
                    {
                        // Otherwise do full Claude Vision detection
                        _logger.LogInformation("PHASE 3: Claude Vision detection");
                        var visionFields = await DetectWithClaudeVision(pdfBytes);
                        
                        // Merge vision fields - UPDATE existing fields with Claude's better labels
                        _logger.LogInformation($"Merging {visionFields.Count} Claude Vision fields with {detectedFields.Count} existing fields");
                        foreach (var visionField in visionFields)
                        {
                            // Find existing field at same position
                            var existingField = detectedFields.FirstOrDefault(f => 
                                f.PageNumber == visionField.PageNumber &&
                                Math.Abs(f.X - visionField.X) < 10 &&
                                Math.Abs(f.Y - visionField.Y) < 10);
                            
                            if (existingField != null)
                            {
                                // UPDATE the existing field with Claude's better information
                                _logger.LogInformation($"Updating field '{existingField.FieldName}' -> '{visionField.FieldName}' (type: {existingField.FieldType} -> {visionField.FieldType})");
                                existingField.FieldName = visionField.FieldName;
                                existingField.FieldType = visionField.FieldType;
                                if (!string.IsNullOrEmpty(visionField.Tooltip))
                                    existingField.Tooltip = visionField.Tooltip;
                                existingField.Source = $"{existingField.Source}+Claude";
                            }
                            else
                            {
                                // Add new field if it doesn't exist
                                _logger.LogInformation($"Adding new Claude Vision field '{visionField.FieldName}' at ({visionField.X}, {visionField.Y})");
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
            
            // Deduplicate checkboxes that are too close together
            fields = DeduplicateCheckboxes(fields);
            
            // Handle duplicate field names across all field types
            fields = ResolveDuplicateFieldNames(fields);
            
            return (pdfBytes, fields);
        }

        private List<FieldDetectionResult> DeduplicateCheckboxes(List<FieldDetectionResult> fields)
        {
            var deduplicatedFields = new List<FieldDetectionResult>();
            var checkboxGroups = new Dictionary<string, List<FieldDetectionResult>>();
            
            // Group checkboxes by their position (within 5px tolerance)
            foreach (var field in fields)
            {
                if (field.FieldType.ToLower() == "checkbox")
                {
                    // Create a key based on approximate position
                    var posKey = $"{field.PageNumber}_{Math.Round(field.X / 10) * 10}_{Math.Round(field.Y / 10) * 10}";
                    
                    if (!checkboxGroups.ContainsKey(posKey))
                        checkboxGroups[posKey] = new List<FieldDetectionResult>();
                    
                    checkboxGroups[posKey].Add(field);
                }
                else
                {
                    // Non-checkbox fields go straight through
                    deduplicatedFields.Add(field);
                }
            }
            
            // For each group of checkboxes at the same position, keep only one
            foreach (var group in checkboxGroups.Values)
            {
                if (group.Count > 1)
                {
                    // Keep the one with the best name (not generic)
                    var bestCheckbox = group.OrderBy(f => 
                    {
                        // Prioritize non-generic names
                        if (f.FieldName.Contains("Check") || f.FieldName.Contains("502fba303ae4"))
                            return 2;
                        if (string.IsNullOrWhiteSpace(f.FieldName))
                            return 3;
                        return 0;
                    }).ThenBy(f => f.ShortId).First();
                    
                    _logger.LogDebug($"Deduplicating {group.Count} checkboxes at position ({bestCheckbox.X}, {bestCheckbox.Y}), keeping '{bestCheckbox.FieldName}'");
                    deduplicatedFields.Add(bestCheckbox);
                }
                else
                {
                    deduplicatedFields.Add(group.First());
                }
            }
            
            // Also check for "text" fields that are actually checkboxes based on size
            var finalFields = new List<FieldDetectionResult>();
            foreach (var field in deduplicatedFields)
            {
                // If it's a tiny text field (< 20x20), it's probably a misidentified checkbox
                if (field.FieldType.ToLower() == "text" && 
                    field.Width < 20 && field.Height < 20)
                {
                    // Skip it if there's already a checkbox at this position
                    var hasCheckboxNearby = finalFields.Any(f => 
                        f.FieldType.ToLower() == "checkbox" &&
                        f.PageNumber == field.PageNumber &&
                        Math.Abs(f.X - field.X) < 10 &&
                        Math.Abs(f.Y - field.Y) < 10);
                    
                    if (hasCheckboxNearby)
                    {
                        _logger.LogDebug($"Skipping tiny text field '{field.FieldName}' at ({field.X}, {field.Y}) - likely duplicate of checkbox");
                        continue;
                    }
                }
                finalFields.Add(field);
            }
            
            // Sort by page then position for consistent ordering
            finalFields.Sort((a, b) =>
            {
                var pageCmp = a.PageNumber.CompareTo(b.PageNumber);
                if (pageCmp != 0) return pageCmp;
                var yCmp = a.Y.CompareTo(b.Y);
                if (yCmp != 0) return yCmp;
                return a.X.CompareTo(b.X);
            });
            
            _logger.LogInformation($"Deduplicated fields: {fields.Count} → {finalFields.Count} (removed {fields.Count - finalFields.Count} duplicates)");
            
            return finalFields;
        }
        
        private List<FieldDetectionResult> ResolveDuplicateFieldNames(List<FieldDetectionResult> fields)
        {
            var nameGroups = fields.GroupBy(f => f.FieldName).ToList();
            var resolvedFields = new List<FieldDetectionResult>();
            
            foreach (var group in nameGroups)
            {
                var groupFields = group.ToList();
                if (groupFields.Count == 1)
                {
                    // No duplicates, just add it
                    resolvedFields.Add(groupFields[0]);
                }
                else
                {
                    // We have duplicates - need to disambiguate
                    _logger.LogInformation($"Found {groupFields.Count} fields with duplicate name '{group.Key}'");
                    
                    // Sort by position (top to bottom, left to right)
                    groupFields.Sort((a, b) =>
                    {
                        var pageCmp = a.PageNumber.CompareTo(b.PageNumber);
                        if (pageCmp != 0) return pageCmp;
                        var yCmp = a.Y.CompareTo(b.Y);
                        if (yCmp != 0) return yCmp;
                        return a.X.CompareTo(b.X);
                    });
                    
                    // Disambiguate based on field type and position
                    for (int i = 0; i < groupFields.Count; i++)
                    {
                        var field = groupFields[i];
                        
                        // If fields have different types, append the type
                        var typeCounts = groupFields.GroupBy(f => f.FieldType).Count();
                        if (typeCounts > 1)
                        {
                            // Different types with same name - append type to make unique
                            field.FieldName = $"{field.FieldName}_{field.FieldType}";
                            _logger.LogInformation($"Renamed duplicate field to '{field.FieldName}' based on type");
                        }
                        else if (groupFields.Count > 1)
                        {
                            // Same type, same name - append position index
                            if (i > 0) // Keep first one as-is
                            {
                                field.FieldName = $"{field.FieldName}_{i + 1}";
                                _logger.LogInformation($"Renamed duplicate field to '{field.FieldName}' based on position");
                            }
                        }
                        
                        resolvedFields.Add(field);
                    }
                }
            }
            
            return resolvedFields;
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
            
            // Extract markdown context to help Claude Vision understand the document
            string pdfMarkdown = null;
            try
            {
                if (_loggerFactory == null)
                {
                    _logger.LogError("ERROR: _loggerFactory is NULL! Cannot create markdown converter.");
                    pdfMarkdown = "";
                }
                else
                {
                    // PdfToMarkdownConverter not available
                    pdfMarkdown = "";
                    _logger.LogInformation($"PdfToMarkdownConverter not available, using empty markdown");
                }
                
                // Debug: Write markdown to file if we have it
                if (!string.IsNullOrEmpty(pdfMarkdown))
                {
                    var debugPath = "/tmp/claude_vision_markdown.md";
                    System.IO.File.WriteAllText(debugPath, pdfMarkdown);
                    _logger.LogInformation($"Wrote markdown context to {debugPath}");
                    
                    // Check for specific labels
                    if (pdfMarkdown.Contains("Date Sent") || pdfMarkdown.Contains("Delivered"))
                    {
                        _logger.LogInformation("Markdown contains 'Date Sent/Delivered' label");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to extract markdown for Claude Vision: {ex.Message}");
            }
            
            var visionResults = await _visionDetector.AnalyzePdfWithVision(pdfBytes, 5, pdfMarkdown);
            
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
                // Syncfusion fields already have PDF coordinates (bottom-left origin)
                // Only convert if the field is NOT from Syncfusion
                float pdfY = field.Y;
                
                if (field.Source != "Syncfusion" && !field.Source.StartsWith("Syncfusion"))
                {
                    // For non-Syncfusion sources, convert from top-left to bottom-left origin
                    var page = pdfDoc.Pages[field.PageNumber - 1];
                    float pageHeight = page.Size.Height;
                    pdfY = pageHeight - field.Y - field.Height;
                }
                
                // No adjustment needed - use the calculated position directly
                // pdfY -= 10;
                
                var bounds = new RectangleF(field.X, pdfY, field.Width, field.Height);
                
                // Apply smart sizing based on field type
                bounds = ApplySmartFieldSizing(bounds, field.FieldType, field.FieldName);
                
                // Add the field based on type
                PdfField pdfField = null;
                string tooltip = GetFieldTooltip(field);
                
                switch (field.FieldType.ToLower())
                {
                    case "checkbox":
                        var checkField = new PdfCheckBoxField(pdfDoc.Pages[field.PageNumber - 1],
                            field.FieldName);
                        checkField.Bounds = bounds;
                        checkField.ToolTip = tooltip;
                        pdfField = checkField;
                        break;
                        
                    case "radio":
                        var radioField = new PdfRadioButtonListField(pdfDoc.Pages[field.PageNumber - 1],
                            field.FieldName);
                        // Radio button list needs items, add a default one
                        var radioItem = new PdfRadioButtonListItem("Option1");
                        radioItem.Bounds = bounds;
                        radioField.Items.Add(radioItem);
                        radioField.ToolTip = tooltip;
                        pdfField = radioField;
                        break;
                        
                    case "signature":
                        // CREATE TEXT FIELD INSTEAD OF SIGNATURE FIELD TO PREVENT DOCUMENT LOCKING
                        var sigTextField = new PdfTextBoxField(pdfDoc.Pages[field.PageNumber - 1],
                            field.FieldName);
                        sigTextField.Bounds = bounds;
                        sigTextField.ToolTip = tooltip + " (Signature)";
                        sigTextField.BackColor = new PdfColor(245, 245, 245); // Light gray background
                        pdfField = sigTextField;
                        _logger.LogInformation($"Created text field instead of signature field for '{field.FieldName}' to prevent locking");
                        break;
                        
                    default:
                        // Create text field for all text-based types
                        var textField = new PdfTextBoxField(pdfDoc.Pages[field.PageNumber - 1],
                            field.FieldName);
                        textField.Bounds = bounds;
                        
                        // Apply field-type specific formatting and validation
                        ApplyFieldTypeFormatting(textField, field.FieldType);
                        
                        // Set tooltip with type-specific guidance
                        textField.ToolTip = FieldTooltipGenerator.GenerateTooltip(field.FieldType, field.FieldName);
                        
                        // Store the field type in the field's export value for processing
                        textField.DefaultValue = $"";
                        
                        // Add field type to the field's appearance
                        if (!string.IsNullOrEmpty(FieldTooltipGenerator.GetFormatHint(field.FieldType)))
                        {
                            textField.ToolTip += $" Format: {FieldTooltipGenerator.GetFormatHint(field.FieldType)}";
                        }
                        
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
        
        // Field width constants (in points/pixels)
        private const float CHECKBOX_SIZE = 20f;
        private const float NAME_FIELD_WIDTH = 250f;
        private const float DATE_FIELD_WIDTH = 100f;
        private const float ADDRESS_FIELD_WIDTH = 250f;
        private const float CITY_FIELD_WIDTH = 150f;
        private const float STATE_FIELD_WIDTH = 50f;  // 25 was too small, using 50
        private const float ZIP_FIELD_WIDTH = 80f;
        private const float PHONE_FIELD_WIDTH = 120f;
        private const float EMAIL_FIELD_WIDTH = 200f;
        private const float SSN_FIELD_WIDTH = 100f;
        private const float EIN_FIELD_WIDTH = 100f;
        private const float SIGNATURE_FIELD_WIDTH = 200f;
        private const float NUMERIC_FIELD_WIDTH = 80f;
        private const float CURRENCY_FIELD_WIDTH = 100f;
        private const float PERCENTAGE_FIELD_WIDTH = 60f;
        private const float DEFAULT_TEXT_WIDTH = 150f;
        private const float TEXTAREA_WIDTH = 300f;  // Reduced from 350
        private const float STANDARD_FIELD_HEIGHT = 20f;
        private const float TEXTAREA_HEIGHT = 40f;  // Reduced from 60 to prevent overflow
        
        private RectangleF ApplySmartFieldSizing(RectangleF bounds, string fieldType, string fieldName)
        {
            // ONLY CHANGE WIDTH - NEVER TOUCH X, Y, or HEIGHT!
            var newBounds = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            
            var lowerType = fieldType.ToLower();
            var lowerName = (fieldName ?? "").ToLower();
            
            // ONLY adjust width based on field type/name
            if (lowerType == "checkbox")
            {
                newBounds.Width = 20f;
                // DO NOT CHANGE HEIGHT!
            }
            else if (lowerType == "signature")
            {
                newBounds.Width = 200f;
                // DO NOT CHANGE HEIGHT!
            }
            else if (lowerType == "date" || lowerName.Contains("date"))
            {
                newBounds.Width = 100f;
                // DO NOT CHANGE HEIGHT!
            }
            else if (lowerType == "name" || lowerName.Contains("name"))
            {
                newBounds.Width = 150f;
                // DO NOT CHANGE HEIGHT!
            }
            else if (lowerType == "textarea" || lowerName.Contains("description") || lowerName.Contains("reason") || lowerName.Contains("refusal"))
            {
                newBounds.Width = 300f;
                // DO NOT CHANGE HEIGHT!
            }
            else if (lowerType == "address" || lowerName.Contains("address"))
            {
                newBounds.Width = 200f;
                // DO NOT CHANGE HEIGHT!
            }
            else if (lowerType == "city" || lowerName.Contains("city"))
            {
                newBounds.Width = 150f;
                // DO NOT CHANGE HEIGHT!
            }
            else if (lowerType == "state" || lowerName.Contains("state"))
            {
                newBounds.Width = 30f;
                // DO NOT CHANGE HEIGHT!
            }
            else if (lowerType == "zip" || lowerName.Contains("zip"))
            {
                newBounds.Width = 60f;
                // DO NOT CHANGE HEIGHT!
            }
            else
            {
                // Default for text fields
                newBounds.Width = 150f;
                // DO NOT CHANGE HEIGHT!
            }
            
            return newBounds;
        }
        
        private void ApplyFieldTypeFormatting(PdfTextBoxField textField, string fieldType)
        {
            switch (fieldType.ToLower())
            {
                case "date":
                    textField.MaxLength = 10; // MM/DD/YYYY
                    break;
                    
                case "time":
                    textField.MaxLength = 8; // HH:MM AM
                    break;
                    
                case "phone":
                    textField.MaxLength = 14; // (XXX) XXX-XXXX
                    break;
                    
                case "email":
                    textField.MaxLength = 100;
                    break;
                    
                case "ssn":
                    textField.MaxLength = 11; // XXX-XX-XXXX
                    textField.Password = false; // Don't hide SSN for accessibility
                    break;
                    
                case "ssn_partial":
                    textField.MaxLength = 4; // XXXX
                    break;
                    
                case "ein":
                    textField.MaxLength = 10; // XX-XXXXXXX
                    break;
                    
                case "tin":
                    textField.MaxLength = 11; // Tax ID
                    break;
                    
                case "drivers_license":
                    textField.MaxLength = 20; // Varies by state
                    break;
                    
                case "url":
                    textField.MaxLength = 200;
                    break;
                    
                case "currency":
                    textField.MaxLength = 15; // $999,999,999.99
                    break;
                    
                case "percentage":
                    textField.MaxLength = 6; // 100.00
                    break;
                    
                case "case_number":
                    textField.MaxLength = 20;
                    break;
                    
                case "zip":
                case "zipcode":
                    textField.MaxLength = 10; // XXXXX-XXXX
                    break;
                    
                case "numeric":
                case "number":
                case "integer":
                    // General numeric fields
                    break;
                    
                case "textarea":
                    // Multi-line text - no max length
                    break;
                    
                case "name":
                    textField.MaxLength = 100;
                    break;
                    
                case "address":
                    textField.MaxLength = 200;
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
        /// Analyze extracted text to get field names
        /// </summary>
        private async Task<List<string>> AnalyzeTextForFieldNames(string extractedText)
        {
            var fieldNames = new List<string>();
            
            if (string.IsNullOrEmpty(extractedText))
                return fieldNames;
            
            try
            {
                // Send text to Claude for field name extraction  
                var prompt = @"List ALL field labels from this form. 

IMPORTANT RULES:
- Return ONLY field names/labels, nothing else
- One field name per line
- No JSON, no formatting, no explanations
- No quotes, brackets, or special characters
- Just the plain text of each field label

Include ALL of these:
- Every checkbox label
- Every text field label  
- Every radio button option
- Every dropdown option
- Any field that expects user input

For example, if you see checkboxes for disabilities, list each one:
Autism
ADHD  
Blindness
Deafness

DO NOT write 'Here is...' or any other text. ONLY field names.

Document:
" + extractedText;
                
                // Use Anthropic service to analyze
                var analysis = await _anthropicService.AnalyzeFormFieldsAsync(prompt);
                if (!string.IsNullOrEmpty(analysis))
                {
                    // Clean up Claude's response - remove JSON formatting and explanatory text
                    var lines = analysis.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var cleaned = line.Trim();
                        
                        // Skip JSON formatting, explanatory text, and code blocks
                        if (cleaned.StartsWith("```") || cleaned.StartsWith("{") || cleaned.StartsWith("}") || 
                            cleaned.StartsWith("[") || cleaned.StartsWith("]") || cleaned.Contains("JSON") ||
                            cleaned.Contains("response") || cleaned.Contains("Here") || cleaned.StartsWith("\"") ||
                            cleaned.Length < 3 || cleaned.Length > 100)
                        {
                            continue;
                        }
                        
                        // Only add legitimate field names
                        fieldNames.Add(cleaned);
                    }
                    _logger.LogInformation($"Extracted {fieldNames.Count} field names from text");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to analyze text for field names: {ex.Message}");
            }
            
            return fieldNames;
        }
        
        /// <summary>
        /// Enhance existing fields with Claude Vision labels
        /// </summary>
        private async Task<List<FieldDetectionResult>> EnhanceFieldsWithClaudeLabels(
            byte[] pdfBytes, List<FieldDetectionResult> existingFields, string extractedText = null)
        {
            _logger.LogInformation($"Enhancing {existingFields.Count} fields with Claude labels");
            
            // Get field names from text if available
            List<string> textFieldNames = null;
            if (!string.IsNullOrEmpty(extractedText))
            {
                textFieldNames = await AnalyzeTextForFieldNames(extractedText);
                _logger.LogInformation($"Got {textFieldNames?.Count ?? 0} field names from text analysis");
            }
            
            // Call the vision detector with extracted text/markdown to get labels
            // Pass the extractedText (which is actually markdown) to help Claude with field naming
            var visionResults = await _visionDetector.AnalyzePdfWithVision(pdfBytes, 5, extractedText);
            
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
                var cvFields = cvPageGroup?.ToList() ?? new List<FieldDetectionResult>();
                
                // Sort BOTH by position for proper matching
                var sfFieldsSorted = sfFields.OrderBy(f => f.Y).ThenBy(f => f.X).ToList();
                var cvFieldsSorted = cvFields.OrderBy(f => f.Y).ThenBy(f => f.X).ToList();  // Sort by position, not name!
                
                _logger.LogInformation($"Page {pageNum}: {sfFields.Count} Syncfusion fields, {cvFields.Count} Claude labels");
                
                // Debug: Log the last few fields to see what's happening at the bottom
                if (sfFieldsSorted.Count > 0)
                {
                    var lastSfField = sfFieldsSorted.Last();
                    _logger.LogInformation($"Last Syncfusion field: {lastSfField.FieldName} at Y={lastSfField.Y}, X={lastSfField.X}, Type={lastSfField.FieldType}");
                }
                if (cvFieldsSorted.Count > 0)
                {
                    var bottomCvFields = cvFieldsSorted.Where(f => f.Y > 250 || cvFieldsSorted.IndexOf(f) >= cvFieldsSorted.Count - 5).ToList();
                    foreach (var cvField in bottomCvFields)
                    {
                        _logger.LogInformation($"Bottom Claude field: {cvField.FieldName} at Y={cvField.Y}, X={cvField.X}, Type={cvField.FieldType}");
                    }
                }
                
                // Match fields - first by name, then by position
                var usedLabels = new HashSet<string>();
                
                // Process all Syncfusion fields
                foreach (var sfField in sfFieldsSorted)
                {
                    FieldDetectionResult bestMatch = null;
                    
                    // First, try to match by name exactly
                    bestMatch = cvFieldsSorted.FirstOrDefault(cv => 
                        !usedLabels.Contains(cv.FieldName) &&
                        string.Equals(cv.FieldName, sfField.FieldName, StringComparison.OrdinalIgnoreCase));
                    
                    // If no name match, try partial name match
                    if (bestMatch == null)
                    {
                        bestMatch = cvFieldsSorted.FirstOrDefault(cv => 
                            !usedLabels.Contains(cv.FieldName) &&
                            (cv.FieldName?.Contains(sfField.FieldName ?? "", StringComparison.OrdinalIgnoreCase) == true ||
                             sfField.FieldName?.Contains(cv.FieldName ?? "", StringComparison.OrdinalIgnoreCase) == true));
                    }
                    
                    // If still no match, find closest by position with type preference
                    if (bestMatch == null)
                    {
                        double minDistance = double.MaxValue;
                        foreach (var cvField in cvFieldsSorted)
                        {
                            if (usedLabels.Contains(cvField.FieldName))
                                continue;
                                
                            double dx = sfField.X - cvField.X;
                            double dy = sfField.Y - cvField.Y;
                            double distance = Math.Sqrt(dx * dx + dy * dy);
                            
                            // Prefer matching field types (checkbox to checkbox, text to text/date)
                            var typeCompatible = 
                                (sfField.FieldType.ToLower() == "checkbox" && cvField.FieldType.ToLower() == "checkbox") ||
                                (sfField.FieldType.ToLower() != "checkbox" && cvField.FieldType.ToLower() != "checkbox");
                            
                            // Add type penalty if types don't match
                            if (!typeCompatible)
                            {
                                distance += 1000; // Large penalty for type mismatch
                            }
                            
                            if (distance < minDistance)
                            {
                                minDistance = distance;
                                bestMatch = cvField;
                            }
                        }
                        
                        if (bestMatch != null)
                        {
                            _logger.LogDebug($"Matched {sfField.FieldName} to {bestMatch.FieldName} by position (distance: {minDistance})");
                        }
                    }
                    
                    // For checkboxes, use special matching that prioritizes Y-position
                    if (sfField.FieldType.ToLower() == "checkbox")
                    {
                        // For checkboxes, find the Claude label that's closest in Y position
                        // This prevents all checkboxes from getting the same label
                        FieldDetectionResult checkboxMatch = null;
                        double minYDistance = double.MaxValue;
                        
                        _logger.LogDebug($"Matching checkbox at Y={sfField.Y}, X={sfField.X}");
                        
                        // First check if we have any Claude fields with valid coordinates
                        var hasValidCoordinates = cvFieldsSorted.Any(f => f.Y > 0);
                        
                        if (hasValidCoordinates)
                        {
                            // Use position-based matching
                            foreach (var cvField in cvFieldsSorted)
                            {
                                // Skip fields with invalid coordinates
                                if (cvField.Y <= 0)
                                    continue;
                                    
                                // For checkboxes, don't skip used labels - multiple checkboxes can share a label
                                // But prioritize matching by Y position
                                double yDistance = Math.Abs(sfField.Y - cvField.Y);
                                double xDistance = Math.Abs(sfField.X - cvField.X);
                                
                                _logger.LogDebug($"  Checking Claude field '{cvField.FieldName}' at Y={cvField.Y}, X={cvField.X}: yDist={yDistance}, xDist={xDistance}");
                                
                                // Consider labels that are on the same line (within 50 pixels Y) 
                                // and to the right of the checkbox (X within reasonable range)
                                if (yDistance < 50 && yDistance < minYDistance)
                                {
                                    minYDistance = yDistance;
                                    checkboxMatch = cvField;
                                    _logger.LogDebug($"    -> New best match: '{cvField.FieldName}' with yDist={yDistance}");
                                }
                            }
                        }
                        else
                        {
                            // Fall back to order-based matching when coordinates aren't available
                            _logger.LogDebug("Claude fields have no valid coordinates, using order-based matching");
                            
                            // Get checkbox fields and labels that look like checkbox options
                            var checkboxLabels = cvFieldsSorted.Where(f => 
                                f.FieldType?.ToLower() == "checkbox" || 
                                f.FieldName?.Contains("option", StringComparison.OrdinalIgnoreCase) == true ||
                                f.FieldName?.Contains("check", StringComparison.OrdinalIgnoreCase) == true ||
                                f.FieldName?.Contains("select", StringComparison.OrdinalIgnoreCase) == true
                            ).ToList();
                            
                            // Match by index in sorted order
                            var checkboxIndex = sfFieldsSorted.Where(f => f.FieldType.ToLower() == "checkbox").ToList().IndexOf(sfField);
                            if (checkboxIndex >= 0 && checkboxIndex < checkboxLabels.Count)
                            {
                                checkboxMatch = checkboxLabels[checkboxIndex];
                                _logger.LogDebug($"Matched checkbox by index {checkboxIndex}: '{checkboxMatch.FieldName}'");
                            }
                        }
                        
                        if (checkboxMatch != null)
                        {
                            // Keep it as checkbox but use Claude's label
                            var enhanced = new FieldDetectionResult
                            {
                                ShortId = sfField.ShortId,
                                FieldName = checkboxMatch.FieldName,     // Claude's label matched by Y position
                                FieldType = "checkbox",                  // ALWAYS keep as checkbox
                                X = sfField.X,
                                Y = sfField.Y,
                                Width = sfField.Width,
                                Height = sfField.Height,
                                PageNumber = sfField.PageNumber,
                                Source = "Syncfusion+Claude",
                                Confidence = sfField.Confidence,
                                IsValid = sfField.IsValid,
                                Tooltip = FieldTooltipGenerator.GenerateTooltip("checkbox", checkboxMatch.FieldName)
                            };
                            enhancedFields.Add(enhanced);
                            _logger.LogDebug($"Enhanced checkbox {sfField.ShortId} at Y={sfField.Y}: label → '{checkboxMatch.FieldName}'");
                            
                            // Don't mark as used for checkboxes - allow reuse
                        }
                        else
                        {
                            // No match found, keep original
                            string labelToUse = sfField.FieldName;
                            
                            if (!string.IsNullOrEmpty(labelToUse))
                            {
                                sfField.FieldName = labelToUse;
                                sfField.Tooltip = FieldTooltipGenerator.GenerateTooltip("checkbox", labelToUse);
                            }
                            else if (!string.IsNullOrWhiteSpace(sfField.FieldName) && !sfField.FieldName.StartsWith("Check") && !sfField.FieldName.Contains("502fba303ae4"))
                            {
                                // Keep original name if it's meaningful
                                sfField.Tooltip = FieldTooltipGenerator.GenerateTooltip("checkbox", sfField.FieldName);
                            }
                            else
                            {
                                // Generate a contextual default based on position
                                sfField.FieldName = $"Option {sfField.ShortId.Replace("SF", "")}";
                                sfField.Tooltip = $"Check if Option {sfField.ShortId.Replace("SF", "")} applies";
                            }
                            enhancedFields.Add(sfField);
                            _logger.LogDebug($"No Claude label for checkbox {sfField.ShortId}, using: {sfField.FieldName}");
                        }
                    }
                    else
                    {
                        // For text fields, use the matched field
                        if (bestMatch != null)
                        {
                            usedLabels.Add(bestMatch.FieldName);
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
                                IsValid = sfField.IsValid,
                                Tooltip = FieldTooltipGenerator.GenerateTooltip(compatibleType, bestMatch.FieldName)
                            };
                            enhancedFields.Add(enhanced);
                            _logger.LogDebug($"Enhanced field {sfField.ShortId}: '{sfField.FieldName}' → '{bestMatch.FieldName}' (type: {compatibleType})");
                        }
                        else
                        {
                            // No more labels available
                            sfField.Tooltip = FieldTooltipGenerator.GenerateTooltip(sfField.FieldType, sfField.FieldName);
                            enhancedFields.Add(sfField);
                        }
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