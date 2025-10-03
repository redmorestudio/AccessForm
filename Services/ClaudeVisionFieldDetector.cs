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
        public async Task<List<VisualFieldDetectionResult>> AnalyzePdfWithVision(byte[] pdfBytes, int maxPages = 10, string documentMarkdown = null)
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
                _logger.LogInformation($"PDF has {pdfDocument.Pages.Count} total pages, processing {pagesToProcess} pages");

                for (int pageIndex = 0; pageIndex < pagesToProcess; pageIndex++)
                {
                    try
                    {
                        _logger.LogInformation($"Starting Claude Vision analysis of page {pageIndex + 1} of {pagesToProcess}");
                        _logger.LogInformation($"Converting page {pageIndex + 1} to image for vision analysis");
                        
                        // Convert PDF page to image
                        var imageBytes = ConvertPdfPageToImage(pdfBytes, pageIndex);
                        
                        if (imageBytes != null && imageBytes.Length > 0)
                        {
                            // Send to Claude Vision for analysis
                            var pageResult = await AnalyzePageImage(imageBytes, pageIndex + 1, documentMarkdown);
                            results.Add(pageResult);

                            _logger.LogInformation($"Detected {pageResult.Fields.Count} fields on page {pageIndex + 1}");
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
                // Log summary of all pages processed
                var totalFields = results.SelectMany(r => r.Fields).Count();
                var fieldsByPage = results.Select(r => $"Page {r.PageNumber}: {r.Fields.Count} fields").ToList();
                _logger.LogInformation($"Claude Vision summary - Total fields detected: {totalFields}, Breakdown: {string.Join(", ", fieldsByPage)}");
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
        private async Task<VisualFieldDetectionResult> AnalyzePageImage(byte[] imageBytes, int pageNumber, string documentMarkdown = null)
        {
            _logger.LogDebug($"Analyzing page {pageNumber} image ({imageBytes.Length} bytes)");

            var result = new VisualFieldDetectionResult
            {
                PageNumber = pageNumber,
                Success = false
            };
            
            try
            {
                // Convert image to base64
                string base64Image = Convert.ToBase64String(imageBytes);
                
                // Build the prompt with optional markdown context
                var promptText = @"***OUTPUT ONLY VALID JSON - NO OTHER TEXT***

Analyze this form image and identify ALL fillable fields with their exact positions.

RESPONSE FORMAT - YOU MUST RETURN ONLY THIS JSON STRUCTURE:
{
  ""fields"": [
    {
      ""name"": ""Field Label"",
      ""type"": ""text|checkbox|date|phone|email|ssn|signature|etc"",
      ""bounds"": {""x"": 10, ""y"": 20, ""width"": 30, ""height"": 5}
    }
  ]
}

FIELD TYPES: text, checkbox, radio, date, time, phone, email, ssn, ein, password, signature, name, address, number, currency, zip, state, dropdown, textarea, file_upload

POSITIONING (CRITICAL):
- bounds use PERCENTAGES (0-100) of page width/height
- x, y: Top-left corner of INPUT AREA (not label)
- For checkboxes: typically 2-3% width/height
- For text fields: typically 3-5% height

DETECTION REQUIREMENTS:
1. Find ALL fields - count carefully, don't miss any
2. Detect ALL checkboxes (□ ☐ [ ]) - forms often have 10-20+
3. Detect ALL signature fields - look for:
   - Large ""X"" markers (common signature placeholder)
   - Labels containing ""Signature:"", ""Sign here:"", ""Signed:"", etc.
   - Horizontal lines with ""X"" or ""Signature"" nearby
   - Wider rectangular areas (signature fields are typically 2-4x wider than regular text fields)
4. Provide EXACT bounds for each field
5. Use precise field labels from the form

***RETURN ONLY THE JSON - NO EXPLANATORY TEXT BEFORE OR AFTER***";
                
                // Add markdown context if available to help with field naming
                if (!string.IsNullOrEmpty(documentMarkdown))
                {
                    _logger.LogInformation($"Adding markdown context to Claude Vision prompt ({documentMarkdown.Length} characters)");
                    
                    // Debug: write markdown to file
                    var debugPath = $"/tmp/claude_vision_markdown_page_{pageNumber}.md";
                    System.IO.File.WriteAllText(debugPath, documentMarkdown);
                    _logger.LogInformation($"Wrote markdown context for page {pageNumber} to {debugPath}");
                    
                    // Check for specific labels
                    if (documentMarkdown.Contains("Date Sent") || documentMarkdown.Contains("Delivered"))
                    {
                        _logger.LogInformation("Markdown contains 'Date Sent/Delivered' label - Claude should detect this field correctly");
                    }
                    
                    promptText = $@"DOCUMENT CONTEXT (extracted text to help with field naming):
```markdown
{documentMarkdown}
```

{promptText}

IMPORTANT: Use the document context above to accurately name fields. For example:
- If you see 'Date Sent/Delivered:' in the text near a date field, name it 'Date Sent/Delivered'
- If you see checkbox labels like 'In person, hand-delivered', 'Mailed', 'Emailed', 'Faxed', use those exact labels
- Match field names to the text labels shown in the document context";
                }
                else
                {
                    _logger.LogWarning("No markdown context available for Claude Vision - field naming may be less accurate");
                }
                
                // Prepare the Claude Vision API request
                var requestBody = new
                {
                    model = "claude-sonnet-4-20250514",
                    max_tokens = 4096,
                    temperature = 0.0,  // Make results deterministic
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
                                    text = promptText
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
                        _logger.LogDebug($"Sending page {pageNumber} to Claude Vision API");

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

                        _logger.LogDebug($"Claude Vision API response: {response.StatusCode}");
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

                    // Log raw response only in debug mode
                    _logger.LogDebug($"Claude Vision raw response for page {pageNumber}: {content?.Substring(0, Math.Min(500, content?.Length ?? 0))}");

                    result.RawAnalysis = content;
                    result.Fields = ParseVisionResponse(content, pageNumber);
                    result.Success = true;

                    if (result.Fields.Count == 0)
                    {
                        _logger.LogWarning($"No fields detected on page {pageNumber} - check if this is expected");
                    }
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

                    _logger.LogDebug($"Parsing Claude Vision JSON response for page {pageNumber}");

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

                                    _logger.LogDebug($"Parsed field '{field.FieldName}' on page {pageNumber}: bounds=({field.Bounds.XPercent:F1}%, {field.Bounds.YPercent:F1}%, {field.Bounds.WidthPercent:F1}%, {field.Bounds.HeightPercent:F1}%), type={field.FieldType}");
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
        /// IMPORTANT: Claude Vision provides top-left origin coordinates (Y=0 at top)
        /// This method converts to PDF's bottom-left origin (Y=0 at bottom)
        /// </summary>
        public static Syncfusion.Drawing.RectangleF ConvertPercentageToPdfBounds(
            BoundingBox percentBounds,
            float pageWidth,
            float pageHeight,
            string fieldName = "Unknown",
            ILogger logger = null)
        {
            // Log coordinate conversion details - ENHANCED LOGGING
            logger?.LogInformation($"[COORD-CONVERT] Converting field '{fieldName}' from percentages to PDF coordinates");
            logger?.LogInformation($"[COORD-CONVERT] Input percentages: X={percentBounds.XPercent:F2}%, Y={percentBounds.YPercent:F2}%, W={percentBounds.WidthPercent:F2}%, H={percentBounds.HeightPercent:F2}%");
            logger?.LogInformation($"[COORD-CONVERT] Page dimensions: {pageWidth:F0}x{pageHeight:F0} points");

            // Convert percentages to points - NO SCALING
            float x = (percentBounds.XPercent / 100f) * pageWidth;
            float width = (percentBounds.WidthPercent / 100f) * pageWidth;
            float height = (percentBounds.HeightPercent / 100f) * pageHeight;

            logger?.LogInformation($"[COORD-CONVERT] After % to points: X={x:F1}, W={width:F1}, H={height:F1}");

            // COORDINATE CONVERSION REQUIRED!
            // Claude Vision provides Y as distance from TOP (top-left origin, Y=0 at top)
            // Syncfusion PDF uses BOTTOM-LEFT origin (Y=0 at bottom) for form field coordinates
            // Need to flip: bottom_y = pageHeight - top_y - height
            float y = pageHeight - ((percentBounds.YPercent / 100f) * pageHeight) - height;

            logger?.LogInformation($"[COORD-CONVERT] Y={percentBounds.YPercent:F1}% (from top) -> {y:F1} points (bottom-left origin, FLIPPED)");

            // DO NOT apply arbitrary size limits - trust Claude's detection
            // Only apply constraints for specific cases
            string fieldType = fieldName.ToLower();
            if (fieldType.Contains("signature") || fieldType.Contains("sign"))
            {
                // Signatures need minimum size to be usable
                if (width < 100f) width = 100f;
                if (height < 30f) height = 30f;
                logger?.LogInformation($"[COORD-CONVERT] Signature field adjusted to minimum size: W={width:F1}, H={height:F1}");
            }
            // For all other fields, use the detected size as-is

            logger?.LogInformation($"[COORD-CONVERT] FINAL OUTPUT: X={x:F1}, Y={y:F1}, W={width:F1}, H={height:F1} (top-left origin, matches Syncfusion)");

            // Validate bounds are within page
            if (x < 0 || y < 0 || x + width > pageWidth || y + height > pageHeight)
            {
                logger?.LogWarning($"Field '{fieldName}' bounds outside page: X={x:F0}, Y={y:F0}, W={width:F0}, H={height:F0} (Page: {pageWidth:F0}x{pageHeight:F0})");
            }

            var finalRect = new Syncfusion.Drawing.RectangleF(x, y, width, height);

            return finalRect;
        }
    }
}