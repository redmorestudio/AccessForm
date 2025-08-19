using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Interactive;
using Syncfusion.Pdf.Parsing;

namespace AccessFormServer.Services
{
    /// <summary>
    /// Core AI-powered semantic remediation engine as described in Section 2 of the specification.
    /// Transforms raw, unstructured information into meaningful, human-centric content.
    /// </summary>
    public class SemanticRemediationEngine
    {
        private readonly ILogger<SemanticRemediationEngine> _logger;
        private readonly AzureFormRecognizerService _formRecognizer;
        private readonly LlamaGroqService _llamaService;
        private readonly NLPLabelGenerator _nlpGenerator;
        private readonly DocumentLayoutAnalyzer _layoutAnalyzer;
        private readonly TagStructureBuilder _tagBuilder;

        // Pattern library for structured data formats
        private static readonly Dictionary<string, Regex> DataPatterns = new()
        {
            ["SSN"] = new Regex(@"\b\d{3}-?\d{2}-?\d{4}\b", RegexOptions.IgnoreCase),
            ["Phone"] = new Regex(@"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b", RegexOptions.IgnoreCase),
            ["Email"] = new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.IgnoreCase),
            ["ZIP"] = new Regex(@"\b\d{5}(-\d{4})?\b", RegexOptions.IgnoreCase),
            ["Date"] = new Regex(@"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b", RegexOptions.IgnoreCase),
            ["Currency"] = new Regex(@"\$\s?\d+(\.\d{2})?", RegexOptions.IgnoreCase)
        };

        // Named entity types for field classification
        private static readonly HashSet<string> PersonEntities = new() { "name", "first", "last", "middle", "initial", "applicant", "beneficiary" };
        private static readonly HashSet<string> LocationEntities = new() { "address", "street", "city", "state", "country", "zip", "postal" };
        private static readonly HashSet<string> DateEntities = new() { "date", "dob", "birth", "expire", "effective", "due" };
        private static readonly HashSet<string> ContactEntities = new() { "phone", "email", "fax", "mobile", "contact" };

        public SemanticRemediationEngine(
            ILogger<SemanticRemediationEngine> logger,
            AzureFormRecognizerService formRecognizer,
            LlamaGroqService llamaService,
            NLPLabelGenerator nlpGenerator,
            DocumentLayoutAnalyzer layoutAnalyzer,
            TagStructureBuilder tagBuilder)
        {
            _logger = logger;
            _formRecognizer = formRecognizer;
            _llamaService = llamaService;
            _nlpGenerator = nlpGenerator;
            _layoutAnalyzer = layoutAnalyzer;
            _tagBuilder = tagBuilder;
        }

