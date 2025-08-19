using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AccessFormServer.Services
{
    /// <summary>
    /// NLP-based label and tooltip generator implementing Section 2.2 of the specification
    /// Handles both semantic normalization and descriptive expansion
    /// </summary>
    public class NLPLabelGenerator
    {
        private readonly ILogger<NLPLabelGenerator> _logger;
        private readonly LlamaGroqService _aiService;

        // Common stop words for text normalization
        private static readonly HashSet<string> StopWords = new()
        {
            "the", "a", "an", "is", "are", "was", "were", "been", "be",
            "have", "has", "had", "do", "does", "did", "will", "would",
            "could", "should", "may", "might", "must", "can", "shall",
            "to", "of", "in", "for", "on", "at", "by", "with", "from",
            "as", "this", "that", "these", "those", "it", "its"
        };

        // Domain-specific entity patterns
        private static readonly Dictionary<string, List<string>> DomainEntities = new()
        {
            ["PERSON"] = new() { "name", "first", "last", "middle", "initial", "surname", "given" },
            ["ADDRESS"] = new() { "address", "street", "avenue", "road", "lane", "drive", "court" },
            ["FINANCIAL"] = new() { "account", "routing", "bank", "income", "salary", "wage", "tax" },
            ["MEDICAL"] = new() { "diagnosis", "prescription", "medication", "condition", "allergy" },
            ["LEGAL"] = new() { "case", "docket", "plaintiff", "defendant", "attorney", "court" },
            ["EDUCATION"] = new() { "degree", "school", "university", "gpa", "transcript", "major" }
        };

        // Format-specific validation patterns
        private static readonly Dictionary<string, string> ValidationPatterns = new()
        {
            ["SSN"] = @"^\d{3}-\d{2}-\d{4}$",
            ["EIN"] = @"^\d{2}-\d{7}$",
            ["Phone"] = @"^\(\d{3}\) \d{3}-\d{4}$",
            ["Email"] = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
            ["ZIP"] = @"^\d{5}(-\d{4})?$",
            ["Date"] = @"^\d{2}/\d{2}/\d{4}$"
        };

        public NLPLabelGenerator(ILogger<NLPLabelGenerator> logger, LlamaGroqService aiService = null)
        {
            _logger = logger;
            _aiService = aiService;
        }

        /// <summary>
        /// Generate both programmatic name and descriptive tooltip for a field
        /// Implements dual-output NLP as specified in Section 2.2
        /// </summary>
        public async Task<LabelGenerationResult> GenerateLabelsAsync(string visualLabel, string fieldType, FieldContext context)
        {
            var result = new LabelGenerationResult();

            try
            {
                // Branch 1: Semantic Normalization for programmatic name
                result.ProgrammaticName = GenerateProgrammaticName(visualLabel, context);

                // Branch 2: Descriptive Expansion for tooltip
                result.Tooltip = await GenerateDescriptiveTooltipAsync(visualLabel, fieldType, context);

                // Generate validation rules based on detected patterns
                result.ValidationRules = GenerateValidationRules(visualLabel, fieldType);

                // Detect data format for additional hints
                result.DataFormat = DetectDataFormat(visualLabel);

                // Calculate confidence score
                result.ConfidenceScore = CalculateConfidence(visualLabel, result);

                result.Success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to generate labels for '{visualLabel}'");
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Semantic normalization to create standardized programmatic names
        /// </summary>
        private string GenerateProgrammaticName(string visualLabel, FieldContext context)
        {
            if (string.IsNullOrWhiteSpace(visualLabel))
                return "field_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            // Step 1: Clean and tokenize
            var cleaned = Regex.Replace(visualLabel, @"[^\w\s]", " ");
            var tokens = cleaned.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Step 2: Remove stop words
            tokens = tokens.Where(t => !StopWords.Contains(t)).ToArray();

            // Step 3: Apply lemmatization (simplified)
            tokens = tokens.Select(t => Lemmatize(t)).ToArray();

            // Step 4: Detect and preserve important patterns
            if (context != null && !string.IsNullOrEmpty(context.SectionName))
            {
                var sectionPrefix = NormalizeSectionName(context.SectionName);
                if (!string.IsNullOrEmpty(sectionPrefix))
                {
                    tokens = new[] { sectionPrefix }.Concat(tokens).ToArray();
                }
            }

            // Step 5: Join with underscores
            var name = string.Join("_", tokens.Where(t => !string.IsNullOrWhiteSpace(t)));

            // Ensure valid identifier
            if (string.IsNullOrEmpty(name) || char.IsDigit(name[0]))
                name = "field_" + name;

            return name.Length > 50 ? name.Substring(0, 50) : name;
        }

        /// <summary>
        /// Generate context-aware, instructional tooltips
        /// </summary>
        private async Task<TooltipContent> GenerateDescriptiveTooltipAsync(string visualLabel, string fieldType, FieldContext context)
        {
            var tooltip = new TooltipContent();

            // Use AI service if available
            if (_aiService != null && context?.UseAI == true)
            {
                try
                {
                    var aiPrompt = BuildAIPrompt(visualLabel, fieldType, context);
                    var aiResponse = await _aiService.GenerateFieldDescriptionAsync(aiPrompt, fieldType);
                    
                    if (!string.IsNullOrEmpty(aiResponse))
                    {
                        tooltip.Primary = aiResponse;
                        tooltip.IsAIGenerated = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AI tooltip generation failed, using fallback");
                }
            }

            // Fallback to intelligent rule-based generation
            if (string.IsNullOrEmpty(tooltip.Primary))
            {
                tooltip = GenerateRuleBasedTooltip(visualLabel, fieldType, context);
            }

            // Add format hints
            tooltip.FormatHint = GenerateFormatHint(visualLabel, fieldType);

            // Add contextual instructions
            if (context?.IsRequired == true)
            {
                tooltip.RequiredIndicator = "This field is required";
            }

            return tooltip;
        }

        /// <summary>
        /// Generate intelligent rule-based tooltips when AI is unavailable
        /// </summary>
        private TooltipContent GenerateRuleBasedTooltip(string visualLabel, string fieldType, FieldContext context)
        {
            var tooltip = new TooltipContent();
            var cleanLabel = CleanLabel(visualLabel);

            // Detect field purpose from label
            var purpose = DetectFieldPurpose(cleanLabel);

            switch (purpose)
            {
                case "SSN":
                    tooltip.Primary = "Enter your nine-digit Social Security Number";
                    tooltip.Secondary = "Format: 123-45-6789. Do not include spaces.";
                    break;

                case "EMAIL":
                    tooltip.Primary = "Enter your email address";
                    tooltip.Secondary = "Format: name@example.com";
                    break;

                case "PHONE":
                    tooltip.Primary = "Enter your phone number including area code";
                    tooltip.Secondary = "Format: (123) 456-7890";
                    break;

                case "DATE":
                    tooltip.Primary = $"Enter the {cleanLabel.ToLower()}";
                    tooltip.Secondary = "Format: MM/DD/YYYY";
                    break;

                case "NAME":
                    tooltip.Primary = $"Enter your {cleanLabel.ToLower()}";
                    tooltip.Secondary = "Use your legal name as it appears on official documents";
                    break;

                case "ADDRESS":
                    tooltip.Primary = "Enter your complete mailing address";
                    tooltip.Secondary = "Include apartment or unit number if applicable";
                    break;

                case "CURRENCY":
                    tooltip.Primary = $"Enter the {cleanLabel.ToLower()} amount";
                    tooltip.Secondary = "Enter numbers only, without dollar sign or commas";
                    break;

                default:
                    tooltip.Primary = $"Enter {cleanLabel.ToLower()}";
                    if (fieldType == "dropdown" || fieldType == "select")
                    {
                        tooltip.Secondary = "Select one option from the list";
                    }
                    else if (fieldType == "checkbox")
                    {
                        tooltip.Secondary = "Check this box if applicable";
                    }
                    else if (fieldType == "radio")
                    {
                        tooltip.Secondary = "Select one option";
                    }
                    break;
            }

            // Add context-specific information
            if (context != null)
            {
                if (!string.IsNullOrEmpty(context.SectionName))
                {
                    tooltip.Context = $"Part of {context.SectionName} section";
                }

                if (context.MaxLength > 0)
                {
                    tooltip.Constraints = $"Maximum {context.MaxLength} characters";
                }
            }

            return tooltip;
        }

        /// <summary>
        /// Generate validation rules based on field analysis
        /// </summary>
        private List<ValidationRule> GenerateValidationRules(string visualLabel, string fieldType)
        {
            var rules = new List<ValidationRule>();
            var purpose = DetectFieldPurpose(visualLabel);

            // Add pattern validation
            if (ValidationPatterns.ContainsKey(purpose))
            {
                rules.Add(new ValidationRule
                {
                    Type = "pattern",
                    Value = ValidationPatterns[purpose],
                    Message = $"Please enter a valid {purpose.ToLower()}"
                });
            }

            // Add type-specific validation
            switch (fieldType.ToLower())
            {
                case "email":
                    rules.Add(new ValidationRule
                    {
                        Type = "email",
                        Message = "Please enter a valid email address"
                    });
                    break;

                case "date":
                    rules.Add(new ValidationRule
                    {
                        Type = "date",
                        Message = "Please enter a valid date"
                    });
                    break;

                case "number":
                    rules.Add(new ValidationRule
                    {
                        Type = "numeric",
                        Message = "Please enter numbers only"
                    });
                    break;
            }

            return rules;
        }

        // Helper methods

        private string Lemmatize(string word)
        {
            // Simplified lemmatization
            if (word.EndsWith("ies"))
                return word.Substring(0, word.Length - 3) + "y";
            if (word.EndsWith("es"))
                return word.Substring(0, word.Length - 2);
            if (word.EndsWith("s") && word.Length > 3)
                return word.Substring(0, word.Length - 1);
            if (word.EndsWith("ed"))
                return word.Substring(0, word.Length - 2);
            if (word.EndsWith("ing"))
                return word.Substring(0, word.Length - 3);
            return word;
        }

        private string NormalizeSectionName(string sectionName)
        {
            if (string.IsNullOrEmpty(sectionName))
                return "";

            var normalized = Regex.Replace(sectionName, @"[^\w\s]", "");
            var words = normalized.ToLower().Split(' ').Take(2);
            return string.Join("_", words.Where(w => !StopWords.Contains(w)));
        }

        private string CleanLabel(string label)
        {
            return Regex.Replace(label, @"[*:_\-]", " ").Trim();
        }

        private string DetectFieldPurpose(string label)
        {
            var lower = label.ToLower();

            if (lower.Contains("ssn") || lower.Contains("social security"))
                return "SSN";
            if (lower.Contains("email") || lower.Contains("e-mail"))
                return "EMAIL";
            if (lower.Contains("phone") || lower.Contains("telephone") || lower.Contains("mobile"))
                return "PHONE";
            if (lower.Contains("date") || lower.Contains("dob") || lower.Contains("birth"))
                return "DATE";
            if (lower.Contains("name") && !lower.Contains("company") && !lower.Contains("organization"))
                return "NAME";
            if (lower.Contains("address") || lower.Contains("street") || lower.Contains("city") || lower.Contains("zip"))
                return "ADDRESS";
            if (lower.Contains("amount") || lower.Contains("price") || lower.Contains("salary") || lower.Contains("income"))
                return "CURRENCY";

            // Check domain entities
            foreach (var domain in DomainEntities)
            {
                if (domain.Value.Any(term => lower.Contains(term)))
                    return domain.Key;
            }

            return "GENERAL";
        }

        private string DetectDataFormat(string label)
        {
            var purpose = DetectFieldPurpose(label);
            return purpose switch
            {
                "SSN" => "###-##-####",
                "EMAIL" => "email",
                "PHONE" => "(###) ###-####",
                "DATE" => "MM/DD/YYYY",
                "CURRENCY" => "currency",
                _ => "text"
            };
        }

        private string GenerateFormatHint(string label, string fieldType)
        {
            var purpose = DetectFieldPurpose(label);
            return purpose switch
            {
                "SSN" => "123-45-6789",
                "EMAIL" => "john.doe@example.com",
                "PHONE" => "(555) 123-4567",
                "DATE" => "01/31/2024",
                "CURRENCY" => "1234.56",
                _ => ""
            };
        }

        private float CalculateConfidence(string visualLabel, LabelGenerationResult result)
        {
            float confidence = 0.5f;

            // Increase confidence for known patterns
            if (!string.IsNullOrEmpty(result.DataFormat) && result.DataFormat != "text")
                confidence += 0.2f;

            // Increase confidence for successful programmatic name generation
            if (!result.ProgrammaticName.StartsWith("field_"))
                confidence += 0.15f;

            // Increase confidence if validation rules were generated
            if (result.ValidationRules.Any())
                confidence += 0.15f;

            return Math.Min(1.0f, confidence);
        }

        private string BuildAIPrompt(string visualLabel, string fieldType, FieldContext context)
        {
            var prompt = $"Generate a helpful, accessible tooltip for a form field.\n";
            prompt += $"Field Label: {visualLabel}\n";
            prompt += $"Field Type: {fieldType}\n";

            if (context != null)
            {
                if (!string.IsNullOrEmpty(context.DocumentTitle))
                    prompt += $"Form Title: {context.DocumentTitle}\n";
                if (!string.IsNullOrEmpty(context.SectionName))
                    prompt += $"Section: {context.SectionName}\n";
                if (context.IsRequired)
                    prompt += "This field is required.\n";
            }

            prompt += "Generate a clear, instructional tooltip that helps users understand what to enter.";
            return prompt;
        }
    }

    // Supporting classes

    public class LabelGenerationResult
    {
        public bool Success { get; set; }
        public string ProgrammaticName { get; set; }
        public TooltipContent Tooltip { get; set; }
        public List<ValidationRule> ValidationRules { get; set; } = new();
        public string DataFormat { get; set; }
        public float ConfidenceScore { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class TooltipContent
    {
        public string Primary { get; set; }
        public string Secondary { get; set; }
        public string FormatHint { get; set; }
        public string RequiredIndicator { get; set; }
        public string Context { get; set; }
        public string Constraints { get; set; }
        public bool IsAIGenerated { get; set; }
    }

    public class FieldContext
    {
        public string DocumentTitle { get; set; }
        public string SectionName { get; set; }
        public bool IsRequired { get; set; }
        public int MaxLength { get; set; }
        public bool UseAI { get; set; }
        public List<string> NearbyLabels { get; set; } = new();
    }
}
