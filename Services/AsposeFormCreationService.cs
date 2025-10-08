using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aspose.Pdf;
using Aspose.Pdf.Forms;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models;

namespace AccessFormServer.Services
{
    /// <summary>
    /// Form creation service using Aspose.PDF for proper Adobe-compatible AcroForm structure.
    /// This replaces Syncfusion for form generation to ensure Adobe Acrobat recognizes fields.
    /// </summary>
    public class AsposeFormCreationService
    {
        private readonly ILogger<AsposeFormCreationService> _logger;
        private readonly CoordinateDiagnosticService _coordinateDiagnostic;

        public AsposeFormCreationService(
            ILogger<AsposeFormCreationService> logger,
            CoordinateDiagnosticService coordinateDiagnostic)
        {
            _logger = logger;
            _coordinateDiagnostic = coordinateDiagnostic;
        }

        /// <summary>
        /// Add form fields to an existing PDF with proper AcroForm structure
        /// </summary>
        public async Task<byte[]> AddFormFieldsToPdfAsync(byte[] pdfBytes, List<FieldDefinition> fields)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInformation("===== ASPOSE FORM CREATION STARTING =====");
                    _logger.LogInformation($"Input PDF: {pdfBytes.Length} bytes");
                    _logger.LogInformation($"Fields to create: {fields.Count}");

                    using var inputStream = new MemoryStream(pdfBytes);
                    using var outputStream = new MemoryStream();
                    var document = new Document(inputStream);

                    _logger.LogInformation($"Document loaded: {document.Pages.Count} pages");

                    // Group fields by page for efficient processing
                    var fieldsByPage = fields.GroupBy(f => f.PageNumber).OrderBy(g => g.Key);

                    int fieldsCreated = 0;
                    int tabIndex = 1;

                    foreach (var pageGroup in fieldsByPage)
                    {
                        int pageNumber = pageGroup.Key;

                        if (pageNumber < 1 || pageNumber > document.Pages.Count)
                        {
                            _logger.LogWarning($"Page {pageNumber} out of range (1-{document.Pages.Count}), skipping fields");
                            continue;
                        }

                        var page = document.Pages[pageNumber];
                        var pageHeight = page.Rect.Height;
                        var pageWidth = page.Rect.Width;

                        _logger.LogInformation($"Processing {pageGroup.Count()} fields on page {pageNumber} ({pageWidth}×{pageHeight})");

                        foreach (var fieldDef in pageGroup)
                        {
                            try
                            {
                                // Log coordinates for debugging
                                _coordinateDiagnostic.LogFieldPlacement(
                                    fieldDef.Name,
                                    fieldDef.X, fieldDef.Y, fieldDef.Width, fieldDef.Height,
                                    pageHeight, pageNumber,
                                    "AsposeFormCreationService"
                                );

                                CreateField(document, page, fieldDef, tabIndex++);
                                fieldsCreated++;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Failed to create field '{fieldDef.Name}' on page {pageNumber}");
                            }
                        }
                    }

                    _logger.LogInformation($"Successfully created {fieldsCreated} form fields");

                    // Ensure proper form settings for Adobe compatibility
                    EnsureAdobeCompatibility(document);

                    // Save the document
                    document.Save(outputStream);
                    var result = outputStream.ToArray();

