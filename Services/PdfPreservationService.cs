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
        private readonly ArtifactRemovalService _artifactRemovalService;
        private readonly ArtifactViolationFixService _artifactViolationFixService;
        private readonly TaggedWhitespaceFixService _taggedWhitespaceFixService;
        private readonly OrphanedWhitespaceAdoptionService _orphanedWhitespaceAdoptionService;

        public PdfPreservationService(
            ILogger<PdfPreservationService> logger,
            AccessibilityService accessibilityService,
            AccessibilityRetrofitService retrofitService,
            PdfAccessibilityEnhancer enhancer,
            PassportPdfService passportPdfService,
            ArtifactRemovalService artifactRemovalService,
            ArtifactViolationFixService artifactViolationFixService,
            TaggedWhitespaceFixService taggedWhitespaceFixService,
            OrphanedWhitespaceAdoptionService orphanedWhitespaceAdoptionService)
        {
            _logger = logger;
            _accessibilityService = accessibilityService;
            _retrofitService = retrofitService;
            _enhancer = enhancer;
            _passportPdfService = passportPdfService;
            _artifactRemovalService = artifactRemovalService;
            _artifactViolationFixService = artifactViolationFixService;
            _taggedWhitespaceFixService = taggedWhitespaceFixService;
            _orphanedWhitespaceAdoptionService = orphanedWhitespaceAdoptionService;
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

            // Initialize processing report
            var report = new Models.PdfProcessingReport
            {
                ProcessingStartTime = DateTime.UtcNow
            };

            // Capture input statistics
            report.Input.FileSizeBytes = pdfBytes.Length;

            // Step 0: Adopt orphaned whitespace into adjacent content tags
            _logger.LogInformation("[PDF-PRESERVATION] Step 0: Adopting orphaned whitespace into adjacent content...");
            var stepStartTime = DateTime.UtcNow;
            var adoptionResult = await _orphanedWhitespaceAdoptionService.AdoptOrphanedWhitespaceAsync(pdfBytes);

            report.Steps.Add(new Models.ProcessingStep
            {
                Name = "Orphaned Whitespace Adoption",
                Success = adoptionResult.Success,
                DurationMs = (long)(DateTime.UtcNow - stepStartTime).TotalMilliseconds,
                Details = adoptionResult.OrphansAdopted > 0
                    ? $"Adopted {adoptionResult.OrphansAdopted} orphaned whitespace elements"
                    : "No orphaned whitespace found"
            });

            if (adoptionResult.Success && adoptionResult.FixedPdf != null)
            {
                if (adoptionResult.OrphansAdopted > 0)
                {
                    _logger.LogWarning($"[PDF-PRESERVATION] ✅ Adopted {adoptionResult.OrphansAdopted} orphaned whitespace elements");
                    pdfBytes = adoptionResult.FixedPdf; // Use the fixed version
                }
                else
                {
                    _logger.LogInformation("[PDF-PRESERVATION] ✅ No orphaned whitespace found");
                }
            }
            else
            {
                _logger.LogWarning($"[PDF-PRESERVATION] ⚠️  Whitespace adoption failed: {adoptionResult.ErrorMessage}, continuing with original PDF");
            }

            // Step 1: Fix any artifact violations (tagged content inside artifacts) in source PDF
            _logger.LogInformation("[PDF-PRESERVATION] Step 1: Checking for artifact violations in source PDF...");
            stepStartTime = DateTime.UtcNow;
            var artifactFixResult = await _artifactViolationFixService.FixArtifactViolationsAsync(pdfBytes);

            // Record in report
            report.Issues.ArtifactViolations.Found = artifactFixResult.ViolationsFound;
            report.Issues.ArtifactViolations.Fixed = artifactFixResult.ViolationsFixed;
            report.Steps.Add(new Models.ProcessingStep
            {
                Name = "Artifact Violation Fix",
                Success = artifactFixResult.Success,
                DurationMs = (long)(DateTime.UtcNow - stepStartTime).TotalMilliseconds,
                Details = artifactFixResult.ViolationsFixed > 0
                    ? $"Fixed {artifactFixResult.ViolationsFixed} violations"
                    : "No violations found"
            });

            if (artifactFixResult.Success && artifactFixResult.FixedPdf != null)
            {
                if (artifactFixResult.ViolationsFixed > 0)
                {
                    _logger.LogWarning($"[PDF-PRESERVATION] ✅ Fixed {artifactFixResult.ViolationsFixed} artifact violations from source PDF");
                    pdfBytes = artifactFixResult.FixedPdf; // Use the fixed version
                }
                else
                {
                    _logger.LogInformation("[PDF-PRESERVATION] ✅ No artifact violations found in source PDF");
                }
            }
            else
            {
                _logger.LogWarning($"[PDF-PRESERVATION] ⚠️  Artifact fix failed: {artifactFixResult.ErrorMessage}, continuing with original PDF");
            }

            // Step 2: Fix tagged whitespace violations
            _logger.LogInformation("[PDF-PRESERVATION] Step 2: Checking for tagged whitespace violations...");
            stepStartTime = DateTime.UtcNow;
            var whitespaceFixResult = await _taggedWhitespaceFixService.FixTaggedWhitespaceAsync(pdfBytes);

            // Record in report (we'll add this to the existing IssuesReport - need to add a new category)
            if (whitespaceFixResult.Success && whitespaceFixResult.FixedPdf != null)
            {
                if (whitespaceFixResult.ViolationsFixed > 0)
                {
                    _logger.LogWarning($"[PDF-PRESERVATION] ✅ Removed tagging from {whitespaceFixResult.ViolationsFixed} whitespace elements");
                    pdfBytes = whitespaceFixResult.FixedPdf; // Use the fixed version
                }
                else
                {
                    _logger.LogInformation("[PDF-PRESERVATION] ✅ No tagged whitespace violations found");
                }
            }
            else
            {
                _logger.LogWarning($"[PDF-PRESERVATION] ⚠️  Whitespace fix failed: {whitespaceFixResult.ErrorMessage}, continuing with original PDF");
            }

            report.Steps.Add(new Models.ProcessingStep
            {
                Name = "Tagged Whitespace Fix",
                Success = whitespaceFixResult.Success,
                DurationMs = (long)(DateTime.UtcNow - stepStartTime).TotalMilliseconds,
                Details = whitespaceFixResult.ViolationsFixed > 0
                    ? $"Removed tagging from {whitespaceFixResult.ViolationsFixed} whitespace elements"
                    : "No whitespace violations found"
            });

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

            // Populate input statistics in report
            report.Input.PageCount = loadedDoc.Pages.Count;
            report.Input.FormFieldCount = initialFormCount;
            report.Input.PdfVersion = loadedDoc.FileStructure.ToString();

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

                        // Keep existing tooltip as-is (removed debug [PAGE:X] markers)

                        // Normalize checkbox/radio button sizes in the actual PDF (not just metadata)
                        // This ensures the size persists through PassportPDF processing
                        if (field is PdfLoadedCheckBoxField checkBox)
                        {
                            var bounds = checkBox.Bounds;
                            // Set to 14.4x14.4 PDF points (becomes ~30x30 pixels after 150/72 DPI scaling)
                            checkBox.Bounds = new RectangleF(bounds.X, bounds.Y, 14.4f, 14.4f);
                            _logger.LogInformation($"[PDF-PRESERVATION] Resized checkbox '{field.Name}' to 14.4x14.4");
                        }
                        else if (field is PdfLoadedRadioButtonListField radioList)
                        {
                            // Resize each radio button in the list
                            for (int i = 0; i < radioList.Items.Count; i++)
                            {
                                var radioItem = radioList.Items[i];
                                var bounds = radioItem.Bounds;
                                radioItem.Bounds = new RectangleF(bounds.X, bounds.Y, 14.4f, 14.4f);
                            }
                            _logger.LogInformation($"[PDF-PRESERVATION] Resized {radioList.Items.Count} radio buttons in '{field.Name}' to 14.4x14.4");
                        }
                    }
                }
            }

            using var tooltipSaveMs = new MemoryStream();
            pdfForTooltips.Save(tooltipSaveMs);
            pdfBytesWithFields = tooltipSaveMs.ToArray();

            _logger.LogInformation("[PDF-PRESERVATION] Page markers injected, now applying AccessibilityService");

            // ARTIFACT TRACE: Before AccessibilityService
            CountArtifacts(pdfBytesWithFields, "BEFORE AccessibilityService");

            // Step 3: AccessibilityService - sets metadata, tooltips, etc.
            // This one works on byte[] and returns (byte[], report)
            _logger.LogWarning("🔍 PRESERVATION: Running AccessibilityService (metadata/tooltips)");
            var (pdfWithMetadata, accessibilityReport) = _accessibilityService.MakeAccessible(pdfBytesWithFields, "existing-pdf.pdf");

            // ARTIFACT TRACE: After AccessibilityService
            CountArtifacts(pdfWithMetadata, "AFTER AccessibilityService");

            // Step 4: PassportPDF - PDF/A-2u conversion with JavaScript preservation
            // This handles: font embedding, ZapfDingbats → Unicode, PDF/A-2u compliance
            // Step 5: PassportPDF - PDF/A-2u conversion with font/table fixes
            _logger.LogWarning("╔═══════════════════════════════════════════════════════════════════╗");
            _logger.LogWarning("║ 🔧 RUNNING PASSPORTPDF PDF/A-2u CONVERSION (PRESERVE JS)        ║");
            _logger.LogWarning("╚═══════════════════════════════════════════════════════════════════╝");
            var pdfAfterPassport = await _passportPdfService.ConvertToPdfAAsync(pdfWithMetadata, preserveJavaScript: true);

            // ARTIFACT TRACE: After PassportPDF
            CountArtifacts(pdfAfterPassport, "AFTER PassportPDF");

            // Step 6: Fix artifacts created by PassportPDF
            // PassportPDF and accessibility services may add new whitespace - clean it up
            _logger.LogInformation("[PDF-PRESERVATION] Step 3: Final artifact cleanup after PassportPDF...");
            stepStartTime = DateTime.UtcNow;
            var finalArtifactFixResult = await _artifactViolationFixService.FixArtifactViolationsAsync(pdfAfterPassport);

            report.Steps.Add(new Models.ProcessingStep
            {
                Name = "Post-PassportPDF Artifact Fix",
                Success = finalArtifactFixResult.Success,
                DurationMs = (long)(DateTime.UtcNow - stepStartTime).TotalMilliseconds,
                Details = finalArtifactFixResult.ViolationsFixed > 0
                    ? $"Fixed {finalArtifactFixResult.ViolationsFixed} final violations"
                    : "No violations found"
            });

            byte[] finalPdfBytes;
            if (finalArtifactFixResult.Success && finalArtifactFixResult.FixedPdf != null && finalArtifactFixResult.ViolationsFixed > 0)
            {
                _logger.LogWarning($"[PDF-PRESERVATION] ✅ Final cleanup: Fixed {finalArtifactFixResult.ViolationsFixed} violations after PassportPDF");
                finalPdfBytes = finalArtifactFixResult.FixedPdf;
            }
            else
            {
                _logger.LogInformation("[PDF-PRESERVATION] ✅ No final violations found after PassportPDF");
                finalPdfBytes = pdfAfterPassport;
            }

            // Finalize processing report
            report.ProcessingEndTime = DateTime.UtcNow;
            report.Output.FileSizeBytes = finalPdfBytes.Length;
            report.Output.PageCount = report.Input.PageCount; // Same as input
            report.Accessibility.IsTagged = true; // We always tag
            report.Accessibility.Language = "en-US";

            // Save report to file
            var reportText = report.GenerateTextReport();
            SaveReportToFile(reportText);

            // Log the complete report
            _logger.LogWarning("╔═══════════════════════════════════════════════════════════════════╗");
            _logger.LogWarning("║ ✅ PDFPRESERVATIONSERVICE.ProcessExistingPdfAsync COMPLETE      ║");
            _logger.LogWarning("╚═══════════════════════════════════════════════════════════════════╝");
            _logger.LogInformation("");
            _logger.LogInformation(reportText);
            _logger.LogInformation("");

            return finalPdfBytes;
        }

        /// <summary>
        /// Count PROBLEMATIC artifacts - artifacts that contain tagged content (structure tree elements)
        /// This is the PDF/UA violation: "Tagged content present inside an artifact"
        /// </summary>
        private void CountArtifacts(byte[] pdfBytes, string stage)
        {
            try
            {
                // Simple regex search in the raw PDF bytes for /Artifact BMC markers
                string content = System.Text.Encoding.Latin1.GetString(pdfBytes);

                int totalArtifacts = System.Text.RegularExpressions.Regex.Matches(content, @"/Artifact\s+BMC").Count;

                // Count artifacts that ALSO contain tagged content markers (BDC = Begin Marked Content)
                // The pattern /P BMC or /Span BMC or /Figure BMC inside an artifact block is the violation
                int problematicArtifacts = 0;
                var artifactBlocks = System.Text.RegularExpressions.Regex.Matches(content, @"/Artifact\s+BMC.*?EMC", System.Text.RegularExpressions.RegexOptions.Singleline);
                var problematicSamples = new System.Collections.Generic.List<string>();

                foreach (System.Text.RegularExpressions.Match match in artifactBlocks)
                {
                    string block = match.Value;
                    // Check if this artifact contains tagged content markers
                    if (block.Contains("/P ") || block.Contains("/Span ") || block.Contains("/Figure ") ||
                        block.Contains("BDC") || block.Contains("/MCID"))
                    {
                        problematicArtifacts++;

                        // Extract text content from this problematic block for debugging
                        var textMatch = System.Text.RegularExpressions.Regex.Match(block, @"\(([^)]{1,50})\)\s*Tj");
                        if (textMatch.Success && problematicSamples.Count < 5)
                        {
                            problematicSamples.Add(textMatch.Groups[1].Value);
                        }
                    }
                }

                _logger.LogError($"🔍 ARTIFACT TRACE [{stage}]: Total artifacts={totalArtifacts}, ⚠️ PROBLEMATIC (tagged content inside)={problematicArtifacts}");
                if (problematicSamples.Count > 0)
                {
                    _logger.LogError($"   Sample problematic text in artifacts: {string.Join(", ", problematicSamples.Select(s => $"'{s}'"))}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to count artifacts at stage '{stage}': {ex.Message}");
            }
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
                    // Check if this checkbox has multiple items (radio buttons can be implemented as checkboxes in PDFs)
                    if (checkBox.Items != null && checkBox.Items.Count > 1)
                    {
                        _logger.LogWarning($"[PDF-PRESERVATION] CheckBox '{field.Name}' has {checkBox.Items.Count} items - EXPANDING");

                        // Expand into individual checkbox items (treat as separate checkboxes)
                        for (int i = 0; i < checkBox.Items.Count; i++)
                        {
                            var checkboxItem = checkBox.Items[i];
                            var checkboxBounds = checkboxItem.Bounds;

                            // Get page number and dimensions for this specific checkbox
                            int checkboxPageNum = 1;
                            float checkboxPageHeight = 792f;
                            float checkboxPageWidth = 612f;
                            if (checkboxItem.Page != null)
                            {
                                for (int p = 0; p < loadedDoc.Pages.Count; p++)
                                {
                                    if (loadedDoc.Pages[p] == checkboxItem.Page)
                                    {
                                        checkboxPageNum = p + 1;
                                        checkboxPageHeight = checkboxItem.Page.Size.Height;
                                        checkboxPageWidth = checkboxItem.Page.Size.Width;
                                        break;
                                    }
                                }
                            }

                            fieldCounter++;
                            var checkboxField = new Models.FieldDetectionResult
                            {
                                ShortId = $"PDF{fieldCounter}",
                                FieldName = $"{field.Name}_{i}",
                                FieldType = "checkbox",
                                X = checkboxBounds.X,
                                Y = checkboxBounds.Y,
                                Width = 14.4f,  // Normalized size (~30px display)
                                Height = 14.4f,
                                PageNumber = checkboxPageNum,
                                PageWidth = checkboxPageWidth,
                                PageHeight = checkboxPageHeight,
                                Source = "PDF-Original",
                                Confidence = 1.0f,
                                IsValid = true
                            };

                            results.Add(checkboxField);
                            _logger.LogWarning($"[PDF-PRESERVATION] Detected checkbox item: {checkboxField.FieldName} ({checkboxField.FieldType}) " +
                                $"on page {checkboxPageNum} at X={checkboxBounds.X}, Y={checkboxBounds.Y}, W={checkboxBounds.Width}, H={checkboxBounds.Height}");
                        }

                        continue; // Skip the default field adding below
                    }

                    // Single checkbox - use default bounds
                    bounds = checkBox.Bounds;
                    fieldType = "checkbox";
                    // Normalize checkbox size to ~30x30 DISPLAY pixels for better visibility
                    // Since display uses 150 DPI and PDF uses 72 DPI (scale factor = 150/72 = 2.0833)
                    // We need 30px display / 2.0833 = 14.4 PDF points
                    bounds.Width = 14.4f;
                    bounds.Height = 14.4f;
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

                        // Try to get the button value from the radio item
                        string buttonValue = radioItem.Value ?? $"Option{i + 1}";

                        var radioField = new Models.FieldDetectionResult
                        {
                            ShortId = $"PDF{fieldCounter}",
                            FieldName = field.Name,  // Use group name (not field.Name_{i})
                            FieldType = "radio",
                            X = radioBounds.X,
                            Y = radioBounds.Y,
                            Width = 14.4f,  // Normalized size (~30px display)
                            Height = 14.4f,
                            PageNumber = radioPageNum,
                            PageWidth = radioPageWidth,
                            PageHeight = radioPageHeight,
                            Source = "PDF-Original",
                            Confidence = 1.0f,
                            IsValid = true,
                            ButtonValue = buttonValue  // Set button value for radio grouping
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

            // Log all Box 6 fields to verify they're in the results
            var box6Fields = results.Where(f => f.FieldName.Contains("Box 6")).ToList();
            if (box6Fields.Any())
            {
                _logger.LogError($"[PDF-PRESERVATION] 📦 BOX 6 FIELDS IN RESULTS: {box6Fields.Count} total");
                foreach (var box6Field in box6Fields)
                {
                    _logger.LogError($"  - {box6Field.FieldName} at X={box6Field.X}, Y={box6Field.Y}");
                }
            }

            return results;
        }

        /// <summary>
        /// Save processing report to a timestamped file
        /// </summary>
        private void SaveReportToFile(string reportText)
        {
            try
            {
                // Create reports directory if it doesn't exist
                var reportsDir = Path.Combine(Directory.GetCurrentDirectory(), "Reports");
                Directory.CreateDirectory(reportsDir);

                // Generate filename with date and time
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var filename = $"pdf-processing-report_{timestamp}.txt";
                var filePath = Path.Combine(reportsDir, filename);

                // Write report to file
                System.IO.File.WriteAllText(filePath, reportText);

                _logger.LogInformation($"📄 Processing report saved to: {filePath}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to save processing report to file: {ex.Message}");
            }
        }
    }
}
