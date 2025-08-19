using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using Syncfusion.Pdf.Interactive;
using Syncfusion.Drawing;
using System.Text.RegularExpressions;
using System.Linq;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Advanced algorithmic retrofitting for PDF accessibility
    /// Simplified version that works with available Syncfusion API
    /// </summary>
    public class AccessibilityRetrofitService
    {
        private readonly Dictionary<string, string> _fieldGroupings = new();
        
        public void RetrofitAccessibility(PdfLoadedDocument document)
        {
            // 1. Smart Field Analysis and Enhancement
            AnalyzeAndEnhanceFormFields(document);
            
            // 2. Auto-generate Navigation
            GenerateBookmarksFromContent(document);
        }
        
        /// <summary>
        /// Algorithmically analyze form fields to determine their purpose and relationships
        /// </summary>
        private void AnalyzeAndEnhanceFormFields(PdfLoadedDocument document)
        {
            if (document.Form == null) return;
            
            var fields = document.Form.Fields.Cast<PdfLoadedField>().ToList();
            
            // Detect patterns in field names
            DetectFieldPatterns(fields);
            
            // Apply detected patterns
            ApplyFieldEnhancements(document);
        }
        
        private void DetectFieldPatterns(List<PdfLoadedField> fields)
        {
            // Common patterns to detect algorithmically
            var patterns = new Dictionary<string, Regex>
            {
                ["date"] = new Regex(@"(date|fecha|mm|dd|yyyy|^\d{1,2}[-/]\d{1,2})", RegexOptions.IgnoreCase),
                ["time"] = new Regex(@"(time|hora|am|pm|:00|start|end|from|to)", RegexOptions.IgnoreCase),
                ["phone"] = new Regex(@"(phone|tel|cell|mobile|fax|\d{3}[-.]?\d{3}[-.]?\d{4})", RegexOptions.IgnoreCase),
                ["email"] = new Regex(@"(email|e-mail|@|mail)", RegexOptions.IgnoreCase),
                ["ssn"] = new Regex(@"(ssn|social|security|xxx-xx-xxxx|\d{3}-\d{2}-\d{4})", RegexOptions.IgnoreCase),
                ["ein"] = new Regex(@"(ein|employer|tax|federal|id|\d{2}-\d{7})", RegexOptions.IgnoreCase),
                ["address"] = new Regex(@"(address|street|city|state|zip|postal)", RegexOptions.IgnoreCase),
                ["name"] = new Regex(@"(name|nombre|first|last|middle|apellido)", RegexOptions.IgnoreCase),
                ["amount"] = new Regex(@"(amount|total|sum|price|cost|fee|\$|dollar)", RegexOptions.IgnoreCase),
                ["percentage"] = new Regex(@"(percent|%|rate)", RegexOptions.IgnoreCase),
                ["number"] = new Regex(@"(number|#|no\.|num)", RegexOptions.IgnoreCase)
            };
            
            foreach (var field in fields)
            {
                foreach (var pattern in patterns)
                {
                    if (pattern.Value.IsMatch(field.Name))
                    {
                        // Store the detected type
                        _fieldGroupings[field.Name] = pattern.Key;
                        ApplyPatternBasedLabel(field as PdfLoadedTextBoxField, pattern.Key);
                        break;
                    }
                }
            }
        }
        
        private void ApplyPatternBasedLabel(PdfLoadedTextBoxField? textField, string patternType)
        {
            if (textField == null) return;
            
            // Apply smart labels based on detected pattern
            var labels = new Dictionary<string, string>
            {
                ["date"] = "Date (MM/DD/YYYY)",
                ["time"] = "Time",
                ["phone"] = "Phone Number (XXX) XXX-XXXX",
                ["email"] = "Email Address",
                ["ssn"] = "Social Security Number (XXX-XX-XXXX)",
                ["ein"] = "Employer ID Number",
                ["address"] = "Street Address",
                ["name"] = "Full Name",
                ["amount"] = "Amount ($)",
                ["percentage"] = "Percentage (%)",
                ["number"] = "Number"
            };
            
            if (string.IsNullOrEmpty(textField.ToolTip) && labels.ContainsKey(patternType))
            {
                textField.ToolTip = labels[patternType];
            }
            
            // Apply format validation hints
            if (patternType == "phone")
            {
                textField.ToolTip = "Phone Number (XXX) XXX-XXXX";
            }
            else if (patternType == "date")
            {
                textField.ToolTip = "Date (MM/DD/YYYY)";
            }
            else if (patternType == "email")
            {
                textField.ToolTip = "Email Address (example@domain.com)";
            }
            else if (patternType == "ssn")
            {
                textField.ToolTip = "Social Security Number (XXX-XX-XXXX)";
                textField.Password = true; // Mask SSN fields
            }
            else if (patternType == "amount")
            {
                textField.ToolTip = "Dollar Amount";
            }
            else if (patternType == "percentage")
            {
                textField.ToolTip = "Percentage (0-100)";
            }
        }
        
        private void DetectFieldGroups(List<PdfLoadedField> fields)
        {
            // Detect common field groupings (e.g., address blocks)
            var addressFields = new[] { "street", "city", "state", "zip", "country" };
            var nameFields = new[] { "first", "middle", "last", "suffix", "prefix" };
            var contactFields = new[] { "phone", "email", "fax", "mobile" };
            
            foreach (var field in fields)
            {
                var fieldNameLower = field.Name.ToLower();
                
                // Check for address group
                if (addressFields.Any(af => fieldNameLower.Contains(af)))
                {
                    _fieldGroupings[field.Name] = "address_group";
                    if (string.IsNullOrEmpty(field.ToolTip))
                    {
                        field.ToolTip = "Address Information";
                    }
                }
                // Check for name group
                else if (nameFields.Any(nf => fieldNameLower.Contains(nf)))
                {
                    _fieldGroupings[field.Name] = "name_group";
                    if (string.IsNullOrEmpty(field.ToolTip))
                    {
                        field.ToolTip = "Name Information";
                    }
                }
                // Check for contact group
                else if (contactFields.Any(cf => fieldNameLower.Contains(cf)))
                {
                    _fieldGroupings[field.Name] = "contact_group";
                    if (string.IsNullOrEmpty(field.ToolTip))
                    {
                        field.ToolTip = "Contact Information";
                    }
                }
            }
        }
        
        private void GenerateBookmarksFromContent(PdfLoadedDocument document)
        {
            // Auto-generate bookmarks based on document structure
            // This would normally analyze text content to find headings
            
            // For now, create bookmarks for each page with forms
            if (document.Form == null) return;
            
            var bookmarks = new List<string>();
            
            for (int i = 0; i < document.Pages.Count; i++)
            {
                var pageHasFields = false;
                
                // Check if any fields are on this page
                foreach (PdfLoadedField field in document.Form.Fields)
                {
                    // Since we can't easily get field position, assume fields are distributed
                    // In a real implementation, you'd check field bounds against page bounds
                    pageHasFields = true;
                    break;
                }
                
                if (pageHasFields)
                {
                    // Create a bookmark for this page
                    var bookmarkTitle = $"Page {i + 1}";
                    bookmarks.Add(bookmarkTitle);
                }
            }
            
            // Note: Actual bookmark creation would use PdfBookmark API
        }
        
        private void ApplyFieldEnhancements(PdfLoadedDocument document)
        {
            if (document.Form == null) return;
            
            // Apply all the enhancements we've detected
            foreach (PdfLoadedField field in document.Form.Fields)
            {
                // Ensure every field has a tooltip
                if (string.IsNullOrEmpty(field.ToolTip))
                {
                    field.ToolTip = GenerateReadableLabel(field.Name);
                }
                
                // Apply grouping context
                if (_fieldGroupings.ContainsKey(field.Name))
                {
                    var groupType = _fieldGroupings[field.Name];
                    if (!field.ToolTip.Contains(groupType))
                    {
                        // Add group context if not already present
                        field.ToolTip = $"{field.ToolTip} ({groupType.Replace("_", " ")})";
                    }
                }
                
                // Ensure required fields are marked
                if (field.Required && !field.ToolTip.Contains("Required"))
                {
                    field.ToolTip = $"{field.ToolTip} (Required)";
                }
            }
        }
        
        private string GenerateReadableLabel(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                return "Field";
            
            // Convert various naming conventions to readable format
            var label = fieldName
                .Replace("_", " ")
                .Replace("-", " ")
                .Replace(".", " ");
            
            // Handle camelCase
            label = Regex.Replace(label, "([a-z])([A-Z])", "$1 $2");
            
            // Handle acronyms
            label = Regex.Replace(label, "([A-Z]+)([A-Z][a-z])", "$1 $2");
            
            // Remove common prefixes/suffixes
            label = Regex.Replace(label, @"^(txt|lbl|fld|chk|rad|cmb)", "", RegexOptions.IgnoreCase);
            
            // Capitalize first letter of each word
            label = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(label.ToLower().Trim());
            
            return label;
        }
        
        // Helper class for field relationships
        private class FieldRelationship
        {
            public string LabelField { get; set; } = "";
            public string DataField { get; set; } = "";
            public string RelationType { get; set; } = "";
        }
    }
}
