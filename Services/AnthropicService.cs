using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AccessFormServer.Services
{
    public class AnthropicService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AnthropicService> _logger;
        private readonly string _apiKey;

        public AnthropicService(HttpClient httpClient, IConfiguration configuration, ILogger<AnthropicService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = configuration["ApiKeys:Anthropic"] ?? throw new ArgumentNullException("Anthropic API key not configured");
            
            _httpClient.BaseAddress = new Uri("https://api.anthropic.com/");
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }

        public async Task<string> AnalyzeFormFieldsAsync(string extractedText)
        {
            try
            {
                _logger.LogInformation("Analyzing form fields with Anthropic");
                
                var request = new
                {
                    model = "claude-3-opus-20240229",
                    max_tokens = 4000,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = $@"Analyze this form content and identify all form fields. 
                            For each field, provide:
                            - Field name
                            - Field type (text, checkbox, radio, dropdown, date, signature, etc.)
                            - Whether it's required
                            - Any validation rules
                            - Accessibility label suggestions
                            - Tab order recommendation
                            
                            Format your response as JSON with an array of field objects.
                            
                            Form content:
                            {extractedText}"
                        }
                    }
                };
                
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync("v1/messages", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var responseJson = JsonDocument.Parse(responseContent);
                    
                    if (responseJson.RootElement.TryGetProperty("content", out var contentArray) && 
                        contentArray.GetArrayLength() > 0)
                    {
                        var firstContent = contentArray[0];
                        if (firstContent.TryGetProperty("text", out var textElement))
                        {
                            return textElement.GetString() ?? "No analysis available";
                        }
                    }
                    
                    return responseContent;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Anthropic API error: {response.StatusCode} - {error}");
                    return $"Analysis failed: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing form fields with Anthropic");
                return $"Analysis error: {ex.Message}";
            }
        }

        public List<FieldAnalysisResult> ParseAnalysisResult(string analysisText)
        {
            var results = new List<FieldAnalysisResult>();
            
            try
            {
                // Try to parse as JSON first
                var jsonDoc = JsonDocument.Parse(analysisText);
                if (jsonDoc.RootElement.TryGetProperty("fields", out var fieldsArray))
                {
                    foreach (var field in fieldsArray.EnumerateArray())
                    {
                        var result = new FieldAnalysisResult
                        {
                            Success = true,
                            FieldName = field.GetProperty("name").GetString() ?? "Unknown",
                            FieldType = field.GetProperty("type").GetString() ?? "text",
                            IsRequired = field.TryGetProperty("required", out var req) && req.GetBoolean()
                        };
                        results.Add(result);
                    }
                }
            }
            catch
            {
                // If not JSON, parse as text
                var lines = analysisText.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("Field:") || line.Contains("field:"))
                    {
                        results.Add(new FieldAnalysisResult
                        {
                            Success = true,
                            FieldName = $"Field_{results.Count + 1}",
                            FieldType = "text",
                            IsRequired = false
                        });
                    }
                }
            }
            
            return results;
        }
        
        // Nested class for compatibility with AiDebugProcessor
        public class FieldAnalysisResult
        {
            public bool Success { get; set; }
            public string Analysis { get; set; }
            public List<FormField> Fields { get; set; }
            public string ErrorMessage { get; set; }
            public string FieldName { get; set; }
            public string FieldType { get; set; }
            public bool IsRequired { get; set; }
            
            public FieldAnalysisResult()
            {
                Fields = new List<FormField>();
                Analysis = string.Empty;
                ErrorMessage = string.Empty;
                FieldName = string.Empty;
                FieldType = string.Empty;
            }
        }
        
        public class FormField
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public bool Required { get; set; }
            public string AccessibilityLabel { get; set; }
            public int TabOrder { get; set; }
            
            public FormField()
            {
                Name = string.Empty;
                Type = string.Empty;
                AccessibilityLabel = string.Empty;
            }
        }
    }
}
