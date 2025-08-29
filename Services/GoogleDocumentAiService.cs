using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Google.Cloud.DocumentAI.V1;
using Google.Protobuf;
using Google.Api.Gax;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Service to use Google Document AI for form field detection and extraction
    /// </summary>
    public class GoogleDocumentAiService
    {
        private readonly ILogger<GoogleDocumentAiService> _logger;
        private readonly IConfiguration _configuration;
        private readonly DocumentProcessorServiceClient _client;
        private readonly string _projectId;
        private readonly string _location = "us"; // Document AI is available in us or eu
        private readonly string _processorId;

        public GoogleDocumentAiService(
            ILogger<GoogleDocumentAiService> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            
            // Initialize Google credentials
            var credentialsPath = configuration["Google:CredentialsPath"] ?? 
                                  Path.Combine(Directory.GetCurrentDirectory(), "google-service-account.json");
            
            if (File.Exists(credentialsPath))
            {
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialsPath);
                _logger.LogInformation($"Using Google credentials from {credentialsPath}");
            }
            else
            {
                _logger.LogWarning($"Google credentials file not found at {credentialsPath}");
            }
            
            _projectId = configuration["Google:ProjectId"] ?? "271487227879";  // Your actual project ID
            _processorId = configuration["Google:ProcessorId"] ?? "6e7bfa00d33a5e03"; // Your layout parser processor
            _location = configuration["Google:Location"] ?? "us";
            
            try
            {
                _client = DocumentProcessorServiceClient.Create();
                _logger.LogInformation("Google Document AI client initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Google Document AI client");
            }
        }

        public class GoogleFormField
        {
            public string FieldName { get; set; }
            public string FieldValue { get; set; }
            public string FieldType { get; set; }
            public float Confidence { get; set; }
            public BoundingBox Bounds { get; set; }
            public int PageNumber { get; set; }
        }

        public class BoundingBox
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Width { get; set; }
            public float Height { get; set; }
            
            // Normalized coordinates (0-1)
            public float NormalizedX { get; set; }
            public float NormalizedY { get; set; }
            public float NormalizedWidth { get; set; }
            public float NormalizedHeight { get; set; }
        }

        /// <summary>
        /// Process a PDF document using Google Document AI Form Parser
        /// </summary>
        public async Task<List<GoogleFormField>> ProcessPdfAsync(byte[] pdfBytes)
        {
            var fields = new List<GoogleFormField>();
            
            if (_client == null)
            {
                _logger.LogWarning("Google Document AI client not initialized");
                return fields;
            }
            
            try
            {
                _logger.LogInformation($"Processing PDF with Google Document AI ({pdfBytes.Length} bytes)");
                
                // Create processor name
                var processorName = new ProcessorName(_projectId, _location, _processorId);
                
                // Create the request
                var request = new ProcessRequest
                {
                    Name = processorName.ToString(),
                    RawDocument = new RawDocument
                    {
                        Content = ByteString.CopyFrom(pdfBytes),
                        MimeType = "application/pdf"
                    }
                };
                
                // Process the document
                var response = await _client.ProcessDocumentAsync(request);
                
                // Check if we have document text (from layout parser) or pages (from form parser)
                if (!string.IsNullOrEmpty(response.Document.Text))
                {
                    _logger.LogInformation($"Google Document AI Layout Parser extracted text: {response.Document.Text.Length} characters");
                    fields.AddRange(ExtractFieldsFromLayout(response.Document));
                }
                
                if (response.Document.Pages != null && response.Document.Pages.Count > 0)
                {
                    _logger.LogInformation($"Google Document AI found {response.Document.Pages.Count} pages");
                    
                    // Extract form fields from the response
                    foreach (var page in response.Document.Pages)
                {
                    var pageNum = page.PageNumber;
                    
                    // Process form fields
                    if (page.FormFields != null)
                    {
                        foreach (var formField in page.FormFields)
                        {
                            var field = new GoogleFormField
                            {
                                PageNumber = pageNum,
                                Confidence = formField.FieldName?.Confidence ?? 0
                            };
                            
                            // Get field name
                            if (formField.FieldName != null)
                            {
                                field.FieldName = ExtractText(formField.FieldName.TextAnchor, response.Document);
                            }
                            
                            // Get field value
                            if (formField.FieldValue != null)
                            {
                                field.FieldValue = ExtractText(formField.FieldValue.TextAnchor, response.Document);
                            }
                            
                            // Determine field type
                            field.FieldType = DetermineFieldType(formField);
                            
                            // Get bounding box
                            if (formField.FieldName?.BoundingPoly != null)
                            {
                                field.Bounds = ConvertBoundingPoly(formField.FieldName.BoundingPoly, page);
                            }
                            else if (formField.FieldValue?.BoundingPoly != null)
                            {
                                field.Bounds = ConvertBoundingPoly(formField.FieldValue.BoundingPoly, page);
                            }
                            
                            fields.Add(field);
                            _logger.LogDebug($"Found field: {field.FieldName} ({field.FieldType}) on page {pageNum}");
                        }
                    }
                    
                    // Also process detected tables as potential form areas
                    if (page.Tables != null)
                    {
                        foreach (var table in page.Tables)
                        {
                            // Tables often contain form fields
                            ProcessTableAsFormFields(table, page, pageNum, fields);
                        }
                    }
                }
                }
                
                _logger.LogInformation($"Google Document AI extracted {fields.Count} form fields");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process document with Google Document AI");
            }
            
            return fields;
        }
        
        /// <summary>
        /// Extracts form fields from Google Document AI Layout Parser response
        /// </summary>
        private List<GoogleFormField> ExtractFieldsFromLayout(Document document)
        {
            var fields = new List<GoogleFormField>();
            
            try
            {
                // For now, just log that we have layout data
                // The actual DocumentLayout type may not be available in our version of the library
                _logger.LogInformation("Google Document AI returned layout format - extracting text-based fields");
                
                // Extract fields from the document text if available
                if (!string.IsNullOrEmpty(document.Text))
                {
                    var lines = document.Text.Split('\n');
                    foreach (var line in lines)
                    {
                        var text = line.Trim();
                        
                        // Look for field patterns
                        if (text.EndsWith(":") && !text.Contains("?") && text.Length > 2)
                        {
                            var fieldName = text.TrimEnd(':');
                            fields.Add(new GoogleFormField
                            {
                                FieldName = fieldName,
                                FieldType = DetermineFieldTypeFromName(fieldName),
                                PageNumber = 1,
                                Confidence = 0.7f
                            });
                            _logger.LogDebug($"Found field from text: {fieldName}");
                        }
                        // Detect checkboxes
                        else if (text.Contains("☐"))
                        {
                            // Extract label from the line
                            var label = text.Replace("☐", "").Trim();
                            if (!string.IsNullOrEmpty(label))
                            {
                                fields.Add(new GoogleFormField
                                {
                                    FieldName = label,
                                    FieldType = "checkbox",
                                    PageNumber = 1,
                                    Confidence = 0.85f
                                });
                                _logger.LogDebug($"Found checkbox: {label}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not fully process layout format: {ex.Message}");
            }
            
            return fields;
        }

        private string ExtractText(Document.Types.TextAnchor textAnchor, Document document)
        {
            if (textAnchor == null || string.IsNullOrEmpty(document.Text))
                return string.Empty;
            
            var text = new StringBuilder();
            foreach (var segment in textAnchor.TextSegments)
            {
                var startIndex = (int)segment.StartIndex;
                var endIndex = (int)segment.EndIndex;
                
                if (startIndex < document.Text.Length && endIndex <= document.Text.Length)
                {
                    text.Append(document.Text.Substring(startIndex, endIndex - startIndex));
                }
            }
            
            return text.ToString().Trim();
        }

        private string DetermineFieldTypeFromName(string fieldName)
        {
            // Use the comprehensive FieldTypeDetector
            return FieldTypeDetector.DetectFieldType(fieldName, null, null);
        }
        
        private string DetermineFieldType(Document.Types.Page.Types.FormField formField)
        {
            // Extract text values for analysis
            var fieldName = ExtractText(formField.FieldName?.TextAnchor, null);
            var fieldValue = ExtractText(formField.FieldValue?.TextAnchor, null);
            
            // Use the comprehensive FieldTypeDetector with all available context
            return FieldTypeDetector.DetectFieldType(fieldName, fieldValue, null);
        }

        private BoundingBox ConvertBoundingPoly(BoundingPoly boundingPoly, Document.Types.Page page)
        {
            if (boundingPoly?.NormalizedVertices == null || boundingPoly.NormalizedVertices.Count < 4)
                return new BoundingBox();
            
            // Get min/max coordinates from the vertices
            float minX = boundingPoly.NormalizedVertices.Min(v => v.X);
            float maxX = boundingPoly.NormalizedVertices.Max(v => v.X);
            float minY = boundingPoly.NormalizedVertices.Min(v => v.Y);
            float maxY = boundingPoly.NormalizedVertices.Max(v => v.Y);
            
            // Calculate dimensions
            float width = maxX - minX;
            float height = maxY - minY;
            
            // Convert to actual coordinates based on page dimensions
            var pageDimension = page.Dimension;
            float pageWidth = pageDimension?.Width ?? 8.5f * 72; // Default to letter size
            float pageHeight = pageDimension?.Height ?? 11f * 72;
            
            return new BoundingBox
            {
                X = minX * pageWidth,
                Y = minY * pageHeight,
                Width = width * pageWidth,
                Height = height * pageHeight,
                NormalizedX = minX,
                NormalizedY = minY,
                NormalizedWidth = width,
                NormalizedHeight = height
            };
        }

        private void ProcessTableAsFormFields(
            Document.Types.Page.Types.Table table,
            Document.Types.Page page,
            int pageNum,
            List<GoogleFormField> fields)
        {
            if (table.BodyRows == null)
                return;
            
            foreach (var row in table.BodyRows)
            {
                if (row.Cells == null)
                    continue;
                    
                foreach (var cell in row.Cells)
                {
                    // Look for cells that might be form fields
                    var cellText = ExtractText(cell.Layout?.TextAnchor, null);
                    
                    // Check if this looks like a form field (empty or has field indicators)
                    if (string.IsNullOrWhiteSpace(cellText) || 
                        cellText.Contains("___") || 
                        cellText.StartsWith("[") && cellText.EndsWith("]"))
                    {
                        var field = new GoogleFormField
                        {
                            PageNumber = pageNum,
                            FieldType = "text",
                            FieldName = $"Table_Field_{fields.Count + 1}",
                            Confidence = 0.7f
                        };
                        
                        if (cell.Layout?.BoundingPoly != null)
                        {
                            field.Bounds = ConvertBoundingPoly(cell.Layout.BoundingPoly, page);
                        }
                        
                        fields.Add(field);
                    }
                }
            }
        }
    }
}