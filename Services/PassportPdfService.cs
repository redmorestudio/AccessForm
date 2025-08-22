using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AccessFormServer.Services
{
    public class PassportPdfService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<PassportPdfService> _logger;
        private readonly string _apiKey;
        private readonly string _baseUrl;

        public PassportPdfService(HttpClient httpClient, IConfiguration configuration, ILogger<PassportPdfService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            
            var config = configuration.GetSection("PassportPDF");
            _apiKey = config["ApiKey"] ?? "";
            _baseUrl = config["BaseUrl"] ?? "https://api.passportpdf.com/v1";

            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-PassportPDF-API-Key", _apiKey);
            }
        }

        public async Task<byte[]> CreateAccessiblePdfAsync(
            byte[] pdfBytes, 
            TagStructureDefinition tagStructure,
            List<FormFieldDefinition> formFields,
            AccessibilityOptions options = null)
        {
            options ??= AccessibilityOptions.Default();

            try
            {
                // Step 1: Upload document
                var documentId = await UploadDocument(pdfBytes);
                
                // Step 2: Apply tag structure
                if (tagStructure != null)
                {
                    await ApplyTagStructure(documentId, tagStructure);
                }
                
                // Step 3: Enhance form fields
                if (formFields != null && formFields.Any())
                {
                    await EnhanceFormFields(documentId, formFields);
                }
                
                // Step 4: Apply PDF/UA compliance
                await ApplyPdfUaCompliance(documentId, options);
                
                // Step 5: Download processed document
                return await DownloadDocument(documentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing PDF with PassportPDF");
                throw;
            }
        }

        private async Task<string> UploadDocument(byte[] pdfBytes)
        {
            var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(pdfBytes), "file", "document.pdf");
            
            var response = await _httpClient.PostAsync($"{_baseUrl}/pdf/upload", content);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadAsStringAsync();
            var json = JsonSerializer.Deserialize<JsonElement>(result);
            return json.GetProperty("documentId").GetString();
        }

        private async Task ApplyTagStructure(string documentId, TagStructureDefinition tagStructure)
        {
            var request = new
            {
                documentId,
                tagStructure = new
                {
                    documentTitle = tagStructure.DocumentTitle,
                    language = tagStructure.Language,
                    tags = tagStructure.Tags,
                    preserveExisting = tagStructure.PreserveExisting,
                    createTOC = tagStructure.CreateTOC
                }
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{_baseUrl}/pdf/tags/apply", content);
            response.EnsureSuccessStatusCode();
        }

        private async Task EnhanceFormFields(string documentId, List<FormFieldDefinition> fields)
        {
            var request = new
            {
                documentId,
                fields = fields.Select(f => new
                {
                    name = f.Name,
                    type = f.Type.ToString(),
                    tooltip = f.Tooltip,
                    alternativeText = f.AlternativeText,
                    required = f.IsRequired,
                    tabIndex = f.TabIndex,
                    pageNumber = f.PageNumber,
                    boundingBox = f.BoundingBox,
                    validationPattern = f.ValidationPattern,
                    formatScript = f.FormatScript,
                    options = f.Options,
                    defaultValue = f.DefaultValue
                })
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{_baseUrl}/pdf/forms/enhance", content);
            response.EnsureSuccessStatusCode();
        }

        private async Task ApplyPdfUaCompliance(string documentId, AccessibilityOptions options)
        {
            var request = new
            {
                documentId,
                compliance = new
                {
                    standard = "PDF/UA-1",
                    embedFonts = true,
                    setDocumentLanguage = options.Language,
                    addMissingAltText = options.AutoGenerateAltText,
                    fixColorContrast = options.FixColorContrast,
                    createBookmarks = options.CreateBookmarks
                }
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{_baseUrl}/pdf/accessibility/apply-ua", content);
            response.EnsureSuccessStatusCode();
        }

        private async Task<byte[]> DownloadDocument(string documentId)
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/pdf/download/{documentId}");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }

        public async Task<ValidationResult> ValidateAccessibility(byte[] pdfBytes)
        {
            var documentId = await UploadDocument(pdfBytes);
            
            var response = await _httpClient.GetAsync($"{_baseUrl}/pdf/accessibility/validate/{documentId}");
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
        public bool PreserveExisting { get; set; } = false;
        public bool CreateTOC { get; set; } = true;
    }

    public class TagNode
    {
        public string Type { get; set; } // H1, H2, P, Figure, etc.
        public string Content { get; set; }
        public List<TagNode> Children { get; set; } = new();
        public Dictionary<string, string> Attributes { get; set; } = new();
    }

    public class FormFieldDefinition
    {
        public string Name { get; set; }
        public FieldType Type { get; set; }
        public string Tooltip { get; set; }
        public string AlternativeText { get; set; }
        public bool IsRequired { get; set; }
        public int TabIndex { get; set; }
        public int PageNumber { get; set; }
        public BoundingBox BoundingBox { get; set; }
        public string ValidationPattern { get; set; }
        public string FormatScript { get; set; }
        public List<string> Options { get; set; }
        public string DefaultValue { get; set; }
    }

    public enum FieldType
    {
        Text, Date, Email, Phone, SSN, Signature, Checkbox, Radio, Dropdown
    }


    public class AccessibilityOptions
    {
        public string Language { get; set; } = "en-US";
        public bool AutoGenerateAltText { get; set; } = true;
        public bool FixColorContrast { get; set; } = true;
        public bool CreateBookmarks { get; set; } = true;
        
        public static AccessibilityOptions Default() => new();
        public int PageNumber { get; set; }
    }
}
