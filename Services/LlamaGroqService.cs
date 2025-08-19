using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace AccessFormServer.Services
{
    public class LlamaGroqService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<LlamaGroqService> _logger;
        private readonly IMemoryCache _cache;
        private readonly CostTrackingService _costTracking;
        private readonly HttpClient _httpClient;
        private readonly bool _enabled;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _baseUrl;
        private readonly int _maxTokens;
        private readonly float _temperature;

        public LlamaGroqService(
            IConfiguration configuration, 
            ILogger<LlamaGroqService> logger,
            IMemoryCache cache,
            CostTrackingService costTracking,
            IHttpClientFactory httpClientFactory = null)
        {
            _configuration = configuration;
            _logger = logger;
            _cache = cache;
            _costTracking = costTracking;
            
            _enabled = _configuration.GetValue<bool>("AiServices:LlamaGroq:Enabled", false);
            _apiKey = _configuration["AiServices:LlamaGroq:ApiKey"];
            _model = _configuration["AiServices:LlamaGroq:Model"] ?? "llama-3.3-70b-versatile";
            _baseUrl = _configuration["AiServices:LlamaGroq:BaseUrl"] ?? "https://api.groq.com/openai/v1";
            _maxTokens = _configuration.GetValue<int>("AiServices:LlamaGroq:MaxTokens", 4096);
            _temperature = _configuration.GetValue<float>("AiServices:LlamaGroq:Temperature", 0.3f);
            
            _httpClient = httpClientFactory?.CreateClient() ?? new HttpClient();
            
            if (_enabled && !string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new AuthenticationHeaderValue("Bearer", _apiKey);
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                _logger.LogInformation("Llama Groq service initialized with model: {Model}", _model);
            }
            else
            {
                _logger.LogWarning("Llama Groq service is disabled or not configured");
            }
        }

        public async Task<FormAnalysisResult> AnalyzeFormStructureAsync(string formContent, CancellationToken cancellationToken = default)
        {
            if (!_enabled || string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogInformation("Groq API not configured, using algorithmic analysis");
                return GetAlgorithmicAnalysis(formContent);
            }

            // Check cost limit
            if (!await _costTracking.CanProcessRequestAsync(0.02m))
            {
                _logger.LogWarning("Daily cost limit reached, using algorithmic approach");
                return GetAlgorithmicAnalysis(formContent);
            }

            try
            {
                var prompt = BuildFormAnalysisPrompt(formContent);
                var response = await CallGroqApiAsync(prompt, cancellationToken);
                
                if (response != null)
                {
                    var result = ParseFormAnalysisResponse(response);
                    result.AiProvider = "LlamaGroq";
                    
                    // Track cost
                    await _costTracking.RecordCostAsync("LlamaGroq", 0.02m);
                    
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze form with Groq API");
            }

            return GetAlgorithmicAnalysis(formContent);
        }

        public async Task<string> GenerateFieldDescriptionAsync(string fieldContext, string fieldType, CancellationToken cancellationToken = default)
        {
            if (!_enabled || string.IsNullOrEmpty(_apiKey))
            {
                return GetDefaultDescription(fieldType);
            }

            try
            {
                var prompt = $@"Generate a helpful, accessible tooltip description for a form field.
Field Context: {fieldContext}
Field Type: {fieldType}

Provide a clear, concise description that helps users understand what to enter.
Include format hints if applicable (e.g., date format, phone format).
Keep it under 100 characters.

Tooltip:";

                var response = await CallGroqApiAsync(prompt, cancellationToken);
                if (!string.IsNullOrEmpty(response))
                {
                    return response.Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate field description");
            }

            return GetDefaultDescription(fieldType);
        }

        public async Task<List<ValidationRule>> SuggestValidationRulesAsync(string fieldLabel, string fieldType, CancellationToken cancellationToken = default)
        {
            if (!_enabled || string.IsNullOrEmpty(_apiKey))
            {
                return GetDefaultValidationRules(fieldType);
            }

            try
            {
                var prompt = $@"Suggest validation rules for a form field.
Field Label: {fieldLabel}
Field Type: {fieldType}

Provide validation rules in JSON format with 'type', 'value', and 'message' fields.
Example: [{{""type"": ""required"", ""value"": true, ""message"": ""This field is required""}}]

Rules:";

                var response = await CallGroqApiAsync(prompt, cancellationToken);
                if (!string.IsNullOrEmpty(response))
                {
                    return ParseValidationRules(response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to suggest validation rules");
            }

            return GetDefaultValidationRules(fieldType);
        }

        private async Task<string> CallGroqApiAsync(string prompt, CancellationToken cancellationToken)
        {
            try
            {
                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "system", content = "You are an expert in PDF accessibility and form field analysis. Provide concise, accurate responses." },
                        new { role = "user", content = prompt }
                    },
                    max_tokens = _maxTokens,
                    temperature = _temperature
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/chat/completions", content, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Groq API error: {StatusCode} - {Error}", response.StatusCode, error);
                    return null;
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var responseObj = JsonDocument.Parse(responseJson);
                
                if (responseObj.RootElement.TryGetProperty("choices", out var choices))
                {
                    var firstChoice = choices.EnumerateArray().FirstOrDefault();
                    if (firstChoice.ValueKind != JsonValueKind.Undefined)
                    {
                        if (firstChoice.TryGetProperty("message", out var message))
                        {
                            if (message.TryGetProperty("content", out var contentProp))
                            {
                                return contentProp.GetString();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Groq API");
            }

            return null;
        }

        private string BuildFormAnalysisPrompt(string formContent)
        {
            return $@"Analyze this form content for accessibility issues and structure.

Form Content:
{formContent.Substring(0, Math.Min(formContent.Length, 2000))}

Provide analysis in the following areas:
1. Identify all form fields and their types
2. Detect any accessibility issues
3. Suggest improvements for WCAG 2.1 AA compliance
4. Identify conditional logic or field dependencies
5. Rate the overall accessibility (0-100)

Format your response as JSON with these keys:
- fields: array of field objects with name, type, label, and issues
- critical_issues: array of critical accessibility problems
- recommendations: array of improvement suggestions
- accessibility_score: number 0-100
- conditional_logic: array of detected dependencies

Response:";
        }

        private FormAnalysisResult ParseFormAnalysisResponse(string response)
        {
            var result = new FormAnalysisResult();

            try
            {
                // Try to parse as JSON first
                if (response.TrimStart().StartsWith("{"))
                {
                    var json = JsonDocument.Parse(response);
                    var root = json.RootElement;

                    if (root.TryGetProperty("fields", out var fields))
                    {
                        foreach (var field in fields.EnumerateArray())
                        {
                            var fieldInfo = new FormFieldInfo
                            {
                                Id = field.TryGetProperty("id", out var id) ? id.GetString() : Guid.NewGuid().ToString(),
                                Type = field.TryGetProperty("type", out var type) ? type.GetString() : "text",
                                Label = field.TryGetProperty("label", out var label) ? label.GetString() : "",
                                Required = field.TryGetProperty("required", out var req) && req.GetBoolean()
                            };

                            if (field.TryGetProperty("issues", out var issues))
                            {
                                foreach (var issue in issues.EnumerateArray())
                                {
                                    fieldInfo.AccessibilityIssues.Add(issue.GetString());
                                }
                            }

                            result.Fields.Add(fieldInfo);
                        }
                    }

                    if (root.TryGetProperty("critical_issues", out var criticalIssues))
                    {
                        foreach (var issue in criticalIssues.EnumerateArray())
                        {
                            result.CriticalIssues.Add(issue.GetString());
                        }
                    }

                    if (root.TryGetProperty("recommendations", out var recommendations))
                    {
                        foreach (var rec in recommendations.EnumerateArray())
                        {
                            result.Recommendations.Add(rec.GetString());
                        }
                    }

                    if (root.TryGetProperty("accessibility_score", out var score))
                    {
                        result.AccessibilityScore = score.GetInt32();
                    }
                }
                else
                {
                    // Parse as plain text response
                    result = ParsePlainTextResponse(response);
                }

                result.Success = true;
                result.ProcessingTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Groq API response");
                result = GetAlgorithmicAnalysis(response);
            }

            return result;
        }

        private FormAnalysisResult ParsePlainTextResponse(string response)
        {
            var result = new FormAnalysisResult();
            
            // Basic parsing of plain text response
            var lines = response.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("field", StringComparison.OrdinalIgnoreCase))
                {
                    result.Fields.Add(new FormFieldInfo
                    {
                        Id = Guid.NewGuid().ToString(),
                        Label = line.Trim()
                    });
                }
                else if (line.Contains("issue", StringComparison.OrdinalIgnoreCase))
                {
                    result.CriticalIssues.Add(line.Trim());
                }
                else if (line.Contains("recommend", StringComparison.OrdinalIgnoreCase))
                {
                    result.Recommendations.Add(line.Trim());
                }
            }
            
            result.AccessibilityScore = 75; // Default score
            return result;
        }

        private List<ValidationRule> ParseValidationRules(string response)
        {
            var rules = new List<ValidationRule>();

            try
            {
                if (response.TrimStart().StartsWith("["))
                {
                    var json = JsonDocument.Parse(response);
                    foreach (var element in json.RootElement.EnumerateArray())
                    {
                        rules.Add(new ValidationRule
                        {
                            Type = element.TryGetProperty("type", out var type) ? type.GetString() : "",
                            Value = element.TryGetProperty("value", out var value) ? value.GetString() : "",
                            Message = element.TryGetProperty("message", out var msg) ? msg.GetString() : ""
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse validation rules");
            }

            return rules.Any() ? rules : GetDefaultValidationRules("text");
        }

        private FormAnalysisResult GetAlgorithmicAnalysis(string formContent)
        {
            _logger.LogInformation("Using algorithmic form analysis");
            
            var fields = new List<FormFieldInfo>();
            var lines = formContent.Split('\n');
            
            foreach (var line in lines)
            {
                if (ContainsFieldIndicator(line))
                {
                    fields.Add(ExtractFieldInfo(line));
                }
            }

            return new FormAnalysisResult
            {
                Success = true,
                Fields = fields,
                ConditionalLogic = new List<ConditionalLogicRule>(),
                AccessibilityScore = CalculateBasicAccessibilityScore(fields),
                CriticalIssues = IdentifyBasicIssues(fields),
                Recommendations = GenerateBasicRecommendations(fields),
                ProcessingTime = DateTime.UtcNow,
                AiProvider = "Algorithmic"
            };
        }

        private bool ContainsFieldIndicator(string line)
        {
            var indicators = new[] { "___", "[ ]", "( )", "Name", "Date", "Email", "Phone" };
            return indicators.Any(ind => line.Contains(ind, StringComparison.OrdinalIgnoreCase));
        }

        private FormFieldInfo ExtractFieldInfo(string line)
        {
            return new FormFieldInfo
            {
                Id = Guid.NewGuid().ToString(),
                Type = DetermineFieldType(line),
                Label = ExtractLabel(line),
                Required = line.Contains("*") || line.Contains("required", StringComparison.OrdinalIgnoreCase)
            };
        }

        private string DetermineFieldType(string line)
        {
            if (line.Contains("[ ]")) return "checkbox";
            if (line.Contains("( )")) return "radio";
            if (line.Contains("Date", StringComparison.OrdinalIgnoreCase)) return "date";
            if (line.Contains("Email", StringComparison.OrdinalIgnoreCase)) return "email";
            if (line.Contains("Phone", StringComparison.OrdinalIgnoreCase)) return "phone";
            return "text";
        }

        private string ExtractLabel(string line)
        {
            var label = line;
            var indicators = new[] { "___", "[ ]", "( )", ":", "*" };
            foreach (var indicator in indicators)
            {
                label = label.Replace(indicator, "");
            }
            return label.Trim();
        }

        private int CalculateBasicAccessibilityScore(List<FormFieldInfo> fields)
        {
            if (!fields.Any()) return 100;
            var score = 100 - (fields.Count(f => string.IsNullOrWhiteSpace(f.Label)) * 5);
            return Math.Max(0, Math.Min(100, score));
        }

        private List<string> IdentifyBasicIssues(List<FormFieldInfo> fields)
        {
            var issues = new List<string>();
            var unlabeledCount = fields.Count(f => string.IsNullOrWhiteSpace(f.Label));
            if (unlabeledCount > 0)
            {
                issues.Add($"{unlabeledCount} fields missing labels");
            }
            return issues;
        }

        private List<string> GenerateBasicRecommendations(List<FormFieldInfo> fields)
        {
            return new List<string>
            {
                "Add descriptive labels to all form fields",
                "Ensure logical tab order",
                "Provide clear instructions",
                "Mark required fields clearly"
            };
        }

        private string GetDefaultDescription(string fieldType)
        {
            return fieldType switch
            {
                "email" => "Enter a valid email address",
                "phone" => "Enter a phone number",
                "date" => "Select or enter a date",
                _ => "Enter information in this field"
            };
        }

        private List<ValidationRule> GetDefaultValidationRules(string fieldType)
        {
            var rules = new List<ValidationRule>();
            if (fieldType == "email")
            {
                rules.Add(new ValidationRule { Type = "pattern", Value = @"^[^\s@]+@[^\s@]+\.[^\s@]+$" });
            }
            return rules;
        }
    }

    public class FormAnalysisResult
    {
        public bool Success { get; set; }
        public List<FormFieldInfo> Fields { get; set; } = new();
        public List<ConditionalLogicRule> ConditionalLogic { get; set; } = new();
        public int AccessibilityScore { get; set; }
        public List<string> CriticalIssues { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
        public DateTime ProcessingTime { get; set; }
        public string AiProvider { get; set; } = "";
    }

    public class FormFieldInfo
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string Label { get; set; } = "";
        public bool Required { get; set; }
        public string Validation { get; set; } = "";
        public List<string> AccessibilityIssues { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }

    public class ConditionalLogicRule
    {
        public string Trigger { get; set; } = "";
        public string Condition { get; set; } = "";
        public List<string> Actions { get; set; } = new();
    }

    public class ValidationRule
    {
        public string Type { get; set; } = "";
        public string Value { get; set; } = "";
        public string Message { get; set; } = "";
    }
}
