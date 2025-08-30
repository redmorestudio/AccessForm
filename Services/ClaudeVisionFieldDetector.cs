using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using Syncfusion.Pdf.Graphics;
using SkiaSharp;
using Syncfusion.Pdf.Exporting;
using PDFtoImage;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Uses Claude Vision API to detect form fields visually in PDF documents
    /// This helps identify fields that aren't programmatically marked but are visually apparent
    /// </summary>
    public class ClaudeVisionFieldDetector
    {
        private readonly ILogger<ClaudeVisionFieldDetector> _logger;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public ClaudeVisionFieldDetector(
            ILogger<ClaudeVisionFieldDetector> logger,
            IConfiguration configuration,
            HttpClient httpClient)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClient = httpClient;
            _apiKey = configuration["ApiKeys:Anthropic"] ?? configuration["AnthropicApiKey"];
            
            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogWarning("Anthropic API key not configured - Claude Vision field detection will not be available");
            }
        }

        public class VisualFieldDetectionResult
        {
            public List<DetectedField> Fields { get; set; } = new();
            public int PageNumber { get; set; }
            public string RawAnalysis { get; set; }
            public bool Success { get; set; }
            public string Error { get; set; }
        }

        public class DetectedField
        {
            public string FieldName { get; set; }
            public string FieldType { get; set; } // text, checkbox, signature, date, etc.
            public BoundingBox Bounds { get; set; }
            public string Description { get; set; }
            public bool IsRequired { get; set; }
            public int PageNumber { get; set; }
        }

        public class BoundingBox
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Width { get; set; }
            public float Height { get; set; }
            
            // Percentage-based coordinates (0-100) for easier mapping
            public float XPercent { get; set; }
            public float YPercent { get; set; }
            public float WidthPercent { get; set; }
            public float HeightPercent { get; set; }
        }

        /// <summary>
        /// Analyzes a PDF document page by page using Claude Vision to detect form fields
        /// </summary>
        public async Task<List<VisualFieldDetectionResult>> AnalyzePdfWithVision(byte[] pdfBytes, int maxPages = 10)
        {
            var results = new List<VisualFieldDetectionResult>();
            
            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogWarning("Cannot perform vision analysis - AnthropicApiKey not configured");
                return results;
            }
            
            try
            {
                using var pdfStream = new MemoryStream(pdfBytes);
                using var pdfDocument = new PdfLoadedDocument(pdfStream);
                
                int pagesToProcess = Math.Min(pdfDocument.Pages.Count, maxPages);
                _logger.LogInformation($"Processing {pagesToProcess} pages with Claude Vision");
                
                for (int pageIndex = 0; pageIndex < pagesToProcess; pageIndex++)
                {
                    try
                    {
                        _logger.LogInformation($"Converting page {pageIndex + 1} to image for vision analysis");
                        
                        // Convert PDF page to image
                        var imageBytes = ConvertPdfPageToImage(pdfBytes, pageIndex);
                        
                        if (imageBytes != null && imageBytes.Length > 0)
                        {
                            // Send to Claude Vision for analysis
                            var pageResult = await AnalyzePageImage(imageBytes, pageIndex + 1);
                            results.Add(pageResult);
                            
                            _logger.LogInformation($"Page {pageIndex + 1}: Detected {pageResult.Fields.Count} fields visually");
                        }
                        else
                        {
                            _logger.LogWarning($"Could not convert page {pageIndex + 1} to image");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error processing page {pageIndex + 1}");
                        results.Add(new VisualFieldDetectionResult
                        {
                            PageNumber = pageIndex + 1,
                            Success = false,
                            Error = ex.Message
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze PDF with vision");
                throw;
            }
            
            return results;
        }

        /// <summary>
        /// Converts a specific PDF page to PNG image for vision analysis using PDFtoImage library
        /// </summary>
        private byte[] ConvertPdfPageToImage(byte[] pdfBytes, int pageIndex)
        {
            try
            {
                _logger.LogInformation($"Converting page {pageIndex + 1} to PNG image for vision analysis");
                
                // Convert the PDF page to image at high DPI for better OCR
                // Use high DPI for accurate checkbox and field detection
                PDFtoImage.RenderOptions options = new PDFtoImage.RenderOptions
                {
                    Dpi = 200,  // Good balance of quality and speed
                    WithAnnotations = true,  // Include form field annotations
                    WithFormFill = true,     // Include filled form data
                    AntiAliasing = PDFtoImage.PdfAntiAliasing.All  // Better quality
                };
                
                // PDFtoImage.Conversion.ToImage expects a file path or byte array
                using var bitmap = PDFtoImage.Conversion.ToImage(pdfBytes, pageIndex);
                
                if (bitmap != null)
                {
                    // Convert SkiaSharp SKBitmap to PNG bytes with high quality
                    using var image = SKImage.FromBitmap(bitmap);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);  // Maximum quality
                    
                    var imageBytes = data.ToArray();
                    _logger.LogInformation($"Successfully converted page {pageIndex + 1} to PNG ({imageBytes.Length} bytes)");
                    return imageBytes;
                }
                else
                {
                    _logger.LogWarning($"PDFtoImage returned null for page {pageIndex + 1}");
                    // Fallback to our custom rendering
                    using var fallbackStream = new MemoryStream(pdfBytes);
                    using var pdfDocument = new PdfLoadedDocument(fallbackStream);
                    return RenderPdfPageWithSkiaSharp(pdfDocument, pageIndex);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to convert PDF page {pageIndex} to image: {ex.Message}");
                
                // Try fallback with Syncfusion
                try
                {
                    using var pdfStream = new MemoryStream(pdfBytes);
                    using var pdfDocument = new PdfLoadedDocument(pdfStream);
                    return RenderPdfPageWithSkiaSharp(pdfDocument, pageIndex);
                }
                catch
                {
                    // Final fallback: create a blank image
                    return CreateBlankPageImage(pageIndex);
                }
            }
        }
        
        /// <summary>
        /// Renders a PDF page using SkiaSharp with text extraction
        /// </summary>
        private byte[] RenderPdfPageWithSkiaSharp(PdfLoadedDocument pdfDocument, int pageIndex)
        {
            try
            {
                _logger.LogInformation($"Rendering page {pageIndex + 1} with SkiaSharp");
                
                var page = pdfDocument.Pages[pageIndex] as PdfLoadedPage;
                
                // Get page dimensions - use high DPI for quality
                const float dpi = 200f;  // Match main converter DPI
                const float dpiScale = dpi / 72f; // PDF uses 72 DPI by default
                
                var pageWidth = (int)(page.Size.Width * dpiScale);
                var pageHeight = (int)(page.Size.Height * dpiScale);
                
                using var bitmap = new SKBitmap(pageWidth, pageHeight);
                using var canvas = new SKCanvas(bitmap);
                
                // White background
                canvas.Clear(SKColors.White);
                
                // Extract text from the page
                var pageText = page.ExtractText();
                
                // Draw the extracted text onto the canvas
                using var textPaint = new SKPaint
                {
                    Color = SKColors.Black,
                    TextSize = 12 * dpiScale,
                    IsAntialias = true,
                    Typeface = SKTypeface.FromFamilyName("Arial")
                };
                
                // Draw text lines
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    var lines = pageText.Split('\n');
                    float y = 30 * dpiScale;
                    float lineHeight = 20 * dpiScale;
                    
                    foreach (var line in lines.Take(100)) // Limit to prevent overflow
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            canvas.DrawText(line.Trim(), 20 * dpiScale, y, textPaint);
                            y += lineHeight;
                            
                            if (y > pageHeight - 30 * dpiScale)
                                break;
                        }
                    }
                }
                
                // Draw form field indicators where we detect common patterns
                DrawFormFieldIndicators(canvas, pageText, pageWidth, pageHeight, dpiScale);
                
                // Draw a border
                using var borderPaint = new SKPaint
                {
                    Color = SKColors.Gray,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1
                };
                canvas.DrawRect(5, 5, pageWidth - 10, pageHeight - 10, borderPaint);
                
                // Convert to PNG
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 85);
                
                var imageBytes = data.ToArray();
                _logger.LogInformation($"Created PNG image ({imageBytes.Length} bytes) for page {pageIndex + 1}");
                
                return imageBytes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"SkiaSharp rendering failed for page {pageIndex}");
                return null;
            }
        }
        
        /// <summary>
        /// Draw visual indicators for potential form fields
        /// </summary>
        private void DrawFormFieldIndicators(SKCanvas canvas, string pageText, int width, int height, float scale)
        {
            try
            {
                using var fieldPaint = new SKPaint
                {
                    Color = SKColors.LightBlue.WithAlpha(50),
                    Style = SKPaintStyle.Fill
                };
                
                using var fieldBorderPaint = new SKPaint
                {
                    Color = SKColors.Blue,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 5, 5 }, 0)
                };
                
                // Look for common field patterns in text
                var lines = pageText.Split('\n');
                float y = 30 * scale;
                float lineHeight = 20 * scale;
                
                foreach (var line in lines)
                {
                    // Check for field indicators
                    if (line.Contains("___") || line.Contains("...") || 
                        line.EndsWith(":") || line.Contains("[ ]") || line.Contains("( )"))
                    {
                        // Draw a light blue box to indicate potential field
                        var rect = new SKRect(100 * scale, y - 15 * scale, 
                                            width - 50 * scale, y + 5 * scale);
                        canvas.DrawRect(rect, fieldPaint);
                        canvas.DrawRect(rect, fieldBorderPaint);
                    }
                    
                    y += lineHeight;
                    if (y > height - 30 * scale)
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not draw field indicators: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Creates a blank page image as final fallback
        /// </summary>
        private byte[] CreateBlankPageImage(int pageIndex)
        {
            try
            {
                _logger.LogInformation($"Creating blank page image for page {pageIndex + 1}");
                
                const int width = 1700;  // ~8.5 inches at 200 DPI
                const int height = 2200; // ~11 inches at 200 DPI
                
                using var bitmap = new SKBitmap(width, height);
                using var canvas = new SKCanvas(bitmap);
                
                canvas.Clear(SKColors.White);
                
                using var paint = new SKPaint
                {
                    Color = SKColors.Gray,
                    TextSize = 48,
                    IsAntialias = true
                };
                
                var text = $"Page {pageIndex + 1}";
                var textBounds = new SKRect();
                paint.MeasureText(text, ref textBounds);
                
                canvas.DrawText(text, 
                    (width - textBounds.Width) / 2, 
                    (height - textBounds.Height) / 2, 
                    paint);
                
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 90);
                
                return data.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to create blank page for {pageIndex}");
                return null;
            }
        }

        /// <summary>
        /// Analyzes a page image using Claude Vision API
        /// </summary>
        private async Task<VisualFieldDetectionResult> AnalyzePageImage(byte[] imageBytes, int pageNumber)
        {
            var result = new VisualFieldDetectionResult
            {
                PageNumber = pageNumber,
                Success = false
            };
            
            try
            {
                // Convert image to base64
                string base64Image = Convert.ToBase64String(imageBytes);
                
                // Prepare the Claude Vision API request
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
                                    text = @"CRITICAL: You MUST identify EVERY SINGLE fillable field on this form page, especially ALL checkboxes! Count them carefully!

Analyze this government form image and identify ALL fillable fields. This form likely has 30-40+ fields including many checkboxes. You must detect ALL 33 field types defined in AccessForm specification:

CRITICAL - DETECT ALL THESE FIELD TYPES:

1. SECURITY FIELDS (High Priority):
- SSN: Social Security Number fields (XXX-XX-XXXX format)
- EIN: Employer Identification Number (XX-XXXXXXX format)  
- Password: Password entry fields
- PIN: PIN code fields (4-6 digits)

2. PERSONAL INFORMATION:
- Name: Full name, first/last name fields (may be composite)
- Email: Email address fields
- Phone: Phone number fields (XXX) XXX-XXXX
- Address: Street address fields (may be multi-line composite)

3. CHECKBOXES & SELECTIONS (DETECT EVERY SINGLE ONE):
- Checkbox: ANY square boxes □ ☐ [ ] that can be checked - LOOK FOR ALL OF THEM!
  * Small squares next to text labels
  * Often appear in groups or lists
  * May have labels like Yes, No, disability types, service options, etc.
- Radio: Circle buttons ○ ◯ ( ) for single selection
- Dropdown: Fields with dropdown arrows ▼ or selection lists
- Listbox: Multi-select list fields

CHECKBOX DETECTION IS CRITICAL - Forms often have 10-20+ checkboxes!

4. DATE & TIME:
- Date: Date entry fields (MM/DD/YYYY)
- Time: Time entry fields (HH:MM AM/PM)
- DateTime: Combined date and time fields

5. NUMERIC FIELDS:
- Number: Numeric entry fields (age, quantity, etc.)
- Currency: Dollar amount fields ($)
- Percentage: Percentage fields (%)

6. TEXT AREAS:
- Text: Single-line text input (default type)
- Textarea: Multi-line text boxes for comments/notes
- Signature: Signature lines (often marked ""Sign Here"")

7. LOCATION FIELDS:
- Zip: ZIP code fields (XXXXX or XXXXX-XXXX)
- State: State selection fields
- Country: Country selection fields

8. MEDIA & UPLOAD:
- File_Upload: File attachment/upload areas
- Image: Image/photo upload fields
- Barcode: Barcode fields
- QR_Code: QR code fields

9. INTERACTIVE:
- Rating: Star or numeric rating fields
- Slider: Range/slider controls
- Color_Picker: Color selection fields
- URL: Website/URL fields

10. SPECIAL:
- Table: Table/grid data entry areas

For each field found, provide:
1. Field name/label exactly as shown
2. Field type from the 33 types above (use exact type names: ssn, ein, checkbox, etc.)
3. Position as percentages (0-100): {x%, y%, width%, height%}
4. Required indicator (* or ""Required"")
5. Any tooltips or help text visible

IMPORTANT POSITIONING:
- x, y: Top-left corner of the INPUT AREA (not the label)
- width: Width of the actual input field/checkbox
- height: Height of the input area
- For checkboxes: typical size is 2-3% width/height
- For text fields: height typically 3-5%, width varies

IMPORTANT: Be EXHAUSTIVE! If you see 40 fields, return 40 fields. Do not stop early or summarize!
Count carefully: text fields, checkboxes (especially in groups), dates, signatures, etc.

Return JSON format:
{
  ""fields"": [
    {
      ""name"": ""Field Label"",
      ""type"": ""text|email|phone|ssn|date|checkbox|radio|signature|etc"",
      ""bounds"": {""x"": 10, ""y"": 20, ""width"": 30, ""height"": 5},
      ""required"": true|false,
      ""description"": ""Help text or additional context""
    }
  ],
  ""confidence"": ""high|medium|low"",
  ""notes"": ""Form observations and detected patterns""
}"
                                }
                            }
                        }
                    }
                };
                
                // Call Claude Vision API with retry logic
                const int maxRetries = 3;
                int retryCount = 0;
                HttpResponseMessage response = null;
                string responseContent = null;
                
                while (retryCount < maxRetries)
                {
                    try
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
                        request.Headers.Add("x-api-key", _apiKey);
                        request.Headers.Add("anthropic-version", "2023-06-01");
                        request.Content = new StringContent(
                            JsonSerializer.Serialize(requestBody),
                            System.Text.Encoding.UTF8,
                            "application/json"
                        );
                        
                        response = await _httpClient.SendAsync(request);
                        responseContent = await response.Content.ReadAsStringAsync();
                        break; // Success, exit retry loop
                    }
                    catch (HttpRequestException httpEx) when (retryCount < maxRetries - 1)
                    {
                        retryCount++;
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount)); // Exponential backoff
                        _logger.LogWarning($"HTTP request failed (attempt {retryCount}/{maxRetries}), retrying in {delay.TotalSeconds}s: {httpEx.Message}");
                        await Task.Delay(delay);
                    }
                }
                
                if (response.IsSuccessStatusCode)
                {
                    var apiResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    var content = apiResponse.GetProperty("content")[0].GetProperty("text").GetString();
                    
                    result.RawAnalysis = content;
                    result.Fields = ParseVisionResponse(content, pageNumber);
                    result.Success = true;
                    
                    _logger.LogInformation($"Claude Vision detected {result.Fields.Count} fields on page {pageNumber}");
                }
                else
                {
                    _logger.LogError($"Claude Vision API error: {response.StatusCode} - {responseContent}");
                    result.Error = $"API Error: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to analyze page {pageNumber} with Claude Vision");
                result.Error = ex.Message;
            }
            
            return result;
        }

        /// <summary>
        /// Parses the Claude Vision response to extract field information
        /// </summary>
        private List<DetectedField> ParseVisionResponse(string response, int pageNumber)
        {
            var fields = new List<DetectedField>();
            
            try
            {
                // Extract JSON from the response (Claude might include explanation text)
                int jsonStart = response.IndexOf('{');
                int jsonEnd = response.LastIndexOf('}') + 1;
                
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    string jsonContent = response.Substring(jsonStart, jsonEnd - jsonStart);
                    var jsonDoc = JsonDocument.Parse(jsonContent);
                    
                    if (jsonDoc.RootElement.TryGetProperty("fields", out var fieldsArray))
                    {
                        foreach (var fieldElement in fieldsArray.EnumerateArray())
                        {
                            try
                            {
                                var field = new DetectedField
                                {
                                    PageNumber = pageNumber,
                                    FieldName = fieldElement.GetProperty("name").GetString() ?? $"Field_{fields.Count + 1}",
                                    FieldType = fieldElement.GetProperty("type").GetString() ?? "text"
                                };
                                
                                // Parse bounds
                                if (fieldElement.TryGetProperty("bounds", out var bounds))
                                {
                                    field.Bounds = new BoundingBox
                                    {
                                        XPercent = bounds.GetProperty("x").GetSingle(),
                                        YPercent = bounds.GetProperty("y").GetSingle(),
                                        WidthPercent = bounds.GetProperty("width").GetSingle(),
                                        HeightPercent = bounds.GetProperty("height").GetSingle()
                                    };
                                }
                                
                                // Parse optional properties
                                if (fieldElement.TryGetProperty("required", out var required))
                                {
                                    field.IsRequired = required.GetBoolean();
                                }
                                
                                if (fieldElement.TryGetProperty("description", out var description))
                                {
                                    field.Description = description.GetString();
                                }
                                
                                fields.Add(field);
                                _logger.LogDebug($"Parsed field: {field.FieldName} ({field.FieldType}) at page {pageNumber}");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Could not parse field element: {ex.Message}");
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("No JSON content found in Claude Vision response");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Claude Vision response");
            }
            
            return fields;
        }

        /// <summary>
        /// Converts percentage-based bounds to actual PDF coordinates with corrections
        /// </summary>
        public static Syncfusion.Drawing.RectangleF ConvertPercentageToPdfBounds(
            BoundingBox percentBounds, 
            float pageWidth, 
            float pageHeight)
        {
            // Apply scaling factor to correct for typical misalignment
            // Vision models often over-estimate field sizes
            const float SCALE_FACTOR = 0.6f;  // Reduce size by 40%
            const float MIN_WIDTH = 50f;
            const float MIN_HEIGHT = 15f;
            const float MAX_WIDTH = 400f;
            const float MAX_HEIGHT = 60f;
            
            // Convert percentages to actual coordinates
            float x = (percentBounds.XPercent / 100f) * pageWidth;
            float y = (percentBounds.YPercent / 100f) * pageHeight;
            float width = (percentBounds.WidthPercent / 100f) * pageWidth * SCALE_FACTOR;
            float height = (percentBounds.HeightPercent / 100f) * pageHeight * SCALE_FACTOR;
            
            // Apply reasonable limits
            width = Math.Max(MIN_WIDTH, Math.Min(MAX_WIDTH, width));
            height = Math.Max(MIN_HEIGHT, Math.Min(MAX_HEIGHT, height));
            
            return new Syncfusion.Drawing.RectangleF(x, y, width, height);
        }
    }
}