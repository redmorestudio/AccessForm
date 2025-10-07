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
        private readonly PassportPdfService _passportPdfService;

        public PdfPreservationService(
            ILogger<PdfPreservationService> logger,
            AccessibilityService accessibilityService,
            AccessibilityRetrofitService retrofitService,
            PdfAccessibilityEnhancer enhancer,
            PassportPdfService passportPdfService)
        {
            _logger = logger;
            _accessibilityService = accessibilityService;
            _retrofitService = retrofitService;
            _enhancer = enhancer;
            _passportPdfService = passportPdfService;
        }

        /// <summary>
        /// Process an existing PDF by preserving all fields (including calculations) and adding accessibility enhancements
        /// This includes font replacement, tagging, metadata, etc - everything needed for PDF/UA compliance
        /// </summary>
        public async Task<byte[]> ProcessExistingPdfAsync(byte[] pdfBytes, List<Models.FieldDetectionResult>? additionalFields = null)
        {
            _logger.LogWarning("╔═══════════════════════════════════════════════════════════════════╗");
            _logger.LogWarning("║ 🏁 PDFPRESERVATIONSERVICE.ProcessExistingPdfAsync ENTRY         ║");
            _logger.LogWarning("╚═══════════════════════════════════════════════════════════════════╝");

            using var ms = new MemoryStream(pdfBytes);

            // Check PDF version first
            _logger.LogWarning($"🔍 Checking PDF version from raw bytes...");
            var pdfHeader = System.Text.Encoding.ASCII.GetString(pdfBytes, 0, Math.Min(20, pdfBytes.Length));
            _logger.LogError($"🚨 PDF HEADER: {pdfHeader}");

            using var loadedDoc = new PdfLoadedDocument(ms);

            _logger.LogWarning($"🔍 PDF LOADED - Document info:");
            _logger.LogWarning($"  - Pages: {loadedDoc.Pages.Count}");
            _logger.LogWarning($"  - FileStructure: {loadedDoc.FileStructure}");

            _logger.LogWarning($"🔍 Checking for forms...");
            _logger.LogWarning($"🔍 loadedDoc.Form is null? {loadedDoc.Form == null}");
            if (loadedDoc.Form != null)
            {
                _logger.LogWarning($"🔍 loadedDoc.Form.Fields is null? {loadedDoc.Form.Fields == null}");
                if (loadedDoc.Form.Fields != null)
                {
                    _logger.LogWarning($"🔍 loadedDoc.Form.Fields.Count: {loadedDoc.Form.Fields.Count}");
                }
            }

            var initialFormCount = loadedDoc.Form?.Fields?.Count ?? 0;
            _logger.LogError($"🚨 SYNCFUSION SEES {initialFormCount} FORM FIELDS IN THE PDF!");

            // Try to detect forms another way - check pages for annotations
            int annotationCount = 0;
            int widgetAnnotations = 0;
            for (int i = 0; i < loadedDoc.Pages.Count; i++)
            {
                var page = loadedDoc.Pages[i];
                var annotations = page.Annotations;
                _logger.LogWarning($"🔍 Page {i + 1}: {annotations.Count} annotations");

                // Check annotation types
                foreach (var annotation in annotations)
                {
                    _logger.LogWarning($"    - Annotation type: {annotation.GetType().Name}");
                    if (annotation.GetType().Name.Contains("Widget"))
                    {
                        widgetAnnotations++;
                    }
                }

                annotationCount += annotations.Count;
            }
            _logger.LogWarning($"🔍 TOTAL ANNOTATIONS: {annotationCount} (Widget annotations: {widgetAnnotations})");

            _logger.LogError("╔═══════════════════════════════════════════════════════════════════╗");
            _logger.LogError($"║ 📊 FIELD COUNT: {initialFormCount} fields detected by Syncfusion");
            _logger.LogError("╚═══════════════════════════════════════════════════════════════════╝");

            // CRITICAL FIX: If Syncfusion can't see any form fields, DON'T process the PDF
            // This prevents us from destroying forms that Syncfusion can't read
            if (initialFormCount == 0)
            {
                _logger.LogError("╔═══════════════════════════════════════════════════════════════════╗");
                _logger.LogError("║ 🚨 SYNCFUSION CANNOT READ FORMS - RETURNING ORIGINAL PDF        ║");
                _logger.LogError("║    Syncfusion doesn't support this PDF's form format             ║");
                _logger.LogError("║    Returning original bytes to preserve forms                    ║");
                _logger.LogError($"║    Annotations found: {annotationCount} total, {widgetAnnotations} widgets");
                _logger.LogError("╚═══════════════════════════════════════════════════════════════════╝");
                return pdfBytes;  // Return original unchanged
            }

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

            _logger.LogInformation("[PDF-PRESERVATION] Saved PDF with accessibility enhancements, now injecting page numbers into tooltips");

            // Step 2.5: Inject page numbers into tooltips so they survive PassportPDF processing
            // This is crucial because PassportPDF removes widget annotations, so we can't detect pages later
            using var tooltipMs = new MemoryStream(pdfBytesWithFields);
            using var pdfForTooltips = new PdfLoadedDocument(tooltipMs);

            if (pdfForTooltips.Form?.Fields != null)
            {
                foreach (var fieldObj in pdfForTooltips.Form.Fields)
                {
                    if (fieldObj is PdfLoadedField field)
                    {
                        // Determine the page number for this field
                        int fieldPageNum = 1;
                        if (field.Page != null)
                        {
                            for (int i = 0; i < pdfForTooltips.Pages.Count; i++)
                            {
                                if (pdfForTooltips.Pages[i] == field.Page)
                                {
                                    fieldPageNum = i + 1;
                                    break;
                                }
                            }
                        }

                        // Add [PAGE:X] to tooltip if not already present
                        string existingTooltip = field.ToolTip ?? "";
                        if (!existingTooltip.Contains("[PAGE:"))
                        {
                            field.ToolTip = $"{existingTooltip} [PAGE:{fieldPageNum}]".Trim();
                            _logger.LogInformation($"[PDF-PRESERVATION] Added page marker to '{field.Name}': [PAGE:{fieldPageNum}]");
                        }

                        // Normalize checkbox/radio button sizes in the actual PDF (not just metadata)
                        // This ensures the size persists through PassportPDF processing
                        if (field is PdfLoadedCheckBoxField checkBox)
                        {
                            var bounds = checkBox.Bounds;
                            // Set to 7.2x7.2 PDF points (becomes 15x15 pixels after 150/72 DPI scaling)
                            checkBox.Bounds = new RectangleF(bounds.X, bounds.Y, 7.2f, 7.2f);
                            _logger.LogInformation($"[PDF-PRESERVATION] Resized checkbox '{field.Name}' to 7.2x7.2");
                        }
                        else if (field is PdfLoadedRadioButtonListField radioList)
                        {
                            // Resize each radio button in the list
                            for (int i = 0; i < radioList.Items.Count; i++)
                            {
                                var radioItem = radioList.Items[i];
                                var bounds = radioItem.Bounds;
                                radioItem.Bounds = new RectangleF(bounds.X, bounds.Y, 7.2f, 7.2f);
                            }
                            _logger.LogInformation($"[PDF-PRESERVATION] Resized {radioList.Items.Count} radio buttons in '{field.Name}' to 7.2x7.2");
                        }
                    }
                }
            }

            using var tooltipSaveMs = new MemoryStream();
            pdfForTooltips.Save(tooltipSaveMs);
            pdfBytesWithFields = tooltipSaveMs.ToArray();

            _logger.LogInformation("[PDF-PRESERVATION] Page markers injected, now applying AccessibilityService");

            // Step 3: AccessibilityService - sets metadata, tooltips, etc.
            // This one works on byte[] and returns (byte[], report)
            _logger.LogWarning("🔍 PRESERVATION: Running AccessibilityService (metadata/tooltips)");
            var (pdfWithMetadata, accessibilityReport) = _accessibilityService.MakeAccessible(pdfBytesWithFields, "existing-pdf.pdf");

            // Step 4: PassportPDF - PDF/A-2u conversion with JavaScript preservation
            // This handles: font embedding, ZapfDingbats → Unicode, PDF/A-2u compliance
            _logger.LogWarning("╔═══════════════════════════════════════════════════════════════════╗");
            _logger.LogWarning("║ 🔧 RUNNING PASSPORTPDF PDF/A-2u CONVERSION (PRESERVE JS)        ║");
            _logger.LogWarning("╚═══════════════════════════════════════════════════════════════════╝");
            var finalPdfBytes = await _passportPdfService.ConvertToPdfAAsync(pdfWithMetadata, preserveJavaScript: true);

            _logger.LogWarning("╔═══════════════════════════════════════════════════════════════════╗");
            _logger.LogWarning("║ ✅ PDFPRESERVATIONSERVICE.ProcessExistingPdfAsync COMPLETE      ║");
            _logger.LogWarning("╚═══════════════════════════════════════════════════════════════════╝");

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

            _logger.LogError("╔═══════════════════════════════════════════════════════════════════╗");
            _logger.LogError("║ 📋 GetExistingFields CALLED - Loading PDF to extract fields     ║");
            _logger.LogError("╚═══════════════════════════════════════════════════════════════════╝");

            using var ms = new MemoryStream(pdfBytes);
            using var loadedDoc = new PdfLoadedDocument(ms);

            var fieldCount = loadedDoc.Form?.Fields?.Count ?? 0;
            _logger.LogError($"📊 Syncfusion loadedDoc.Form.Fields.Count = {fieldCount}");

            if (loadedDoc.Form?.Fields == null)
            {
                _logger.LogError("[PDF-PRESERVATION] ❌ loadedDoc.Form.Fields is NULL - returning empty list");
                return results;
            }

            if (fieldCount == 0)
            {
                _logger.LogError("[PDF-PRESERVATION] ❌ loadedDoc.Form.Fields.Count is ZERO - returning empty list");
                return results;
            }

            int fieldCounter = 0;
            foreach (var fieldObj in loadedDoc.Form.Fields)
            {
                if (!(fieldObj is PdfLoadedField field))
                    continue;

                fieldCounter++;

                // Log detailed field information
                _logger.LogWarning($"[PDF-PRESERVATION] Field #{fieldCounter}: Name='{field.Name}', Type={field.GetType().Name}, " +
                    $"Page={(field.Page != null ? "HasPage" : "NULL")}");

                // Get field page number and page dimensions
                int pageNum = 1;
                float pageHeight = 792f; // Default letter size height
                float pageWidth = 612f; // Default letter size width
                if (field.Page != null)
                {
                    for (int i = 0; i < loadedDoc.Pages.Count; i++)
                    {
                        if (loadedDoc.Pages[i] == field.Page)
                        {
                            pageNum = i + 1;
                            pageHeight = field.Page.Size.Height;
                            pageWidth = field.Page.Size.Width;
                            break;
                        }
                    }
                }

                // Get bounds - need to cast to specific field type
                RectangleF bounds;
                string fieldType;
                if (field is PdfLoadedTextBoxField textBox)
                {
                    bounds = textBox.Bounds;
                    fieldType = "text";
                }
                else if (field is PdfLoadedCheckBoxField checkBox)
                {
                    bounds = checkBox.Bounds;
                    fieldType = "checkbox";
                    // Normalize checkbox size to 15x15 DISPLAY pixels
                    // Since display uses 150 DPI and PDF uses 72 DPI (scale factor = 150/72 = 2.0833)
                    // We need 15px display / 2.0833 = 7.2 PDF points
                    bounds.Width = 7.2f;
                    bounds.Height = 7.2f;
                }
                else if (field is PdfLoadedRadioButtonListField radioList)
                {
                    // Radio button lists need to be handled item by item
                    _logger.LogWarning($"[PDF-PRESERVATION] RadioButtonList '{field.Name}' has {radioList.Items.Count} items");

                    for (int i = 0; i < radioList.Items.Count; i++)
                    {
                        var radioItem = radioList.Items[i];
                        var radioBounds = radioItem.Bounds;

                        // Get page number and dimensions for this specific radio button
                        int radioPageNum = 1;
                        float radioPageHeight = 792f;
                        float radioPageWidth = 612f;
                        if (radioItem.Page != null)
                        {
                            for (int p = 0; p < loadedDoc.Pages.Count; p++)
                            {
                                if (loadedDoc.Pages[p] == radioItem.Page)
                                {
                                    radioPageNum = p + 1;
                                    radioPageHeight = radioItem.Page.Size.Height;
                                    radioPageWidth = radioItem.Page.Size.Width;
                                    break;
                                }
                            }
                        }

                        fieldCounter++;
                        var radioField = new Models.FieldDetectionResult
                        {
                            ShortId = $"PDF{fieldCounter}",
                            FieldName = $"{field.Name}_{i}",
                            FieldType = "radio",
                            X = radioBounds.X,
                            Y = radioBounds.Y,
                            Width = radioBounds.Width,
                            Height = radioBounds.Height,
                            PageNumber = radioPageNum,
                            PageWidth = radioPageWidth,
                            PageHeight = radioPageHeight,
                            Source = "PDF-Original",
                            Confidence = 1.0f,
                            IsValid = true
                        };

                        results.Add(radioField);
                        _logger.LogWarning($"[PDF-PRESERVATION] Detected radio button: {radioField.FieldName} ({radioField.FieldType}) " +
                            $"on page {radioPageNum} at X={radioBounds.X}, Y={radioBounds.Y}, W={radioBounds.Width}, H={radioBounds.Height}");
                    }

                    continue; // Skip the default field adding below
                }
                else if (field is PdfLoadedComboBoxField comboBox)
                {
                    bounds = comboBox.Bounds;
                    fieldType = "dropdown";
                }
                else if (field is PdfLoadedListBoxField listBox)
                {
                    bounds = listBox.Bounds;
                    fieldType = "listbox";
                }
                else if (field is PdfLoadedSignatureField signature)
                {
                    bounds = signature.Bounds;
                    fieldType = "signature";
                }
                else
                    continue; // Skip unknown field types

                var detectedField = new Models.FieldDetectionResult
                {
                    ShortId = $"PDF{fieldCounter}",
                    FieldName = field.Name ?? $"Field{fieldCounter}",
                    FieldType = fieldType,
                    X = bounds.X,
                    Y = bounds.Y,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    PageNumber = pageNum,
                    PageWidth = pageWidth,
                    PageHeight = pageHeight,
                    Source = "PDF-Original",
                    Confidence = 1.0f, // Existing fields are 100% confident
                    IsValid = true
                };

                results.Add(detectedField);

                _logger.LogWarning($"[PDF-PRESERVATION] Detected existing field: {detectedField.FieldName} ({detectedField.FieldType}) " +
                    $"on page {pageNum} at X={bounds.X}, Y={bounds.Y}, W={bounds.Width}, H={bounds.Height}");
            }

            _logger.LogError("╔═══════════════════════════════════════════════════════════════════╗");
            _logger.LogError($"║ ✅ GetExistingFields COMPLETE - Returning {results.Count} fields");
            _logger.LogError("╚═══════════════════════════════════════════════════════════════════╝");

            return results;
        }
    }
}
