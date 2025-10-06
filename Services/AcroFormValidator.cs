using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aspose.Pdf;
using Aspose.Pdf.Forms;
using Microsoft.Extensions.Logging;

namespace AccessFormServer.Services
{
    /// <summary>
    /// Diagnostic tool to validate PDF AcroForm structure for Adobe Acrobat compatibility.
    /// Identifies issues that cause Adobe to not recognize forms or create duplicate fields.
    /// </summary>
    public class AcroFormValidator
    {
        private readonly ILogger<AcroFormValidator> _logger;

        public AcroFormValidator(ILogger<AcroFormValidator> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Comprehensive validation of PDF form structure
        /// </summary>
        public async Task<AcroFormValidationResult> ValidateAsync(byte[] pdfBytes)
        {
            return await Task.Run(() =>
            {
                var result = new AcroFormValidationResult();

                try
                {
                    _logger.LogInformation("===== ACROFORM VALIDATION STARTING =====");

                    using var inputStream = new MemoryStream(pdfBytes);
                    var document = new Document(inputStream);

                    // Check 1: Does document have a form?
                    result.HasForm = document.Form != null;
                    _logger.LogInformation($"Has Form: {result.HasForm}");

                    if (!result.HasForm)
                    {
                        result.IsValid = false;
                        result.Issues.Add("Document has no AcroForm structure");
                        result.Severity = ValidationSeverity.Critical;
                        return result;
                    }

                    // Check 2: Field count
                    result.FieldCount = document.Form.Fields.Count;
                    _logger.LogInformation($"Field Count: {result.FieldCount}");

                    if (result.FieldCount == 0)
                    {
                        result.Issues.Add("Form exists but has no fields");
                        result.Severity = ValidationSeverity.Warning;
                    }

                    // Check 3: Validate each field
                    foreach (Field field in document.Form.Fields)
                    {
                        ValidateField(field, result);
                    }

                    // Check 4: Form properties
                    ValidateFormProperties(document, result);

                    // Check 5: Appearance streams
                    ValidateAppearanceStreams(document, result);

                    // Check 6: Field naming conflicts
                    ValidateFieldNaming(document, result);

                    // Determine overall validity
                    result.IsValid = result.Issues.Count == 0 ||
                                    result.Severity == ValidationSeverity.Warning;

                    _logger.LogInformation($"Validation Complete: {(result.IsValid ? "VALID" : "INVALID")}");
                    _logger.LogInformation($"Issues Found: {result.Issues.Count}");
                    _logger.LogInformation("===== ACROFORM VALIDATION COMPLETE =====");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during AcroForm validation");
                    result.IsValid = false;
                    result.Issues.Add($"Validation error: {ex.Message}");
                    result.Severity = ValidationSeverity.Critical;
                }

                return result;
            });
        }

        /// <summary>
        /// Validate individual field structure
        /// </summary>
        private void ValidateField(Field field, AcroFormValidationResult result)
        {
            var fieldName = field.PartialName ?? "(unnamed)";

            // Check field name
            if (string.IsNullOrEmpty(field.PartialName))
            {
                result.Issues.Add($"Field has no name (FullName: {field.FullName})");
                result.Severity = MaxSeverity(result.Severity, ValidationSeverity.Error);
            }

            // Check field type
            if (field is TextBoxField textField)
            {
                result.FieldTypes["TextBox"]++;
                ValidateTextBoxField(textField, result);
            }
            else if (field is CheckboxField checkField)
            {
                result.FieldTypes["Checkbox"]++;
                ValidateCheckboxField(checkField, result);
            }
            else if (field is RadioButtonField radioField)
            {
                result.FieldTypes["RadioButton"]++;
                ValidateRadioButtonField(radioField, result);
            }
            else if (field is ComboBoxField comboField)
            {
                result.FieldTypes["ComboBox"]++;
                ValidateComboBoxField(comboField, result);
            }
            else if (field is SignatureField sigField)
            {
                result.FieldTypes["Signature"]++;
            }
            else
            {
                result.FieldTypes["Other"]++;
            }

            // Check tooltip/alternate name
            if (string.IsNullOrEmpty(field.AlternateName))
            {
                result.FieldsWithoutTooltip++;
                _logger.LogDebug($"Field '{fieldName}' has no tooltip (AlternateName)");
            }

            // Check bounds
            if (field.Rect.Width <= 0 || field.Rect.Height <= 0)
            {
                result.Issues.Add($"Field '{fieldName}' has invalid bounds: {field.Rect.Width}x{field.Rect.Height}");
                result.Severity = MaxSeverity(result.Severity, ValidationSeverity.Error);
            }
        }

        private void ValidateTextBoxField(TextBoxField field, AcroFormValidationResult result)
        {
            // Check for common issues
            if (field.MaxLen > 0 && field.Value?.Length > field.MaxLen)
            {
                result.Issues.Add($"TextField '{field.PartialName}' value exceeds MaxLen");
                result.Severity = MaxSeverity(result.Severity, ValidationSeverity.Warning);
            }
        }

        private void ValidateCheckboxField(CheckboxField field, AcroFormValidationResult result)
        {
            // Checkboxes should have export values
            if (string.IsNullOrEmpty(field.ExportValue))
            {
                result.Issues.Add($"Checkbox '{field.PartialName}' has no export value");
                result.Severity = MaxSeverity(result.Severity, ValidationSeverity.Warning);
            }
        }

        private void ValidateRadioButtonField(RadioButtonField field, AcroFormValidationResult result)
        {
            // Radio buttons should have options
            if (field.Options == null || field.Options.Count == 0)
            {
                result.Issues.Add($"RadioButton '{field.PartialName}' has no options");
                result.Severity = MaxSeverity(result.Severity, ValidationSeverity.Error);
            }
        }

        private void ValidateComboBoxField(ComboBoxField field, AcroFormValidationResult result)
        {
            // Combo boxes should have options
            if (field.Options == null || field.Options.Count == 0)
            {
                result.Issues.Add($"ComboBox '{field.PartialName}' has no options");
                result.Severity = MaxSeverity(result.Severity, ValidationSeverity.Warning);
            }
        }

        /// <summary>
        /// Validate form-level properties
        /// </summary>
        private void ValidateFormProperties(Document document, AcroFormValidationResult result)
        {
            try
            {
                var form = document.Form;

                // Check if NeedAppearances is set (Adobe may ignore forms without appearance streams if this is false)
                // Note: Aspose may not expose this directly, but we can check field appearances

                // Check for XFA forms (not compatible with standard AcroForms)
                if (form.Type == FormType.Xfa)
                {
                    result.Issues.Add("Form is XFA type - Adobe may handle differently");
                    result.Severity = MaxSeverity(result.Severity, ValidationSeverity.Warning);
                    result.IsXfaForm = true;
                }

                _logger.LogInformation($"Form Type: {form.Type}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error validating form properties");
            }
        }

        /// <summary>
        /// Validate appearance streams (critical for Adobe recognition)
        /// </summary>
        private void ValidateAppearanceStreams(Document document, AcroFormValidationResult result)
        {
            int fieldsWithoutAppearance = 0;

            foreach (Field field in document.Form.Fields)
            {
                // In Aspose, appearance is complex - we'll do a simple check
                // Fields should have visual representation
                if (field.Rect.Width <= 0 || field.Rect.Height <= 0)
                {
                    fieldsWithoutAppearance++;
                }
            }

            if (fieldsWithoutAppearance > 0)
            {
                result.Issues.Add($"{fieldsWithoutAppearance} fields may have missing or invalid appearance streams");
                result.Severity = MaxSeverity(result.Severity, ValidationSeverity.Warning);
            }

            _logger.LogInformation($"Fields with potential appearance issues: {fieldsWithoutAppearance}");
        }

        /// <summary>
        /// Check for field naming conflicts
        /// </summary>
        private void ValidateFieldNaming(Document document, AcroFormValidationResult result)
        {
            var fieldNames = new Dictionary<string, int>();

            foreach (Field field in document.Form.Fields)
            {
                var name = field.FullName ?? field.PartialName ?? "(unnamed)";

                if (fieldNames.ContainsKey(name))
                {
                    fieldNames[name]++;
                }
                else
                {
                    fieldNames[name] = 1;
                }
            }

            var duplicates = fieldNames.Where(kvp => kvp.Value > 1).ToList();

            foreach (var dup in duplicates)
            {
                result.Issues.Add($"Duplicate field name: '{dup.Key}' ({dup.Value} occurrences)");
                result.Severity = MaxSeverity(result.Severity, ValidationSeverity.Error);
            }

            if (duplicates.Count > 0)
            {
                _logger.LogWarning($"Found {duplicates.Count} duplicate field names");
            }
        }

        /// <summary>
        /// Generate detailed validation report
        /// </summary>
        public string GenerateReport(AcroFormValidationResult result)
        {
            var report = $@"
═══════════════════════════════════════════════════════════
PDF ACROFORM VALIDATION REPORT
═══════════════════════════════════════════════════════════
Overall Status: {(result.IsValid ? "✓ VALID" : "✗ INVALID")}
Severity: {result.Severity}

FORM STRUCTURE:
  Has Form: {(result.HasForm ? "Yes" : "No")}
  Total Fields: {result.FieldCount}
  XFA Form: {(result.IsXfaForm ? "Yes" : "No")}

FIELD TYPES:";

            foreach (var type in result.FieldTypes)
            {
                report += $"\n  {type.Key}: {type.Value}";
            }

            report += $@"

FIELD QUALITY:
  Fields without tooltips: {result.FieldsWithoutTooltip}

ISSUES FOUND: {result.Issues.Count}";

            if (result.Issues.Count > 0)
            {
                report += "\n";
                for (int i = 0; i < result.Issues.Count; i++)
                {
                    report += $"\n  {i + 1}. {result.Issues[i]}";
                }
            }
            else
            {
                report += "\n  ✓ No issues found";
            }

            report += $@"

ADOBE COMPATIBILITY ASSESSMENT:
{GetCompatibilityAssessment(result)}

═══════════════════════════════════════════════════════════";

            return report;
        }

        private string GetCompatibilityAssessment(AcroFormValidationResult result)
        {
            if (!result.HasForm)
            {
                return "  ✗ CRITICAL: No form structure - Adobe will not recognize fields";
            }

            if (result.Severity == ValidationSeverity.Critical)
            {
                return "  ✗ CRITICAL: Major issues - Adobe may not recognize form or create duplicates";
            }

            if (result.Severity == ValidationSeverity.Error)
            {
                return "  ⚠ WARNING: Significant issues - Adobe may have problems with some fields";
            }

            if (result.Severity == ValidationSeverity.Warning)
            {
                return "  ⚠ MINOR: Minor issues - Adobe should recognize form but may have quirks";
            }

            return "  ✓ GOOD: Form structure appears Adobe-compatible";
        }

        private ValidationSeverity MaxSeverity(ValidationSeverity a, ValidationSeverity b)
        {
            return (ValidationSeverity)Math.Max((int)a, (int)b);
        }
    }

    /// <summary>
    /// Result of AcroForm validation
    /// </summary>
    public class AcroFormValidationResult
    {
        public bool IsValid { get; set; } = true;
        public bool HasForm { get; set; }
        public int FieldCount { get; set; }
        public bool IsXfaForm { get; set; }
        public ValidationSeverity Severity { get; set; } = ValidationSeverity.None;
        public List<string> Issues { get; set; } = new List<string>();
        public Dictionary<string, int> FieldTypes { get; set; } = new Dictionary<string, int>();
        public int FieldsWithoutTooltip { get; set; }
    }

    public enum ValidationSeverity
    {
        None = 0,
        Warning = 1,
        Error = 2,
        Critical = 3
    }
}
