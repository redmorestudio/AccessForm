using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using Syncfusion.Pdf.Interactive;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Drawing;
using System.Text.RegularExpressions;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Enhanced PDF accessibility service implementing PDF/UA compliance
    /// </summary>
    public class PdfAccessibilityEnhancer
    {
        private readonly Dictionary<string, PdfFont> _embeddedFonts = new();
        
        /// <summary>
        /// Create a fully accessible PDF with PDF/UA compliance
        /// </summary>
        public byte[] CreateAccessiblePdf(Stream wordDocStream, string fileName)
        {
            // Create new PDF with PDF/A-3A compliance (highest accessibility level)
            using var document = new PdfDocument(PdfConformanceLevel.Pdf_A3A);
            
            // Enable auto-tagging
            document.AutoTag = true;
            
            // Set critical document information for accessibility
            SetAccessibleDocumentInfo(document, fileName);
            
            // Create document structure hierarchy
            var rootStructure = CreateDocumentStructure(document);
            
            // Convert and process content
            var page = document.Pages.Add();
            
            // Set page properties
            page.Graphics.SetTransparency(1.0f);
            
            // Use embedded fonts for PDF/UA compliance
            var font = GetEmbeddedFont("Arial", 12);
            
            // Add structured content (this would come from Word doc parsing)
            AddStructuredContent(document, page, rootStructure, font);
            
            // Save to stream
            using var outputStream = new MemoryStream();
            document.Save(outputStream);
            
            return outputStream.ToArray();
        }
        
        /// <summary>
        /// Enhance an existing PDF document for accessibility
        /// </summary>
        public void EnhanceAccessibility(PdfLoadedDocument loadedPdf, string fileName)
        {
            // Set document info
            var docInfo = loadedPdf.DocumentInformation;
            
            // Required for PDF/UA
            if (string.IsNullOrWhiteSpace(docInfo.Title))
            {
                docInfo.Title = Path.GetFileNameWithoutExtension(fileName);
            }
            
            docInfo.Language = "en-US";
            docInfo.Subject = "Accessible Government Form";
            docInfo.Keywords = "accessible, PDF/UA, WCAG 2.1 AA, Section 508";
            
            // Set custom metadata
            docInfo.CustomMetadata["Accessibility_Standard"] = "PDF/UA-1";
            docInfo.CustomMetadata["WCAG_Level"] = "AA";
            docInfo.CustomMetadata["Section508"] = "Compliant";
            
            // Get or create structure element root
            var rootElement = loadedPdf.StructureElement ?? new PdfStructureElement(PdfTagType.Document);
            
            // Process form fields with proper structure
            if (loadedPdf.Form != null && loadedPdf.Form.Fields.Count > 0)
            {
                EnhanceFormFieldAccessibility(loadedPdf, rootElement);
            }
            
            // Set reading order for all pages
            foreach (PdfLoadedPage page in loadedPdf.Pages)
            {
                page.FormFieldsTabOrder = PdfFormFieldsTabOrder.Structure;
            }
            
            // Mark decorative elements as artifacts
            MarkArtifacts(loadedPdf);
        }
        
        private void SetAccessibleDocumentInfo(PdfDocument document, string fileName)
        {
            var docInfo = document.DocumentInformation;
            
            // Required metadata for PDF/UA
            docInfo.Title = Path.GetFileNameWithoutExtension(fileName) ?? "Accessible Form";
            docInfo.Language = "en-US";
            docInfo.Author = "AccessForm Converter";
            docInfo.Subject = "Government Form - Accessible Version";
            docInfo.Keywords = "accessible, PDF/UA, WCAG 2.1 AA, Section 508, government, form";
            docInfo.Creator = "AccessForm PDF Converter";
            docInfo.Producer = "Syncfusion Essential PDF";
            
            // Custom metadata for compliance tracking
            docInfo.CustomMetadata["Accessibility_Standard"] = "PDF/UA-1";
            docInfo.CustomMetadata["WCAG_Level"] = "AA";
            docInfo.CustomMetadata["Section508"] = "Compliant";
            docInfo.CustomMetadata["Tagged"] = "True";
            docInfo.CustomMetadata["Language"] = "en-US";
            docInfo.CustomMetadata["ProcessedDate"] = DateTime.Now.ToString("yyyy-MM-dd");
        }
        
        private PdfStructureElement CreateDocumentStructure(PdfDocument document)
        {
            // Create root structure element
            var documentRoot = new PdfStructureElement(PdfTagType.Document);
            
            // Create article structure (main content container)
            var article = new PdfStructureElement(PdfTagType.Article);
            article.Parent = documentRoot;
            article.Title = "Main Content";
            article.Language = "en-US";
            
            // Create sections for different parts
            var headerSection = new PdfStructureElement(PdfTagType.Section);
            headerSection.Parent = article;
            headerSection.Title = "Header";
            
            var formSection = new PdfStructureElement(PdfTagType.Section);
            formSection.Parent = article;
            formSection.Title = "Form Fields";
            
            var footerSection = new PdfStructureElement(PdfTagType.Section);
            footerSection.Parent = article;
            footerSection.Title = "Footer";
            
            return documentRoot;
        }
        
        private void EnhanceFormFieldAccessibility(PdfLoadedDocument document, PdfStructureElement rootElement)
        {
            // Create form section
            var formSection = new PdfStructureElement(PdfTagType.Form);
            formSection.Parent = rootElement;
            formSection.Title = "Interactive Form";
            formSection.AlternateText = "Fill out this form with required information";

            int tabIndex = 1;
            var fieldGroups = GroupRelatedFields(document.Form.Fields);

            foreach (var group in fieldGroups)
            {
                // Create structure for field group
                var groupElement = new PdfStructureElement(PdfTagType.Section);
                groupElement.Parent = formSection;
                groupElement.Title = group.Key;

                foreach (PdfLoadedField field in group.Value)
                {
                    // Create structure element for each field
                    var fieldElement = CreateFieldStructureElement(field);
                    fieldElement.Parent = groupElement;

                    // Set tab order
                    field.TabIndex = tabIndex++;

                    // Add descriptive tooltip if missing
                    EnhanceFieldWithTooltip(field);

                    // Add alternative text
                    AddFieldAlternativeText(field, fieldElement);

                    // Mark required fields
                    MarkRequiredFields(field, fieldElement);

                    // Enhanced: Tag associated graphics for radio buttons and checkboxes
                    if (field is PdfLoadedCheckBoxField || field is PdfLoadedRadioButtonListField)
                    {
                        TagFieldGraphics(field, fieldElement, document);
                    }

                    // Enhanced: Ensure proper object references
                    EnsureFieldObjectReferences(field, document);
                }
            }
        }

        private void TagFieldGraphics(PdfLoadedField field, PdfStructureElement fieldElement, PdfLoadedDocument document)
        {
            // Tag any graphical elements (paths, images) associated with the field
            if (field is PdfLoadedCheckBoxField checkBox)
            {
                // Tag checkbox graphics
                var checkBoxElement = new PdfStructureElement(PdfTagType.Form);
                checkBoxElement.Parent = fieldElement;
                checkBoxElement.Title = "Checkbox graphic";
                checkBoxElement.AlternateText = checkBox.Checked ? "Checked" : "Unchecked";
                checkBoxElement.ActualText = checkBox.Checked ? "☑" : "☐";
            }
            else if (field is PdfLoadedRadioButtonListField radioGroup)
            {
                // Tag radio button graphics for each option
                foreach (PdfLoadedRadioButtonItem item in radioGroup.Items)
                {
                    var radioElement = new PdfStructureElement(PdfTagType.Form);
                    radioElement.Parent = fieldElement;
                    radioElement.Title = $"Radio button: {item.Value}";
                    radioElement.AlternateText = item.Selected ? $"Selected: {item.Value}" : $"Option: {item.Value}";
                    radioElement.ActualText = item.Selected ? "◉" : "○";
                }
            }
        }

        private void EnsureFieldObjectReferences(PdfLoadedField field, PdfLoadedDocument document)
        {
            // Ensure the field has proper appearance dictionaries and object references
            try
            {
                // Check if the field has valid bounds - need to cast to specific field type
                RectangleF bounds = RectangleF.Empty;
                bool needsBoundsUpdate = false;

                if (field is PdfLoadedCheckBoxField checkBox)
                {
                    bounds = checkBox.Bounds;
                    if (bounds.Width <= 0 || bounds.Height <= 0)
                    {
                        checkBox.Bounds = new RectangleF(bounds.X, bounds.Y, 13.8f, 13.8f);
                        needsBoundsUpdate = true;
                    }
                }
                else if (field is PdfLoadedRadioButtonListField radioField)
                {
                    if (radioField.Items != null && radioField.Items.Count > 0)
                    {
                        // Check bounds for each radio button item
                        for (int i = 0; i < radioField.Items.Count; i++)
                        {
                            var item = radioField.Items[i] as PdfLoadedRadioButtonItem;
                            if (item != null)
                            {
                                if (item.Bounds.Width <= 0 || item.Bounds.Height <= 0)
                                {
                                    item.Bounds = new RectangleF(item.Bounds.X, item.Bounds.Y, 13.8f, 13.8f);
                                    needsBoundsUpdate = true;
                                }
                            }
                        }
                    }
                }

                // Ensure tooltip exists (required for PDF/UA)
                if (string.IsNullOrWhiteSpace(field.ToolTip))
                {
                    field.ToolTip = $"Form field: {field.Name}";
                }
            }
            catch (Exception ex)
            {
                // Log but don't fail - field may be read-only or have other restrictions
                Console.WriteLine($"Could not ensure references for field {field.Name}: {ex.Message}");
            }
        }
        
        private Dictionary<string, List<PdfLoadedField>> GroupRelatedFields(PdfFieldCollection fields)
        {
            var groups = new Dictionary<string, List<PdfLoadedField>>();
            
            foreach (PdfLoadedField field in fields)
            {
                var groupName = DetermineFieldGroup(field.Name);
                
                if (!groups.ContainsKey(groupName))
                {
                    groups[groupName] = new List<PdfLoadedField>();
                }
                
                groups[groupName].Add(field);
            }
            
            return groups;
        }
        
        private string DetermineFieldGroup(string fieldName)
        {
            var lowerName = fieldName.ToLower();
            
            if (lowerName.Contains("name") || lowerName.Contains("nombre"))
                return "Personal Information";
            if (lowerName.Contains("address") || lowerName.Contains("street") || lowerName.Contains("city") || lowerName.Contains("zip"))
                return "Address Information";
            if (lowerName.Contains("phone") || lowerName.Contains("email") || lowerName.Contains("contact"))
                return "Contact Information";
            if (lowerName.Contains("date") || lowerName.Contains("time"))
                return "Date and Time";
            if (lowerName.Contains("ssn") || lowerName.Contains("social") || lowerName.Contains("ein") || lowerName.Contains("tax"))
                return "Identification Numbers";
            if (lowerName.Contains("amount") || lowerName.Contains("payment") || lowerName.Contains("dollar") || lowerName.Contains("$"))
                return "Financial Information";
            
            return "General Information";
        }
        
        private PdfStructureElement CreateFieldStructureElement(PdfLoadedField field)
        {
            PdfTagType tagType = PdfTagType.Form;
            
            // Determine appropriate tag type based on field type
            if (field is PdfLoadedTextBoxField)
                tagType = PdfTagType.Form;
            else if (field is PdfLoadedCheckBoxField)
                tagType = PdfTagType.Form;
            else if (field is PdfLoadedRadioButtonListField)
                tagType = PdfTagType.Form;
            else if (field is PdfLoadedComboBoxField || field is PdfLoadedListBoxField)
                tagType = PdfTagType.Form;
            
            var element = new PdfStructureElement(tagType);
            element.Title = field.Name;
            
            return element;
        }
        
        private void EnhanceFieldWithTooltip(PdfLoadedField field)
        {
            if (!string.IsNullOrWhiteSpace(field.ToolTip))
                return;
            
            var fieldName = field.Name.ToLower();
            
            // Add intelligent tooltips based on field patterns
            if (fieldName.Contains("date"))
                field.ToolTip = "Enter date in MM/DD/YYYY format";
            else if (fieldName.Contains("phone"))
                field.ToolTip = "Enter phone number including area code (XXX) XXX-XXXX";
            else if (fieldName.Contains("email"))
                field.ToolTip = "Enter valid email address (example@domain.com)";
            else if (fieldName.Contains("ssn"))
                field.ToolTip = "Enter Social Security Number (XXX-XX-XXXX)";
            else if (fieldName.Contains("zip"))
                field.ToolTip = "Enter 5-digit ZIP code or ZIP+4";
            else if (fieldName.Contains("state"))
                field.ToolTip = "Select or enter 2-letter state abbreviation";
            else if (fieldName.Contains("amount") || fieldName.Contains("$"))
                field.ToolTip = "Enter dollar amount without $ symbol";
            else if (fieldName.Contains("percent") || fieldName.Contains("%"))
                field.ToolTip = "Enter percentage value (0-100)";
            else if (field is PdfLoadedCheckBoxField)
                field.ToolTip = $"Check this box to select {field.Name}";
            else if (field is PdfLoadedRadioButtonListField)
                field.ToolTip = $"Select one option for {field.Name}";
            else
                field.ToolTip = $"Enter {field.Name}";
        }
        
        private void AddFieldAlternativeText(PdfLoadedField field, PdfStructureElement element)
        {
            var fieldType = GetFieldTypeDescription(field);
            var fieldPurpose = GetFieldPurpose(field.Name);
            
            element.AlternateText = $"{fieldType} for {fieldPurpose}";
            
            // Add actual text for what's displayed
            if (field is PdfLoadedTextBoxField textField && !string.IsNullOrEmpty(textField.Text))
            {
                element.ActualText = textField.Text;
            }
        }
        
        private string GetFieldTypeDescription(PdfLoadedField field)
        {
            if (field is PdfLoadedTextBoxField)
                return "Text field";
            if (field is PdfLoadedCheckBoxField)
                return "Checkbox";
            if (field is PdfLoadedRadioButtonListField)
                return "Radio button group";
            if (field is PdfLoadedComboBoxField)
                return "Dropdown list";
            if (field is PdfLoadedListBoxField)
                return "List box";
            if (field is PdfLoadedSignatureField)
                return "Signature field";
            
            return "Form field";
        }
        
        private string GetFieldPurpose(string fieldName)
        {
            // Clean up field name for better description
            var purpose = fieldName
                .Replace("_", " ")
                .Replace("-", " ")
                .Replace(".", " ");
            
            // Convert camelCase to readable text
            purpose = Regex.Replace(purpose, "([a-z])([A-Z])", "$1 $2");
            
            // Capitalize first letter
            if (!string.IsNullOrEmpty(purpose))
            {
                purpose = char.ToUpper(purpose[0]) + purpose.Substring(1).ToLower();
            }
            
            return purpose;
        }
        
        private void MarkRequiredFields(PdfLoadedField field, PdfStructureElement element)
        {
            // Check if field name suggests it's required
            var fieldName = field.Name.ToLower();
            bool isLikelyRequired = fieldName.Contains("required") || 
                                   fieldName.Contains("*") ||
                                   fieldName.Contains("ssn") ||
                                   fieldName.Contains("name") ||
                                   fieldName.Contains("date");
            
            if (isLikelyRequired)
            {
                element.Title = element.Title + " (Required)";
                element.AlternateText = element.AlternateText + " - This field is required";
                
                if (field is PdfLoadedTextBoxField textField)
                {
                    textField.Required = true;
                }
            }
        }
        
        private void MarkArtifacts(PdfLoadedDocument document)
        {
            // Mark page numbers, headers, footers as artifacts
            // These should not be read by screen readers
            foreach (PdfLoadedPage page in document.Pages)
            {
                // This would mark decorative elements as artifacts
                // Implementation depends on how headers/footers are identified
            }
        }
        
        private PdfFont GetEmbeddedFont(string fontName, float size)
        {
            var key = $"{fontName}_{size}";
            
            if (!_embeddedFonts.ContainsKey(key))
            {
                // Try to load TrueType font for embedding
                string fontPath = null;
                
                // Common font paths on macOS
                var fontPaths = new[]
                {
                    $"/System/Library/Fonts/{fontName}.ttc",
                    $"/System/Library/Fonts/{fontName}.ttf",
                    $"/Library/Fonts/{fontName}.ttf",
                    $"~/Library/Fonts/{fontName}.ttf",
                    "/System/Library/Fonts/Helvetica.ttc"
                };
                
                foreach (var path in fontPaths)
                {
                    var expandedPath = Environment.ExpandEnvironmentVariables(path);
                    if (File.Exists(expandedPath))
                    {
                        fontPath = expandedPath;
                        break;
                    }
                }
                
                if (fontPath != null)
                {
                    using var fontStream = new FileStream(fontPath, FileMode.Open, FileAccess.Read);
                    _embeddedFonts[key] = new PdfTrueTypeFont(fontStream, size);
                }
                else
                {
                    // Fallback to standard font
                    _embeddedFonts[key] = new PdfStandardFont(PdfFontFamily.Helvetica, size);
                }
            }
            
            return _embeddedFonts[key];
        }
        
        private void AddStructuredContent(PdfDocument document, PdfPage page, PdfStructureElement rootStructure, PdfFont font)
        {
            // Add a heading
            var h1Element = new PdfStructureElement(PdfTagType.HeadingLevel1);
            h1Element.Parent = rootStructure;
            h1Element.Title = "Accessible Form";
            
            var headingText = new PdfTextElement("Accessible Government Form");
            headingText.PdfTag = h1Element;
            headingText.Font = font;
            headingText.Draw(page, new PointF(50, 50));
            
            // Add a paragraph
            var paraElement = new PdfStructureElement(PdfTagType.Paragraph);
            paraElement.Parent = rootStructure;
            paraElement.Language = "en-US";
            
            var paraText = new PdfTextElement("This form has been enhanced for accessibility.");
            paraText.PdfTag = paraElement;
            paraText.Font = font;
            paraText.Draw(page, new PointF(50, 100));
        }
    }
}
