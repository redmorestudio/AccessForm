using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Syncfusion.DocIO.DLS;
using Syncfusion.Pdf.Parsing;

namespace AccessFormServer.Services
{
    public class AiDebugProcessor
    {
        private readonly ILogger<AiDebugProcessor> _logger;
        private readonly AnthropicService _anthropicService;
        private readonly DebugCacheService _debugCache;

        public AiDebugProcessor(
            ILogger<AiDebugProcessor> logger,
            AnthropicService anthropicService,
            DebugCacheService debugCache)
        {
            _logger = logger;
            _anthropicService = anthropicService;
            _debugCache = debugCache;
        }

        public async Task<AiProcessingResult> ProcessWithDebugAsync(Stream fileStream, string fileName)
        {
            var result = new AiProcessingResult();
            var startTime = DateTime.UtcNow;

            try
            {
                // Skip Azure, go straight to Anthropic Claude
                _logger.LogInformation("Starting Anthropic Claude analysis");
                
                // Extract ACTUAL text from document for Anthropic
                string documentContent = await ExtractTextContent(fileStream, fileName);
                _logger.LogInformation($"Sending {documentContent.Length} characters to Anthropic");
                
                var anthropicResponse = await _anthropicService.AnalyzeFormFieldsAsync(documentContent);
                result.AnthropicResponse = anthropicResponse;
                
                // Parse Anthropic response
                if (!string.IsNullOrEmpty(anthropicResponse))
                {
                    var fields = _anthropicService.ParseAnalysisResult(anthropicResponse);
                    result.AnthropicFields = fields;
                    result.DetectedFields = fields?.Count ?? 0;
                }
                
                // Calculate processing time
                result.ProcessingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                
                // Store debug data and get ID
                var debugInfo = new
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    fileName = fileName,
                    processingTimeMs = result.ProcessingTime,
                    aiProvider = "Anthropic Claude",
                    detectedFields = result.DetectedFields,
                    documentTextLength = documentContent.Length,
                    documentTextPreview = documentContent.Length > 500 ? documentContent.Substring(0, 500) + "..." : documentContent,
                    anthropicResponse = anthropicResponse,
                    anthropicFieldCount = result.AnthropicFields?.Count ?? 0
                };
                result.DebugId = _debugCache.StoreDebugData(debugInfo);                
                _logger.LogInformation("AI processing complete. Debug ID: {DebugId}", result.DebugId);
                result.Success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI processing failed");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                
                // Return simulated data on failure
                var errorDebugInfo = new
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    fileName = "AI Analysis Error",
                    processingTimeMs = 0L,
                    aiProvider = "Anthropic Claude",
                    error = true,
                    message = ex.Message,
                    anthropicResponse = "Error: " + ex.Message
                };
                result.DebugId = _debugCache.StoreDebugData(errorDebugInfo);
            }
            
            return result;
        }

        private async Task<string> ExtractTextContent(Stream fileStream, string fileName)
        {
            try
            {
                // Read the entire stream into memory
                using var memoryStream = new MemoryStream();
                await fileStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                
                // For .docx files, extract text using Syncfusion
                if (fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    using var wordDocument = new Syncfusion.DocIO.DLS.WordDocument(memoryStream, Syncfusion.DocIO.FormatType.Docx);
                    var text = wordDocument.GetText();
                    _logger.LogInformation($"Extracted {text.Length} characters from Word document");
                    return text;
                }
                else if (fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    // For PDF files, use Syncfusion PDF library
                    using var pdfDocument = new Syncfusion.Pdf.Parsing.PdfLoadedDocument(memoryStream);
                    var extractedText = new StringBuilder();
                    
                    for (int i = 0; i < pdfDocument.Pages.Count; i++)
                    {
                        var page = pdfDocument.Pages[i];
                        extractedText.AppendLine(page.ExtractText());
                    }
                    
                    var text = extractedText.ToString();
                    _logger.LogInformation($"Extracted {text.Length} characters from PDF document");
                    return text;
                }
                else
                {
                    // For other file types, try to read as text
                    memoryStream.Position = 0;
                    using var reader = new StreamReader(memoryStream);
                    var text = await reader.ReadToEndAsync();
                    _logger.LogInformation($"Read {text.Length} characters from text file");
                    return text;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract text from document");
                // Return at least the filename so Claude has something
                return $"[Error extracting text from {fileName}: {ex.Message}]";
            }
        }
    }

    public class AiProcessingResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string DebugId { get; set; }
        public object AzureResponse { get; set; }
        public string AnthropicResponse { get; set; }
        public int DetectedFields { get; set; }
        public System.Collections.Generic.List<AnthropicService.FieldAnalysisResult> AnthropicFields { get; set; }
        public long ProcessingTime { get; set; }
        public int AccessibilityScore { get; set; } = 85;
        public string AiProvider { get; set; } = "Anthropic Claude";
    }
}
