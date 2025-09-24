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
            _httpClient.Timeout = TimeSpan.FromSeconds(300); // 5 minutes timeout for Claude API
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
                    _logger.LogInformation($"=== CLAUDE RAW RESPONSE ===");
                    _logger.LogInformation($"Response length: {responseContent.Length} characters");
                    
                    // Log first 1000 chars of response
                    var preview = responseContent.Length > 1000 ? responseContent.Substring(0, 1000) + "..." : responseContent;
                    _logger.LogInformation($"Response preview: {preview}");
                    
                    var responseJson = JsonDocument.Parse(responseContent);
                    
                    if (responseJson.RootElement.TryGetProperty("content", out var contentArray) && 
                        contentArray.GetArrayLength() > 0)
                    {
                        var firstContent = contentArray[0];
                        if (firstContent.TryGetProperty("text", out var textElement))
                        {
                            var claudeText = textElement.GetString() ?? "No analysis available";
                            _logger.LogInformation($"Extracted Claude text: {claudeText.Length} characters");
                            if (claudeText.Length > 500)
                            {
                                _logger.LogInformation($"Claude text preview: {claudeText.Substring(0, 500)}...");
                            }
                            else
                            {
                                _logger.LogInformation($"Claude text: {claudeText}");
                            }
                            return claudeText;
                        }
                    }
                    
                    _logger.LogWarning("Could not extract text from Claude response, returning raw JSON");
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

            // First try to extract JSON array from markdown if present
            if (analysisText.Contains("```json") && analysisText.Contains("["))
            {
                try
                {
                    var startIdx = analysisText.IndexOf("[");
                    var endIdx = analysisText.LastIndexOf("]");
                    if (startIdx >= 0 && endIdx > startIdx)
                    {
                        var jsonArray = analysisText.Substring(startIdx, endIdx - startIdx + 1);
                        var doc = JsonDocument.Parse(jsonArray);
                        foreach (var field in doc.RootElement.EnumerateArray())
                        {
                            results.Add(new FieldAnalysisResult
                            {
                                Success = true,
                                FieldName = field.TryGetProperty("name", out var n) ? n.GetString() ?? "Field" : "Field",
                                FieldType = field.TryGetProperty("type", out var t) ? t.GetString() ?? "text" : "text",
                                IsRequired = field.TryGetProperty("required", out var r) && r.GetBoolean()
                            });
                        }
                        if (results.Count > 0) return results;
                    }
                }
                catch { /* Continue to existing parsing */ }
            }

            
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
            public int PageNumber { get; set; } = 1;
            
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
        
        public async Task<string> AnalyzeImageForAltText(byte[] imageBytes)
        {
            try
            {
                _logger.LogInformation($"Analyzing image for alt-text generation ({imageBytes.Length} bytes)");
                
                // Convert image bytes to base64
                var base64Image = Convert.ToBase64String(imageBytes);
                
                // Determine image type (assuming JPEG for now, but could be improved)
                var mediaType = "image/jpeg";
                if (imageBytes.Length > 4)
                {
                    // Check for PNG signature
                    if (imageBytes[0] == 0x89 && imageBytes[1] == 0x50 && imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
                    {
                        mediaType = "image/png";
                    }
                }
                
                var request = new
                {
                    model = "claude-3-opus-20240229",
                    max_tokens = 500,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new
                                {
                                    type = "text",
                                    text = @"You are an accessibility expert generating alt-text for images in PDF documents.
                                    Analyze this image and provide a concise, descriptive alt-text that would help someone using a screen reader understand what the image contains.
                                    
                                    Guidelines:
                                    - Be descriptive but concise (typically 125 characters or less)
                                    - Focus on the essential information conveyed by the image
                                    - If it's a logo, identify the organization if possible
                                    - If it's a diagram or chart, describe its purpose and key information
                                    - If it's decorative, you can say 'Decorative image' or describe it briefly
                                    - Do not start with 'Image of' or 'Picture of'
                                    
                                    Respond with ONLY the alt-text, nothing else."
                                },
                                new
                                {
                                    type = "image",
                                    source = new
                                    {
                                        type = "base64",
                                        media_type = mediaType,
                                        data = base64Image
                                    }
                                }
                            }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("v1/messages", content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"Claude vision response: {responseJson.Substring(0, Math.Min(500, responseJson.Length))}...");

                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("content", out var contentArray) && 
                    contentArray.GetArrayLength() > 0)
                {
                    var firstContent = contentArray[0];
                    if (firstContent.TryGetProperty("text", out var textElement))
                    {
                        var altText = textElement.GetString()?.Trim();
                        if (!string.IsNullOrEmpty(altText))
                        {
                            _logger.LogInformation($"Generated alt-text: {altText}");
                            return altText;
                        }
                    }
                }

                _logger.LogWarning("Could not extract alt-text from Claude response");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing image for alt-text");
                return null;
            }
        }
    }
}
