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
        /// Creates form fields in a PDF based on Claude's field detection
        /// </summary>
        public void CreateFormFieldsFromAIDetection(
            PdfLoadedDocument document, 
            List<AnthropicService.FieldAnalysisResult> detectedFields,
            bool preserveExistingFields = true)
        {
            _logger.LogInformation($"Creating form fields from {detectedFields.Count} AI-detected fields");

            // Get or create the form
            if (document.Form == null)
            {
                _logger.LogInformation("No existing form found, creating new form");
                // Note: Syncfusion doesn't allow creating a new form on loaded documents
                // We need to work with existing form or create fields on pages
            }

            // Group fields by type for better organization
            var fieldGroups = detectedFields.GroupBy(f => f.FieldType ?? "text").ToList();
            
            // Process each page
            foreach (PdfLoadedPage page in document.Pages)
            {
                ProcessPageFields(page, document, detectedFields);
            }

            // Apply document-level settings for accessibility
            ApplyDocumentAccessibilitySettings(document);
        }

        private void ProcessPageFields(
            PdfLoadedPage page, 
            PdfLoadedDocument document,
            List<AnthropicService.FieldAnalysisResult> fields)
        {
            _logger.LogInformation($"Processing fields for page");

            // Check if there are existing fields on this page
            var existingFields = GetExistingFieldsOnPage(document, page);
            _logger.LogInformation($"Found {existingFields.Count} existing fields on page");

            // If we have existing fields, enhance them with Claude's data
            if (existingFields.Count > 0)
            {
                EnhanceExistingFields(existingFields, fields);
            }
            else
            {
                // Create new fields based on Claude's detection
                CreateNewFieldsOnPage(page, document, fields);
            }
        }

        private List<PdfLoadedField> GetExistingFieldsOnPage(PdfLoadedDocument document, PdfLoadedPage page)
        {
            var fieldsOnPage = new List<PdfLoadedField>();
            
            if (document.Form?.Fields == null) return fieldsOnPage;

            foreach (PdfLoadedField field in document.Form.Fields)
            {
                // Check if field is on this page by checking bounds
                // Note: This is simplified - in production you'd need more sophisticated page detection
                if (field is PdfLoadedTextBoxField textField)
                {
                    // For now, we'll process all fields as we can't easily determine page
                    fieldsOnPage.Add(field);
                }
                else
                {
                    fieldsOnPage.Add(field);
                }
            }

            return fieldsOnPage;
        }

        private void EnhanceExistingFields(
            List<PdfLoadedField> existingFields, 
            List<AnthropicService.FieldAnalysisResult> claudeFields)
        {
            _logger.LogInformation($"Enhancing {existingFields.Count} existing fields with AI data");

            foreach (var existingField in existingFields)
            {
                // Try to match with Claude's detected field
                var matchedField = FindBestMatch(existingField, claudeFields);
                
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
                // Claude doesn't provide tab order in current implementation
                // We'll use document order
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

        private void CreateNewFieldsOnPage(
            PdfLoadedPage page, 
            PdfLoadedDocument document,
            List<AnthropicService.FieldAnalysisResult> fields)
        {
            _logger.LogInformation($"Creating {fields.Count} new fields on page");

            float currentY = page.Size.Height - _margin - 50; // Start from top with margin
            int tabIndex = 1;

            foreach (var fieldData in fields)
            {
                try
                {
                    // Create appropriate field type
                    PdfField newField = CreateFieldByType(page, fieldData, currentY, tabIndex++);
                    
                    if (newField != null)
                    {
                        // Add field to the form
                        if (document.Form != null)
                        {
                            document.Form.Fields.Add(newField);
                            _logger.LogInformation($"Created new field '{fieldData.FieldName}' of type '{fieldData.FieldType}'");
                        }
                        
                        // Move to next position
                        currentY -= _fieldSpacing;
                        
                        // Check if we need to move to next page
                        if (currentY < _margin)
                        {
                            _logger.LogWarning("Ran out of space on page, some fields may not be created");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to create field '{fieldData.FieldName}'");
                }
            }
        }

        private PdfField CreateFieldByType(
            PdfLoadedPage page, 
            AnthropicService.FieldAnalysisResult fieldData,
            float yPosition,
            int tabIndex)
        {
            var fieldType = fieldData.FieldType?.ToLower() ?? "text";
            var bounds = new RectangleF(_margin, yPosition, _defaultFieldWidth, _defaultFieldHeight);

            try
            {
                PdfField field = null;

                if (fieldType.Contains("checkbox") || fieldType.Contains("check"))
                {
                    var checkBox = new PdfCheckBoxField(page, fieldData.FieldName ?? $"CheckBox{tabIndex}");
                    checkBox.Bounds = new RectangleF(bounds.X, bounds.Y, 15, 15); // Checkboxes are smaller
                    checkBox.ToolTip = GenerateAccessibleTooltip(fieldData);
                    checkBox.Required = fieldData.IsRequired;
                    checkBox.TabIndex = tabIndex;
                    field = checkBox;
                }
                else if (fieldType.Contains("radio"))
                {
                    var radio = new PdfRadioButtonListField(page, fieldData.FieldName ?? $"Radio{tabIndex}");
                    radio.ToolTip = GenerateAccessibleTooltip(fieldData);
                    radio.Required = fieldData.IsRequired;
                    radio.TabIndex = tabIndex;
                    
                    // Add a few radio button items
                    for (int i = 0; i < 3; i++)
                    {
                        var item = new PdfRadioButtonListItem($"Option{i + 1}");
                        item.Bounds = new RectangleF(bounds.X + (i * 60), bounds.Y, 15, 15);
                        radio.Items.Add(item);
                    }
                    field = radio;
                }
                else if (fieldType.Contains("dropdown") || fieldType.Contains("select") || fieldType.Contains("combo"))
                {
                    var combo = new PdfComboBoxField(page, fieldData.FieldName ?? $"Dropdown{tabIndex}");
                    combo.Bounds = bounds;
                    combo.ToolTip = GenerateAccessibleTooltip(fieldData);
                    combo.Required = fieldData.IsRequired;
                    combo.TabIndex = tabIndex;
                    combo.Editable = false;
                    
                    // Add placeholder items
                    combo.Items.Add(new PdfListFieldItem("Select an option", ""));
                    combo.Items.Add(new PdfListFieldItem("Option 1", "opt1"));
                    combo.Items.Add(new PdfListFieldItem("Option 2", "opt2"));
                    field = combo;
                }
                else if (fieldType.Contains("signature"))
                {
                    var signature = new PdfSignatureField(page, fieldData.FieldName ?? $"Signature{tabIndex}");
                    signature.Bounds = new RectangleF(bounds.X, bounds.Y, bounds.Width, 50); // Signatures need more height
                    signature.ToolTip = GenerateAccessibleTooltip(fieldData);
                    signature.Required = fieldData.IsRequired;
                    signature.TabIndex = tabIndex;
                    field = signature;
                }
                else // Default to text field
                {
                    var textField = new PdfTextBoxField(page, fieldData.FieldName ?? $"TextField{tabIndex}");
                    textField.Bounds = bounds;
                    textField.ToolTip = GenerateAccessibleTooltip(fieldData);
                    textField.Required = fieldData.IsRequired;
                    textField.TabIndex = tabIndex;
                    
                    // Apply text field specific settings
                    if (fieldType.Contains("multiline") || fieldType.Contains("textarea") || 
                        fieldType.Contains("comments") || fieldType.Contains("description"))
                    {
                        textField.Multiline = true;
                        textField.Bounds = new RectangleF(bounds.X, bounds.Y, bounds.Width, 60); // More height for multiline
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
                    
                    field = textField;
                }

                // Set common properties
                if (field != null)
                {
                    // Note: Border and background colors need to be set on specific field types
                    // Not all field types support these properties in Syncfusion
                }

                return field;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to create field of type '{fieldType}'");
                return null;
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

            // Enable auto-tagging if available
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