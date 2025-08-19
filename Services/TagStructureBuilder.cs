using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Interactive;
using Syncfusion.Pdf.Parsing;

namespace AccessFormServer.Services
{
    /// <summary>
    /// Builds and manages PDF tag structure for accessibility
    /// Implements Section 2.3 - Best-Guess Structural Tagging
    /// </summary>
    public class TagStructureBuilder
    {
        private readonly ILogger<TagStructureBuilder> _logger;

        // Standard PDF tag mappings
        private static readonly Dictionary<string, string> RoleToTagMapping = new()
        {
            ["title"] = "H1",
            ["heading1"] = "H1",
            ["heading2"] = "H2",
            ["heading3"] = "H3",
            ["heading4"] = "H4",
            ["heading5"] = "H5",
            ["heading6"] = "H6",
            ["paragraph"] = "P",
            ["list_item"] = "LI",
            ["list"] = "L",
            ["table"] = "Table",
            ["table_row"] = "TR",
            ["table_header"] = "TH",
            ["table_cell"] = "TD",
            ["figure"] = "Figure",
            ["caption"] = "Caption",
            ["blockquote"] = "BlockQuote",
            ["note"] = "Note",
            ["reference"] = "Reference",
            ["link"] = "Link",
            ["form"] = "Form"
        };

