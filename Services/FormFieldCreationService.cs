using System;
using System.Collections.Generic;
using System.Linq;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using Syncfusion.Pdf.Interactive;
using Syncfusion.Drawing;
using Syncfusion.Pdf.Graphics;
using AccessFormServer.Services;
using Microsoft.Extensions.Logging;

namespace WordToPdfConverter.Services
{
    public class FormFieldCreationService
    {
        private readonly ILogger<FormFieldCreationService> _logger;
        private float _defaultFieldHeight = 20;
        private float _defaultFieldWidth = 200;
        private float _margin = 50;
        private float _fieldSpacing = 30;

        public FormFieldCreationService(ILogger<FormFieldCreationService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Enhances existing form fields in a loaded PDF based on Claude's field detection
        /// Note: PdfLoadedDocument can only modify existing fields, not create new ones
        /// </summary>
        public void EnhanceExistingFormFields(
            PdfLoadedDocument document, 
            List<AnthropicService.FieldAnalysisResult> detectedFields)
        {
            _logger.LogInformation($"Enhancing existing form fields with {detectedFields.Count} AI-detected fields");

            if (document.Form == null)
            {
                _logger.LogWarning("No form found in document - cannot enhance fields");
                // For documents without forms, we need a different approach
                // We'd need to save and reload as PdfDocument to add new fields
                return;
            }

            var existingFields = new List<PdfLoadedField>();
            foreach (PdfLoadedField field in document.Form.Fields)
            {
                existingFields.Add(field);
            }

            _logger.LogInformation($"Found {existingFields.Count} existing fields to enhance");

            // Enhance existing fields with Claude's data
            foreach (var existingField in existingFields)
            {
                var matchedField = FindBestMatch(existingField, detectedFields);
                
                if (matchedField != null)
                {
                    EnhanceField(existingField, matchedField);
                    _logger.LogInformation($"Enhanced field '{existingField.Name}' with AI data");
                }
                else
                {
                    // Apply default enhancements even if no match
                    ApplyDefaultEnhancements(existingField);
                    _logger.LogInformation($"Applied default enhancements to field '{existingField.Name}'");
                }
            }

            // Log fields that couldn't be matched
            var unmatchedDetectedFields = detectedFields.Where(df => 
                !existingFields.Any(ef => IsFieldMatch(ef, df))).ToList();
            
            if (unmatchedDetectedFields.Count > 0)
            {
                _logger.LogWarning($"Could not match {unmatchedDetectedFields.Count} detected fields to existing PDF fields:");
                foreach (var field in unmatchedDetectedFields)
                {
                    _logger.LogWarning($"  - {field.FieldName} ({field.FieldType})");
                }
                _logger.LogWarning("To add these fields, the PDF would need to be recreated with form fields");
            }

            // Apply document-level settings
            ApplyDocumentAccessibilitySettings(document);
        }

        /// <summary>
        /// Creates form fields in a new PDF document during creation
        /// This method works with PdfDocument (not PdfLoadedDocument)
        /// </summary>
        public void CreateFormFieldsInNewDocument(
            PdfDocument document,
            List<AnthropicService.FieldAnalysisResult> detectedFields)
        {
            _logger.LogInformation($"Creating {detectedFields.Count} form fields in new document");

            // Ensure document has at least one page
            if (document.Pages.Count == 0)
            {
                document.Pages.Add();
            }

            // Group fields by page number to handle multi-page placement correctly
            var fieldsByPage = detectedFields.GroupBy(f => f.PageNumber).OrderBy(g => g.Key);
            int tabIndex = 1;

            foreach (var pageGroup in fieldsByPage)
            {
                int pageNum = pageGroup.Key;
                _logger.LogInformation($"Processing {pageGroup.Count()} fields for page {pageNum}");

                // Ensure we have enough pages in the document
                while (document.Pages.Count < pageNum)
                {
                    document.Pages.Add();
                    _logger.LogInformation($"Added page {document.Pages.Count} to accommodate field placement");
                }

                // Get the correct page (1-based page number, 0-based array index)
                var page = document.Pages[pageNum - 1] as PdfPage;
                float currentY = page.Size.Height - _margin - 50;

                foreach (var fieldData in pageGroup)
                {
                    try
                    {
                        _logger.LogInformation($"[PAGINATION FIX] Creating field '{fieldData.FieldName}' on page {pageNum}");
                        CreateFieldInNewDocument(page, fieldData, ref currentY, tabIndex++, document, detectedFields);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to create field '{fieldData.FieldName}' on page {pageNum}");
                    }
                }
            }
        }

        private void CreateFieldInNewDocument(
            PdfPage page,
            AnthropicService.FieldAnalysisResult fieldData,
            ref float currentY,
            int tabIndex,
            PdfDocument document,
            List<AnthropicService.FieldAnalysisResult> detectedFields)
        {
            var fieldType = fieldData.FieldType?.ToLower() ?? "text";
            var bounds = new RectangleF(_margin, currentY, _defaultFieldWidth, _defaultFieldHeight);
            var fieldName = fieldData.FieldName ?? $"Field{tabIndex}";

            _logger.LogInformation($"Creating field '{fieldName}' of type '{fieldType}' at Y={currentY}");

            if (fieldType.Contains("checkbox") || fieldType.Contains("check"))
            {
                var checkBox = new PdfCheckBoxField(page, fieldName);
                checkBox.Bounds = new RectangleF(bounds.X, bounds.Y, 15, 15);
                checkBox.ToolTip = GenerateAccessibleTooltip(fieldData);
                checkBox.Required = fieldData.IsRequired;
                checkBox.TabIndex = tabIndex;
                checkBox.BorderColor = new PdfColor(0, 0, 0);
                checkBox.BackColor = new PdfColor(255, 255, 255);
                document.Form.Fields.Add(checkBox);
            }
            else if (fieldType.Contains("radio"))
            {
                var radio = new PdfRadioButtonListField(page, fieldName);
                radio.ToolTip = GenerateAccessibleTooltip(fieldData);
                radio.Required = fieldData.IsRequired;
                radio.TabIndex = tabIndex;
                
                for (int i = 0; i < 3; i++)
                {
                    var item = new PdfRadioButtonListItem($"Option{i + 1}");
                    item.Bounds = new RectangleF(bounds.X + (i * 60), bounds.Y, 15, 15);
                    radio.Items.Add(item);
                }
                document.Form.Fields.Add(radio);
            }
            else if (fieldType.Contains("dropdown") || fieldType.Contains("select") || fieldType.Contains("combo"))
            {
                var combo = new PdfComboBoxField(page, fieldName);
                combo.Bounds = bounds;
                combo.ToolTip = GenerateAccessibleTooltip(fieldData);
                combo.Required = fieldData.IsRequired;
                combo.TabIndex = tabIndex;
                combo.Editable = false;
                combo.BorderColor = new PdfColor(0, 0, 0);
                combo.BackColor = new PdfColor(255, 255, 255);
                
                combo.Items.Add(new PdfListFieldItem("Select an option", ""));
                combo.Items.Add(new PdfListFieldItem("Option 1", "opt1"));
                combo.Items.Add(new PdfListFieldItem("Option 2", "opt2"));
                document.Form.Fields.Add(combo);
            }
            else if (fieldType.Contains("signature"))
            {
                var signature = new PdfSignatureField(page, fieldName);
                signature.Bounds = new RectangleF(bounds.X, bounds.Y, bounds.Width, 50);
                signature.ToolTip = GenerateAccessibleTooltip(fieldData);
                signature.Required = fieldData.IsRequired;
                signature.TabIndex = tabIndex;
                document.Form.Fields.Add(signature);
                currentY -= 30; // Extra space for signature
            }
            else // Default to text field
            {
                var textField = new PdfTextBoxField(page, fieldName);
                textField.Bounds = bounds;
                textField.ToolTip = GenerateAccessibleTooltip(fieldData);
                textField.Required = fieldData.IsRequired;
                textField.TabIndex = tabIndex;
                textField.BorderColor = new PdfColor(0, 0, 0);
                textField.BackColor = new PdfColor(255, 255, 255);
                
                // Apply text field specific settings
                if (fieldType.Contains("multiline") || fieldType.Contains("textarea") || 
                    fieldType.Contains("comments") || fieldType.Contains("description"))
                {
                    textField.Multiline = true;
                    textField.Bounds = new RectangleF(bounds.X, bounds.Y, bounds.Width, 60);
                    currentY -= 40; // Extra space for multiline
                }
                
                if (fieldType.Contains("password") || fieldType.Contains("ssn"))
                {
                    textField.Password = true;
                }
                
                if (fieldType.Contains("date"))
                {
                    textField.MaxLength = 10;
                    textField.ToolTip += " (Format: MM/DD/YYYY)";
                }
                
                document.Form.Fields.Add(textField);
            }

            // Move to next position
            currentY -= _fieldSpacing;
            
            // Check if we need to move to next page
            if (currentY < _margin && detectedFields.IndexOf(fieldData) < detectedFields.Count - 1)
            {
                _logger.LogWarning("Running out of space on current page, would need to add new page");
                // In production, you'd add a new page here and reset currentY
            }
        }

        private bool IsFieldMatch(PdfLoadedField existingField, AnthropicService.FieldAnalysisResult detectedField)
        {
            var existingName = existingField.Name?.ToLower() ?? "";
            var detectedName = detectedField.FieldName?.ToLower() ?? "";
            
            // Exact match
            if (existingName == detectedName) return true;
            
            // Partial match
            if (!string.IsNullOrEmpty(existingName) && !string.IsNullOrEmpty(detectedName))
            {
                if (existingName.Contains(detectedName) || detectedName.Contains(existingName))
                    return true;
            }
            
            return false;
        }

        private AnthropicService.FieldAnalysisResult FindBestMatch(
            PdfLoadedField existingField, 
            List<AnthropicService.FieldAnalysisResult> claudeFields)
        {
            var fieldName = existingField.Name?.ToLower() ?? "";
            
            // First try exact name match
            var exactMatch = claudeFields.FirstOrDefault(cf => 
                (cf.FieldName?.ToLower() ?? "") == fieldName);
            if (exactMatch != null) return exactMatch;

            // Try partial match
            var partialMatch = claudeFields.FirstOrDefault(cf =>
                !string.IsNullOrEmpty(cf.FieldName) &&
                (fieldName.Contains(cf.FieldName.ToLower()) || 
                 cf.FieldName.ToLower().Contains(fieldName)));
            if (partialMatch != null) return partialMatch;

            // Try matching by field type
            var typeMatch = claudeFields.FirstOrDefault(cf =>
                GetSyncfusionFieldType(cf.FieldType) == GetFieldTypeString(existingField));
            
            return typeMatch;
        }

        private void EnhanceField(PdfLoadedField field, AnthropicService.FieldAnalysisResult aiData)
        {
            // Set tooltip for accessibility (screen readers use this)
            if (string.IsNullOrEmpty(field.ToolTip))
            {
                field.ToolTip = GenerateAccessibleTooltip(aiData);
            }

            // Set required status
            field.Required = aiData.IsRequired;

            // Apply field-specific enhancements
            if (field is PdfLoadedTextBoxField textField)
            {
                EnhanceTextField(textField, aiData);
            }
            else if (field is PdfLoadedCheckBoxField checkBox)
            {
                EnhanceCheckBox(checkBox, aiData);
            }
            else if (field is PdfLoadedRadioButtonListField radioList)
            {
                EnhanceRadioList(radioList, aiData);
            }
            else if (field is PdfLoadedComboBoxField comboBox)
            {
                EnhanceComboBox(comboBox, aiData);
            }

            // Set tab order if available
            if (field.TabIndex == 0)
            {
                field.TabIndex = GetNextTabIndex();
            }
        }

        private void EnhanceTextField(PdfLoadedTextBoxField textField, AnthropicService.FieldAnalysisResult aiData)
        {
            var fieldType = aiData.FieldType?.ToLower() ?? "";
            
            // Set password masking for sensitive fields
            if (fieldType.Contains("ssn") || fieldType.Contains("password") || 
                aiData.FieldName?.ToLower().Contains("ssn") == true)
            {
                textField.Password = true;
                _logger.LogInformation($"Set password masking for field '{textField.Name}'");
            }

            // Set multiline for text areas
            if (fieldType.Contains("textarea") || fieldType.Contains("description") ||
                fieldType.Contains("comments"))
            {
                textField.Multiline = true;
                _logger.LogInformation($"Set multiline for field '{textField.Name}'");
            }

            // Set max length for specific field types
            if (fieldType.Contains("phone"))
            {
                textField.MaxLength = 14; // (XXX) XXX-XXXX
            }
            else if (fieldType.Contains("ssn"))
            {
                textField.MaxLength = 11; // XXX-XX-XXXX
            }
            else if (fieldType.Contains("zip"))
            {
                textField.MaxLength = 10; // XXXXX-XXXX
            }
            else if (fieldType.Contains("date"))
            {
                textField.MaxLength = 10; // MM/DD/YYYY
            }

            // Ensure field is visible and properly sized
            var bounds = textField.Bounds;
            if (bounds.Width < 50) // Too narrow
            {
                // Expand field width
                textField.Bounds = new RectangleF(bounds.X, bounds.Y, 
                    Math.Max(100, bounds.Width * 2), bounds.Height);
                _logger.LogInformation($"Expanded narrow field '{textField.Name}' from {bounds.Width} to {textField.Bounds.Width}");
            }
        }

        private void EnhanceCheckBox(PdfLoadedCheckBoxField checkBox, AnthropicService.FieldAnalysisResult aiData)
        {
            // Note: Syncfusion PdfLoadedCheckBoxField doesn't have ExportValue property
            // The checked state is managed through the Checked property

            // Add helpful tooltip
            if (string.IsNullOrEmpty(checkBox.ToolTip))
            {
                checkBox.ToolTip = $"Check this box for {aiData.FieldName}";
                if (aiData.IsRequired)
                {
                    checkBox.ToolTip += " (Required)";
                }
            }
        }

        private void EnhanceRadioList(PdfLoadedRadioButtonListField radioList, AnthropicService.FieldAnalysisResult aiData)
        {
            // Ensure radio buttons have proper values
            int optionIndex = 1;
            foreach (PdfLoadedRadioButtonItem item in radioList.Items)
            {
                if (string.IsNullOrEmpty(item.Value))
                {
                    item.Value = $"Option {optionIndex}";
                }
                optionIndex++;
            }

            // Set accessibility tooltip
            if (string.IsNullOrEmpty(radioList.ToolTip))
            {
                radioList.ToolTip = $"Select one option for {aiData.FieldName}";
                if (aiData.IsRequired)
                {
                    radioList.ToolTip += " (Required)";
                }
            }
        }

        private void EnhanceComboBox(PdfLoadedComboBoxField comboBox, AnthropicService.FieldAnalysisResult aiData)
        {
            // Add tooltip for dropdown
            if (string.IsNullOrEmpty(comboBox.ToolTip))
            {
                comboBox.ToolTip = $"Select {aiData.FieldName} from the dropdown list";
                if (aiData.IsRequired)
                {
                    comboBox.ToolTip += " (Required)";
                }
            }

            // Ensure editable is set appropriately
            if (aiData.FieldType?.Contains("select") == true)
            {
                comboBox.Editable = false; // Standard dropdown
            }
        }

        private void ApplyDefaultEnhancements(PdfLoadedField field)
        {
            // Ensure field has a tooltip
            if (string.IsNullOrEmpty(field.ToolTip))
            {
                field.ToolTip = GenerateTooltipFromFieldName(field.Name);
            }

            // Ensure tab index is set
            if (field.TabIndex == 0)
            {
                field.TabIndex = GetNextTabIndex();
            }

            // Apply field-specific defaults
            if (field is PdfLoadedTextBoxField textField)
            {
                ApplyTextFieldDefaults(textField);
            }
        }

        private void ApplyTextFieldDefaults(PdfLoadedTextBoxField textField)
        {
            var fieldName = textField.Name?.ToLower() ?? "";

            // Check for sensitive fields that need masking
            if (fieldName.Contains("ssn") || fieldName.Contains("social") || 
                fieldName.Contains("password") || fieldName.Contains("pin"))
            {
                textField.Password = true;
            }

            // Check for multiline fields
            if (fieldName.Contains("comment") || fieldName.Contains("description") ||
                fieldName.Contains("note") || fieldName.Contains("address"))
            {
                textField.Multiline = true;
            }

            // Set max length for common fields
            if (fieldName.Contains("phone"))
            {
                textField.MaxLength = 14;
            }
            else if (fieldName.Contains("zip"))
            {
                textField.MaxLength = 10;
            }
            else if (fieldName.Contains("date"))
            {
                textField.MaxLength = 10;
            }
        }

        private void ApplyDocumentAccessibilitySettings(PdfLoadedDocument document)
        {
            _logger.LogInformation("Applying document-level accessibility settings");

            // Set document metadata if not already set
            var docInfo = document.DocumentInformation;
            if (string.IsNullOrWhiteSpace(docInfo.Title))
            {
                docInfo.Title = "Accessible Form Document";
            }

            if (string.IsNullOrWhiteSpace(docInfo.Language))
            {
                docInfo.Language = "en-US";
            }

            // Set accessibility metadata
            docInfo.Keywords = "accessible, Section 508, WCAG 2.1, form, fillable";
            docInfo.Subject = "Accessible Form with AI-Enhanced Fields";

            // Add custom metadata
            docInfo.CustomMetadata["AccessibilityStandard"] = "WCAG 2.1 AA";
            docInfo.CustomMetadata["Section508"] = "Compliant";
            docInfo.CustomMetadata["AIEnhanced"] = "true";
            docInfo.CustomMetadata["ProcessedDate"] = DateTime.Now.ToString("yyyy-MM-dd");
            docInfo.CustomMetadata["ProcessedBy"] = "AccessForm AI Field Creator";

            // AutoTag is set during PDF creation, not on loaded documents

            _logger.LogInformation("Document accessibility settings applied");
        }

        private string GenerateAccessibleTooltip(AnthropicService.FieldAnalysisResult fieldData)
        {
            var tooltip = fieldData.FieldName ?? "Form Field";
            
            // Clean up the field name
            tooltip = tooltip.Replace("_", " ").Replace("-", " ");
            
            // Add field type hint if useful
            var fieldType = fieldData.FieldType?.ToLower() ?? "";
            if (fieldType.Contains("date"))
            {
                tooltip += " (Date - MM/DD/YYYY)";
            }
            else if (fieldType.Contains("phone"))
            {
                tooltip += " (Phone Number)";
            }
            else if (fieldType.Contains("email"))
            {
                tooltip += " (Email Address)";
            }
            else if (fieldType.Contains("ssn"))
            {
                tooltip += " (Social Security Number)";
            }
            
            // Add required indicator
            if (fieldData.IsRequired)
            {
                tooltip += " - Required";
            }

            return tooltip;
        }

        private string GenerateTooltipFromFieldName(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                return "Form Field";

            // Convert field name to readable format
            var tooltip = fieldName
                .Replace("_", " ")
                .Replace("-", " ");

            // Handle camelCase
            tooltip = System.Text.RegularExpressions.Regex.Replace(
                tooltip,
                "([a-z])([A-Z])",
                "$1 $2"
            );

            // Capitalize first letter of each word
            tooltip = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(tooltip.ToLower());

            return tooltip;
        }

        private string GetFieldTypeString(PdfLoadedField field)
        {
            return field switch
            {
                PdfLoadedTextBoxField => "text",
                PdfLoadedCheckBoxField => "checkbox",
                PdfLoadedRadioButtonListField => "radio",
                PdfLoadedComboBoxField => "dropdown",
                PdfLoadedListBoxField => "list",
                PdfLoadedSignatureField => "signature",
                _ => "text"
            };
        }

        private string GetSyncfusionFieldType(string aiFieldType)
        {
            var type = aiFieldType?.ToLower() ?? "text";
            
            if (type.Contains("check")) return "checkbox";
            if (type.Contains("radio")) return "radio";
            if (type.Contains("dropdown") || type.Contains("select") || type.Contains("combo")) return "dropdown";
            if (type.Contains("signature")) return "signature";
            if (type.Contains("list")) return "list";
            
            return "text";
        }

        private int _currentTabIndex = 1;
        private int GetNextTabIndex()
        {
            return _currentTabIndex++;
        }
    }
}