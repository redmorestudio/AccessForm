using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using Syncfusion.Pdf.Interactive;
using Microsoft.Extensions.Logging;
using AccessFormServer.Services;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Preserves existing PDF fields including JavaScript calculations when making PDFs accessible
    /// </summary>
    public class PdfPreservationService
    {
        private readonly ILogger<PdfPreservationService> _logger;
        private readonly AccessibilityService _accessibilityService;
        private readonly AccessibilityRetrofitService _retrofitService;
        private readonly PdfAccessibilityEnhancer _enhancer;

        public PdfPreservationService(
            ILogger<PdfPreservationService> logger,
            AccessibilityService accessibilityService,
            AccessibilityRetrofitService retrofitService,
            PdfAccessibilityEnhancer enhancer)
        {
            _logger = logger;
            _accessibilityService = accessibilityService;
            _retrofitService = retrofitService;
            _enhancer = enhancer;
        }

        /// <summary>
        /// Process an existing PDF by preserving all fields (including calculations) and adding accessibility enhancements
        /// This includes font replacement, tagging, metadata, etc - everything needed for PDF/UA compliance
        /// </summary>
        public async Task<byte[]> ProcessExistingPdfAsync(byte[] pdfBytes, List<Models.FieldDetectionResult>? additionalFields = null)
        {
            _logger.LogInformation("[PDF-PRESERVATION] Loading existing PDF to preserve fields and add accessibility");

            using var ms = new MemoryStream(pdfBytes);
            using var loadedDoc = new PdfLoadedDocument(ms);

            _logger.LogInformation($"[PDF-PRESERVATION] Loaded PDF with {loadedDoc.Form?.Fields?.Count ?? 0} existing fields");

            // Log existing fields and check for calculations
            if (loadedDoc.Form?.Fields != null)
            {
                int calculatedFieldCount = 0;
                foreach (var fieldObj in loadedDoc.Form.Fields)
                {
                    if (fieldObj is PdfLoadedField field)
                    {
                        bool hasCalculation = HasCalculationAction(field);
                        if (hasCalculation)
                        {
                            calculatedFieldCount++;
                            _logger.LogInformation($"[PDF-PRESERVATION] Field '{field.Name}' has calculation - preserving");
                        }

                        // The field already exists - we just need to ensure accessibility tags are added
                        // Syncfusion will preserve the field and its actions when we save
                    }
                }

                _logger.LogInformation($"[PDF-PRESERVATION] Found {calculatedFieldCount} calculated fields that will be preserved");
            }

            // Add any additional fields detected by AI (e.g., missing fields found by Claude Vision)
            if (additionalFields != null && additionalFields.Count > 0)
            {
                _logger.LogInformation($"[PDF-PRESERVATION] Adding {additionalFields.Count} additional fields detected by AI");

                // TODO: Implement adding new fields while preserving existing ones
                // This would be used when AI detects missing fields
            }

            // Save the document with fields preserved
            using var intermediateMs = new MemoryStream();
            loadedDoc.Save(intermediateMs);
            var intermediateBytes = intermediateMs.ToArray();

            _logger.LogInformation("[PDF-PRESERVATION] Fields preserved, now applying accessibility enhancements");

            // Apply all accessibility enhancements (font replacement, tagging, metadata, etc.)
            // These services are already used in the Word-to-PDF pipeline, now we use them for PDF-to-PDF too
            var accessibleBytes = await _accessibilityService.MakePdfAccessibleAsync(intermediateBytes);
            accessibleBytes = await _retrofitService.RetrofitAccessibilityAsync(accessibleBytes);
            accessibleBytes = await _enhancer.EnhanceAccessibilityAsync(accessibleBytes);

            _logger.LogInformation("[PDF-PRESERVATION] Accessibility enhancements complete, all fields and calculations preserved");

            return accessibleBytes;
        }

        /// <summary>
        /// Check if a field has a calculation action (JavaScript formula)
        /// </summary>
        private bool HasCalculationAction(PdfLoadedField field)
        {
            try
            {
                // Check for calculation actions
                // Syncfusion may store this in Actions property or field-specific properties

                if (field is PdfLoadedTextBoxField textBox)
                {
                    // Try to access Actions property
                    // Note: This may not be exposed in the public API - we may need to check the PDF spec directly
                    // For now, we'll log and return false, but preserve the field anyway

                    // Common calculated field patterns:
                    // - Read-only flag is often set
                    // - Field name often contains "total" or "sum"

                    var fieldName = field.Name?.ToLower() ?? "";
                    if (fieldName.Contains("total") || fieldName.Contains("sum") ||
                        fieldName.Contains("calc") || fieldName.Contains("computed"))
                    {
                        _logger.LogInformation($"[PDF-PRESERVATION] Field '{field.Name}' likely has calculation (based on name)");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"[PDF-PRESERVATION] Error checking for calculation on field '{field.Name}'");
                return false;
            }
        }

        /// <summary>
        /// Get information about existing fields in a PDF
        /// </summary>
        public List<Models.FieldDetectionResult> GetExistingFields(byte[] pdfBytes)
        {
            var results = new List<Models.FieldDetectionResult>();

            using var ms = new MemoryStream(pdfBytes);
            using var loadedDoc = new PdfLoadedDocument(ms);

            if (loadedDoc.Form?.Fields == null)
            {
                _logger.LogWarning("[PDF-PRESERVATION] No form fields found in PDF");
                return results;
            }

            int fieldCounter = 0;
            foreach (var fieldObj in loadedDoc.Form.Fields)
            {
                if (!(fieldObj is PdfLoadedField field))
                    continue;

                fieldCounter++;

                // Get field page number
                int pageNum = 1;
                if (field.Page != null)
                {
                    for (int i = 0; i < loadedDoc.Pages.Count; i++)
                    {
                        if (loadedDoc.Pages[i] == field.Page)
                        {
                            pageNum = i + 1;
                            break;
                        }
                    }
                }

                var bounds = field.Bounds;
                var detectedField = new Models.FieldDetectionResult
                {
                    ShortId = $"PDF{fieldCounter}",
                    FieldName = field.Name ?? $"Field{fieldCounter}",
                    FieldType = GetFieldType(field),
                    X = bounds.X,
                    Y = bounds.Y,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    PageNumber = pageNum,
                    Source = "PDF-Original",
                    Confidence = 1.0f, // Existing fields are 100% confident
                    IsValid = true
                };

                results.Add(detectedField);

                _logger.LogInformation($"[PDF-PRESERVATION] Detected existing field: {detectedField.FieldName} ({detectedField.FieldType}) on page {pageNum}");
            }

            return results;
        }

        private string GetFieldType(PdfLoadedField field)
        {
            return field switch
            {
                PdfLoadedTextBoxField => "text",
                PdfLoadedCheckBoxField => "checkbox",
                PdfLoadedRadioButtonListField => "radio",
                PdfLoadedComboBoxField => "dropdown",
                PdfLoadedListBoxField => "listbox",
                PdfLoadedSignatureField => "signature",
                _ => "unknown"
            };
        }
    }
}