        /// <summary>
        /// Section 2.1: Automated Form Field Analysis
        /// Performs detailed analysis to extract all relevant properties from form fields
        /// </summary>
        public async Task<FormFieldAnalysisResult> AnalyzeFormFieldsAsync(PdfLoadedDocument document)
        {
            _logger.LogInformation("Starting automated form field analysis");
            var result = new FormFieldAnalysisResult();

            try
            {
                // Extract existing form fields from PDF
                var form = document.Form;
                if (form == null)
                {
                    _logger.LogWarning("No form fields found in document");
                    return result;
                }

                foreach (PdfLoadedField field in form.Fields)
                {
                    var fieldInfo = await AnalyzeFieldAsync(field, document);
                    result.Fields.Add(fieldInfo);
                }

                // Detect radio button groups
                GroupRadioButtons(result.Fields);

                // Identify required field indicators
                await IdentifyRequiredFieldsAsync(result.Fields, document);

                _logger.LogInformation($"Analyzed {result.Fields.Count} form fields");
                result.Success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during form field analysis");
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Section 2.2.1: Context-Aware Label Inference
        /// Generates meaningful programmatic names using NLP cascade
        /// </summary>
        private async Task<FieldInfo> AnalyzeFieldAsync(PdfLoadedField field, PdfLoadedDocument document)
        {
            var fieldInfo = new FieldInfo
            {
                OriginalName = field.Name,
                FieldType = ClassifyFieldType(field),
                PageNumber = 0, // TODO: Find page number from field
                Bounds = GetFieldBounds(field)
            };

            // Extract visual label text near the field
            var labelText = await ExtractNearbyLabelText(field, document);
            fieldInfo.VisualLabel = labelText;

            // Apply NLP cascade for label inference
            // 1. Named Entity Recognition
            fieldInfo.DetectedEntity = DetectNamedEntity(labelText);

            // 2. Pattern Matching with Regular Expressions
            fieldInfo.DataFormat = DetectDataFormat(labelText);

            // 3. Text Preprocessing and Normalization
            fieldInfo.ProgrammaticName = NormalizeToProgrammaticName(labelText);

            // Extract additional properties
            if (field is PdfLoadedTextBoxField textField)
            {
                fieldInfo.DefaultValue = textField.Text;
                fieldInfo.MaxLength = textField.MaxLength;
            }
            else if (field is PdfLoadedCheckBoxField checkField)
            {
                fieldInfo.IsChecked = checkField.Checked;
            }
            else if (field is PdfLoadedRadioButtonListField radioField)
            {
                fieldInfo.RadioGroupName = radioField.Name;
                fieldInfo.SelectedValue = radioField.SelectedValue;
            }
            else if (field is PdfLoadedComboBoxField comboField)
            {
                fieldInfo.Options = new List<string>();
                foreach (PdfLoadedListItem item in comboField.Values)
                {
                    fieldInfo.Options.Add(item.Text);
                }
                fieldInfo.SelectedValue = comboField.SelectedValue;
            }

            return fieldInfo;
        }

        /// <summary>
        /// Section 2.2.2: Generative AI for Helpful Tooltips
        /// Creates context-aware, instructional tooltips using hierarchical context
        /// </summary>
        public async Task<TooltipGenerationResult> GenerateTooltipsAsync(List<FieldInfo> fields, DocumentContext context)
        {
            _logger.LogInformation("Generating intelligent tooltips for form fields");
            var result = new TooltipGenerationResult();

            foreach (var field in fields)
            {
                try
                {
                    // Build hierarchical context for tooltip generation
                    var hierarchicalContext = new HierarchicalContext
                    {
                        FieldLevel = field.ProgrammaticName,
                        SectionLevel = await ExtractSectionContext(field, context),
                        DocumentLevel = context.DocumentTitle
                    };

                    // Generate tooltip using AI or fallback to smart defaults
                    var tooltip = await GenerateContextualTooltip(field, hierarchicalContext);
                    
                    field.AccessibleDescription = tooltip.Description;
                    field.InstructionalText = tooltip.Instructions;
                    field.FormatHint = tooltip.FormatHint;
                    
                    result.ProcessedFields.Add(field);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to generate tooltip for field {field.OriginalName}");
                    
                    // Fallback to basic tooltip
                    field.AccessibleDescription = GenerateBasicTooltip(field);
                    result.FieldsWithFallback.Add(field.OriginalName);
                }
            }

            result.Success = true;
            result.TotalFields = fields.Count;
            result.FieldsWithAI = result.ProcessedFields.Count - result.FieldsWithFallback.Count;

            return result;
        }

        /// <summary>
        /// Section 2.3: Best-Guess Structural Tagging and Reading Order
        /// Creates initial accessibility structure for human review
        /// </summary>
        public async Task<DocumentTagStructure> GenerateTagStructureAsync(PdfLoadedDocument document)
        {
            _logger.LogInformation("Generating best-guess document tag structure");
            
            var structure = new DocumentTagStructure();

            try
            {
                // Analyze document layout to identify logical roles
                var layoutAnalysis = await _layoutAnalyzer.AnalyzeDocumentLayout(document);

                // Map logical roles to PDF accessibility tags
                foreach (var element in layoutAnalysis.Elements)
                {
                    var tag = MapLogicalRoleToTag(element.Role);
                    structure.Tags.Add(new TagInfo
                    {
                        Type = tag,
                        Content = element.Content,
                        PageNumber = element.PageNumber,
                        BoundingBox = new FieldBounds 
                        {
                            X = element.Bounds.X,
                            Y = element.Bounds.Y,
                            Width = element.Bounds.Width,
                            Height = element.Bounds.Height
                        },
                        ConfidenceScore = element.Confidence,
                        LogicalRole = element.Role
                    });
                }

                // Determine logical reading order
                structure.ReadingOrder = DetermineReadingOrder(structure.Tags);

                // Build hierarchical tag tree
                structure.TagTree = BuildTagHierarchy(structure.Tags, structure.ReadingOrder);

                // Flag potential accessibility issues
                structure.AccessibilityIssues = IdentifyStructuralIssues(structure.TagTree);

                structure.Success = true;
                _logger.LogInformation($"Generated tag structure with {structure.Tags.Count} tags");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate tag structure");
                structure.Success = false;
                structure.ErrorMessage = ex.Message;
            }

            return structure;
        }

        // Helper methods for field classification and NLP processing

        private string ClassifyFieldType(PdfLoadedField field)
        {
            return field switch
            {
                PdfLoadedTextBoxField => "text",
                PdfLoadedCheckBoxField => "checkbox",
                PdfLoadedRadioButtonListField => "radio",
                PdfLoadedComboBoxField => "dropdown",
                PdfLoadedListBoxField => "listbox",
                PdfLoadedSignatureField => "signature",
                _ => "unknown"
            };
        }

        private FieldBounds GetFieldBounds(PdfLoadedField field)
        {
            // Different field types have bounds in different properties
            if (field is PdfLoadedTextBoxField textField)
            {
                return new FieldBounds
                {
                    X = textField.Bounds.X,
                    Y = textField.Bounds.Y,
                    Width = textField.Bounds.Width,
                    Height = textField.Bounds.Height
                };
            }
            else if (field is PdfLoadedCheckBoxField checkField)
            {
                return new FieldBounds
                {
                    X = checkField.Bounds.X,
                    Y = checkField.Bounds.Y,
                    Width = checkField.Bounds.Width,
                    Height = checkField.Bounds.Height
                };
            }
            else if (field is PdfLoadedRadioButtonListField radioField)
            {
                // Radio fields might have different structure
                return new FieldBounds
                {
                    X = 0,
                    Y = 0,
                    Width = 100,
                    Height = 20
                };
            }
            else if (field is PdfLoadedComboBoxField comboField)
            {
                return new FieldBounds
                {
                    X = comboField.Bounds.X,
                    Y = comboField.Bounds.Y,
                    Width = comboField.Bounds.Width,
                    Height = comboField.Bounds.Height
                };
            }
            
            // Default bounds
            return new FieldBounds
            {
                X = 0,
                Y = 0,
                Width = 100,
                Height = 20
            };
        }

        private async Task<string> ExtractNearbyLabelText(PdfLoadedField field, PdfLoadedDocument document)
        {
            // This would use text extraction to find label text near the field
            // For now, returning a placeholder
            return field.Name;
        }

        private string DetectNamedEntity(string text)
        {
            var lowerText = text.ToLower();
            
            if (PersonEntities.Any(e => lowerText.Contains(e)))
                return "PERSON";
            if (LocationEntities.Any(e => lowerText.Contains(e)))
                return "LOCATION";
            if (DateEntities.Any(e => lowerText.Contains(e)))
                return "DATE";
            if (ContactEntities.Any(e => lowerText.Contains(e)))
                return "CONTACT";
            
            return "UNKNOWN";
        }

        private string DetectDataFormat(string text)
        {
            foreach (var pattern in DataPatterns)
            {
                if (pattern.Value.IsMatch(text))
                    return pattern.Key;
            }
            return "TEXT";
        }

        private string NormalizeToProgrammaticName(string text)
        {
            // Text preprocessing and normalization
            // 1. Remove special characters
            var normalized = Regex.Replace(text, @"[^\w\s]", "");
            
            // 2. Convert to lowercase
            normalized = normalized.ToLower();
            
            // 3. Remove stop words
            var stopWords = new[] { "the", "a", "an", "is", "are", "was", "were", "been", "be", "have", "has", "had", "do", "does", "did", "will", "would", "could", "should", "may", "might", "must", "can", "shall" };
            var words = normalized.Split(' ').Where(w => !stopWords.Contains(w) && !string.IsNullOrWhiteSpace(w));
            
            // 4. Join with underscores
            return string.Join("_", words);
        }

        private void GroupRadioButtons(List<FieldInfo> fields)
        {
            var radioFields = fields.Where(f => f.FieldType == "radio").ToList();
            var groups = new Dictionary<string, List<FieldInfo>>();

            foreach (var field in radioFields)
            {
                // Group by name prefix (before underscore or number)
                var groupName = Regex.Replace(field.OriginalName, @"[_\d]+$", "");
                if (!groups.ContainsKey(groupName))
                    groups[groupName] = new List<FieldInfo>();
                groups[groupName].Add(field);
            }

            foreach (var group in groups)
            {
                foreach (var field in group.Value)
                {
                    field.RadioGroupName = group.Key;
                    field.RadioGroupSize = group.Value.Count;
                }
            }
        }

        private async Task IdentifyRequiredFieldsAsync(List<FieldInfo> fields, PdfLoadedDocument document)
        {
            foreach (var field in fields)
            {
                // Check for visual indicators like asterisks
                if (field.VisualLabel.Contains("*") || 
                    field.VisualLabel.Contains("required", StringComparison.OrdinalIgnoreCase) ||
                    field.VisualLabel.Contains("mandatory", StringComparison.OrdinalIgnoreCase))
                {
                    field.IsRequired = true;
                }
            }
        }

        private async Task<string> ExtractSectionContext(FieldInfo field, DocumentContext context)
        {
            // Find the nearest heading above this field
            var headings = context.Headings.Where(h => h.PageNumber <= field.PageNumber).ToList();
            if (headings.Any())
            {
                return headings.Last().Text;
            }
            return "";
        }

        private async Task<TooltipInfo> GenerateContextualTooltip(FieldInfo field, HierarchicalContext context)
        {
            var tooltip = new TooltipInfo();

            // Use AI service if available, otherwise use smart defaults
            var aiEnabled = _llamaService != null;
            if (aiEnabled)
            {
                var prompt = BuildTooltipPrompt(field, context);
                tooltip.Description = await _llamaService.GenerateFieldDescriptionAsync(prompt, field.FieldType);
            }
            else
            {
                tooltip.Description = GenerateSmartTooltip(field, context);
            }

            // Add format hints based on field type and detected format
            tooltip.FormatHint = GenerateFormatHint(field);

            // Add instructional text for complex fields
            if (field.FieldType == "dropdown" || field.FieldType == "listbox")
            {
                tooltip.Instructions = $"Select one option from the list of {field.Options?.Count ?? 0} choices";
            }
            else if (field.FieldType == "radio")
            {
                tooltip.Instructions = $"Select one option from this group of {field.RadioGroupSize} choices";
            }

            return tooltip;
        }

        private string BuildTooltipPrompt(FieldInfo field, HierarchicalContext context)
        {
            return $"Given a form titled '{context.DocumentLevel}' " +
                   $"and a section titled '{context.SectionLevel}', " +
                   $"generate a helpful, one-sentence tooltip for a field labeled '{field.ProgrammaticName}'";
        }

        private string GenerateSmartTooltip(FieldInfo field, HierarchicalContext context)
        {
            // Generate intelligent tooltips based on field analysis
            var tooltip = $"Enter {field.VisualLabel}";

            switch (field.DataFormat)
            {
                case "SSN":
                    return "Enter the nine-digit Social Security Number (SSN). Do not include dashes or spaces.";
                case "Phone":
                    return "Enter a 10-digit phone number including area code.";
                case "Email":
                    return "Enter a valid email address (example: name@domain.com).";
                case "ZIP":
                    return "Enter the 5-digit ZIP code or ZIP+4 format (example: 12345 or 12345-6789).";
                case "Date":
                    return "Enter the date in MM/DD/YYYY format.";
                case "Currency":
                    return "Enter the dollar amount without the currency symbol.";
                default:
                    if (field.IsRequired)
                        tooltip += " (Required)";
                    if (field.MaxLength > 0)
                        tooltip += $" Maximum {field.MaxLength} characters.";
                    return tooltip;
            }
        }

        private string GenerateBasicTooltip(FieldInfo field)
        {
            return $"Enter information in the {field.VisualLabel} field";
        }

        private string GenerateFormatHint(FieldInfo field)
        {
            return field.DataFormat switch
            {
                "SSN" => "Format: 123-45-6789",
                "Phone" => "Format: (123) 456-7890",
                "Email" => "Format: user@example.com",
                "ZIP" => "Format: 12345 or 12345-6789",
                "Date" => "Format: MM/DD/YYYY",
                "Currency" => "Format: 1234.56",
                _ => ""
            };
        }

        private string MapLogicalRoleToTag(string role)
        {
            return role.ToLower() switch
            {
                "title" => "H1",
                "heading1" => "H1",
                "heading2" => "H2",
                "heading3" => "H3",
                "sectionheading" => "H2",
                "paragraph" => "P",
                "list_item" => "LI",
                "table" => "Table",
                "table_row" => "TR",
                "table_cell" => "TD",
                "table_header" => "TH",
                "figure" => "Figure",
                "caption" => "Caption",
                _ => "P"
            };
        }

        private List<int> DetermineReadingOrder(List<TagInfo> tags)
        {
            // Sort tags by page, then top-to-bottom, left-to-right
            var orderedTags = tags
                .Select((tag, index) => new { Tag = tag, Index = index })
                .OrderBy(t => t.Tag.PageNumber)
                .ThenBy(t => t.Tag.BoundingBox.Y)
                .ThenBy(t => t.Tag.BoundingBox.X)
                .Select(t => t.Index)
                .ToList();

            return orderedTags;
        }

        private TagNode BuildTagHierarchy(List<TagInfo> tags, List<int> readingOrder)
        {
            var root = new TagNode { Type = "Document", Children = new List<TagNode>() };
            var currentParent = root;
            var headingStack = new Stack<TagNode>();

            foreach (var index in readingOrder)
            {
                var tag = tags[index];
                var node = new TagNode
                {
                    Type = tag.Type,
                    Content = tag.Content,
                    TagInfo = tag,
                    Children = new List<TagNode>()
                };

                // Build hierarchy based on heading levels
                if (tag.Type.StartsWith("H"))
                {
                    var level = int.Parse(tag.Type.Substring(1));
                    
                    // Pop stack until we find appropriate parent
                    while (headingStack.Count > 0 && GetHeadingLevel(headingStack.Peek().Type) >= level)
                    {
                        headingStack.Pop();
                    }

                    currentParent = headingStack.Count > 0 ? headingStack.Peek() : root;
                    headingStack.Push(node);
                }

                currentParent.Children.Add(node);
            }

            return root;
        }

        private int GetHeadingLevel(string tagType)
        {
            if (tagType.StartsWith("H") && tagType.Length == 2)
                return int.Parse(tagType.Substring(1));
            return 0;
        }

        private List<AccessibilityIssue> IdentifyStructuralIssues(TagNode root)
        {
            var issues = new List<AccessibilityIssue>();

            // Check for skipped heading levels
            CheckHeadingHierarchy(root, issues);

            // Check for tables without headers
            CheckTableStructure(root, issues);

            // Check for lists without proper structure
            CheckListStructure(root, issues);

            // Check for images without alt text
            CheckImageAltText(root, issues);

            return issues;
        }

        private void CheckHeadingHierarchy(TagNode node, List<AccessibilityIssue> issues)
        {
            var headings = new List<TagNode>();
            CollectHeadings(node, headings);

            for (int i = 1; i < headings.Count; i++)
            {
                var prevLevel = GetHeadingLevel(headings[i - 1].Type);
                var currLevel = GetHeadingLevel(headings[i].Type);

                if (currLevel > prevLevel + 1)
                {
                    issues.Add(new AccessibilityIssue
                    {
                        Type = "SkippedHeadingLevel",
                        Severity = "Warning",
                        Description = $"Heading level skipped from H{prevLevel} to H{currLevel}",
                        Location = headings[i].TagInfo?.PageNumber ?? 0
                    });
                }
            }
        }

        private void CollectHeadings(TagNode node, List<TagNode> headings)
        {
            if (node.Type.StartsWith("H"))
                headings.Add(node);

            foreach (var child in node.Children)
                CollectHeadings(child, headings);
        }

        private void CheckTableStructure(TagNode node, List<AccessibilityIssue> issues)
        {
            if (node.Type == "Table")
            {
                var hasHeader = node.Children.Any(c => c.Children.Any(cc => cc.Type == "TH"));
                if (!hasHeader)
                {
                    issues.Add(new AccessibilityIssue
                    {
                        Type = "TableWithoutHeader",
                        Severity = "Error",
                        Description = "Table lacks header row with TH elements",
                        Location = node.TagInfo?.PageNumber ?? 0
                    });
                }
            }

            foreach (var child in node.Children)
                CheckTableStructure(child, issues);
        }

        private void CheckListStructure(TagNode node, List<AccessibilityIssue> issues)
        {
            if (node.Type == "LI" && node.Parent?.Type != "L")
            {
                issues.Add(new AccessibilityIssue
                {
                    Type = "ListItemWithoutList",
                    Severity = "Error",
                    Description = "List item (LI) not contained within a list (L)",
                    Location = node.TagInfo?.PageNumber ?? 0
                });
            }

            foreach (var child in node.Children)
                CheckListStructure(child, issues);
        }

        private void CheckImageAltText(TagNode node, List<AccessibilityIssue> issues)
        {
            if (node.Type == "Figure" && string.IsNullOrEmpty(node.TagInfo?.AltText))
            {
                issues.Add(new AccessibilityIssue
                {
                    Type = "ImageWithoutAltText",
                    Severity = "Error",
                    Description = "Image lacks alternative text description",
                    Location = node.TagInfo?.PageNumber ?? 0
                });
            }

            foreach (var child in node.Children)
                CheckImageAltText(child, issues);
        }
    }

    // Supporting classes and data structures

    public class FormFieldAnalysisResult
    {
        public bool Success { get; set; }
        public List<FieldInfo> Fields { get; set; } = new();
        public string ErrorMessage { get; set; }
    }

    public class FieldInfo
    {
        public string OriginalName { get; set; }
        public string ProgrammaticName { get; set; }
        public string VisualLabel { get; set; }
        public string FieldType { get; set; }
        public string DetectedEntity { get; set; }
        public string DataFormat { get; set; }
        public string AccessibleDescription { get; set; }
        public string InstructionalText { get; set; }
        public string FormatHint { get; set; }
        public int PageNumber { get; set; }
        public FieldBounds Bounds { get; set; }
        public bool IsRequired { get; set; }
        public string DefaultValue { get; set; }
        public int MaxLength { get; set; }
        public bool IsChecked { get; set; }
        public string RadioGroupName { get; set; }
        public int RadioGroupSize { get; set; }
        public List<string> Options { get; set; }
        public string SelectedValue { get; set; }
    }

    public class FieldBounds
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
    }

