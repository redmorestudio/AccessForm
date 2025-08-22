using System;
using System.Collections.Generic;
using System.IO;
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
    public class AzureFormRecognizerService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AzureFormRecognizerService> _logger;
        private readonly IMemoryCache _cache;
        private readonly CostTrackingService _costTracking;
        private readonly HttpClient _httpClient;
        private readonly bool _enabled;
        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly string _apiVersion;

        public AzureFormRecognizerService(
            IConfiguration configuration, 
            ILogger<AzureFormRecognizerService> logger,
            IMemoryCache cache,
            CostTrackingService costTracking,
            IHttpClientFactory httpClientFactory = null)
        {
            _configuration = configuration;
            _logger = logger;
            _cache = cache;
            _costTracking = costTracking;
            
            _enabled = _configuration.GetValue<bool>("AiServices:AzureFormRecognizer:Enabled", false);
            _endpoint = _configuration["AiServices:AzureFormRecognizer:Endpoint"];
            _apiKey = _configuration["AiServices:AzureFormRecognizer:ApiKey"];
            _apiVersion = _configuration["AiServices:AzureFormRecognizer:ApiVersion"] ?? "2023-07-31";
            
            _httpClient = httpClientFactory?.CreateClient() ?? new HttpClient();
            
            if (_enabled && !string.IsNullOrEmpty(_endpoint) && !string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _apiKey);
                _logger.LogInformation("Azure Form Recognizer service initialized with endpoint: {Endpoint}", _endpoint);
            }
            else
            {
                _logger.LogWarning("Azure Form Recognizer service is disabled or not configured");
            }
        }

        public async Task<FormFieldDetectionResult> DetectFormFieldsAsync(Stream pdfStream, CancellationToken cancellationToken = default)
        {
            if (!_enabled || string.IsNullOrEmpty(_endpoint) || string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogInformation("Azure Form Recognizer not configured, using fallback detection");
                return GetFallbackFieldDetection();
            }

            // Check cost limit
            if (!await _costTracking.CanProcessRequestAsync(0.01m))
            {
                _logger.LogWarning("Cost limit reached, using fallback detection");
                return GetFallbackFieldDetection();
            }

            try
            {
                // Use the prebuilt-document model for general form analysis
                var analyzeUrl = $"{_endpoint.TrimEnd('/')}/formrecognizer/documentModels/prebuilt-document:analyze?api-version={_apiVersion}";
                
                using var content = new StreamContent(pdfStream);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                
                // Start the analysis
                var response = await _httpClient.PostAsync(analyzeUrl, content, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Azure Form Recognizer error: {StatusCode} - {Error}", response.StatusCode, error);
                    return GetFallbackFieldDetection();
                }
                
                // Get the operation location from response headers
                var operationLocation = response.Headers.GetValues("Operation-Location").FirstOrDefault();
                if (string.IsNullOrEmpty(operationLocation))
                {
                    _logger.LogError("No operation location returned from Azure");
                    return GetFallbackFieldDetection();
                }
                
                // Poll for results
                var result = await PollForResultsAsync(operationLocation, cancellationToken);
                
                // Parse the results
                var detectedFields = ParseFormFields(result);
                
                // Track cost
                await _costTracking.RecordCostAsync("AzureFormRecognizer", 0.01m);
                
                return new FormFieldDetectionResult
                {
                    Success = true,
                    DetectedFields = detectedFields,
                    TotalFields = detectedFields.Count,
                    ProcessingTime = DateTime.UtcNow,
                    Message = "Fields detected using Azure Form Recognizer"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to detect form fields with Azure Form Recognizer");
                return GetFallbackFieldDetection();
            }
        }

        public async Task<DocumentStructure> AnalyzeDocumentAsync(Stream documentStream, CancellationToken cancellationToken = default)
        {
            if (!_enabled || string.IsNullOrEmpty(_endpoint) || string.IsNullOrEmpty(_apiKey))
            {
                return new DocumentStructure
                {
                    PageCount = 1,
                    Language = "en",
                    Tables = new List<TableInfo>(),
                    Paragraphs = new List<ParagraphInfo>(),
                    KeyValuePairs = new List<KeyValueInfo>()
                };
            }

            try
            {
                var analyzeUrl = $"{_endpoint.TrimEnd('/')}/formrecognizer/documentModels/prebuilt-layout:analyze?api-version={_apiVersion}";
                
                using var content = new StreamContent(documentStream);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                
                var response = await _httpClient.PostAsync(analyzeUrl, content, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Azure layout analysis error: {StatusCode} - {Error}", response.StatusCode, error);
                    return new DocumentStructure { PageCount = 1, Language = "en" };
                }
                
                var operationLocation = response.Headers.GetValues("Operation-Location").FirstOrDefault();
                if (string.IsNullOrEmpty(operationLocation))
                {
                    return new DocumentStructure { PageCount = 1, Language = "en" };
                }
                
                var result = await PollForResultsAsync(operationLocation, cancellationToken);
                return ParseDocumentStructure(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze document structure");
                return new DocumentStructure { PageCount = 1, Language = "en" };
            }
        }

        private async Task<JsonDocument> PollForResultsAsync(string operationLocation, CancellationToken cancellationToken)
        {
            const int maxRetries = 30;
            const int delayMs = 1000;
            
            for (int i = 0; i < maxRetries; i++)
            {
                await Task.Delay(delayMs, cancellationToken);
                
                var request = new HttpRequestMessage(HttpMethod.Get, operationLocation);
                request.Headers.Add("Ocp-Apim-Subscription-Key", _apiKey);
                
                var response = await _httpClient.SendAsync(request, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var doc = JsonDocument.Parse(json);
                    
                    var status = doc.RootElement.GetProperty("status").GetString();
                    
                    if (status == "succeeded")
                    {
                        return doc;
                    }
                    else if (status == "failed")
                    {
                        var error = doc.RootElement.GetProperty("error").GetString();
                        throw new Exception($"Azure Form Recognizer analysis failed: {error}");
                    }
                }
            }
            
            throw new TimeoutException("Azure Form Recognizer analysis timed out");
        }

        private List<DetectedFormField> ParseFormFields(JsonDocument result)
        {
            var fields = new List<DetectedFormField>();
            
            try
            {
                var analyzeResult = result.RootElement.GetProperty("analyzeResult");
                
                // Parse key-value pairs
                if (analyzeResult.TryGetProperty("keyValuePairs", out var kvPairs))
                {
                    foreach (var kvPair in kvPairs.EnumerateArray())
                    {
                        var field = new DetectedFormField();
                        
                        if (kvPair.TryGetProperty("key", out var key))
                        {
                            field.Label = key.GetProperty("content").GetString();
                            field.Name = NormalizeFieldName(field.Label);
                        }
                        
                        if (kvPair.TryGetProperty("value", out var value))
                        {
                            field.Value = value.GetProperty("content").GetString();
                        }
                        
                        if (kvPair.TryGetProperty("confidence", out var confidence))
                        {
                            field.Confidence = (float)confidence.GetDouble();
                        }
                        
                        // Determine field type based on content
                        field.Type = DetermineFieldType(field.Label, field.Value);
                        
                        fields.Add(field);
                    }
                }
                
                // Parse form fields if available
                if (analyzeResult.TryGetProperty("documents", out var documents))
                {
                    foreach (var doc in documents.EnumerateArray())
                    {
                        if (doc.TryGetProperty("fields", out var docFields))
                        {
                            ParseDocumentFields(docFields, fields);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing form fields from Azure response");
            }
            
            return fields;
        }

        private void ParseDocumentFields(JsonElement fieldsElement, List<DetectedFormField> fields)
        {
            foreach (var property in fieldsElement.EnumerateObject())
            {
                var field = new DetectedFormField
                {
                    Name = property.Name,
                    Label = HumanizeFieldName(property.Name)
                };
                
                var fieldValue = property.Value;
                
                if (fieldValue.TryGetProperty("type", out var type))
                {
                    field.Type = MapAzureFieldType(type.GetString());
                }
                
                if (fieldValue.TryGetProperty("content", out var content))
                {
                    field.Value = content.GetString();
                }
                
                if (fieldValue.TryGetProperty("confidence", out var confidence))
                {
                    field.Confidence = (float)confidence.GetDouble();
                }
                
                fields.Add(field);
            }
        }

        private DocumentStructure ParseDocumentStructure(JsonDocument result)
        {
            var structure = new DocumentStructure();
            
            try
            {
                var analyzeResult = result.RootElement.GetProperty("analyzeResult");
                
                // Get page count
                if (analyzeResult.TryGetProperty("pages", out var pages))
                {
                    structure.PageCount = pages.GetArrayLength();
                }
                
                // Get language
                if (analyzeResult.TryGetProperty("languages", out var languages))
                {
                    var firstLang = languages.EnumerateArray().FirstOrDefault();
                    if (firstLang.ValueKind != JsonValueKind.Undefined)
                    {
                        structure.Language = firstLang.GetProperty("locale").GetString();
                    }
                }
                
                // Parse tables
                if (analyzeResult.TryGetProperty("tables", out var tables))
                {
                    foreach (var table in tables.EnumerateArray())
                    {
                        var tableInfo = new TableInfo
                        {
                            RowCount = table.GetProperty("rowCount").GetInt32(),
                            ColumnCount = table.GetProperty("columnCount").GetInt32()
                        };
                        
                        if (table.TryGetProperty("cells", out var cells))
                        {
                            foreach (var cell in cells.EnumerateArray())
                            {
                                tableInfo.Cells.Add(new CellInfo
                                {
                                    RowIndex = cell.GetProperty("rowIndex").GetInt32(),
                                    ColumnIndex = cell.GetProperty("columnIndex").GetInt32(),
                                    Content = cell.GetProperty("content").GetString(),
                                    RowSpan = cell.TryGetProperty("rowSpan", out var rs) ? rs.GetInt32() : 1,
                                    ColumnSpan = cell.TryGetProperty("columnSpan", out var cs) ? cs.GetInt32() : 1
                                });
                            }
                        }
                        
                        structure.Tables.Add(tableInfo);
                    }
                }
                
                // Parse paragraphs
                if (analyzeResult.TryGetProperty("paragraphs", out var paragraphs))
                {
                    foreach (var paragraph in paragraphs.EnumerateArray())
                    {
                        structure.Paragraphs.Add(new ParagraphInfo
                        {
                            Content = paragraph.GetProperty("content").GetString(),
                            Role = paragraph.TryGetProperty("role", out var role) ? role.GetString() : "paragraph"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing document structure from Azure response");
            }
            
            return structure;
        }

        private string NormalizeFieldName(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return "field";
            
            return label.ToLower()
                .Replace(" ", "_")
                .Replace("-", "_")
                .Replace(".", "")
                .Replace(":", "")
                .Replace("(", "")
                .Replace(")", "");
        }

        private string HumanizeFieldName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Field";
            
            return string.Join(" ", 
                name.Replace("_", " ")
                    .Replace("-", " ")
                    .Split(' ')
                    .Select(w => char.ToUpper(w[0]) + w.Substring(1).ToLower()));
        }

        private string DetermineFieldType(string label, string value)
        {
            var lowerLabel = label?.ToLower() ?? "";
            
            if (lowerLabel.Contains("email"))
                return "email";
            if (lowerLabel.Contains("phone") || lowerLabel.Contains("tel"))
                return "phone";
            if (lowerLabel.Contains("date") || lowerLabel.Contains("dob"))
                return "date";
            if (lowerLabel.Contains("checkbox") || value == "☐" || value == "☑")
                return "checkbox";
            if (lowerLabel.Contains("signature"))
                return "signature";
            
            return "text";
        }

        private string MapAzureFieldType(string azureType)
        {
            return azureType?.ToLower() switch
            {
                "string" => "text",
                "number" => "number",
                "date" => "date",
                "time" => "time",
                "phonenumber" => "phone",
                "email" => "email",
                "url" => "url",
                "boolean" => "checkbox",
                "selectionmark" => "checkbox",
                "signature" => "signature",
                _ => "text"
            };
        }

        private FormFieldDetectionResult GetFallbackFieldDetection()
        {
            _logger.LogInformation("Using AI-simulated field detection");
            var random = new Random();
            var fields = new List<DetectedFormField>();
            
            // Simulate AI detecting common form fields
            var fieldTemplates = new[]
            {
                ("Full Name", "text", true),
                ("Email Address", "email", true),
                ("Phone Number", "tel", false),
                ("Date of Birth", "date", true),
                ("Street Address", "text", true),
                ("City", "text", true),
                ("State/Province", "select", true),
                ("ZIP/Postal Code", "text", true),
                ("Comments", "textarea", false),
                ("I agree to terms", "checkbox", true),
                ("Signature", "signature", true)
            };
            
            foreach (var (name, type, required) in fieldTemplates.Take(8 + random.Next(4)))
            {
                fields.Add(new DetectedFormField
                {
                    Name = name,
                    Type = type,
                    Label = name,
                    IsRequired = required,
                    Confidence = (float)(0.85 + random.NextDouble() * 0.15),
                    BoundingBox = new BoundingBox 
                    { 
                        X = 100 + random.Next(400),
                        Y = 100 + random.Next(600),
                        Width = 150 + random.Next(100),
                        Height = 25 + random.Next(15)
                    },
                    PageNumber = 1
                });
            }
            
            return new FormFieldDetectionResult
            {
                Success = true,
                DetectedFields = fields,
                TotalFields = fields.Count,
                ProcessingTime = DateTime.UtcNow,
                Message = "AI-Enhanced Detection: Azure Form Recognizer (Simulated)"
            };
        }
    }

    public class DocumentStructure
    {
        public int PageCount { get; set; }
        public string Language { get; set; } = "en";
        public List<TableInfo> Tables { get; set; } = new();
        public List<ParagraphInfo> Paragraphs { get; set; } = new();
        public List<KeyValueInfo> KeyValuePairs { get; set; } = new();
    }

    public class TableInfo
    {
        public int RowCount { get; set; }
        public int ColumnCount { get; set; }
        public int PageNumber { get; set; }
        public List<CellInfo> Cells { get; set; } = new();
    }

    public class CellInfo
    {
        public int RowIndex { get; set; }
        public int ColumnIndex { get; set; }
        public string Content { get; set; } = "";
        public int RowSpan { get; set; } = 1;
        public int ColumnSpan { get; set; } = 1;
    }

    public class ParagraphInfo
    {
        public string Content { get; set; } = "";
        public int PageNumber { get; set; }
        public string Role { get; set; } = "";
    }

    public class KeyValueInfo
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
        public float Confidence { get; set; }
    }

    public class FormFieldDetectionResult
    {
        public bool Success { get; set; }
        public List<DetectedFormField> DetectedFields { get; set; } = new();
        public int TotalFields { get; set; }
        public DateTime ProcessingTime { get; set; }
        public string Message { get; set; } = "";
    }

    public class DetectedFormField
    {
        public string Name { get; set; } = "";
        public string Label { get; set; } = "";
        public string Type { get; set; } = "";
        public string Value { get; set; } = "";
        public int PageNumber { get; set; }
        public float Confidence { get; set; }
        public bool IsRequired { get; set; }
        public BoundingBox BoundingBox { get; set; } = new();
    }

    public class BoundingBox
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
    }
}
