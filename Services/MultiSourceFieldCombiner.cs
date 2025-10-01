using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Syncfusion.Drawing;
using AccessFormServer.Services;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Combines field detection results from multiple sources (Vision, Google AI, text analysis)
    /// to produce the most accurate field placement
    /// </summary>
    public class MultiSourceFieldCombiner
    {
        private readonly ILogger<MultiSourceFieldCombiner> _logger;
        
        public MultiSourceFieldCombiner(ILogger<MultiSourceFieldCombiner> logger)
        {
            _logger = logger;
        }
        
        public class CombinedField
        {
            public string FieldName { get; set; }
            public string FieldType { get; set; }
            public RectangleF Bounds { get; set; }
            public int PageNumber { get; set; }
            public float Confidence { get; set; }
            public string Source { get; set; } // Which detection method found this
            public bool IsRequired { get; set; }
            public string Description { get; set; }
        }
        
        /// <summary>
        /// Combines field results from multiple detection sources
        /// </summary>
        public List<CombinedField> CombineFieldResults(
            List<ClaudeVisionFieldDetector.VisualFieldDetectionResult> visionResults,
            List<GoogleDocumentAiService.GoogleFormField> googleResults,
            List<AnthropicService.FieldAnalysisResult> textResults,
            List<SyncfusionDetectedField> syncfusionResults,
            float pageWidth,
            float pageHeight)
        {
            var combinedFields = new List<CombinedField>();
            
            _logger.LogInformation($"Combining results: Vision={visionResults?.SelectMany(r => r.Fields).Count() ?? 0}, " +
                                  $"Google={googleResults?.Count ?? 0}, Text={textResults?.Count ?? 0}, " +
                                  $"Syncfusion={syncfusionResults?.Count ?? 0}");
            
            // Log field types detected by vision
            if (visionResults != null)
            {
                var visionFieldTypes = visionResults
                    .SelectMany(r => r.Fields)
                    .GroupBy(f => f.FieldType)
                    .Select(g => $"{g.Key}: {g.Count()}")
                    .ToList();
                _logger.LogInformation($"Vision field types: {string.Join(", ", visionFieldTypes)}");
            }
            
            // Log field types detected by text analysis
            if (textResults != null)
            {
                var textFieldTypes = textResults
                    .GroupBy(f => f.FieldType)
                    .Select(g => $"{g.Key}: {g.Count()}")
                    .ToList();
                _logger.LogInformation($"Text analysis field types: {string.Join(", ", textFieldTypes)}");
            }
            
            // Log field types detected by Syncfusion
            if (syncfusionResults != null)
            {
                var syncfusionFieldTypes = syncfusionResults
                    .GroupBy(f => f.Type)
                    .Select(g => $"{g.Key}: {g.Count()}")
                    .ToList();
                _logger.LogInformation($"Syncfusion field types: {string.Join(", ", syncfusionFieldTypes)}");
            }

            // Step 1: Process Syncfusion results FIRST (they have the most accurate positioning for Word fields)
            if (syncfusionResults != null && syncfusionResults.Count > 0)
            {
                _logger.LogInformation($"Starting with {syncfusionResults.Count} Syncfusion fields as base");

                foreach (var sfField in syncfusionResults)
                {
                    _logger.LogInformation($"[COMBINER] Processing Syncfusion field:");
                    _logger.LogInformation($"[COMBINER]   - Name: '{sfField.Name}'");
                    _logger.LogInformation($"[COMBINER]   - Type: '{sfField.Type}'");
                    _logger.LogInformation($"[COMBINER]   - Page: {sfField.PageNumber}");
                    _logger.LogInformation($"[COMBINER]   - Bounds: X={sfField.Bounds.X:F1}, Y={sfField.Bounds.Y:F1}, W={sfField.Bounds.Width:F1}, H={sfField.Bounds.Height:F1}");

                    var combined = new CombinedField
                    {
                        FieldName = sfField.Name,
                        FieldType = sfField.Type,
                        PageNumber = sfField.PageNumber,
                        IsRequired = sfField.IsRequired,
                        Bounds = sfField.Bounds,
                        Source = "Syncfusion",
                        Confidence = 0.9f // High confidence for Syncfusion
                    };

                    combinedFields.Add(combined);
                    _logger.LogDebug($"Added Syncfusion field: {sfField.Name} ({sfField.Type}) at page {sfField.PageNumber}");
                    _logger.LogInformation($"[COMBINER] Added field '{combined.FieldName}' to combined list (total: {combinedFields.Count})");
                }
            }
            
            // Step 2: Process Vision results (add new fields or enhance existing)
            if (visionResults != null)
            {
                foreach (var pageResult in visionResults.Where(r => r.Success))
                {
                    _logger.LogDebug($"Processing Vision page {pageResult.PageNumber} with {pageResult.Fields.Count} fields");
                    
                    foreach (var visionField in pageResult.Fields)
                    {
                        // Check if this field already exists from Syncfusion (by name OR position)
                        // CRITICAL FIX: Don't require same page number - that's what we're trying to correct!
                        var existingField = combinedFields.FirstOrDefault(sf =>
                            // Same name
                            IsSimilarName(sf.FieldName, visionField.FieldName) ||
                            // Or overlapping bounds (for fields with different names but same location)
                            (visionField.Bounds != null && AreBoundsOverlapping(
                                sf.Bounds,
                                ClaudeVisionFieldDetector.ConvertPercentageToPdfBounds(
                                    visionField.Bounds, pageWidth, pageHeight)))
                        );
                        
                        if (existingField != null)
                        {
                            // Enhance existing field with Vision data - DO NOT TOUCH COORDINATES AT ALL
                            _logger.LogInformation($"[COMBINER] Found existing field for Vision field '{visionField.FieldName}':");
                            _logger.LogInformation($"[COMBINER]   - Existing name: '{existingField.FieldName}' from {existingField.Source}");
                            _logger.LogInformation($"[COMBINER]   - Vision name: '{visionField.FieldName}'");
                            _logger.LogInformation($"[COMBINER] ⚠️ COMPLETELY IGNORING Vision coordinates - Syncfusion is PERFECT, Vision causes misalignment");

                            // Use Vision's name if it's more descriptive than Syncfusion's generic names
                            if (!string.IsNullOrEmpty(visionField.FieldName) &&
                                (existingField.FieldName.StartsWith("Text") ||
                                 existingField.FieldName.StartsWith("Check") ||
                                 existingField.FieldName.StartsWith("SF") ||
                                 visionField.FieldName.Length > existingField.FieldName.Length))
                            {
                                _logger.LogInformation($"[COMBINER] UPDATING field name from '{existingField.FieldName}' to '{visionField.FieldName}' (Vision has better name)");
                                existingField.FieldName = visionField.FieldName;
                            }
                            else
                            {
                                _logger.LogInformation($"[COMBINER] KEEPING existing field name '{existingField.FieldName}' (better than Vision's '{visionField.FieldName}')");
                            }

                            // Always use Vision's description if available - it's usually better
                            if (!string.IsNullOrEmpty(visionField.Description))
                            {
                                existingField.Description = visionField.Description;
                            }

                            // Use Vision's field type if it's more specific than Syncfusion's
                            if (!string.IsNullOrEmpty(visionField.FieldType) &&
                                visionField.FieldType != "text" &&
                                (string.IsNullOrEmpty(existingField.FieldType) || existingField.FieldType == "text"))
                            {
                                _logger.LogInformation($"[COMBINER] UPDATING field type from '{existingField.FieldType}' to '{visionField.FieldType}' (Vision has better type)");
                                existingField.FieldType = visionField.FieldType;
                            }

                            // DO NOT TOUCH BOUNDS - Syncfusion coordinates are perfect, Vision causes misalignment
                            // Vision is ONLY used for metadata (name, type, description), NEVER for coordinates

                            // CRITICAL: Trust Syncfusion's page assignment over Vision's
                            // Syncfusion has direct PDF structure access, Vision is guessing from images
                            // Only use Vision's page if Syncfusion doesn't have a valid page number
                            if (existingField.PageNumber <= 0 && visionField.PageNumber > 0)
                            {
                                _logger.LogInformation($"[COMBINER] USING Vision page number {visionField.PageNumber} for field '{existingField.FieldName}' (Syncfusion had no page info)");
                                existingField.PageNumber = visionField.PageNumber;
                            }
                            else if (visionField.PageNumber > 0 && visionField.PageNumber != existingField.PageNumber)
                            {
                                _logger.LogInformation($"[COMBINER] KEEPING Syncfusion page number {existingField.PageNumber} for field '{existingField.FieldName}' over Vision's {visionField.PageNumber} (Syncfusion is more reliable)");
                            }

                            if (visionField.IsRequired)
                                existingField.IsRequired = true;

                            existingField.Source = existingField.Source.Contains("Vision") ?
                                existingField.Source : $"{existingField.Source}+Vision";
                            existingField.Confidence = Math.Min(1.0f, existingField.Confidence + 0.1f);

                            _logger.LogDebug($"Enhanced existing field '{existingField.FieldName}' with Vision metadata ONLY (all Syncfusion coordinates preserved)");
                            continue;
                        }
                        
                        // Add new field from Vision
                        var combined = new CombinedField
                        {
                            FieldName = visionField.FieldName,
                            FieldType = visionField.FieldType,
                            PageNumber = visionField.PageNumber,
                            IsRequired = visionField.IsRequired,
                            Description = visionField.Description,
                            Source = "Vision",
                            Confidence = 0.8f
                        };
                        
                        // 🚨 COMBINER COORDINATE TRACKING - Third conversion attempt!
                        if (visionField.Bounds != null)
                        {
                            _logger.LogWarning($"🔵🔵🔵 [COMBINER_COORD_1] ⚠️ THIRD COORDINATE CONVERSION in MultiSourceFieldCombiner!");
                            _logger.LogWarning($"🔵🔵🔵   - Field: '{visionField.FieldName}' Type: {visionField.FieldType}");
                            _logger.LogWarning($"🔵🔵🔵   - Input Percentages: X={visionField.Bounds.XPercent:F3}%, Y={visionField.Bounds.YPercent:F3}%, W={visionField.Bounds.WidthPercent:F3}%, H={visionField.Bounds.HeightPercent:F3}%");
                            _logger.LogWarning($"🔵🔵🔵   - Page size for conversion: {pageWidth:F1} x {pageHeight:F1}");

                            // Vision gives percentages, convert to actual coordinates
                            // But apply corrections for typical misalignments
                            var originalBounds = ClaudeVisionFieldDetector.ConvertPercentageToPdfBounds(
                                visionField.Bounds, pageWidth, pageHeight, visionField.FieldName, _logger);
                            _logger.LogWarning($"🔵🔵🔵 [COMBINER_COORD_2] ClaudeVisionFieldDetector returned: X={originalBounds.X:F1}, Y={originalBounds.Y:F1}, W={originalBounds.Width:F1}, H={originalBounds.Height:F1}");

                            combined.Bounds = CorrectBoundingBox(originalBounds, visionField.FieldType);
                            _logger.LogWarning($"🔵🔵🔵 [COMBINER_COORD_3] After CorrectBoundingBox: X={combined.Bounds.X:F1}, Y={combined.Bounds.Y:F1}, W={combined.Bounds.Width:F1}, H={combined.Bounds.Height:F1}");
                        }
                        
                        combinedFields.Add(combined);
                    }
                }
            }
            
            // Step 2: Enhance with Google Document AI results
            if (googleResults != null)
            {
                foreach (var googleField in googleResults)
                {
                    // Try to find matching field from vision
                    var matchingField = FindMatchingField(combinedFields, googleField.FieldName, 
                                                         googleField.PageNumber);
                    
                    if (matchingField != null)
                    {
                        // Google often has better field names/types
                        if (!string.IsNullOrEmpty(googleField.FieldName))
                        {
                            matchingField.FieldName = googleField.FieldName;
                        }
                        
                        // Use Google's bounds if Vision's seem wrong
                        if (googleField.Bounds != null && IsBoundsSuspicious(matchingField.Bounds))
                        {
                            matchingField.Bounds = new RectangleF(
                                googleField.Bounds.X,
                                googleField.Bounds.Y,
                                googleField.Bounds.Width,
                                googleField.Bounds.Height
                            );
                            matchingField.Source = "Google+Vision";
                        }
                        
                        // Increase confidence when multiple sources agree
                        matchingField.Confidence = Math.Min(1.0f, matchingField.Confidence + 0.1f);
                    }
                    else if (googleField.Bounds != null)
                    {
                        // Add new field from Google
                        combinedFields.Add(new CombinedField
                        {
                            FieldName = googleField.FieldName ?? $"Field_{combinedFields.Count + 1}",
                            FieldType = googleField.FieldType,
                            PageNumber = googleField.PageNumber,
                            Bounds = new RectangleF(
                                googleField.Bounds.X,
                                googleField.Bounds.Y,
                                googleField.Bounds.Width,
                                googleField.Bounds.Height
                            ),
                            Source = "Google",
                            Confidence = googleField.Confidence
                        });
                    }
                }
            }
            
            // Step 3: Add any text-detected fields that weren't found visually
            if (textResults != null)
            {
                foreach (var textField in textResults)
                {
                    var matchingField = FindMatchingField(combinedFields, textField.FieldName, 1);
                    
                    if (matchingField != null)
                    {
                        // Text analysis often has better semantic understanding
                        // Use the FieldName as description if available
                        if (!string.IsNullOrEmpty(textField.FieldName))
                        {
                            matchingField.Description = textField.FieldName;
                        }
                        
                        if (textField.IsRequired)
                        {
                            matchingField.IsRequired = true;
                        }
                        
                        matchingField.Confidence = Math.Min(1.0f, matchingField.Confidence + 0.1f);
                    }
                    else
                    {
                        // Add placeholder for text-only detected field
                        // These need visual detection to place properly
                        _logger.LogDebug($"Text-only field detected: {textField.FieldName} - needs visual placement");
                    }
                }
            }
            
            // Step 4: Apply final corrections and validations
            foreach (var field in combinedFields)
            {
                // Ensure reasonable bounds
                field.Bounds = EnsureReasonableBounds(field.Bounds, field.FieldType, pageWidth, pageHeight);
                
                // Clean up field names
                field.FieldName = CleanFieldName(field.FieldName);
                
                // Re-detect field type using comprehensive detector if needed
                if (field.FieldType == "text" || string.IsNullOrEmpty(field.FieldType))
                {
                    field.FieldType = FieldTypeDetector.DetectFieldType(
                        field.FieldName, 
                        null, 
                        field.Description);
                }
            }
            
            // Step 5: Remove duplicates and overlapping fields
            combinedFields = RemoveDuplicates(combinedFields);
            
            // Step 6: Detect and fix overlapping fields
            combinedFields = FixOverlappingFields(combinedFields);
            
            _logger.LogInformation($"Combined into {combinedFields.Count} final fields");
            
            return combinedFields;
        }
        
        private CombinedField FindMatchingField(List<CombinedField> fields, string name, int pageNumber)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            
            // First try exact match
            var exact = fields.FirstOrDefault(f => 
                f.PageNumber == pageNumber && 
                string.Equals(f.FieldName, name, StringComparison.OrdinalIgnoreCase));
            
            if (exact != null)
                return exact;
            
            // Try fuzzy match
            var nameLower = name.ToLower();
            return fields.FirstOrDefault(f => 
                f.PageNumber == pageNumber && 
                (f.FieldName?.ToLower().Contains(nameLower) == true ||
                 nameLower.Contains(f.FieldName?.ToLower() ?? "")));
        }
        
        private bool IsBoundsSuspicious(RectangleF bounds)
        {
            // Check if bounds seem wrong
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return true;
            
            // Check for unreasonably large bounds
            if (bounds.Width > 500 || bounds.Height > 100)
                return true;
            
            // Check for unreasonably small bounds
            if (bounds.Width < 20 || bounds.Height < 10)
                return true;
            
            return false;
        }
        
        private RectangleF CorrectBoundingBox(RectangleF bounds, string fieldType)
        {
            // Apply typical corrections based on field type
            var corrected = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            
            switch (fieldType?.ToLower())
            {
                case "checkbox":
                case "radio":
                    // These should be small and square
                    var size = Math.Min(corrected.Width, corrected.Height);
                    size = Math.Max(12, Math.Min(20, size)); // Between 12-20 points
                    corrected.Width = size;
                    corrected.Height = size;
                    break;
                    
                case "signature":
                    // Signatures need more space
                    corrected.Width = Math.Max(150, corrected.Width);
                    corrected.Height = Math.Max(40, corrected.Height);
                    break;
                    
                case "date":
                    // Date fields are typically medium width
                    corrected.Width = Math.Min(120, Math.Max(80, corrected.Width));
                    corrected.Height = Math.Max(20, Math.Min(30, corrected.Height));
                    break;
                    
                default: // text fields
                    // Ensure reasonable text field size
                    corrected.Width = Math.Max(100, Math.Min(400, corrected.Width));
                    corrected.Height = Math.Max(18, Math.Min(30, corrected.Height));
                    break;
            }
            
            return corrected;
        }
        
        private RectangleF EnsureReasonableBounds(RectangleF bounds, string fieldType, float pageWidth, float pageHeight)
        {
            var corrected = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            
            // Ensure within page bounds
            corrected.X = Math.Max(10, Math.Min(pageWidth - 10, corrected.X));
            corrected.Y = Math.Max(10, Math.Min(pageHeight - 10, corrected.Y));
            
            // Ensure doesn't exceed page
            if (corrected.X + corrected.Width > pageWidth - 10)
            {
                corrected.Width = pageWidth - corrected.X - 10;
            }
            
            if (corrected.Y + corrected.Height > pageHeight - 10)
            {
                corrected.Height = pageHeight - corrected.Y - 10;
            }
            
            return corrected;
        }
        
        private string CleanFieldName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Field";
            
            // Remove common prefixes/suffixes
            name = name.Trim()
                      .Replace("_", " ")
                      .Replace("  ", " ");
            
            // Capitalize properly
            if (name.Length > 0)
            {
                name = char.ToUpper(name[0]) + (name.Length > 1 ? name.Substring(1) : "");
            }
            
            return name;
        }
        
        private List<CombinedField> RemoveDuplicates(List<CombinedField> fields)
        {
            _logger.LogInformation($"[DEDUP] Starting duplicate removal for {fields.Count} fields");
            var unique = new List<CombinedField>();

            // Group by page for more efficient processing
            var fieldsByPage = fields.GroupBy(f => f.PageNumber);
            
            foreach (var pageGroup in fieldsByPage)
            {
                var pageFields = pageGroup.OrderByDescending(f => f.Confidence).ToList();
                
                foreach (var field in pageFields)
                {
                    // Check for duplicates based on multiple criteria
                    var duplicate = unique.FirstOrDefault(existing => 
                        existing.PageNumber == field.PageNumber &&
                        (
                            // Same name and overlapping bounds
                            (IsSimilarName(existing.FieldName, field.FieldName) && 
                             AreBoundsOverlapping(existing.Bounds, field.Bounds)) ||
                            
                            // Same position (very close bounds) regardless of name
                            AreBoundsVeryClose(existing.Bounds, field.Bounds) ||
                            
                            // For checkboxes - same type and overlapping
                            (existing.FieldType == "checkbox" && field.FieldType == "checkbox" &&
                             AreBoundsOverlapping(existing.Bounds, field.Bounds))
                        ));
                    
                    if (duplicate != null)
                    {
                        _logger.LogInformation($"[DEDUP] Found duplicate for field '{field.FieldName}':");
                        _logger.LogInformation($"[DEDUP]   - Existing: '{duplicate.FieldName}' (confidence: {duplicate.Confidence}, source: {duplicate.Source})");
                        _logger.LogInformation($"[DEDUP]   - New: '{field.FieldName}' (confidence: {field.Confidence}, source: {field.Source})");
                        _logger.LogInformation($"[DEDUP]   - Existing bounds: X={duplicate.Bounds.X:F1}, Y={duplicate.Bounds.Y:F1}");
                        _logger.LogInformation($"[DEDUP]   - New bounds: X={field.Bounds.X:F1}, Y={field.Bounds.Y:F1}");

                        // Merge the duplicate into the existing field
                        if (field.Confidence > duplicate.Confidence)
                        {
                            // Replace with higher confidence field
                            unique.Remove(duplicate);
                            unique.Add(field);
                            _logger.LogInformation($"[DEDUP] REPLACED lower confidence duplicate: '{duplicate.FieldName}' with '{field.FieldName}' on page {field.PageNumber}");
                        }
                        else
                        {
                            // Keep existing but maybe update some properties
                            if (!string.IsNullOrEmpty(field.Description) && string.IsNullOrEmpty(duplicate.Description))
                                duplicate.Description = field.Description;
                            
                            if (field.IsRequired)
                                duplicate.IsRequired = true;
                                
                            // Combine sources
                            if (!duplicate.Source.Contains(field.Source))
                                duplicate.Source = $"{duplicate.Source}+{field.Source}";
                                
                            _logger.LogDebug($"Merged duplicate field: {field.FieldName} into {duplicate.FieldName} on page {field.PageNumber}");
                        }
                    }
                    else
                    {
                        unique.Add(field);
                    }
                }
            }
            
            _logger.LogInformation($"Duplicate removal: {fields.Count} fields -> {unique.Count} unique fields");
            
            return unique;
        }
        
        private bool IsSimilarName(string name1, string name2)
        {
            if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2))
                return false;
                
            // Exact match
            if (string.Equals(name1, name2, StringComparison.OrdinalIgnoreCase))
                return true;
            
            // Clean and compare
            var clean1 = name1.Replace("_", " ").Replace("-", " ").Trim().ToLower();
            var clean2 = name2.Replace("_", " ").Replace("-", " ").Trim().ToLower();
            
            if (clean1 == clean2)
                return true;
            
            // Check if one contains the other (for partial matches)
            if (clean1.Contains(clean2) || clean2.Contains(clean1))
                return true;
                
            return false;
        }
        
        private bool AreBoundsVeryClose(RectangleF bounds1, RectangleF bounds2)
        {
            // Consider fields very close if they're within 5 points of each other
            const float tolerance = 5f;
            
            return Math.Abs(bounds1.X - bounds2.X) < tolerance &&
                   Math.Abs(bounds1.Y - bounds2.Y) < tolerance &&
                   Math.Abs(bounds1.Width - bounds2.Width) < tolerance * 2 &&
                   Math.Abs(bounds1.Height - bounds2.Height) < tolerance * 2;
        }
        
        private List<CombinedField> FixOverlappingFields(List<CombinedField> fields)
        {
            _logger.LogInformation($"[OVERLAP] Checking for overlapping fields among {fields.Count} fields...");

            var corrected = new List<CombinedField>(fields);
            var overlapsFound = 0;
            
            // Group by page for easier processing
            var fieldsByPage = corrected.GroupBy(f => f.PageNumber);
            
            foreach (var pageGroup in fieldsByPage)
            {
                var pageFields = pageGroup.OrderBy(f => f.Bounds.Y).ThenBy(f => f.Bounds.X).ToList();
                
                for (int i = 0; i < pageFields.Count; i++)
                {
                    for (int j = i + 1; j < pageFields.Count; j++)
                    {
                        var field1 = pageFields[i];
                        var field2 = pageFields[j];
                        
                        if (AreBoundsOverlapping(field1.Bounds, field2.Bounds))
                        {
                            overlapsFound++;
                            _logger.LogWarning($"[OVERLAP] Overlap detected between fields on page {field1.PageNumber}:");
                            _logger.LogWarning($"[OVERLAP]   - Field 1: '{field1.FieldName}' ({field1.FieldType}) at X={field1.Bounds.X:F1}, Y={field1.Bounds.Y:F1}, W={field1.Bounds.Width:F1}, H={field1.Bounds.Height:F1}");
                            _logger.LogWarning($"[OVERLAP]   - Field 2: '{field2.FieldName}' ({field2.FieldType}) at X={field2.Bounds.X:F1}, Y={field2.Bounds.Y:F1}, W={field2.Bounds.Width:F1}, H={field2.Bounds.Height:F1}");

                            // Fix the overlap based on field types and positions
                            FixOverlap(field1, field2);
                        }
                    }
                }
            }
            
            if (overlapsFound > 0)
            {
                _logger.LogInformation($"Fixed {overlapsFound} overlapping field pairs");
            }
            
            return corrected;
        }
        
        private void FixOverlap(CombinedField field1, CombinedField field2)
        {
            // Strategy 1: If one is a checkbox and one is text, separate them
            if (field1.FieldType == "checkbox" && field2.FieldType != "checkbox")
            {
                // Move checkbox to the left of its current position
                field1.Bounds = new RectangleF(
                    field1.Bounds.X - 25, // Move left
                    field1.Bounds.Y,
                    20, // Standard checkbox size
                    20
                );
                _logger.LogDebug($"Moved checkbox '{field1.FieldName}' to avoid overlap with '{field2.FieldName}'");
            }
            else if (field2.FieldType == "checkbox" && field1.FieldType != "checkbox")
            {
                // Move checkbox to the left
                field2.Bounds = new RectangleF(
                    field2.Bounds.X - 25,
                    field2.Bounds.Y,
                    20,
                    20
                );
                _logger.LogDebug($"Moved checkbox '{field2.FieldName}' to avoid overlap with '{field1.FieldName}'");
            }
            // Strategy 2: If both are text fields on same line, adjust horizontally
            else if (Math.Abs(field1.Bounds.Y - field2.Bounds.Y) < 5)
            {
                // They're on the same line - adjust horizontally
                float gap = 10; // Minimum gap between fields
                
                if (field1.Bounds.X < field2.Bounds.X)
                {
                    // field1 is to the left, adjust field2 to the right
                    float newX = field1.Bounds.X + field1.Bounds.Width + gap;
                    field2.Bounds = new RectangleF(
                        newX,
                        field2.Bounds.Y,
                        field2.Bounds.Width,
                        field2.Bounds.Height
                    );
                    _logger.LogDebug($"Moved '{field2.FieldName}' right to avoid overlap with '{field1.FieldName}'");
                }
                else
                {
                    // field2 is to the left, adjust field1 to the right
                    float newX = field2.Bounds.X + field2.Bounds.Width + gap;
                    field1.Bounds = new RectangleF(
                        newX,
                        field1.Bounds.Y,
                        field1.Bounds.Width,
                        field1.Bounds.Height
                    );
                    _logger.LogDebug($"Moved '{field1.FieldName}' right to avoid overlap with '{field2.FieldName}'");
                }
            }
            // Strategy 3: If vertically overlapping, adjust vertically
            else
            {
                float gap = 5; // Vertical gap
                
                if (field1.Bounds.Y < field2.Bounds.Y)
                {
                    // field1 is above, push field2 down
                    float newY = field1.Bounds.Y + field1.Bounds.Height + gap;
                    field2.Bounds = new RectangleF(
                        field2.Bounds.X,
                        newY,
                        field2.Bounds.Width,
                        field2.Bounds.Height
                    );
                    _logger.LogDebug($"Moved '{field2.FieldName}' down to avoid overlap with '{field1.FieldName}'");
                }
                else
                {
                    // field2 is above, push field1 down
                    float newY = field2.Bounds.Y + field2.Bounds.Height + gap;
                    field1.Bounds = new RectangleF(
                        field1.Bounds.X,
                        newY,
                        field1.Bounds.Width,
                        field1.Bounds.Height
                    );
                    _logger.LogDebug($"Moved '{field1.FieldName}' down to avoid overlap with '{field2.FieldName}'");
                }
            }
        }
        
        private bool AreBoundsOverlapping(RectangleF bounds1, RectangleF bounds2)
        {
            // Check if two rectangles overlap significantly
            var intersectX = Math.Max(bounds1.X, bounds2.X);
            var intersectY = Math.Max(bounds1.Y, bounds2.Y);
            var intersectRight = Math.Min(bounds1.X + bounds1.Width, bounds2.X + bounds2.Width);
            var intersectBottom = Math.Min(bounds1.Y + bounds1.Height, bounds2.Y + bounds2.Height);
            
            if (intersectRight > intersectX && intersectBottom > intersectY)
            {
                // Calculate overlap area
                var overlapArea = (intersectRight - intersectX) * (intersectBottom - intersectY);
                var area1 = bounds1.Width * bounds1.Height;
                var area2 = bounds2.Width * bounds2.Height;
                
                // If overlap is more than 50% of either field, consider duplicate
                return overlapArea > (area1 * 0.5) || overlapArea > (area2 * 0.5);
            }
            
            return false;
        }
    }
}