    public class DocumentContext
    {
        public string DocumentTitle { get; set; }
        public List<HeadingInfo> Headings { get; set; } = new();
        public List<SectionInfo> Sections { get; set; } = new();
    }

    public class HeadingInfo
    {
        public string Text { get; set; }
        public int Level { get; set; }
        public int PageNumber { get; set; }
    }

    public class SectionInfo
    {
        public string Title { get; set; }
        public int StartPage { get; set; }
        public int EndPage { get; set; }
    }

    public class HierarchicalContext
    {
        public string FieldLevel { get; set; }
        public string SectionLevel { get; set; }
        public string DocumentLevel { get; set; }
    }

    public class TooltipGenerationResult
    {
        public bool Success { get; set; }
        public int TotalFields { get; set; }
        public int FieldsWithAI { get; set; }
        public List<FieldInfo> ProcessedFields { get; set; } = new();
        public List<string> FieldsWithFallback { get; set; } = new();
    }

    public class TooltipInfo
    {
        public string Description { get; set; }
        public string Instructions { get; set; }
        public string FormatHint { get; set; }
    }

    public class DocumentTagStructure
    {
        public bool Success { get; set; }
        public List<TagInfo> Tags { get; set; } = new();
        public List<int> ReadingOrder { get; set; } = new();
        public TagNode TagTree { get; set; }
        public List<AccessibilityIssue> AccessibilityIssues { get; set; } = new();
        public string ErrorMessage { get; set; }
    }

    public class TagInfo
    {
        public string Type { get; set; }
        public string Content { get; set; }
        public int PageNumber { get; set; }
        public FieldBounds BoundingBox { get; set; }
        public float ConfidenceScore { get; set; }
        public string LogicalRole { get; set; }
        public string AltText { get; set; }
    }

    public class TagNode
    {
        public string Type { get; set; }
        public string Content { get; set; }
        public TagInfo TagInfo { get; set; }
        public List<TagNode> Children { get; set; } = new();
        public TagNode Parent { get; set; }
    }

    public class AccessibilityIssue
    {
        public string Type { get; set; }
        public string Severity { get; set; }
        public string Description { get; set; }
        public int Location { get; set; }
    }
}