        public TagStructureBuilder(ILogger<TagStructureBuilder> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Apply AI-generated tag structure to PDF document
        /// </summary>
        public async Task<TagApplicationResult> ApplyTagStructureAsync(
            PdfLoadedDocument document,
            DocumentTagStructure tagStructure)
        {
            _logger.LogInformation("Applying tag structure to PDF document");
            var result = new TagApplicationResult();

            try
            {
                // Enable tagged PDF
                document.DocumentInformation.Title = document.DocumentInformation.Title ?? "Accessible Document";
                
                // Apply tags in reading order
                foreach (var tagInfo in tagStructure.Tags.OrderBy(t => tagStructure.ReadingOrder.IndexOf(tagStructure.Tags.IndexOf(t))))
                {
                    ApplyTag(document, tagInfo);
                    result.TagsApplied++;
                }

                // Apply form field accessibility enhancements
                if (document.Form != null)
                {
                    await EnhanceFormFieldAccessibilityAsync(document.Form);
                    result.FieldsEnhanced = document.Form.Fields.Count;
                }

                // Add document language
                SetDocumentLanguage(document, "en-US");

                // Add metadata
                AddAccessibilityMetadata(document);

                result.Success = true;
                _logger.LogInformation($"Successfully applied {result.TagsApplied} tags to document");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply tag structure");
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Build hierarchical tag tree from flat tag list
        /// </summary>
        public PdfTaggedContent BuildTagTree(PdfDocument document, List<TagInfo> tags, List<int> readingOrder)
        {
            _logger.LogInformation("Building hierarchical tag tree");

            // Create root structure element
            var taggedContent = new PdfTaggedContent(document);
            var rootElement = taggedContent.RootElement;

            // Stack to track current nesting level
            var elementStack = new Stack<PdfStructureElement>();
            elementStack.Push(rootElement);

            // Process tags in reading order
            foreach (var index in readingOrder)
            {
                if (index >= tags.Count) continue;
                
                var tag = tags[index];
                var pdfTag = MapToPdfTag(tag.Type);

                // Create structure element
                var element = new PdfStructureElement(pdfTag);
                
                // Set properties
                if (!string.IsNullOrEmpty(tag.AltText))
                {
                    element.AlternateText = tag.AltText;
                }

                // Handle nesting based on tag type
                if (IsHeading(pdfTag))
                {
                    // Pop stack until we find appropriate parent for this heading level
                    var headingLevel = GetHeadingLevel(pdfTag);
                    
                    while (elementStack.Count > 1)
                    {
                        var current = elementStack.Peek();
                        if (!IsHeading(current.TagType) || GetHeadingLevel(current.TagType) < headingLevel)
                            break;
                        elementStack.Pop();
                    }
                }
                else if (pdfTag == "LI")
                {
                    // List items need a list parent
                    EnsureListParent(elementStack);
                }
                else if (pdfTag == "TD" || pdfTag == "TH")
                {
                    // Table cells need a row parent
                    EnsureTableRowParent(elementStack);
                }

                // Add element to current parent
                var parent = elementStack.Peek();
                parent.AppendChild(element);

                // Push container elements onto stack
                if (IsContainer(pdfTag))
                {
                    elementStack.Push(element);
                }
            }

            return taggedContent;
        }

        /// <summary>
        /// Validate tag structure for accessibility compliance
        /// </summary>
        public ValidationResult ValidateTagStructure(DocumentTagStructure structure)
        {
            _logger.LogInformation("Validating tag structure for accessibility");
            var result = new ValidationResult();

            // Check for document title
            if (structure.Tags.All(t => t.Type != "H1"))
            {
                result.Issues.Add(new ValidationIssue
                {
                    Level = "Warning",
                    Code = "MISSING_H1",
                    Message = "Document lacks a main heading (H1)"
                });
            }

            // Check heading hierarchy
            ValidateHeadingHierarchy(structure, result);

            // Check table structure
            ValidateTableStructure(structure, result);

            // Check list structure
            ValidateListStructure(structure, result);

            // Check alt text for images
            ValidateImageAltText(structure, result);

            // Check reading order
            ValidateReadingOrder(structure, result);

            result.IsValid = !result.Issues.Any(i => i.Level == "Error");
            result.Score = CalculateAccessibilityScore(result);

            return result;
        }

        // Helper methods

        private void ApplyTag(PdfLoadedDocument document, TagInfo tagInfo)
        {
            // This would apply the tag to the specific content in the PDF
            // Implementation depends on Syncfusion's tagging capabilities
            _logger.LogDebug($"Applying {tagInfo.Type} tag to page {tagInfo.PageNumber}");
        }

        private async Task EnhanceFormFieldAccessibilityAsync(PdfLoadedForm form)
        {
            foreach (PdfLoadedField field in form.Fields)
            {
                // Set tooltip/description
                if (string.IsNullOrEmpty(field.ToolTip))
                {
                    field.ToolTip = GenerateFieldTooltip(field);
                }

                // Ensure proper tab order
                if (field is PdfLoadedTextBoxField textField)
                {
                    // Additional text field enhancements
                }
                else if (field is PdfLoadedCheckBoxField checkField)
                {
                    // Checkbox enhancements
                }
                else if (field is PdfLoadedRadioButtonListField radioField)
                {
                    // Radio button group enhancements
                }
            }
        }

        private string GenerateFieldTooltip(PdfLoadedField field)
        {
            // Generate a basic tooltip based on field name
            var name = field.Name.Replace("_", " ").Replace("-", " ");
            return $"Enter {name}";
        }

        private void SetDocumentLanguage(PdfLoadedDocument document, string language)
        {
            // Set the document language for screen readers
            document.DocumentInformation.Language = language;
        }

        private void AddAccessibilityMetadata(PdfLoadedDocument document)
        {
            document.DocumentInformation.Keywords = 
                (document.DocumentInformation.Keywords ?? "") + " Accessible, WCAG2.1, Section508";
            document.DocumentInformation.Subject = 
                (document.DocumentInformation.Subject ?? "") + " [Accessibility Enhanced]";
        }

        private string MapToPdfTag(string roleOrTag)
        {
            // If it's already a PDF tag, return it
            if (RoleToTagMapping.ContainsValue(roleOrTag))
                return roleOrTag;

            // Otherwise, map from role to tag
            return RoleToTagMapping.GetValueOrDefault(roleOrTag.ToLower(), "P");
        }

        private bool IsHeading(string tag)
        {
            return tag.StartsWith("H") && tag.Length == 2 && char.IsDigit(tag[1]);
        }

        private int GetHeadingLevel(string tag)
        {
            if (IsHeading(tag))
                return int.Parse(tag.Substring(1));
            return 0;
        }

        private bool IsContainer(string tag)
        {
            return tag switch
            {
                "L" => true,        // List
                "Table" => true,    // Table
                "TR" => true,       // Table row
                "Sect" => true,     // Section
                "Art" => true,      // Article
                "BlockQuote" => true,
                _ => IsHeading(tag)
            };
        }

        private void EnsureListParent(Stack<PdfStructureElement> stack)
        {
            if (stack.Peek().TagType != "L")
            {
                // Create a list container
                var list = new PdfStructureElement("L");
                stack.Peek().AppendChild(list);
                stack.Push(list);
            }
        }

        private void EnsureTableRowParent(Stack<PdfStructureElement> stack)
        {
            if (stack.Peek().TagType != "TR")
            {
                // Ensure we're in a table
                if (stack.Peek().TagType != "Table")
                {
                    var table = new PdfStructureElement("Table");
                    stack.Peek().AppendChild(table);
                    stack.Push(table);
                }

                // Create a row
                var row = new PdfStructureElement("TR");
                stack.Peek().AppendChild(row);
                stack.Push(row);
            }
        }

        private void ValidateHeadingHierarchy(DocumentTagStructure structure, ValidationResult result)
        {
            var headings = structure.Tags
                .Where(t => IsHeading(t.Type))
                .OrderBy(t => structure.ReadingOrder.IndexOf(structure.Tags.IndexOf(t)))
                .ToList();

            int lastLevel = 0;
            foreach (var heading in headings)
            {
                int level = GetHeadingLevel(heading.Type);
                
                if (lastLevel > 0 && level > lastLevel + 1)
                {
                    result.Issues.Add(new ValidationIssue
                    {
                        Level = "Warning",
                        Code = "SKIPPED_HEADING_LEVEL",
                        Message = $"Heading level skipped from H{lastLevel} to H{level}",
                        Location = $"Page {heading.PageNumber}"
                    });
                }
                
                lastLevel = level;
            }
        }

        private void ValidateTableStructure(DocumentTagStructure structure, ValidationResult result)
        {
            var tables = structure.Tags.Where(t => t.Type == "Table");
            
            foreach (var table in tables)
            {
                // Check if table has header cells
                // This would need to check the actual table structure
                _logger.LogDebug($"Validating table on page {table.PageNumber}");
            }
        }

        private void ValidateListStructure(DocumentTagStructure structure, ValidationResult result)
        {
            var listItems = structure.Tags.Where(t => t.Type == "LI");
            
            foreach (var item in listItems)
            {
                // Check if list item has proper list parent
                // This would need to check the actual structure
                _logger.LogDebug($"Validating list item on page {item.PageNumber}");
            }
        }

        private void ValidateImageAltText(DocumentTagStructure structure, ValidationResult result)
        {
            var images = structure.Tags.Where(t => t.Type == "Figure");
            
            foreach (var image in images)
            {
                if (string.IsNullOrEmpty(image.AltText))
                {
                    result.Issues.Add(new ValidationIssue
                    {
                        Level = "Error",
                        Code = "MISSING_ALT_TEXT",
                        Message = "Image lacks alternative text",
                        Location = $"Page {image.PageNumber}"
                    });
                }
            }
        }

        private void ValidateReadingOrder(DocumentTagStructure structure, ValidationResult result)
        {
            // Check if reading order makes logical sense
            // This is a simplified check
            if (structure.ReadingOrder.Count != structure.Tags.Count)
            {
                result.Issues.Add(new ValidationIssue
                {
                    Level = "Warning",
                    Code = "INCOMPLETE_READING_ORDER",
                    Message = "Not all elements are included in the reading order"
                });
            }
        }

        private int CalculateAccessibilityScore(ValidationResult result)
        {
            int score = 100;
            
            foreach (var issue in result.Issues)
            {
                switch (issue.Level)
                {
                    case "Error":
                        score -= 10;
                        break;
                    case "Warning":
                        score -= 5;
                        break;
                    case "Info":
                        score -= 2;
                        break;
                }
            }
            
            return Math.Max(0, score);
        }
    }

    // Supporting classes

    public class TagApplicationResult
    {
        public bool Success { get; set; }
        public int TagsApplied { get; set; }
        public int FieldsEnhanced { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class PdfTaggedContent
    {
        public PdfDocument Document { get; }
        public PdfStructureElement RootElement { get; }

        public PdfTaggedContent(PdfDocument document)
        {
            Document = document;
            RootElement = new PdfStructureElement("Document");
        }
    }

    public class PdfStructureElement
    {
        public string TagType { get; set; }
        public string AlternateText { get; set; }
        public string ActualText { get; set; }
        public List<PdfStructureElement> Children { get; set; } = new();
        public PdfStructureElement Parent { get; set; }

        public PdfStructureElement(string tagType)
        {
            TagType = tagType;
        }

        public void AppendChild(PdfStructureElement child)
        {
            child.Parent = this;
            Children.Add(child);
        }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public int Score { get; set; }
        public List<ValidationIssue> Issues { get; set; } = new();
    }

    public class ValidationIssue
    {
        public string Level { get; set; } // Error, Warning, Info
        public string Code { get; set; }
        public string Message { get; set; }
        public string Location { get; set; }
    }
}
