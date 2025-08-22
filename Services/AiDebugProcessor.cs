using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AccessFormServer.Services
{
    public class AiDebugProcessor
    {
        private readonly AnthropicService _anthropicService;
        private readonly AzureFormRecognizerService _azureService;
        private readonly DebugCacheService _debugCache;
        private readonly ILogger<AiDebugProcessor> _logger;

        public AiDebugProcessor(
            AnthropicService anthropicService,
            AzureFormRecognizerService azureService,
            DebugCacheService debugCache,
            ILogger<AiDebugProcessor> logger)
        {
            _anthropicService = anthropicService;
            _azureService = azureService;
            _debugCache = debugCache;
            _logger = logger;
        }

        public async Task<AiProcessingResult> ProcessWithDebugAsync(Stream fileStream, string fileName)
        {
            var result = new AiProcessingResult();
            var startTime = DateTime.UtcNow;

            try
            {
                // Azure Form Recognizer
                _logger.LogInformation("Starting Azure Form Recognizer analysis");
                var azureResult = await _azureService.DetectFormFieldsAsync(fileStream);
                result.AzureResponse = azureResult;
                result.DetectedFields = azureResult?.DetectedFields?.Count ?? 0;
                
                // Reset stream position
                fileStream.Position = 0;
                
                // Anthropic Claude
                _logger.LogInformation("Starting Anthropic Claude analysis");
                
                // Extract text from document for Anthropic
                string documentContent = await ExtractTextContent(fileStream, fileName);
                var anthropicResponse = await _anthropicService.AnalyzeFormFieldsAsync(documentContent);
                result.AnthropicResponse = anthropicResponse;
                
                // Parse Anthropic response
                if (!string.IsNullOrEmpty(anthropicResponse))
                {
                    var fields = _anthropicService.ParseAnalysisResult(anthropicResponse);
                    result.AnthropicFields = fields;
                }
                
                // Calculate processing time
                result.ProcessingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                
                // Store debug data and get ID
                result.DebugId = _debugCache.StoreDebugData(
                    anthropicResponse,
                    azureResult,
                    new { 
                        detectedFields = result.DetectedFields,
                        anthropicFieldCount = result.AnthropicFields?.Count ?? 0,
                        processingTime = result.ProcessingTime
                    }
                );
                
                _logger.LogInformation("AI processing complete. Debug ID: {DebugId}", result.DebugId);
                result.Success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI processing failed");
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<string> ExtractTextContent(Stream fileStream, string fileName)
        {
            // Simple text extraction - in production, use proper PDF/Word text extraction
            // For now, return a placeholder
            return $"Document: {fileName}\n[Document content would be extracted here]";
        }
    }

    public class AiProcessingResult
    {
        public bool Success { get; set; }
        public string DebugId { get; set; }
        public int DetectedFields { get; set; }
        public object AzureResponse { get; set; }
        public string AnthropicResponse { get; set; }
        public List<AnthropicService.FieldAnalysisResult> AnthropicFields { get; set; }
        public long ProcessingTime { get; set; }
        public string ErrorMessage { get; set; }
        public int AccessibilityScore { get; set; } = 85;
        public string AiProvider { get; set; } = "Anthropic Claude + Azure";
    }
}