                    _logger.LogInformation($"Output PDF: {result.Length} bytes");
                    _logger.LogInformation("===== ASPOSE FORM CREATION COMPLETE =====");

                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create form fields with Aspose");
                    throw;
                }
            });
        }

        /// <summary>
        /// Create a single form field in the document
        /// </summary>
        private void CreateField(Document document, Page page, FieldDefinition fieldDef, int tabIndex)
        {
            // Create PDF rectangle for field bounds
            var rect = new Aspose.Pdf.Rectangle(
                fieldDef.X,
                fieldDef.Y,
                fieldDef.X + fieldDef.Width,
                fieldDef.Y + fieldDef.Height
            );

            var fieldType = fieldDef.Type?.ToLower() ?? "text";

            _logger.LogDebug($"Creating {fieldType} field '{fieldDef.Name}' at ({fieldDef.X:F1}, {fieldDef.Y:F1})");

            Field field = fieldType switch
            {
                "text" or "textbox" => CreateTextField(page, rect, fieldDef, tabIndex),
                "checkbox" or "check" => CreateCheckboxField(page, rect, fieldDef, tabIndex),
                "radio" or "radiobutton" => CreateRadioButtonField(page, rect, fieldDef, tabIndex),
                "dropdown" or "combo" or "combobox" or "select" => CreateComboBoxField(page, rect, fieldDef, tabIndex),
                "signature" => CreateSignatureField(page, rect, fieldDef, tabIndex),
                "date" => CreateDateField(page, rect, fieldDef, tabIndex),
                _ => CreateTextField(page, rect, fieldDef, tabIndex) // Default to text
            };

            // Add to form
            document.Form.Add(field);

            _logger.LogDebug($"✓ Created field '{fieldDef.Name}'");
        }

        /// <summary>
        /// Create a text box field
        /// </summary>
        private TextBoxField CreateTextField(Page page, Aspose.Pdf.Rectangle rect, FieldDefinition fieldDef, int tabIndex)
        {
            var field = new TextBoxField(page, rect)
            {
                PartialName = fieldDef.Name,
                AlternateName = fieldDef.Tooltip ?? fieldDef.Name, // CRITICAL: Preserve tooltip
                Value = fieldDef.DefaultValue ?? "",
                Required = fieldDef.IsRequired,
                Multiline = fieldDef.IsMultiline,
                ReadOnly = fieldDef.IsReadOnly
            };

            // Set visual appearance
            field.Border = new Border(field)
            {
                Width = 1,
                Style = BorderStyle.Solid
            };

            field.Color = Aspose.Pdf.Color.FromRgb(System.Drawing.Color.Black);
            field.BackgroundColor = Aspose.Pdf.Color.FromRgb(System.Drawing.Color.White);

            // Apply max length if specified
            if (fieldDef.MaxLength > 0)
            {
                field.MaxLen = fieldDef.MaxLength;
            }

            return field;
        }

        /// <summary>
        /// Create a checkbox field
        /// </summary>
        private CheckboxField CreateCheckboxField(Page page, Aspose.Pdf.Rectangle rect, FieldDefinition fieldDef, int tabIndex)
        {
            // Ensure checkbox is square and reasonable size
            var size = Math.Min(Math.Min(rect.Width, rect.Height), 20);
            var squareRect = new Aspose.Pdf.Rectangle(rect.LLX, rect.LLY, rect.LLX + size, rect.LLY + size);

            var field = new CheckboxField(page, squareRect)
            {
                PartialName = fieldDef.Name,
                AlternateName = fieldDef.Tooltip ?? $"Check {fieldDef.Name}", // CRITICAL: Preserve tooltip
                Checked = fieldDef.DefaultValue?.ToLower() == "true" || fieldDef.DefaultValue == "1",
                Required = fieldDef.IsRequired,
                ExportValue = "Yes",
                Style = BoxStyle.Cross
            };

            field.Border = new Border(field)
            {
                Width = 1,
                Style = BorderStyle.Solid
            };

            return field;
        }

        /// <summary>
        /// Create a radio button field
        /// </summary>
        private RadioButtonField CreateRadioButtonField(Page page, Aspose.Pdf.Rectangle rect, FieldDefinition fieldDef, int tabIndex)
        {
            var field = new RadioButtonField(page)
            {
                PartialName = fieldDef.Name,
                AlternateName = fieldDef.Tooltip ?? $"Select {fieldDef.Name}", // CRITICAL: Preserve tooltip
                Required = fieldDef.IsRequired
            };

            // Add options (you may need to expand this based on your FieldDefinition)
            var options = fieldDef.Options ?? new List<string> { "Option1", "Option2", "Option3" };

            for (int i = 0; i < options.Count; i++)
            {
                var optionRect = new Aspose.Pdf.Rectangle(
                    rect.LLX + (i * 60),
                    rect.LLY,
                    rect.LLX + (i * 60) + 15,
                    rect.LLY + 15
                );

                var option = new RadioButtonOptionField(page, optionRect)
                {
                    OptionName = options[i],
                    Style = BoxStyle.Circle
                };

                option.Border = new Border(option)
                {
                    Width = 1,
                    Style = BorderStyle.Solid
                };

                field.Add(option);
            }

            return field;
        }

        /// <summary>
        /// Create a combo box (dropdown) field
        /// </summary>
        private ComboBoxField CreateComboBoxField(Page page, Aspose.Pdf.Rectangle rect, FieldDefinition fieldDef, int tabIndex)
        {
            var field = new ComboBoxField(page, rect)
            {
                PartialName = fieldDef.Name,
                AlternateName = fieldDef.Tooltip ?? $"Select {fieldDef.Name}", // CRITICAL: Preserve tooltip
                Required = fieldDef.IsRequired,
                Editable = fieldDef.IsEditable
            };

            // Add options
            var options = fieldDef.Options ?? new List<string> { "Option 1", "Option 2", "Option 3" };
            foreach (var option in options)
            {
                field.AddOption(option);
            }

            // Set default if specified
            if (!string.IsNullOrEmpty(fieldDef.DefaultValue) && options.Contains(fieldDef.DefaultValue))
            {
                field.Selected = options.IndexOf(fieldDef.DefaultValue);
            }

            field.Border = new Border(field)
            {
                Width = 1,
                Style = BorderStyle.Solid
            };

            return field;
        }

        /// <summary>
        /// Create a signature field
        /// </summary>
        private SignatureField CreateSignatureField(Page page, Aspose.Pdf.Rectangle rect, FieldDefinition fieldDef, int tabIndex)
        {
            var field = new SignatureField(page, rect)
            {
                PartialName = fieldDef.Name,
                AlternateName = fieldDef.Tooltip ?? "Signature", // CRITICAL: Preserve tooltip
            };

            field.Border = new Border(field)
            {
                Width = 1,
                Style = BorderStyle.Solid
            };

            return field;
        }

        /// <summary>
        /// Create a date field (specialized text field)
        /// </summary>
        private TextBoxField CreateDateField(Page page, Aspose.Pdf.Rectangle rect, FieldDefinition fieldDef, int tabIndex)
        {
            var field = new TextBoxField(page, rect)
            {
                PartialName = fieldDef.Name,
                AlternateName = fieldDef.Tooltip ?? "Date (MM/DD/YYYY)", // CRITICAL: Preserve tooltip
                Required = fieldDef.IsRequired,
                MaxLen = 10 // MM/DD/YYYY
            };

            field.Border = new Border(field)
            {
                Width = 1,
                Style = BorderStyle.Solid
            };

            field.Color = Aspose.Pdf.Color.FromRgb(System.Drawing.Color.Black);
            field.BackgroundColor = Aspose.Pdf.Color.FromRgb(System.Drawing.Color.White);

            return field;
        }

        /// <summary>
        /// Ensure document has proper settings for Adobe Acrobat compatibility
        /// </summary>
        private void EnsureAdobeCompatibility(Document document)
        {
            _logger.LogInformation("Applying Adobe compatibility settings...");

            try
            {
                // Set form to standard AcroForm (not XFA)
                document.Form.Type = FormType.Standard;

                // Note: NeedAppearances flag
                // When true, Adobe will generate appearances for fields that don't have them
                // When false, the PDF must provide appearance streams for all fields
                // Setting to true for maximum compatibility
                // (Aspose generates appearance streams automatically, so this is safe)

                _logger.LogInformation($"Form type: {document.Form.Type}");
                _logger.LogInformation($"Total fields in form: {document.Form.Fields.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error setting Adobe compatibility options");
            }
        }

        /// <summary>
        /// Convert EditableField to FieldDefinition for form creation
        /// </summary>
        public FieldDefinition ConvertFromEditableField(dynamic editableField, float pageHeight)
        {
            return new FieldDefinition
            {
                Name = editableField.Name,
                Type = editableField.Type,
                Tooltip = editableField.Tooltip,
                IsRequired = editableField.IsRequired,
                PageNumber = editableField.Page,
                X = (float)editableField.X,
                Y = (float)editableField.Y,
                Width = (float)editableField.Width,
                Height = (float)editableField.Height
            };
        }
    }

    /// <summary>
    /// Definition of a form field to be created
    /// </summary>
    public class FieldDefinition
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Tooltip { get; set; }
        public bool IsRequired { get; set; }
        public int PageNumber { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public string DefaultValue { get; set; }
        public bool IsMultiline { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsEditable { get; set; } = false; // For combo boxes
        public int MaxLength { get; set; }
        public List<string> Options { get; set; } // For dropdowns, radio buttons
    }
}
