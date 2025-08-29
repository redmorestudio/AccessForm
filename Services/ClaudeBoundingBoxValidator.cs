using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using WordToPdfConverter.Models;
using System.Drawing;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Uses Claude to validate and correct bounding box placements
    /// </summary>
    public class ClaudeBoundingBoxValidator
    {
        private readonly ILogger<ClaudeBoundingBoxValidator> _logger;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public ClaudeBoundingBoxValidator(
            ILogger<ClaudeBoundingBoxValidator> logger,
            IConfiguration configuration,
            HttpClient httpClient)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClient = httpClient;
            _apiKey = configuration["ApiKeys:Anthropic"] ?? configuration["AnthropicApiKey"];
        }

        /// <summary>
        /// Validate and correct bounding boxes using Claude Vision
        /// </summary>
        public async Task<List<FieldDetectionResult>> ValidateAndCorrectBoundingBoxes(
            byte[] pdfPageImage, 
            List<FieldDetectionResult> detectedFields,
            int pageNumber,
            float pageWidth,
            float pageHeight)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogWarning("Cannot validate bounding boxes - Anthropic API key not configured");
                return detectedFields;
            }

            try
            {
                // Convert image to base64
                string base64Image = Convert.ToBase64String(pdfPageImage);
                
                // Create a visual representation of the fields for Claude to analyze
                var fieldsSummary = detectedFields
                    .Where(f => f.PageNumber == pageNumber)
                    .Select(f => new
                    {
                        id = f.ShortId,
                        name = f.FieldName,
                        type = f.FieldType,
                        bounds = new
                        {
                            x_percent = (f.X / pageWidth) * 100,
                            y_percent = (f.Y / pageHeight) * 100,
                            width_percent = (f.Width / pageWidth) * 100,
                            height_percent = (f.Height / pageHeight) * 100
                        }
                    }).ToList();

                var requestBody = new
                {
                    model = "claude-3-5-sonnet-20241022",
                    max_tokens = 4096,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new
                                {
                                    type = "image",
                                    source = new
                                    {
                                        type = "base64",
                                        media_type = "image/png",
                                        data = base64Image
                                    }
                                },
                                new
                                {
                                    type = "text",
                                    text = $@"I have detected these form fields with their bounding boxes. Please validate each one and tell me:
1. Which fields have INCORRECT bounding box placement (e.g., in the middle of paragraphs, overlapping text, etc.)
2. What the CORRECT bounding box should be for misplaced fields
3. Which fields are FALSE POSITIVES (not actually form fields)

Current detected fields:
{JsonSerializer.Serialize(fieldsSummary, new JsonSerializerOptions { WriteIndented = true })}

For each field, analyze:
- Is it actually a fillable form field or just text/table cell?
- Is the bounding box correctly positioned on the INPUT area (not the label)?
- Does the size make sense for the field type (checkboxes ~2-3%, text fields ~3-5% height)?
- Are there overlapping fields that should be merged?

Return your analysis as JSON:
{{
  ""validations"": [
    {{
      ""id"": ""field_short_id"",
      ""is_valid"": true/false,
      ""is_false_positive"": true/false,
      ""reason"": ""explanation"",
      ""corrected_bounds"": {{
        ""x_percent"": 10.5,
        ""y_percent"": 20.3,
        ""width_percent"": 30.0,
        ""height_percent"": 3.5
      }} // only if correction needed
    }}
  ],
  ""missing_fields"": [
    {{
      ""description"": ""Field that was missed"",
      ""type"": ""checkbox/text/etc"",
      ""approximate_location"": ""where on the page""
    }}
  ],
  ""notes"": ""General observations about the form""
}}"
                                }
                            }
                        }
                    }
                };

                // Call Claude API
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
                request.Headers.Add("x-api-key", _apiKey);
                request.Headers.Add("anthropic-version", "2023-06-01");
                request.Content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var apiResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    var content = apiResponse.GetProperty("content")[0].GetProperty("text").GetString();
                    
                    // Parse the validation response
                    var validationResult = ParseValidationResponse(content);
                    
                    // Apply corrections to the fields
                    foreach (var field in detectedFields.Where(f => f.PageNumber == pageNumber))
                    {
                        var validation = validationResult.FirstOrDefault(v => v.Id == field.ShortId);
                        if (validation != null)
                        {
                            field.IsValid = validation.IsValid && !validation.IsFalsePositive;
                            field.ValidationNotes = validation.Reason;
                            
                            if (!validation.IsFalsePositive && validation.CorrectedBounds != null)
                            {
                                // Apply the corrected bounds
                                field.X = (validation.CorrectedBounds.XPercent / 100f) * pageWidth;
                                field.Y = (validation.CorrectedBounds.YPercent / 100f) * pageHeight;
                                field.Width = (validation.CorrectedBounds.WidthPercent / 100f) * pageWidth;
                                field.Height = (validation.CorrectedBounds.HeightPercent / 100f) * pageHeight;
                                
                                _logger.LogInformation($"Corrected bounds for field {field.ShortId}: {field.FieldName}");
                            }
                            
                            if (validation.IsFalsePositive)
                            {
                                _logger.LogInformation($"Marked field {field.ShortId} as false positive: {validation.Reason}");
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogError($"Claude API error during validation: {response.StatusCode} - {responseContent}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate bounding boxes with Claude");
            }

            return detectedFields;
        }

        private class ValidationItem
        {
            public string Id { get; set; }
            public bool IsValid { get; set; }
            public bool IsFalsePositive { get; set; }
            public string Reason { get; set; }
            public CorrectedBounds CorrectedBounds { get; set; }
        }

        private class CorrectedBounds
        {
            public float XPercent { get; set; }
            public float YPercent { get; set; }
            public float WidthPercent { get; set; }
            public float HeightPercent { get; set; }
        }

        private List<ValidationItem> ParseValidationResponse(string jsonResponse)
        {
            var validations = new List<ValidationItem>();
            
            try
            {
                // Extract JSON from the response
                var jsonStart = jsonResponse.IndexOf('{');
                var jsonEnd = jsonResponse.LastIndexOf('}') + 1;
                
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var json = jsonResponse.Substring(jsonStart, jsonEnd - jsonStart);
                    var doc = JsonDocument.Parse(json);
                    
                    if (doc.RootElement.TryGetProperty("validations", out var validationsElement))
                    {
                        foreach (var item in validationsElement.EnumerateArray())
                        {
                            var validation = new ValidationItem
                            {
                                Id = item.GetProperty("id").GetString(),
                                IsValid = item.GetProperty("is_valid").GetBoolean(),
                                IsFalsePositive = item.TryGetProperty("is_false_positive", out var fp) && fp.GetBoolean(),
                                Reason = item.TryGetProperty("reason", out var reason) ? reason.GetString() : ""
                            };
                            
                            if (item.TryGetProperty("corrected_bounds", out var bounds))
                            {
                                validation.CorrectedBounds = new CorrectedBounds
                                {
                                    XPercent = bounds.GetProperty("x_percent").GetSingle(),
                                    YPercent = bounds.GetProperty("y_percent").GetSingle(),
                                    WidthPercent = bounds.GetProperty("width_percent").GetSingle(),
                                    HeightPercent = bounds.GetProperty("height_percent").GetSingle()
                                };
                            }
                            
                            validations.Add(validation);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse validation response");
            }
            
            return validations;
        }
    }
}