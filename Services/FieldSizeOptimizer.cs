using System;
using Microsoft.Extensions.Logging;
using Syncfusion.Pdf.Parsing;
using Syncfusion.Pdf.Interactive;
using Syncfusion.Drawing;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Service to optimize form field sizes so they properly fit their containers
    /// </summary>
    public class FieldSizeOptimizer
    {
        private readonly ILogger<FieldSizeOptimizer> _logger;
        
        // Minimum sizes for usable form fields
        private const float MIN_TEXT_FIELD_WIDTH = 150f;
        private const float MIN_TEXT_FIELD_HEIGHT = 18f;
        private const float MIN_CHECKBOX_SIZE = 12f;
        private const float OPTIMAL_TEXT_FIELD_HEIGHT = 22f;

        public FieldSizeOptimizer(ILogger<FieldSizeOptimizer> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Optimizes form field sizes in a loaded PDF document
        /// </summary>
        public void OptimizeFieldSizes(PdfLoadedDocument document)
        {
            if (document.Form == null || document.Form.Fields.Count == 0)
            {
                _logger.LogInformation("No form fields to optimize");
                return;
            }

            _logger.LogInformation($"Optimizing sizes for {document.Form.Fields.Count} form fields");

            int optimizedCount = 0;
            foreach (PdfLoadedField field in document.Form.Fields)
            {
                try
                {
                    if (OptimizeField(field))
                    {
                        optimizedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not optimize field '{field.Name}': {ex.Message}");
                }
            }

            _logger.LogInformation($"Optimized {optimizedCount} of {document.Form.Fields.Count} form fields");
        }

        private bool OptimizeField(PdfLoadedField field)
        {
            bool wasOptimized = false;
            
            switch (field)
            {
                case PdfLoadedTextBoxField textField:
                    wasOptimized = OptimizeTextField(textField);
                    break;
                    
                case PdfLoadedCheckBoxField checkBox:
                    wasOptimized = OptimizeCheckBox(checkBox);
                    break;
                    
                case PdfLoadedRadioButtonListField radioList:
                    wasOptimized = OptimizeRadioList(radioList);
                    break;
                    
                case PdfLoadedComboBoxField comboBox:
                    wasOptimized = OptimizeComboBox(comboBox);
                    break;
                    
                case PdfLoadedListBoxField listBox:
                    wasOptimized = OptimizeListBox(listBox);
                    break;
            }

            return wasOptimized;
        }

        private bool OptimizeTextField(PdfLoadedTextBoxField textField)
        {
            var bounds = textField.Bounds;
            var originalBounds = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            bool changed = false;

            // Fix width if too narrow
            if (bounds.Width < MIN_TEXT_FIELD_WIDTH)
            {
                // Try to expand to the right
                // Look for reasonable width based on field name
                float targetWidth = DetermineOptimalWidth(textField);
                bounds.Width = Math.Max(MIN_TEXT_FIELD_WIDTH, targetWidth);
                changed = true;
            }

            // Fix height if too small
            if (bounds.Height < MIN_TEXT_FIELD_HEIGHT)
            {
                bounds.Height = OPTIMAL_TEXT_FIELD_HEIGHT;
                changed = true;
            }

            // For multiline fields, ensure adequate height
            if (textField.Multiline && bounds.Height < 50)
            {
                bounds.Height = 60;
                changed = true;
            }

            if (changed)
            {
                textField.Bounds = bounds;
                _logger.LogInformation($"Optimized text field '{textField.Name}': " +
                    $"({originalBounds.Width:F1}x{originalBounds.Height:F1}) -> " +
                    $"({bounds.Width:F1}x{bounds.Height:F1})");
            }

            return changed;
        }

        private bool OptimizeCheckBox(PdfLoadedCheckBoxField checkBox)
        {
            var bounds = checkBox.Bounds;
            bool changed = false;

            // Ensure minimum size for checkboxes
            if (bounds.Width < MIN_CHECKBOX_SIZE || bounds.Height < MIN_CHECKBOX_SIZE)
            {
                bounds.Width = Math.Max(MIN_CHECKBOX_SIZE, bounds.Width);
                bounds.Height = Math.Max(MIN_CHECKBOX_SIZE, bounds.Height);
                
                // Keep it square
                float size = Math.Max(bounds.Width, bounds.Height);
                bounds.Width = size;
                bounds.Height = size;
                
                checkBox.Bounds = bounds;
                changed = true;
                _logger.LogInformation($"Optimized checkbox '{checkBox.Name}' to {size:F1}x{size:F1}");
            }

            return changed;
        }

        private bool OptimizeRadioList(PdfLoadedRadioButtonListField radioList)
        {
            bool changed = false;
            
            foreach (PdfLoadedRadioButtonItem item in radioList.Items)
            {
                var bounds = item.Bounds;
                
                if (bounds.Width < MIN_CHECKBOX_SIZE || bounds.Height < MIN_CHECKBOX_SIZE)
                {
                    float size = Math.Max(MIN_CHECKBOX_SIZE, Math.Max(bounds.Width, bounds.Height));
                    bounds.Width = size;
                    bounds.Height = size;
                    item.Bounds = bounds;
                    changed = true;
                }
            }

            if (changed)
            {
                _logger.LogInformation($"Optimized radio button list '{radioList.Name}'");
            }

            return changed;
        }

        private bool OptimizeComboBox(PdfLoadedComboBoxField comboBox)
        {
            var bounds = comboBox.Bounds;
            var originalBounds = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            bool changed = false;

            // Combo boxes need reasonable width and height
            if (bounds.Width < MIN_TEXT_FIELD_WIDTH)
            {
                bounds.Width = MIN_TEXT_FIELD_WIDTH * 1.2f; // Slightly wider for dropdowns
                changed = true;
            }

            if (bounds.Height < OPTIMAL_TEXT_FIELD_HEIGHT)
            {
                bounds.Height = OPTIMAL_TEXT_FIELD_HEIGHT;
                changed = true;
            }

            if (changed)
            {
                comboBox.Bounds = bounds;
                _logger.LogInformation($"Optimized combo box '{comboBox.Name}': " +
                    $"({originalBounds.Width:F1}x{originalBounds.Height:F1}) -> " +
                    $"({bounds.Width:F1}x{bounds.Height:F1})");
            }

            return changed;
        }

        private bool OptimizeListBox(PdfLoadedListBoxField listBox)
        {
            var bounds = listBox.Bounds;
            bool changed = false;

            // List boxes need more height to show multiple items
            if (bounds.Height < 60)
            {
                bounds.Height = 80;
                changed = true;
            }

            if (bounds.Width < MIN_TEXT_FIELD_WIDTH)
            {
                bounds.Width = MIN_TEXT_FIELD_WIDTH * 1.5f;
                changed = true;
            }

            if (changed)
            {
                listBox.Bounds = bounds;
                _logger.LogInformation($"Optimized list box '{listBox.Name}'");
            }

            return changed;
        }

        private float DetermineOptimalWidth(PdfLoadedTextBoxField textField)
        {
            var fieldName = textField.Name?.ToLower() ?? "";

            // Determine width based on field type/name
            if (fieldName.Contains("email") || fieldName.Contains("address"))
            {
                return 250f;
            }
            else if (fieldName.Contains("name") || fieldName.Contains("company"))
            {
                return 200f;
            }
            else if (fieldName.Contains("phone"))
            {
                return 150f;
            }
            else if (fieldName.Contains("date"))
            {
                return 100f;
            }
            else if (fieldName.Contains("ssn") || fieldName.Contains("tin") || fieldName.Contains("ein"))
            {
                return 120f;
            }
            else if (fieldName.Contains("zip"))
            {
                return 80f;
            }
            else if (fieldName.Contains("state"))
            {
                return 60f;
            }
            else if (textField.Multiline || fieldName.Contains("description") || fieldName.Contains("comment"))
            {
                return 300f;
            }

            // Default reasonable width
            return MIN_TEXT_FIELD_WIDTH;
        }
    }
}