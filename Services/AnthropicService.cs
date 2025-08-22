using System;
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
        private readonly string _model;
        private readonly bool _enabled;

        public AnthropicService(HttpClient httpClient, IConfiguration configuration, ILogger<AnthropicService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            
            var anthropicConfig = configuration.GetSection("Anthropic");
            _apiKey = anthropicConfig["ApiKey"] ?? "";
            _model = anthropicConfig["Model"] ?? "claude-3-opus-20240229";
            _enabled = anthropicConfig.GetValue<bool>("Enabled", false);

            if (_enabled && !string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
                _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            }
        }

        public async Task<string> AnalyzeFormFieldsAsync(string documentContent)
        {
            _logger.LogInformation($"Anthropic service check: Enabled={_enabled}, HasApiKey={!string.IsNullOrEmpty(_apiKey)}, KeyLength={_apiKey?.Length ?? 0}");
            if (!_enabled || string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogWarning("Anthropic service is not enabled or API key is missing");
                return null;
            }

            try
            {
                var prompt = $@"Analyze this form and identify all form fields. For each field, provide:
1. Field ID (programmatic name)
2. Field Type (text, date, email, phone, ssn, signature, checkbox, radio, dropdown)
3. Human-Readable Label
4. Helpful Tooltip for accessibility

Format your response as a markdown table with these columns:
| Field ID | Field Type | Human-Readable Label | Tooltip |

Document content:
{documentContent}";

                var request = new
                {
                    model = _model,
                    max_tokens = 4096,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = prompt
                        }
                    }
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
                    
                    if (result.TryGetProperty("content", out var contentArray) && 
                        contentArray.GetArrayLength() > 0)
                    {
                        var firstContent = contentArray[0];
                        if (firstContent.TryGetProperty("text", out var text))
                        {
                            return text.GetString();
                        }
                    }
                }
                else
                {
                    _logger.LogError($"Anthropic API error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Anthropic API");
            }

            return null;
        }

        public class FieldAnalysisResult
        {
            public string FieldId { get; set; }
            public string FieldType { get; set; }
            public string Label { get; set; }
            public string Tooltip { get; set; }
            public bool IsRequired { get; set; }
        }

        public List<FieldAnalysisResult> ParseAnalysisResult(string anthropicResponse)
        {
            var results = new List<FieldAnalysisResult>();
            
            if (string.IsNullOrEmpty(anthropicResponse))
                return results;

            var lines = anthropicResponse.Split('\n');
            bool inTable = false;
            
            foreach (var line in lines)
            {
                if (line.Contains("| Field ID") && line.Contains("| Field Type"))
                {
                    inTable = true;
                    continue;
                }
                
                if (inTable && line.StartsWith("|") && !line.Contains("---"))
                {
                    var parts = line.Split('|').Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
                    if (parts.Length >= 4)
                    {
                        results.Add(new FieldAnalysisResult
                        {
                            FieldId = parts[0].Trim(),
                            FieldType = parts[1].Trim(),
                            Label = parts[2].Trim(),
                            Tooltip = parts[3].Trim(),
                            IsRequired = parts[2].ToLower().Contains("required") || 
                                       parts[3].ToLower().Contains("required")
                        });
                    }
                }
            }
            
            return results;
        }
    }
}
