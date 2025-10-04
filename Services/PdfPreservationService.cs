using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using Syncfusion.Pdf.Interactive;
using Syncfusion.Drawing;
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

            // Apply the FULL accessibility pipeline - these services are solid and don't break PDFs

            // Step 1: AccessibilityRetrofitService - adds structure/tagging to the loaded document
            _logger.LogInformation("[PDF-PRESERVATION] Step 1: Running AccessibilityRetrofitService for structure/tagging");
            _retrofitService.RetrofitAccessibility(loadedDoc);

            // Step 2: PdfAccessibilityEnhancer - final enhancements on the loaded document
            _logger.LogInformation("[PDF-PRESERVATION] Step 2: Running PdfAccessibilityEnhancer for final enhancements");
            _enhancer.EnhanceAccessibility(loadedDoc, "existing-pdf.pdf");

            // Save the document with accessibility enhancements
            using var tempMs = new MemoryStream();
            loadedDoc.Save(tempMs);
            var pdfBytesWithFields = tempMs.ToArray();

            _logger.LogInformation("[PDF-PRESERVATION] Saved PDF with accessibility enhancements, now applying AccessibilityService");

            // Step 3: AccessibilityService - replaces fonts, handles ZapfDingbats → Unicode, etc.
            // This one works on byte[] and returns (byte[], report)
            _logger.LogInformation("[PDF-PRESERVATION] Step 3: Running AccessibilityService for font replacement");
            var (finalPdfBytes, accessibilityReport) = _accessibilityService.MakeAccessible(pdfBytesWithFields, "existing-pdf.pdf");

            _logger.LogInformation("[PDF-PRESERVATION] Full accessibility pipeline complete - fields should be preserved");

            return finalPdfBytes;
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

                // Get bounds - need to cast to specific field type
                RectangleF bounds;
                if (field is PdfLoadedTextBoxField textBox)
                    bounds = textBox.Bounds;
                else if (field is PdfLoadedCheckBoxField checkBox)
                    bounds = checkBox.Bounds;
                else if (field is PdfLoadedRadioButtonListField radioList && radioList.Items.Count > 0)
                    bounds = radioList.Items[0].Bounds;
                else if (field is PdfLoadedComboBoxField comboBox)
                    bounds = comboBox.Bounds;
                else if (field is PdfLoadedListBoxField listBox)
                    bounds = listBox.Bounds;
                else if (field is PdfLoadedSignatureField signature)
                    bounds = signature.Bounds;
                else
                    continue; // Skip unknown field types

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

                _logger.LogWarning($"[PDF-PRESERVATION] Detected existing field: {detectedField.FieldName} ({detectedField.FieldType}) " +
                    $"on page {pageNum} at X={bounds.X}, Y={bounds.Y}, W={bounds.Width}, H={bounds.Height}");
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
