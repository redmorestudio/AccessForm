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
using WordToPdfConverter.Services;
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

            // DEBUG: Log service initialization status
            _logger.LogInformation($"[SERVICE-INIT] ClaudeVisionFieldDetector: {(_visionDetector != null ? "Available" : "NULL")}");
            _logger.LogInformation($"[SERVICE-INIT] GoogleDocumentAiService: {(_googleAiService != null ? "Available" : "NULL")}");
            _logger.LogInformation($"[SERVICE-INIT] AnthropicService: {(_anthropicService != null ? "Available" : "NULL")}");
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
            
            _logger.LogInformation($"[MODE-CHECK] Processing mode: {config.Mode}, Sequential: {(config.Mode == ProcessingMode.Sequential)}, SyncfusionWithValidation: {(config.Mode == ProcessingMode.SyncfusionWithValidation)}");

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
                _logger.LogInformation($"[PHASE-3-CHECK] UseClaudeVision={config.Services.UseClaudeVision}, VisionDetector={(_visionDetector != null ? "Available" : "NULL")}");
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
            
            // DISABLED: Don't filter out "invalid" fields - we want ALL detected fields
            // Let the user see everything Syncfusion found
            /*
            if (!config.DebugMode)
            {
                detectedFields = detectedFields.Where(f => f.IsValid).ToList();
            }
            */
            
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

                        _logger.LogInformation($"[PAGE DEBUG] Processing field '{field.Name}', field.Page is null: {field.Page == null}");

                        // Use Syncfusion's OFFICIAL recommended method for page detection
                        _logger.LogInformation($"[SYNCFUSION-OFFICIAL] Using official Syncfusion page detection for '{field.Name}'");

                        // Use Syncfusion's official page reference when available
                        bool pageFound = false;
                        if (field.Page != null)
                        {
                            try
                            {
                                // Find page index by iterating through pages
                                var pageIndex = -1;
                                for (int pageIdx = 0; pageIdx < pdfDoc.Pages.Count; pageIdx++)
                                {
                                    if (pdfDoc.Pages[pageIdx] == field.Page)
                                    {
                                        pageIndex = pageIdx;
                                        break;
                                    }
                                }
                                if (pageIndex >= 0)
                                {
                                    pageNum = pageIndex + 1; // Convert 0-based to 1-based
                                    pageFound = true;
                                    _logger.LogInformation($"[SYNCFUSION-OFFICIAL] Field '{field.Name}' found on page {pageNum} using official method");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"[SYNCFUSION-OFFICIAL] Exception getting page for '{field.Name}': {ex.Message}");
                            }
                        }

                        // REMOVED BROKEN COORDINATE FALLBACK
                        // The coordinate detection (Y < 150 = page 2) is broken because all fields have Y ≈ 370
                        // This was forcing all fields to page 1 incorrectly
                        if (!pageFound)
                        {
                            _logger.LogWarning($"[NO-FALLBACK] Official Syncfusion method unavailable for '{field.Name}', defaulting to page 1 (coordinate detection removed due to Y<150 bug)");
                            pageNum = 1; // Safe default instead of broken coordinate detection
                        }

                        // Clean implementation: Official Syncfusion method with coordinate fallback
                        
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

                        // Enhanced debugging for field detection
                        _logger.LogInformation($"[FIELD DETECTION] Detected field: {detectedField.FieldName}");
                        _logger.LogInformation($"  - Original Name: {field.Name}");
                        _logger.LogInformation($"  - Type: {detectedField.FieldType}");
                        _logger.LogInformation($"  - Position: ({detectedField.X:F2}, {detectedField.Y:F2})");
                        _logger.LogInformation($"  - Size: {detectedField.Width:F2}x{detectedField.Height:F2}");
                        _logger.LogInformation($"  - Page: {detectedField.PageNumber} (HasPageRef: {field.Page != null})");
                        _logger.LogInformation($"  - Source: {detectedField.Source}");
                        
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
            // DISABLED ALL FILTERING - We want ALL fields that Syncfusion detects
            // Let Claude Vision and Claude Validation handle any false positives
            return false;

            /* ORIGINAL CODE - DISABLED because it was filtering out valid fields
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
            */
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

            // DON'T CLEAN SYNCFUSION NATIVE FIELD NAMES!
            // Syncfusion creates names like "text332aba" which are perfect for machine processing
            // Only clean names that have obvious prefixes to remove
            if (name.Contains("Textformfield") || name.Contains("formfield"))
            {
                name = name.Replace("Textformfield", "")
                          .Replace("formfield", "")
                          .Replace("_", " ")
                          .Trim();
            }

            // If after cleaning we have nothing, return "Field"
            if (string.IsNullOrWhiteSpace(name))
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

            _logger.LogInformation($"[CV_DEBUG] Claude Vision returned {visionResults.Count} pages");

            foreach (var page in visionResults)
            {
                _logger.LogInformation($"[CV_DEBUG] Processing Claude Vision page {page.PageNumber} with {page.Fields.Count} fields");

                foreach (var vField in page.Fields)
                {
                    var shortId = $"CV{++_fieldCounter}";

                    // Check if bounds are actually populated
                    if (vField.Bounds == null)
                    {
                        _logger.LogWarning($"Claude Vision field '{vField.FieldName}' has null bounds - skipping coordinate extraction");
                        continue;
                    }

                    // Get the page dimensions to convert from percentages
                    float pageWidth = 612f;  // Default to US Letter
                    float pageHeight = 792f;

                    // Try to get actual page dimensions from the PDF
                    try
                    {
                        using (var pdfStream = new MemoryStream(pdfBytes))
                        using (var pdfDoc = new PdfLoadedDocument(pdfStream))
                        {
                            if (page.PageNumber > 0 && page.PageNumber <= pdfDoc.Pages.Count)
                            {
                                var pdfPage = pdfDoc.Pages[page.PageNumber - 1];
                                pageWidth = pdfPage.Size.Width;
                                pageHeight = pdfPage.Size.Height;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Could not get page dimensions for Claude Vision field conversion: {ex.Message}");
                    }

                    // Convert percentage bounds to PDF coordinates (bottom-left origin)
                    var pdfBounds = ClaudeVisionFieldDetector.ConvertPercentageToPdfBounds(
                        vField.Bounds,
                        pageWidth,
                        pageHeight
                    );

                    _logger.LogDebug($"Claude field '{vField.FieldName}' converted bounds: X={pdfBounds.X:F1}, Y={pdfBounds.Y:F1}, W={pdfBounds.Width:F1}, H={pdfBounds.Height:F1}");

                    // [CLAUDE_VISION_DEBUG] - Log page transfer - EASY TO REMOVE
                    _logger.LogInformation($"[CLAUDE_VISION_DEBUG] Transferring field '{vField.FieldName}' from page {vField.PageNumber} to FieldDetectionResult with page {page.PageNumber}");

                    fields.Add(new FieldDetectionResult
                    {
                        ShortId = shortId,
                        FieldName = vField.FieldName,
                        FieldType = vField.FieldType,
                        X = pdfBounds.X,
                        Y = pdfBounds.Y,
                        Width = pdfBounds.Width,
                        Height = pdfBounds.Height,
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
                // All field sources now properly store coordinates in PDF format (bottom-left origin):
                // - Syncfusion: Already in PDF coordinates
                // - ClaudeVision: Converted to PDF coordinates by ConvertPercentageToPdfBounds
                // - Syncfusion+Claude: Already in PDF coordinates
                // - Google: Already in PDF coordinates
                // No coordinate conversion needed!
                float pdfY = field.Y;

                // COORDINATES ARE ALREADY PAGE-RELATIVE - DON'T ADD PAGE OFFSETS!
                // Syncfusion provides page-relative coordinates (0-792 per page)
                // PyMuPDF also expects page-relative coordinates
                // The old code incorrectly made Page 2 fields have Y > 792, pushing them off-page
                _logger.LogInformation($"[COORDINATE FIX] Field '{field.FieldName}' on page {field.PageNumber}: Y={field.Y} stays as Y={pdfY} (page-relative)");
                
                // No adjustment needed - use the calculated position directly
                // pdfY -= 10;
                
                var bounds = new RectangleF(field.X, pdfY, field.Width, field.Height);

                // Only get intelligent field name if Claude Vision is enabled
                // Otherwise use the raw Syncfusion field name (like "text332aba")
                string fieldNameToUse = field.FieldName;
                if (config.Services.UseClaudeVision || config.Services.UseClaudeValidation)
                {
                    fieldNameToUse = GetIntelligentFieldName(field);
                }

                // Apply smart sizing based on field type
                bounds = ApplySmartFieldSizing(bounds, field.FieldType, fieldNameToUse);

                // Add the field based on type
                PdfField pdfField = null;
                string tooltip = GetFieldTooltip(field);
                
                _logger.LogInformation($"[PDF FIELD CREATION] Creating field type: {field.FieldType}");
                _logger.LogInformation($"[PDF FIELD CREATION] Original ShortId: {field.ShortId}");
                _logger.LogInformation($"[PDF FIELD CREATION] Original FieldName: {field.FieldName}");
                _logger.LogInformation($"[PDF FIELD CREATION] Field name to use: {fieldNameToUse}");
                _logger.LogInformation($"[PDF FIELD CREATION] Tooltip: {tooltip}");
                _logger.LogInformation($"[PDF FIELD CREATION] Source: {field.Source}");
                _logger.LogInformation($"[PDF FIELD CREATION] Page: {field.PageNumber}, X: {field.X}, Y: {field.Y}");

                switch (field.FieldType.ToLower())
                {
                    case "checkbox":
                        _logger.LogInformation($"[CHECKBOX CREATION] Creating checkbox with name: '{fieldNameToUse}'");
                        var checkField = new PdfCheckBoxField(pdfDoc.Pages[field.PageNumber - 1],
                            fieldNameToUse);
                        checkField.Bounds = bounds;
                        checkField.ToolTip = $"[PAGE:{field.PageNumber}] {tooltip}";
                        pdfField = checkField;
                        _logger.LogInformation($"[CHECKBOX CREATED] Name set to: '{checkField.Name}', Page stored in ToolTip");
                        break;
                        
                    case "radio":
                        _logger.LogInformation($"[RADIO CREATION] Creating radio with name: '{fieldNameToUse}'");
                        var radioField = new PdfRadioButtonListField(pdfDoc.Pages[field.PageNumber - 1],
                            fieldNameToUse);
                        // Radio button list needs items, add a default one
                        var radioItem = new PdfRadioButtonListItem("Option1");
                        radioItem.Bounds = bounds;
                        radioField.Items.Add(radioItem);
                        radioField.ToolTip = tooltip;
                        pdfField = radioField;
                        _logger.LogInformation($"[RADIO CREATED] Name set to: '{radioField.Name}'");
                        break;
                        
                    case "signature":
                        _logger.LogInformation($"[SIGNATURE CREATION] Creating signature field with name: '{fieldNameToUse}'");
                        // CREATE TEXT FIELD INSTEAD OF SIGNATURE FIELD TO PREVENT DOCUMENT LOCKING
                        var sigTextField = new PdfTextBoxField(pdfDoc.Pages[field.PageNumber - 1],
                            fieldNameToUse);
                        sigTextField.Bounds = bounds;
                        sigTextField.ToolTip = tooltip + " (Signature)";
                        sigTextField.BackColor = new PdfColor(245, 245, 245); // Light gray background
                        pdfField = sigTextField;
                        _logger.LogInformation($"[SIGNATURE CREATED] Text field created instead with name: '{sigTextField.Name}'");
                        break;
                        
                    default:
                        _logger.LogInformation($"[TEXT FIELD CREATION] Creating text field with name: '{fieldNameToUse}' for type: {field.FieldType}");
                        // Create text field for all text-based types
                        var textField = new PdfTextBoxField(pdfDoc.Pages[field.PageNumber - 1],
                            fieldNameToUse);
                        _logger.LogInformation($"[TEXT FIELD CREATED] Name after creation: '{textField.Name}'");
                        textField.Bounds = bounds;
                        
                        // Apply field-type specific formatting and validation
                        ApplyFieldTypeFormatting(textField, field.FieldType);
                        
                        // Set tooltip with type-specific guidance
                        textField.ToolTip = FieldTooltipGenerator.GenerateTooltip(field.FieldType, field.FieldName);
                        
                        // Store page info in DefaultValue for PassportPDF (not accessible to screen readers)
                        textField.DefaultValue = $"PAGE:{field.PageNumber}";
                        
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
                    _logger.LogInformation($"[FIELD ADDITION] About to add field to PDF form");
                    _logger.LogInformation($"[FIELD ADDITION] Field name before adding: '{pdfField.Name}'");

                    // Add debug info to tooltip if in debug mode
                    if (config.ShowFieldIds)
                    {
                        var currentTooltip = GetFieldTooltipFromPdfField(pdfField);
                        var debugTooltip = $"[{field.ShortId}] {field.Source} | Type: {field.FieldType} | {currentTooltip}";
                        _logger.LogInformation($"[DEBUG TOOLTIP] Adding debug tooltip: {debugTooltip}");
                        SetFieldTooltip(pdfField, debugTooltip);
                    }

                    // Store the name before adding
                    string nameBeforeAdd = pdfField.Name;
                    _logger.LogInformation($"[PRE-ADD] Field name before adding to form: '{nameBeforeAdd}'");

                    pdfDoc.Form.Fields.Add(pdfField);

                    // Verify name after adding
                    string nameAfterAdd = pdfField.Name;

                    // ULTRATHINK: Log page information when adding fields
                    string pageInfo = "unknown";
                    if (pdfField is PdfTextBoxField textField)
                    {
                        var calculatedPage = PdfCoordinateConverter.CalculatePageFromY(textField.Bounds.Y, pdfDoc, _logger);
                        pageInfo = $"Y={textField.Bounds.Y}, Page calculated={calculatedPage}, Expected page={field.PageNumber}";
                    }
                    else if (pdfField is PdfCheckBoxField checkField)
                    {
                        var calculatedPage = PdfCoordinateConverter.CalculatePageFromY(checkField.Bounds.Y, pdfDoc, _logger);
                        pageInfo = $"Y={checkField.Bounds.Y}, Page calculated={calculatedPage}, Expected page={field.PageNumber}";
                    }

                    _logger.LogInformation($"[POST-ADD] Field name after adding to form: '{nameAfterAdd}', {pageInfo}");

                    if (nameBeforeAdd != nameAfterAdd)
                    {
                        _logger.LogWarning($"[NAME CHANGED] Field name changed during add: '{nameBeforeAdd}' -> '{nameAfterAdd}'");
                    }

                    _logger.LogInformation($"[FIELD ADDED] Successfully added field '{pdfField.Name}' to PDF form");
                    _logger.LogInformation($"[FIELD ADDED] Total fields in form: {pdfDoc.Form.Fields.Count}");

                    // Double-check by retrieving the field back
                    var lastField = pdfDoc.Form.Fields[pdfDoc.Form.Fields.Count - 1];
                    _logger.LogInformation($"[VERIFY] Last field in form has name: '{lastField.Name}'");
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
        
        private string GetIntelligentFieldName(FieldDetectionResult field)
        {
            // If the field already has an intelligent name (not starting with SF), use it
            if (!string.IsNullOrEmpty(field.FieldName) && !field.FieldName.StartsWith("SF"))
            {
                _logger.LogInformation($"Field already has intelligent name: '{field.FieldName}'");
                return field.FieldName;
            }

            // If the field has a ShortId but the FieldName is different, it's already enhanced
            if (!string.IsNullOrEmpty(field.ShortId) && field.ShortId != field.FieldName)
            {
                _logger.LogInformation($"Field {field.ShortId} already enhanced with name: '{field.FieldName}'");
                return field.FieldName;
            }

            // Try to generate intelligent field name with NLP generator
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

                    if (labelResult.Success && !string.IsNullOrEmpty(labelResult.ProgrammaticName))
                    {
                        _logger.LogInformation($"Generated intelligent name '{labelResult.ProgrammaticName}' for field '{field.FieldName}'");
                        // Return the intelligent programmatic name for the field
                        return labelResult.ProgrammaticName;
                    }
                    else
                    {
                        _logger.LogWarning($"No intelligent name generated for field '{field.FieldName}' - using original");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to generate intelligent field name for {field.FieldName}: {ex.Message}");
                }
            }
            else
            {
                _logger.LogWarning("NLP generator is null - cannot generate intelligent field names");
            }

            // Fallback to original name if NLP generation fails
            return field.FieldName;
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

            // Get page dimensions from first page
            float pageWidth = 612;  // Default to letter size
            float pageHeight = 792;

            using (var loadedDocument = new Syncfusion.Pdf.Parsing.PdfLoadedDocument(pdfBytes))
            {
                if (loadedDocument.Pages.Count > 0)
                {
                    var firstPage = loadedDocument.Pages[0];
                    pageWidth = firstPage.Size.Width;
                    pageHeight = firstPage.Size.Height;
                    _logger.LogInformation($"Page dimensions: {pageWidth} x {pageHeight}");
                }
            }

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
                    // Convert Claude's percentage coordinates to PDF coordinates if available
                    float x = 0, y = 0, width = 100, height = 20;
                    bool hasCoordinates = false;

                    if (vField.Bounds != null && (vField.Bounds.XPercent > 0 || vField.Bounds.YPercent > 0))
                    {
                        // Claude gives percentages with TOP-LEFT origin
                        x = (vField.Bounds.XPercent / 100f) * pageWidth;
                        float yFromTop = (vField.Bounds.YPercent / 100f) * pageHeight;

                        // Get field height first for proper conversion
                        height = (vField.Bounds.HeightPercent / 100f) * pageHeight;
                        width = (vField.Bounds.WidthPercent / 100f) * pageWidth;

                        // CRITICAL: Convert Y from top-left to bottom-left origin
                        // PDF coordinates have origin at bottom-left
                        y = pageHeight - yFromTop - height;  // NOT just pageHeight - yFromTop!

                        hasCoordinates = true;
                        _logger.LogDebug($"Claude field '{vField.FieldName}': " +
                            $"Percent({vField.Bounds.XPercent}, {vField.Bounds.YPercent}) -> " +
                            $"PDF({x:F1}, {y:F1}) [converted from top Y={yFromTop:F1}]");
                    }

                    claudeFields.Add(new FieldDetectionResult
                    {
                        ShortId = $"CV_temp",
                        FieldName = vField.FieldName,
                        FieldType = vField.FieldType,
                        X = x,
                        Y = y,
                        Width = width,
                        Height = height,
                        PageNumber = page.PageNumber,
                        Source = "ClaudeVision",
                        Confidence = hasCoordinates ? 0.95f : 0.9f,
                        IsValid = true,
                        HasValidCoordinates = hasCoordinates  // Track if we have real coordinates
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
                
                // Sort Syncfusion fields by position, Claude fields maintain their detection order
                var sfFieldsSorted = sfFields.OrderBy(f => f.Y).ThenBy(f => f.X).ToList();
                var cvFieldsSorted = cvFields.ToList();  // Keep Claude fields in their original order

                _logger.LogInformation($"Page {pageNum}: {sfFields.Count} Syncfusion fields, {cvFields.Count} Claude labels");

                // DEBUG: Log Claude Vision fields and their coordinates
                if (cvFields.Count > 0)
                {
                    _logger.LogInformation($"[CV_DEBUG] Claude Vision fields on page {pageNum}:");
                    foreach (var cvf in cvFieldsSorted)
                    {
                        _logger.LogInformation($"[CV_DEBUG]   - {cvf.FieldName}: X={cvf.X:F1}, Y={cvf.Y:F1}, W={cvf.Width:F1}, H={cvf.Height:F1}");
                    }
                }
                else
                {
                    _logger.LogWarning($"[CV_DEBUG] No Claude Vision fields found on page {pageNum}!");
                }
                
                // Debug: Log the last few fields to see what's happening at the bottom
                if (sfFieldsSorted.Count > 0)
                {
                    var lastSfField = sfFieldsSorted.Last();
                    _logger.LogInformation($"Last Syncfusion field: {lastSfField.FieldName} at Y={lastSfField.Y}, X={lastSfField.X}, Type={lastSfField.FieldType}");
                }
                if (cvFieldsSorted.Count > 0)
                {
                    var bottomCvFields = cvFieldsSorted.Where((f, idx) => f.Y > 250 || idx >= cvFieldsSorted.Count - 5).ToList();
                    foreach (var cvField in bottomCvFields)
                    {
                        _logger.LogInformation($"Bottom Claude field: {cvField.FieldName} at Y={cvField.Y}, X={cvField.X}, Type={cvField.FieldType}");
                    }
                }
                
                // Match fields - first by name, then by position
                var usedLabels = new HashSet<string>();
                
                // Detect potential phantom fields before processing
                var phantomFields = new List<FieldDetectionResult>();

                // Process all Syncfusion fields
                foreach (var sfField in sfFieldsSorted)
                {
                    FieldDetectionResult bestMatch = null;

                    // Check if this might be a phantom field
                    bool isLikelyPhantom = IsLikelyPhantomField(sfField);

                    if (isLikelyPhantom)
                    {
                        _logger.LogWarning($"Detected likely phantom field at Y={sfField.Y}, X={sfField.X}: {sfField.FieldName ?? "unnamed"}");
                    }

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

                    // If still no match and we have valid coordinates, try proximity matching
                    if (bestMatch == null && !isLikelyPhantom)
                    {
                        // Check if we have any Claude fields with valid coordinates for proximity matching
                        var claudeFieldsWithCoords = cvFieldsSorted.Where(f => f.HasValidCoordinates && !usedLabels.Contains(f.FieldName)).ToList();
                        if (claudeFieldsWithCoords.Count > 0)
                        {
                            // Find the closest Claude field by position
                            double minDistance = double.MaxValue;
                            foreach (var cvField in claudeFieldsWithCoords)
                            {
                                double distance = Math.Sqrt(Math.Pow(sfField.X - cvField.X, 2) + Math.Pow(sfField.Y - cvField.Y, 2));
                                if (distance < minDistance && distance < 100) // Within 100 pixels
                                {
                                    minDistance = distance;
                                    bestMatch = cvField;
                                }
                            }
                            if (bestMatch != null)
                            {
                                _logger.LogDebug($"Matched {sfField.FieldName} to {bestMatch.FieldName} by proximity (distance: {minDistance:F1})");
                            }
                        }
                    }

                    // If still no match, try index-based matching when coordinates aren't available
                    if (bestMatch == null && !isLikelyPhantom) // Don't force-match phantom fields
                    {
                        // Match by index order - find the next unused Claude field
                        foreach (var cvField in cvFieldsSorted)
                        {
                            if (usedLabels.Contains(cvField.FieldName))
                                continue;

                            // Prefer matching field types
                            var typeCompatible =
                                (sfField.FieldType.ToLower() == "checkbox" && cvField.FieldType.ToLower() == "checkbox") ||
                                (sfField.FieldType.ToLower() != "checkbox" && cvField.FieldType.ToLower() != "checkbox");

                            if (typeCompatible)
                            {
                                bestMatch = cvField;
                                _logger.LogDebug($"Matched {sfField.FieldName} to {bestMatch.FieldName} by index order");
                                break;
                            }
                        }

                        // If no type-compatible match, take any unused field
                        if (bestMatch == null)
                        {
                            bestMatch = cvFieldsSorted.FirstOrDefault(cv => !usedLabels.Contains(cv.FieldName));
                            if (bestMatch != null)
                            {
                                _logger.LogDebug($"Matched {sfField.FieldName} to {bestMatch.FieldName} by index (no type match)");
                            }
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
                        var hasValidCoordinates = cvFieldsSorted.Any(f => f.HasValidCoordinates);
                        
                        if (hasValidCoordinates)
                        {
                            // Use position-based matching
                            foreach (var cvField in cvFieldsSorted)
                            {
                                // Skip fields without valid coordinates
                                if (!cvField.HasValidCoordinates)
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
                            var checkboxFields = sfFieldsSorted.Where(f => f.FieldType.ToLower() == "checkbox").ToList();
                            var checkboxIndex = -1;
                            for (int idx = 0; idx < checkboxFields.Count; idx++)
                            {
                                if (checkboxFields[idx] == sfField)
                                {
                                    checkboxIndex = idx;
                                    break;
                                }
                            }
                            if (checkboxIndex >= 0 && checkboxIndex < checkboxLabels.Count)
                            {
                                checkboxMatch = checkboxLabels[checkboxIndex];
                                _logger.LogDebug($"Matched checkbox by index {checkboxIndex}: '{checkboxMatch.FieldName}'");
                            }
                        }
                        
                        if (checkboxMatch != null)
                        {
                            // Keep it as checkbox but use Claude's label
                            _logger.LogInformation($"[ENHANCEMENT] Checkbox {sfField.ShortId} enhancement starting:");
                            _logger.LogInformation($"[ENHANCEMENT]   - Original ShortId: {sfField.ShortId}");
                            _logger.LogInformation($"[ENHANCEMENT]   - Original FieldName: {sfField.FieldName}");
                            _logger.LogInformation($"[ENHANCEMENT]   - Claude match FieldName: {checkboxMatch.FieldName}");
                            _logger.LogInformation($"[ENHANCEMENT]   - Position: X={sfField.X}, Y={sfField.Y}, W={sfField.Width}, H={sfField.Height}");

                            // [CLAUDE_VISION_DEBUG] - Log checkbox merge - EASY TO REMOVE
                            _logger.LogInformation($"[CLAUDE_VISION_DEBUG] CHECKBOX MERGE: SF field '{sfField.FieldName}' (page {sfField.PageNumber}) + Claude '{checkboxMatch.FieldName}' (page {checkboxMatch.PageNumber}) -> Using Claude name + Claude page {(checkboxMatch.PageNumber > 0 ? checkboxMatch.PageNumber : 1)}");

                            var enhanced = new FieldDetectionResult
                            {
                                ShortId = sfField.ShortId,
                                FieldName = checkboxMatch.FieldName,     // Claude's label matched by Y position
                                FieldType = "checkbox",                  // ALWAYS keep as checkbox
                                X = sfField.X,
                                Y = sfField.Y,
                                Width = sfField.Width,
                                Height = sfField.Height,
                                PageNumber = checkboxMatch.PageNumber > 0 ? checkboxMatch.PageNumber : 1, // Trust Claude Vision, fallback to page 1 (NOT Syncfusion's broken detection)
                                Source = "Syncfusion+Claude",
                                Confidence = sfField.Confidence,
                                IsValid = sfField.IsValid,
                                Tooltip = FieldTooltipGenerator.GenerateTooltip("checkbox", checkboxMatch.FieldName, checkboxMatch.PageNumber > 0 ? checkboxMatch.PageNumber : 1) // Trust Claude Vision, fallback to page 1 (NOT broken Syncfusion detection)
                            };

                            _logger.LogInformation($"[ENHANCEMENT]   - Enhanced field created with name: '{enhanced.FieldName}'");
                            _logger.LogInformation($"[ENHANCEMENT]   - Enhanced field tooltip: '{enhanced.Tooltip}'");

                            enhancedFields.Add(enhanced);
                            _logger.LogDebug($"Enhanced checkbox {sfField.ShortId} at Y={sfField.Y}: label → '{checkboxMatch.FieldName}'");
                            _logger.LogInformation($"[ENHANCEMENT] Checkbox {sfField.ShortId} enhancement complete");
                            
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
                            _logger.LogInformation($"[ENHANCEMENT] Text field {sfField.ShortId} enhancement starting:");
                            _logger.LogInformation($"[ENHANCEMENT]   - Original ShortId: {sfField.ShortId}");
                            _logger.LogInformation($"[ENHANCEMENT]   - Original FieldName: {sfField.FieldName}");
                            _logger.LogInformation($"[ENHANCEMENT]   - Original FieldType: {sfField.FieldType}");
                            _logger.LogInformation($"[ENHANCEMENT]   - Best match FieldName: {bestMatch.FieldName}");
                            _logger.LogInformation($"[ENHANCEMENT]   - Best match FieldType: {bestMatch.FieldType}");
                            _logger.LogInformation($"[ENHANCEMENT]   - Position: X={sfField.X}, Y={sfField.Y}, W={sfField.Width}, H={sfField.Height}");

                            // Create enhanced field with intelligent type detection
                            var detectedType = FieldTypeDetector.DetectFieldType(bestMatch.FieldName, null, bestMatch.FieldType);
                            _logger.LogInformation($"[ENHANCEMENT]   - Detected type from name: {detectedType}");

                            // Validate the type conversion is compatible
                            var compatibleType = FieldTypeCompatibilityChecker.GetCompatibleType(sfField.FieldType, detectedType);
                            if (compatibleType != detectedType)
                            {
                                _logger.LogWarning($"Prevented invalid conversion for {sfField.ShortId}: {sfField.FieldType} → {detectedType}, using {compatibleType} instead");
                            }
                            _logger.LogInformation($"[ENHANCEMENT]   - Compatible type: {compatibleType}");

                            // [CLAUDE_VISION_DEBUG] - Log general field merge - EASY TO REMOVE
                            _logger.LogInformation($"[CLAUDE_VISION_DEBUG] FIELD MERGE: SF field '{sfField.FieldName}' (page {sfField.PageNumber}) + Claude '{bestMatch.FieldName}' (page {bestMatch.PageNumber}) -> Using Claude name + Claude page {(bestMatch.PageNumber > 0 ? bestMatch.PageNumber : 1)}");

                            var enhanced = new FieldDetectionResult
                            {
                                ShortId = sfField.ShortId,
                                FieldName = bestMatch.FieldName,  // Use Claude's label
                                FieldType = compatibleType,  // Use compatible type
                                X = sfField.X,
                                Y = sfField.Y,
                                Width = sfField.Width,
                                Height = sfField.Height,
                                PageNumber = bestMatch.PageNumber > 0 ? bestMatch.PageNumber : 1, // Trust Claude Vision, fallback to page 1 (NOT Syncfusion's broken detection)
                                Source = "Syncfusion+Claude",
                                Confidence = Math.Max(sfField.Confidence, bestMatch.Confidence),
                                IsValid = sfField.IsValid,
                                Tooltip = FieldTooltipGenerator.GenerateTooltip(compatibleType, bestMatch.FieldName, bestMatch.PageNumber > 0 ? bestMatch.PageNumber : 1) // Trust Claude Vision, fallback to page 1 (NOT broken Syncfusion detection)
                            };

                            _logger.LogInformation($"[ENHANCEMENT]   - Enhanced field created with name: '{enhanced.FieldName}'");
                            _logger.LogInformation($"[ENHANCEMENT]   - Enhanced field type: '{enhanced.FieldType}'");
                            _logger.LogInformation($"[ENHANCEMENT]   - Enhanced field tooltip: '{enhanced.Tooltip}'");
                            _logger.LogInformation($"[ENHANCEMENT] Text field {sfField.ShortId} enhancement complete");
                            enhancedFields.Add(enhanced);
                            _logger.LogDebug($"Enhanced field {sfField.ShortId}: '{sfField.FieldName}' → '{bestMatch.FieldName}' (type: {compatibleType})");
                        }
                        else if (!isLikelyPhantom)  // Only add non-phantom fields that don't have matches
                        {
                            // No more labels available - ensure field has a reasonable name
                            if (string.IsNullOrEmpty(sfField.FieldName) || sfField.FieldName == "Field")
                            {
                                // Generate a better default name based on type and position
                                sfField.FieldName = $"{sfField.FieldType}_{enhancedFields.Count + 1}";
                            }
                            sfField.Tooltip = FieldTooltipGenerator.GenerateTooltip(sfField.FieldType, sfField.FieldName);
                            enhancedFields.Add(sfField);
                        }
                        else
                        {
                            // This is a phantom field with no match - skip it entirely
                            _logger.LogInformation($"Skipping phantom field {sfField.ShortId} ('{sfField.FieldName}') - no Claude label and detected as phantom");
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
                    // [CLAUDE_VISION_DEBUG] - Log third merge point - EASY TO REMOVE
                    _logger.LogInformation($"[CLAUDE_VISION_DEBUG] THIRD MERGE: SF field '{sfField.FieldName}' (page {sfField.PageNumber}) + Claude '{bestMatch.FieldName}' (page {bestMatch.PageNumber}) -> Using Claude name + Claude page {(bestMatch.PageNumber > 0 ? bestMatch.PageNumber : 1)}");

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
                        PageNumber = bestMatch.PageNumber > 0 ? bestMatch.PageNumber : 1, // Trust Claude Vision, fallback to page 1 (NOT Syncfusion's broken detection)
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
        private bool IsLikelyPhantomField(FieldDetectionResult field)
        {
            // Detect fields that are likely phantoms (false positives that throw off matching)

            // Check for Syncfusion's 12-character hex-like auto-generated names (e.g., "a1bb168e5744")
            bool hasGibberishName = false;
            if (!string.IsNullOrEmpty(field.FieldName) && field.FieldName.Length == 12)
            {
                // Check if all characters are hex digits (0-9, a-f)
                hasGibberishName = field.FieldName.All(c => "0123456789abcdef".Contains(char.ToLower(c)));
                if (hasGibberishName)
                {
                    _logger.LogDebug($"Detected gibberish Syncfusion name: {field.FieldName}");
                }
            }

            // Check if field has no name or other suspicious auto-generated names
            bool hasNoMeaningfulName = string.IsNullOrEmpty(field.FieldName) ||
                                       hasGibberishName ||
                                       field.FieldName.StartsWith("field_", StringComparison.OrdinalIgnoreCase) ||
                                       field.FieldName == "Field";  // Generic default name

            // Check if field is at suspicious location (e.g., very bottom of page)
            const float BOTTOM_THRESHOLD = 100f; // Fields with Y < 100 are near bottom in PDF coordinates
            bool isNearBottom = field.Y < BOTTOM_THRESHOLD;

            // Check if field has suspicious dimensions
            bool hasSuspiciousDimensions = field.Width <= 0 || field.Height <= 0 ||
                                          field.Width > 500 || field.Height > 100;

            // A field is likely phantom if:
            // 1. It has a gibberish 12-char hex name (high confidence phantom)
            // 2. It has no meaningful name AND is at the bottom of the page
            // 3. It has suspicious dimensions
            // 4. It's a Syncfusion-detected field with no proper name at an edge location
            bool isPhantom = hasGibberishName ||  // This alone is enough to mark as phantom
                            (hasNoMeaningfulName && isNearBottom) ||
                            hasSuspiciousDimensions ||
                            (field.Source == "Syncfusion" && hasNoMeaningfulName && (isNearBottom || field.Y > 750));

            if (isPhantom)
            {
                _logger.LogWarning($"Field {field.ShortId} marked as likely phantom: Name='{field.FieldName}', Y={field.Y}, Source={field.Source}");
            }

            return isPhantom;
        }
    }
}