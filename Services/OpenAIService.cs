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

namespace AccessFormServer.Services
{
    /// <summary>
    /// OpenAI GPT-5 multimodal service for vision-based field validation
    /// Supports both text and image inputs for comprehensive form analysis
    /// </summary>
    public class OpenAIService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<OpenAIService> _logger;
        private readonly CostTrackingService _costTracking;
        private readonly HttpClient _httpClient;
        private readonly bool _enabled;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _baseUrl;
        private readonly int _maxTokens;
        private readonly float _temperature;

        public OpenAIService(
            IConfiguration configuration,
            ILogger<OpenAIService> logger,
            CostTrackingService costTracking,
            IHttpClientFactory httpClientFactory = null)
        {
            _configuration = configuration;
            _logger = logger;
            _costTracking = costTracking;

            _enabled = _configuration.GetValue<bool>("AiServices:OpenAI:Enabled", false);
            _apiKey = _configuration["AiServices:OpenAI:ApiKey"];
            _model = _configuration["AiServices:OpenAI:Model"] ?? "gpt-5";
            _baseUrl = _configuration["AiServices:OpenAI:BaseUrl"] ?? "https://api.openai.com/v1";
            _maxTokens = _configuration.GetValue<int>("AiServices:OpenAI:MaxTokens", 8192);
            _temperature = _configuration.GetValue<float>("AiServices:OpenAI:Temperature", 0.0f);

            _httpClient = httpClientFactory?.CreateClient() ?? new HttpClient();

            if (_enabled && !string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _apiKey);
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                _logger.LogInformation("[OPENAI] Service initialized with model: {Model}", _model);
            }
            else
            {
                _logger.LogWarning("[OPENAI] Service is disabled or not configured");
            }
        }

        /// <summary>
        /// Call GPT-5 with vision capabilities - send image + text prompt
        /// </summary>
        public async Task<string> CallVisionApiAsync(
            string textPrompt,
            byte[] imageBytes,
            string imageFormat = "png",
            CancellationToken cancellationToken = default)
        {
            if (!_enabled || string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogWarning("[OPENAI] Service not enabled or configured");
                return null;
            }

            try
            {
                // Convert image to base64 data URL
                var base64Image = Convert.ToBase64String(imageBytes);
                var imageDataUrl = $"data:image/{imageFormat};base64,{base64Image}";

                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "text", text = textPrompt },
                                new { type = "image_url", image_url = new { url = imageDataUrl } }
                            }
                        }
                    },
                    max_completion_tokens = _maxTokens,
                    temperature = _temperature
                };

                var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/chat/completions", content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("[OPENAI] API error: {StatusCode} - {Error}", response.StatusCode, error);
                    return null;
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var responseObj = JsonDocument.Parse(responseJson);

                // GPT-5 response format
                if (responseObj.RootElement.TryGetProperty("choices", out var choices))
                {
                    var firstChoice = choices.EnumerateArray().FirstOrDefault();
                    if (firstChoice.ValueKind != JsonValueKind.Undefined)
                    {
                        if (firstChoice.TryGetProperty("message", out var message))
                        {
                            if (message.TryGetProperty("content", out var contentProp))
                            {
                                var result = contentProp.GetString();

                                // Track cost (estimated)
                                await _costTracking.RecordCostAsync("OpenAI-GPT5", 0.03m);

                                return result;
                            }
                        }
                    }
                }

                _logger.LogWarning("[OPENAI] Unexpected response structure");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[OPENAI] Error calling GPT-5 API");
                return null;
            }
        }

        /// <summary>
        /// Ask GPT-5 to fix a Python script based on execution error
        /// </summary>
        public async Task<string> FixScriptFromErrorAsync(
            string originalScript,
            string errorMessage,
            string stdout = null,
            CancellationToken cancellationToken = default)
        {
            if (!_enabled || string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogWarning("[OPENAI] Service not enabled or configured");
                return null;
            }

            var prompt = $@"The following Python script failed to execute with an error. Please fix the script and return ONLY the corrected Python code (no markdown, no explanations).

ORIGINAL SCRIPT:
```python
{originalScript}
```

ERROR:
{errorMessage}

{(string.IsNullOrEmpty(stdout) ? "" : $@"STDOUT:
{stdout}

")}
Return ONLY the fixed Python script. Do not include markdown code blocks, explanations, or any other text. Just the raw Python code that will execute successfully.";

            try
            {
                var result = await CallTextApiAsync(prompt, cancellationToken);

                // Clean up response - remove markdown if GPT added it anyway
                if (!string.IsNullOrEmpty(result))
                {
                    if (result.Contains("```python"))
                    {
                        var startIdx = result.IndexOf("```python") + 9;
                        var endIdx = result.IndexOf("```", startIdx);
                        if (endIdx > startIdx)
                        {
                            result = result.Substring(startIdx, endIdx - startIdx).Trim();
                        }
                    }
                    else if (result.Contains("```"))
                    {
                        var startIdx = result.IndexOf("```") + 3;
                        var endIdx = result.IndexOf("```", startIdx);
                        if (endIdx > startIdx)
                        {
                            result = result.Substring(startIdx, endIdx - startIdx).Trim();
                        }
                    }
                }

                _logger.LogInformation("[OPENAI] Generated fixed script based on error feedback");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[OPENAI] Error fixing script from error");
                return null;
            }
        }

        /// <summary>
        /// Call GPT-5 with just text (no image)
        /// </summary>
        public async Task<string> CallTextApiAsync(string prompt, CancellationToken cancellationToken = default)
        {
            if (!_enabled || string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogWarning("[OPENAI] Service not enabled or configured");
                return null;
            }

            try
            {
                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = prompt
                        }
                    },
                    max_completion_tokens = _maxTokens,
                    temperature = _temperature
                };

                var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/chat/completions", content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("[OPENAI] API error: {StatusCode} - {Error}", response.StatusCode, error);
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
                                var result = contentProp.GetString();

                                // Track cost
                                await _costTracking.RecordCostAsync("OpenAI-GPT5", 0.01m);

                                return result;
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[OPENAI] Error calling GPT-5 text API");
                return null;
            }
        }

        /// <summary>
        /// Validate field detection with vision - returns structured JSON response
        /// </summary>
        public async Task<ValidationResponse> ValidateFieldsWithVision(
            byte[] annotatedImageBytes,
            List<ValidationFieldInfo> detectedFields,
            ValidationStage stage,
            CancellationToken cancellationToken = default)
        {
            var fieldListJson = JsonSerializer.Serialize(detectedFields, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });

            var prompt = stage switch
            {
                ValidationStage.Completeness => BuildCompletenessPrompt(fieldListJson),
                ValidationStage.Spurious => BuildSpuriousPrompt(fieldListJson),
                ValidationStage.LabelCorrection => BuildLabelCorrectionPrompt(fieldListJson),
                _ => throw new ArgumentException($"Unknown validation stage: {stage}")
            };

            var responseText = await CallVisionApiAsync(prompt, annotatedImageBytes, "png", cancellationToken);

            if (string.IsNullOrEmpty(responseText))
            {
                _logger.LogWarning("[OPENAI] Empty response from vision API");
                return new ValidationResponse { Stage = stage, Success = false };
            }

            return ParseValidationResponse(responseText, stage);
        }

        private string BuildCompletenessPrompt(string fieldListJson)
        {
            return $@"You are validating form field detection for completeness. I've detected these fields and drawn colored rectangles on the image:

{fieldListJson}

Your task: Look at the ENTIRE image and identify any fillable fields (input boxes, checkboxes, signature areas, date fields) that do NOT have a bounding box drawn on them.

Don't worry about whether labels are correct - just find MISSING fields.

Return ONLY valid JSON (no markdown):
{{
  ""missing_fields"": [
    {{
      ""location_description"": ""Bottom left, next to 'Signature:' text"",
      ""field_type"": ""signature"",
      ""estimated_bounds"": {{""x_percent"": 10, ""y_percent"": 85, ""width_percent"": 15, ""height_percent"": 5}},
      ""confidence"": 0.95,
      ""reasoning"": ""Large 'X' mark indicating signature field, no bounding box present""
    }}
  ]
}}

If no fields are missing, return: {{""missing_fields"": []}}";
        }

        private string BuildSpuriousPrompt(string fieldListJson)
        {
            return $@"You are reviewing detected form fields for false positives. I've drawn bounding boxes on these detected fields:

{fieldListJson}

Your task: Identify which of these detected fields are NOT actual fillable form fields.

Common false positives:
- Page numbers
- Logos or graphics
- Section headers/titles
- Decorative elements
- Static text labels

Return ONLY valid JSON (no markdown):
{{
  ""spurious_fields"": [
    {{
      ""id"": ""F7"",
      ""reason"": ""This is a page number, not a form field"",
      ""confidence"": 0.99
    }}
  ]
}}

If all fields are valid, return: {{""spurious_fields"": []}}";
        }

        private string BuildLabelCorrectionPrompt(string fieldListJson)
        {
            return $@"You are validating form field labels. I've detected these fields with their current labels:

{fieldListJson}

Your task: For EACH field, verify if the label accurately describes what the form is asking for based on the nearby text. Look at the context around each field.

Common errors:
- Field labeled ""Gender Male"" when nearby text says ""Previous States""
- Field labeled ""Staff"" when it should be ""Date Signed""
- Field labeled ""Birth City"" when it should be ""License State""

Return ONLY valid JSON (no markdown):
{{
  ""corrections"": [
    {{
      ""id"": ""F3"",
      ""current_label"": ""Gender Male"",
      ""corrected_label"": ""Previous States"",
      ""current_type"": ""checkbox"",
      ""corrected_type"": ""text"",
      ""confidence"": 0.98,
      ""reasoning"": ""Field is next to text 'Previous States:' not gender options""
    }}
  ]
}}

If all labels are correct, return: {{""corrections"": []}}";
        }

        private ValidationResponse ParseValidationResponse(string responseText, ValidationStage stage)
        {
            var response = new ValidationResponse { Stage = stage, Success = true };

            try
            {
                // Clean up response - remove markdown if present
                var jsonStart = responseText.IndexOf('{');
                var jsonEnd = responseText.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    responseText = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
                }

                var json = JsonDocument.Parse(responseText);
                var root = json.RootElement;

                switch (stage)
                {
                    case ValidationStage.Completeness:
                        if (root.TryGetProperty("missing_fields", out var missingFields))
                        {
                            response.MissingFields = ParseMissingFields(missingFields);
                        }
                        break;

                    case ValidationStage.Spurious:
                        if (root.TryGetProperty("spurious_fields", out var spuriousFields))
                        {
                            response.SpuriousFields = ParseSpuriousFields(spuriousFields);
                        }
                        break;

                    case ValidationStage.LabelCorrection:
                        if (root.TryGetProperty("corrections", out var corrections))
                        {
                            response.Corrections = ParseCorrections(corrections);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[OPENAI] Failed to parse validation response");
                response.Success = false;
            }

            return response;
        }

        private List<MissingField> ParseMissingFields(JsonElement element)
        {
            var fields = new List<MissingField>();
            foreach (var item in element.EnumerateArray())
            {
                fields.Add(new MissingField
                {
                    LocationDescription = item.GetProperty("location_description").GetString(),
                    FieldType = item.GetProperty("field_type").GetString(),
                    Confidence = item.GetProperty("confidence").GetSingle(),
                    Reasoning = item.GetProperty("reasoning").GetString(),
                    EstimatedBounds = item.TryGetProperty("estimated_bounds", out var bounds)
                        ? new EstimatedBounds
                        {
                            XPercent = bounds.GetProperty("x_percent").GetSingle(),
                            YPercent = bounds.GetProperty("y_percent").GetSingle(),
                            WidthPercent = bounds.GetProperty("width_percent").GetSingle(),
                            HeightPercent = bounds.GetProperty("height_percent").GetSingle()
                        }
                        : null
                });
            }
            return fields;
        }

        private List<SpuriousField> ParseSpuriousFields(JsonElement element)
        {
            var fields = new List<SpuriousField>();
            foreach (var item in element.EnumerateArray())
            {
                fields.Add(new SpuriousField
                {
                    Id = item.GetProperty("id").GetString(),
                    Reason = item.GetProperty("reason").GetString(),
                    Confidence = item.GetProperty("confidence").GetSingle()
                });
            }
            return fields;
        }

        private List<ValidationFieldCorrection> ParseCorrections(JsonElement element)
        {
            var corrections = new List<ValidationFieldCorrection>();
            foreach (var item in element.EnumerateArray())
            {
                corrections.Add(new ValidationFieldCorrection
                {
                    Id = item.GetProperty("id").GetString(),
                    CurrentLabel = item.TryGetProperty("current_label", out var cl) ? cl.GetString() : null,
                    CorrectedLabel = item.GetProperty("corrected_label").GetString(),
                    CurrentType = item.TryGetProperty("current_type", out var ct) ? ct.GetString() : null,
                    CorrectedType = item.TryGetProperty("corrected_type", out var crt) ? crt.GetString() : null,
                    Confidence = item.GetProperty("confidence").GetSingle(),
                    Reasoning = item.GetProperty("reasoning").GetString()
                });
            }
            return corrections;
        }
    }

    // Data models for validation
    public enum ValidationStage
    {
        Completeness,
        Spurious,
        LabelCorrection
    }

    public class ValidationFieldInfo
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string Type { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
    }

    public class ValidationResponse
    {
        public ValidationStage Stage { get; set; }
        public bool Success { get; set; }
        public List<MissingField> MissingFields { get; set; } = new();
        public List<SpuriousField> SpuriousFields { get; set; } = new();
        public List<ValidationFieldCorrection> Corrections { get; set; } = new();
    }

    public class MissingField
    {
        public string LocationDescription { get; set; }
        public string FieldType { get; set; }
        public EstimatedBounds EstimatedBounds { get; set; }
        public float Confidence { get; set; }
        public string Reasoning { get; set; }
    }

    public class EstimatedBounds
    {
        public float XPercent { get; set; }
        public float YPercent { get; set; }
        public float WidthPercent { get; set; }
        public float HeightPercent { get; set; }
    }

    public class SpuriousField
    {
        public string Id { get; set; }
        public string Reason { get; set; }
        public float Confidence { get; set; }
    }

    public class ValidationFieldCorrection
    {
        public string Id { get; set; }
        public string CurrentLabel { get; set; }
        public string CorrectedLabel { get; set; }
        public string CurrentType { get; set; }
        public string CorrectedType { get; set; }
        public float Confidence { get; set; }
        public string Reasoning { get; set; }
    }
}
