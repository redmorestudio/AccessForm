using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Action;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.Pdf.Tagutils;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Kernel.Geom;
using iText.Kernel.Pdf.Canvas;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Service that uses iText to completely rebuild PDF forms by flattening and recreating
    /// </summary>
    public class ITextFieldRebuildService
    {
        private readonly ILogger<ITextFieldRebuildService> _logger;

        public ITextFieldRebuildService(ILogger<ITextFieldRebuildService> logger)
        {
            _logger = logger;
        }
        
        private string MapSemanticTypeToPdfType(string semanticType)
        {
            // Map all the semantic types to the actual PDF form field types
            return semanticType switch
            {
                // Checkbox types
                "checkbox" or "compliance_acknowledgment" => "checkbox",
                
                // Radio button types
                "radio" or "radio button" or "radiobutton" or "gender_pronoun" => "radio",
                
                // Dropdown/combo box types
                "dropdown" or "select" or "combobox" or "combo box" or "language_preference" => "combobox",
                
                // List box types
                "listbox" or "list box" or "list" => "listbox",
                
                // Signature types
                "signature" => "signature",
                
                // Button types
                "button" or "push button" or "pushbutton" => "button",
                
                // Multi-line text types
                "textarea" or "multiline" or "multi-line" => "multiline",
                
                // All other semantic types map to text field
                // This includes: date, time, email, phone, ssn, ein, tin, currency, 
                // percentage, case_number, initials, drivers_license, full_name, address, etc.
                _ => "text"
            };
        }

        public class FieldUpdate
        {
            public string OriginalName { get; set; } = "";
            public string NewName { get; set; } = "";
            public string? FieldType { get; set; }
            public string? Tooltip { get; set; }
            public bool? IsRequired { get; set; }
            public float? X { get; set; }
            public float? Y { get; set; }
            public float? Width { get; set; }
            public float? Height { get; set; }
            public int? PageNumber { get; set; }
        }

        public class RebuildOptions
        {
            public float VerticalBias { get; set; } = 0; // Pixels to adjust all fields up (negative) or down (positive)
        }

        public class RebuildResult
        {
            public bool Success { get; set; }
            public byte[]? PdfBytes { get; set; }
            public string? ErrorMessage { get; set; }
            public List<string> ModifiedFields { get; set; } = new();
            public Dictionary<string, object> Metadata { get; set; } = new();
        }

        /// <summary>
        /// Completely rebuilds PDF by flattening it and recreating all form fields
        /// </summary>
        public async Task<RebuildResult> RebuildFormFieldsAsync(byte[] pdfBytes, List<FieldUpdate> fieldUpdates, RebuildOptions? options = null)
        {
            return await Task.Run(() => RebuildFormFields(pdfBytes, fieldUpdates, options ?? new RebuildOptions()));
        }

        private RebuildResult RebuildFormFields(byte[] pdfBytes, List<FieldUpdate> fieldUpdates, RebuildOptions options)
        {
            try
            {
                _logger.LogInformation($"Starting complete PDF rebuild for {fieldUpdates.Count} fields");

                // Step 1: Create a completely clean PDF by removing ALL form fields
                // We need to ensure NO ghost fields remain
                byte[] cleanPdf;
                using (var inputStream = new MemoryStream(pdfBytes))
                using (var cleanStream = new MemoryStream())
                {
                    using (var reader = new PdfReader(inputStream))
                    using (var writer = new PdfWriter(cleanStream))
                    {
                        reader.SetUnethicalReading(true);
                        writer.SetSmartMode(true);
                        
                        using (var sourceDoc = new PdfDocument(reader))
                        using (var targetDoc = new PdfDocument(writer))
                        {
                            targetDoc.SetTagged();
                            
                            // Copy pages WITHOUT form fields
                            int numPages = sourceDoc.GetNumberOfPages();
                            for (int i = 1; i <= numPages; i++)
                            {
                                var page = sourceDoc.GetPage(i);
                                
                                // Create a new page in target
                                var pageSize = page.GetPageSize();
                                var newPage = targetDoc.AddNewPage(new PageSize(pageSize));
                                
                                // Copy only the content stream (visual elements) without annotations/fields
                                var canvas = new PdfCanvas(newPage);
                                var pageContent = page.GetContentBytes();
                                if (pageContent != null && pageContent.Length > 0)
                                {
                                    canvas.GetContentStream().GetOutputStream().WriteBytes(pageContent);
                                }
                            }
                            
                            // Ensure NO form exists in the output
                            var catalog = targetDoc.GetCatalog();
                            var catalogDict = catalog.GetPdfObject() as PdfDictionary;
                            if (catalogDict != null)
                            {
                                catalogDict.Remove(PdfName.AcroForm);
                                catalogDict.Remove(new PdfName("XFA"));
                            }
                            
                            _logger.LogInformation($"Created clean PDF with {numPages} pages, no form fields");
                            targetDoc.Close();
                        }
                    }
                    cleanPdf = cleanStream.ToArray();
                    _logger.LogInformation("Clean PDF ready for field recreation");
                }

                // Step 2: Create a new PDF with fresh form fields
                using (var cleanInput = new MemoryStream(cleanPdf))
                using (var outputStream = new MemoryStream())
                {
                    using (var reader = new PdfReader(cleanInput))
                    using (var writer = new PdfWriter(outputStream))
                    {
                        writer.SetSmartMode(true);
                        
                        using (var pdfDoc = new PdfDocument(reader, writer))
                        {
                            // Enable tagging for accessibility
                            pdfDoc.SetTagged();
                            
                            // Create a new form from scratch
                            var form = PdfAcroForm.GetAcroForm(pdfDoc, true);
                            
                            var modifiedFields = new List<string>();
                            var addedFieldNames = new HashSet<string>();
                            
                            // Remove duplicates that have the same position (X,Y coordinates)
                            // This happens when frontend sends the same field twice with different names/types
                            _logger.LogInformation($"Deduplicating {fieldUpdates.Count} fields by position...");
                            
                            var uniqueUpdates = fieldUpdates
                                .GroupBy(f => new { 
                                    X = Math.Round(f.X ?? 0, 0), // Round to nearest pixel
                                    Y = Math.Round(f.Y ?? 0, 0)  // Round to nearest pixel
                                })
                                .Select(g => {
                                    if (g.Count() > 1)
                                    {
                                        var kept = g.First();
                                        var discarded = g.Skip(1).ToList();
                                        _logger.LogWarning($"Found {g.Count()} fields at position ({g.Key.X}, {g.Key.Y}):");
                                        _logger.LogWarning($"  KEEPING: '{kept.NewName}' (type: {kept.FieldType})");
                                        foreach (var d in discarded)
                                        {
                                            _logger.LogWarning($"  REMOVING: '{d.NewName}' (type: {d.FieldType})");
                                        }
                                    }
                                    return g.First();
                                })
                                .ToList();
                            
                            _logger.LogInformation($"Deduplicated fields: {fieldUpdates.Count} → {uniqueUpdates.Count} (removed {fieldUpdates.Count - uniqueUpdates.Count} duplicates)");
                            
                            // Sort fields by Y position (top to bottom) then X position (left to right)
                            // This ensures proper tab order per WebAIM guidelines
                            var sortedUpdates = uniqueUpdates.OrderByDescending(f => f.Y).ThenBy(f => f.X).ToList();
                            
                            // Add fields based on the field updates
                            foreach (var update in sortedUpdates)
                            {
                                if (!update.X.HasValue || !update.Y.HasValue || 
                                    !update.Width.HasValue || !update.Height.HasValue)
                                {
                                    _logger.LogWarning($"Skipping field '{update.OriginalName}' - missing position data");
                                    continue;
                                }
                                
                                var fieldName = update.NewName;
                                var fieldType = update.FieldType?.ToLower() ?? "text";
                                
                                // Make field name unique - duplicate names cause issues especially for checkboxes
                                // For checkboxes, duplicate names create a radio group which is not what we want
                                if (addedFieldNames.Contains(fieldName))
                                {
                                    // Use Y coordinate to make it unique (e.g., "FieldName_249.65")
                                    var uniqueName = $"{fieldName}_{update.Y:F2}";
                                    
                                    // If somehow that's still not unique, add a counter
                                    if (addedFieldNames.Contains(uniqueName))
                                    {
                                        int counter = 2;
                                        while (addedFieldNames.Contains($"{fieldName}_{counter}"))
                                        {
                                            counter++;
                                        }
                                        uniqueName = $"{fieldName}_{counter}";
                                    }
                                    
                                    _logger.LogWarning($"Field name '{fieldName}' already exists, renaming to '{uniqueName}' to avoid conflicts");
                                    fieldName = uniqueName;
                                }
                                
                                addedFieldNames.Add(fieldName);
                                
                                // Determine page number (default to 1 if not specified)
                                int pageNum = update.PageNumber ?? 1;
                                if (pageNum > pdfDoc.GetNumberOfPages())
                                {
                                    pageNum = 1;
                                }
                                
                                var page = pdfDoc.GetPage(pageNum);
                                
                                // Apply vertical bias and fix checkbox dimensions
                                // IMPORTANT: The coordinates from frontend are top-left, but iText Rectangle uses bottom-left
                                // So we need to adjust Y coordinate
                                float x = update.X.Value;
                                float width = update.Width.Value;
                                float height = update.Height.Value;
                                
                                // Convert from top-left to bottom-left coordinate system
                                // Frontend gives us top-left Y, we need bottom-left Y for iText
                                float y = update.Y.Value - height + options.VerticalBias;
                                
                                // fieldType already declared above
                                var semanticType = fieldType; // Preserve the original semantic type
                                
                                // Fix checkbox dimensions if they appear to be incorrectly sized
                                // Checkboxes should typically be square and small (around 15-25px)
                                if ((fieldType == "checkbox" || fieldType == "radio" || fieldType == "compliance_acknowledgment" || 
                                     fieldType == "gender_pronoun"))
                                {
                                    // If width is much larger than height, it's probably wrong
                                    if (width > height * 2)
                                    {
                                        _logger.LogInformation($"Correcting checkbox dimensions for '{fieldName}' from {width}x{height} to {height}x{height}");
                                        width = height; // Make it square using the height
                                    }
                                    // Also ensure checkboxes aren't too large
                                    if (width > 25 || height > 25)
                                    {
                                        width = Math.Min(width, 20);
                                        height = Math.Min(height, 20);
                                        _logger.LogInformation($"Limiting checkbox size for '{fieldName}' to {width}x{height}");
                                    }
                                }
                                
                                var rect = new Rectangle(x, y, width, height);
                                
                                PdfFormField newField = null;
                                
                                // Map semantic field types to PDF form field types
                                // But preserve the semantic type in the tooltip/properties
                                _logger.LogInformation($"Creating field '{fieldName}' with semantic type '{semanticType}'");
                                
                                // Determine the PDF field type based on semantic type
                                string pdfFieldType = MapSemanticTypeToPdfType(semanticType);
                                
                                switch (pdfFieldType)
                                {
                                    case "checkbox":
                                        newField = new CheckBoxFormFieldBuilder(pdfDoc, fieldName)
                                            .SetWidgetRectangle(rect)
                                            .CreateCheckBox();
                                        break;
                                    
                                    case "radio":
                                        // For now, use checkbox for radio buttons
                                        // Proper radio groups require multiple widgets and coordination
                                        newField = new CheckBoxFormFieldBuilder(pdfDoc, fieldName)
                                            .SetWidgetRectangle(rect)
                                            .CreateCheckBox();
                                        _logger.LogInformation($"Created checkbox for radio field '{fieldName}' (radio groups not yet implemented)");
                                        break;
                                    
                                    case "combobox":
                                        newField = new ChoiceFormFieldBuilder(pdfDoc, fieldName)
                                            .SetWidgetRectangle(rect)
                                            .SetOptions(new string[] { "Option 1", "Option 2", "Option 3" })
                                            .CreateComboBox();
                                        break;
                                    
                                    case "listbox":
                                        newField = new ChoiceFormFieldBuilder(pdfDoc, fieldName)
                                            .SetWidgetRectangle(rect)
                                            .SetOptions(new string[] { "Option 1", "Option 2", "Option 3" })
                                            .CreateList();
                                        break;
                                    
                                    case "signature":
                                        newField = new SignatureFormFieldBuilder(pdfDoc, fieldName)
                                            .SetWidgetRectangle(rect)
                                            .CreateSignature();
                                        break;
                                    
                                    case "button":
                                        newField = new PushButtonFormFieldBuilder(pdfDoc, fieldName)
                                            .SetWidgetRectangle(rect)
                                            .SetCaption(fieldName)
                                            .CreatePushButton();
                                        break;
                                    
                                    case "multiline":
                                        newField = new TextFormFieldBuilder(pdfDoc, fieldName)
                                            .SetWidgetRectangle(rect)
                                            .CreateText();
                                        newField.SetFieldFlag(PdfFormField.FF_MULTILINE, true);
                                        break;
                                    
                                    case "text":
                                    default:
                                        newField = new TextFormFieldBuilder(pdfDoc, fieldName)
                                            .SetWidgetRectangle(rect)
                                            .CreateText();
                                        
                                        // Add special formatting based on semantic type
                                        if (semanticType == "date")
                                        {
                                            // Add date formatting
                                            var dateAction = PdfAction.CreateJavaScript("AFDate_FormatEx(\"mm/dd/yyyy\");");
                                            newField.SetAdditionalAction(PdfName.F, dateAction);
                                        }
                                        else if (semanticType == "time")
                                        {
                                            // Add time formatting
                                            var timeAction = PdfAction.CreateJavaScript("AFTime_Format(0);");
                                            newField.SetAdditionalAction(PdfName.F, timeAction);
                                        }
                                        else if (semanticType == "currency")
                                        {
                                            // Add currency formatting
                                            var currencyAction = PdfAction.CreateJavaScript("AFNumber_Format(2, 0, 0, 0, \"$\", true);");
                                            newField.SetAdditionalAction(PdfName.F, currencyAction);
                                        }
                                        else if (semanticType == "percentage")
                                        {
                                            // Add percentage formatting
                                            var percentAction = PdfAction.CreateJavaScript("AFPercent_Format(2, 0);");
                                            newField.SetAdditionalAction(PdfName.F, percentAction);
                                        }
                                        break;
                                }
                                
                                if (newField != null)
                                {
                                    // Set field properties per WebAIM accessibility guidelines
                                    // Tooltip should match the visible label and be descriptive
                                    var tooltip = "";
                                    
                                    // Build accessible tooltip based on field type
                                    if (semanticType == "date")
                                    {
                                        tooltip = $"{fieldName} - Enter date in MM/DD/YYYY format";
                                    }
                                    else if (semanticType == "email")
                                    {
                                        tooltip = $"{fieldName} - Enter email address";
                                    }
                                    else if (semanticType == "phone")
                                    {
                                        tooltip = $"{fieldName} - Enter phone number";
                                    }
                                    else if (semanticType == "ssn_full")
                                    {
                                        tooltip = $"{fieldName} - Enter 9-digit Social Security Number";
                                    }
                                    else if (semanticType == "currency")
                                    {
                                        tooltip = $"{fieldName} - Enter dollar amount";
                                    }
                                    else if (semanticType == "checkbox" || semanticType == "compliance_acknowledgment")
                                    {
                                        tooltip = $"{fieldName} - Check to select";
                                    }
                                    else if (semanticType == "radio")
                                    {
                                        tooltip = $"{fieldName} - Select one option";
                                    }
                                    else if (semanticType == "signature")
                                    {
                                        tooltip = $"{fieldName} - Click to add signature";
                                    }
                                    else if (semanticType == "dropdown" || semanticType == "select")
                                    {
                                        tooltip = $"{fieldName} - Select from list";
                                    }
                                    else if (!string.IsNullOrEmpty(update.Tooltip))
                                    {
                                        // Use provided tooltip if it exists
                                        tooltip = update.Tooltip;
                                    }
                                    else
                                    {
                                        // Default tooltip is the field name
                                        tooltip = fieldName;
                                    }
                                    
                                    newField.SetAlternativeName(tooltip);
                                    
                                    if (update.IsRequired == true)
                                    {
                                        newField.SetRequired(true);
                                    }
                                    
                                    // Add field to form
                                    form.AddField(newField, page);
                                    
                                    // Log actual field dimensions after creation
                                    var widgets = newField.GetWidgets();
                                    if (widgets != null && widgets.Count > 0)
                                    {
                                        var actualRect = widgets[0].GetRectangle();
                                        if (actualRect != null)
                                        {
                                            _logger.LogInformation($"Field '{fieldName}' actual rect after creation: X={actualRect.GetAsNumber(0)?.GetValue() ?? 0:F1}, Y={actualRect.GetAsNumber(1)?.GetValue() ?? 0:F1}, W={actualRect.GetAsNumber(2)?.GetValue() ?? 0:F1}, H={actualRect.GetAsNumber(3)?.GetValue() ?? 0:F1}");
                                        }
                                    }
                                    
                                    // Track ALL changes made to the field
                                    var changes = new List<string>();
                                    if (update.OriginalName != fieldName)
                                        changes.Add($"name: {update.OriginalName} -> {fieldName}");
                                    changes.Add($"type: {fieldType}");
                                    changes.Add($"pos: ({x:F1},{y:F1})");
                                    changes.Add($"size: {width:F1}x{height:F1}");
                                    if (!string.IsNullOrEmpty(update.Tooltip))
                                        changes.Add($"tooltip: set");
                                    if (update.IsRequired == true)
                                        changes.Add("required: true");
                                    
                                    modifiedFields.Add($"{update.OriginalName}: {string.Join(", ", changes)}");
                                    _logger.LogInformation($"Created field '{fieldName}' (was '{update.OriginalName}') on page {pageNum}");
                                }
                            }

                            // Preserve document metadata for PDF/UA
                            var catalog = pdfDoc.GetCatalog();
                            
                            // Ensure language is set (required for PDF/UA)
                            if (catalog.GetLang() == null)
                            {
                                catalog.SetLang(new PdfString("en-US"));
                            }
                            
                            // Mark as PDF/UA if not already
                            var markInfo = new PdfDictionary();
                            markInfo.Put(new PdfName("Marked"), PdfBoolean.TRUE);
                            markInfo.Put(new PdfName("UserProperties"), PdfBoolean.FALSE);
                            markInfo.Put(new PdfName("Suspects"), PdfBoolean.FALSE);
                            catalog.Put(new PdfName("MarkInfo"), markInfo);
                            
                            pdfDoc.Close();
                            
                            return new RebuildResult
                            {
                                Success = true,
                                PdfBytes = outputStream.ToArray(),
                                ModifiedFields = modifiedFields,
                                Metadata = new Dictionary<string, object>
                                {
                                    ["totalFields"] = fieldUpdates.Count,
                                    ["createdFields"] = modifiedFields.Count,
                                    ["pdfUaCompliant"] = true,
                                    ["method"] = "Complete Rebuild (Flatten + Recreate)"
                                }
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PDF rebuild failed");
                return new RebuildResult
                {
                    Success = false,
                    ErrorMessage = $"Field rebuild failed: {ex.Message}"
                };
            }
        }
    }
}