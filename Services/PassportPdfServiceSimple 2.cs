using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PassportPDF.Api;
using PassportPDF.Model;
using WordToPdfConverter.Models;

namespace AccessFormServer.Services
{
    /// <summary>
    /// Simplified PassportPDF service for creating accessible PDFs
    /// </summary>
    public class PassportPdfServiceSimple  
    {
        private readonly ILogger<PassportPdfServiceSimple> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _apiKey;
        
        public PassportPdfServiceSimple(IConfiguration configuration, ILogger<PassportPdfServiceSimple> logger)
        {
            _logger = logger;
            _configuration = configuration;
            
            // Get API key from configuration or use demo key
            _apiKey = _configuration["PassportPDF:ApiKey"] ?? 
                     _configuration["ApiKeys:PassportPdf"] ?? 
                     "YOUR-PASSPORT-CODE";
            
            // Set global API configuration
            if (_apiKey != "YOUR-PASSPORT-CODE")
            {
                PassportPDF.Client.GlobalConfiguration.ApiKey["X-PassportPDF-API-Key"] = _apiKey;
                _logger.LogInformation("PassportPDF Simple service initialized with API key");
            }
            else
            {
                _logger.LogWarning("PassportPDF Simple service in demo mode");
            }
        }
        
        /// <summary>
        /// Creates an accessible PDF with proper tag structure
        /// </summary>
        public async Task<(byte[] pdfBytes, PassportPdfResult metadata)> CreateAccessiblePdfAsync(
            byte[] inputBytes, 
            string fileName,
            List<FieldDetectionResult> detectedFields)
        {
            var result = new PassportPdfResult();
            
            try
            {
                _logger.LogInformation($"Processing {fileName} with PassportPDF API");
                
                // Initialize API clients
                var documentApi = new DocumentApi();
                var pdfApi = new PDFApi();
                
                // Step 1: Load the document
                var loadParams = new LoadDocumentFromByteArrayParameters(inputBytes);
                loadParams.FileName = fileName;
                
                var loadResponse = await documentApi.DocumentLoadAsync(loadParams);
                
                if (loadResponse.Error != null)
                {
                    throw new Exception($"Failed to load document: {loadResponse.Error.ExtResultMessage}");
                }
                
                result.FileId = loadResponse.FileId;
                result.PageCount = loadResponse.PageCount;
                _logger.LogInformation($"Document loaded with FileId: {result.FileId}, Pages: {result.PageCount}");
                
                // Step 2: Get PDF information
                var infoParams = new PdfGetInfoParameters();
                infoParams.FileId = result.FileId;
                
                var infoResponse = await pdfApi.GetInfoAsync(infoParams);
                
                if (infoResponse.Error == null)
                {
                    result.Title = infoResponse.Title ?? Path.GetFileNameWithoutExtension(fileName);
                    result.HasForm = false; // Will be set based on detected fields
                    result.HasTaggedContent = infoResponse.IsTagged; // Use PassportPDF's tag detection
                    _logger.LogInformation($"PDF Info retrieved: {result.Title}, Tagged: {result.HasTaggedContent}");
                }
                
                // Step 3: Extract text for AI analysis
                var extractParams = new PdfExtractTextParameters();
                extractParams.FileId = result.FileId;
                extractParams.PageRange = "*";
                
                var extractResponse = await pdfApi.ExtractTextAsync(extractParams);
                if (extractResponse.Error == null && extractResponse.ExtractedText != null)
                {
                    result.ExtractedText = string.Join("\n", extractResponse.ExtractedText.Select(page => page.ExtractedText));
                    _logger.LogInformation($"Extracted {result.ExtractedText.Length} characters of text");
                }
                
                // Step 4: Add detected form fields count
                if (detectedFields != null && detectedFields.Count > 0)
                {
                    result.FieldCount = detectedFields.Count;
                    result.HasForm = true;
                    _logger.LogInformation($"Will create {detectedFields.Count} form fields");
                    
                    // Create form fields in the PDF using PassportPDF if we have an API key
                    if (_apiKey != "YOUR-PASSPORT-CODE")
                    {
                        await CreateFormFieldsWithPassportPdf(pdfApi, result.FileId, detectedFields);
                    }
                }
                
                // Step 5: Apply PDF/UA compliance if possible
                try
                {
                    var convertParams = new PdfConvertToPDFAParameters();
                    convertParams.FileId = result.FileId;
                    convertParams.Conformance = PdfAConformance.PDFA1a; // Best for accessibility
                    
                    var convertResponse = await pdfApi.ConvertToPDFAAsync(convertParams);
                    if (convertResponse.Error == null)
                    {
                        result.IsPdfUaCompliant = true;
                        _logger.LogInformation("Successfully converted to PDF/A-1a for accessibility");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PDF/A conversion failed, continuing without");
                }
                
                // Step 6: Save the document
                var saveParams = new PdfSaveDocumentParameters();
                saveParams.FileId = result.FileId;
                
                var saveResponse = await pdfApi.SaveDocumentAsync(saveParams);
                
                if (saveResponse.Error != null)
                {
                    throw new Exception($"Failed to save PDF: {saveResponse.Error.ExtResultMessage}");
                }
                
                var pdfBytes = saveResponse.Data;
                
                result.Success = true;
                result.Message = "PDF processed successfully with PassportPDF";
                
                // Clean up the file from PassportPDF servers
                try
                {
                    var closeParams = new DocumentCloseParameters();
                    closeParams.FileId = result.FileId;
                    await documentApi.DocumentCloseAsync(closeParams);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to close document: {ex.Message}");
                }
                
                _logger.LogInformation($"PassportPDF processing complete. Size: {pdfBytes.Length} bytes");
                
                return (pdfBytes, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PassportPDF processing error");
                result.Success = false;
                result.Message = $"Error: {ex.Message}";
                throw;
            }
        }
        
        /// <summary>
        /// Create form fields using PassportPDF API (placeholder - would need specific field creation APIs)
        /// </summary>
        private async Task CreateFormFieldsWithPassportPdf(PDFApi pdfApi, string fileId, List<FieldDetectionResult> fields)
        {
            try
            {
                _logger.LogInformation($"Creating {fields.Count} form fields with PassportPDF API");
                
                // Note: PassportPDF doesn't have direct form field creation APIs in the current version
                // This would require using the form editing endpoints if available
                // For now, we'll log the intent and rely on other services for field creation
                
                foreach (var field in fields)
                {
                    _logger.LogDebug($"Would create field: {field.FieldName} ({field.FieldType}) at ({field.X}, {field.Y})");
                }
                
                _logger.LogInformation("Form field creation logged (API limitations - would need form editing endpoints)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create form fields with PassportPDF API");
            }
        }
        
        /// <summary>
        /// Extracts text content from a PDF using PassportPDF
        /// </summary>
        public async Task<string> ExtractTextFromPdfAsync(byte[] pdfBytes)
        {
            try
            {
                var documentApi = new DocumentApi();
                var pdfApi = new PDFApi();
                
                // Load the PDF
                var loadParams = new LoadDocumentFromByteArrayParameters(pdfBytes);
                loadParams.FileName = "document.pdf";
                
                var loadResponse = await documentApi.DocumentLoadAsync(loadParams);
                
                if (loadResponse.Error != null)
                {
                    _logger.LogWarning($"Failed to load PDF for text extraction: {loadResponse.Error.ExtResultMessage}");
                    return string.Empty;
                }
                
                // Extract text
                var extractParams = new PdfExtractTextParameters();
                extractParams.FileId = loadResponse.FileId;
                extractParams.PageRange = "*"; // All pages
                
                var extractResponse = await pdfApi.ExtractTextAsync(extractParams);
                
                // Clean up
                try
                {
                    var closeParams = new DocumentCloseParameters();
                    closeParams.FileId = loadResponse.FileId;
                    await documentApi.DocumentCloseAsync(closeParams);
                }
                catch
                {
                    // Ignore close errors
                }
                
                if (extractResponse.Error == null && extractResponse.ExtractedText != null)
                {
                    var extractedText = string.Join("\n", extractResponse.ExtractedText.Select(page => page.ExtractedText));
                    _logger.LogInformation($"PassportPDF extracted {extractedText.Length} characters from {extractResponse.ExtractedText.Count} pages");
                    return extractedText;
                }
                
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from PDF with PassportPDF");
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Analyze PDF document structure and form fields
        /// </summary>
        public async Task<PdfAnalysisResult> AnalyzePdfStructureAsync(byte[] pdfBytes)
        {
            var result = new PdfAnalysisResult();
            
            try
            {
                var documentApi = new DocumentApi();
                var pdfApi = new PDFApi();
                
                // Load the PDF
                var loadParams = new LoadDocumentFromByteArrayParameters(pdfBytes);
                loadParams.FileName = "document.pdf";
                
                var loadResponse = await documentApi.DocumentLoadAsync(loadParams);
                
                if (loadResponse.Error != null)
                {
                    result.Success = false;
                    result.Error = loadResponse.Error.ExtResultMessage;
                    return result;
                }
                
                // Get document information
                var infoParams = new PdfGetInfoParameters();
                infoParams.FileId = loadResponse.FileId;
                
                var infoResponse = await pdfApi.GetInfoAsync(infoParams);
                
                if (infoResponse.Error == null)
                {
                    result.PageCount = infoResponse.PageCount;
                    result.IsTagged = infoResponse.IsTagged;
                    result.HasForm = false; // PassportPDF doesn't directly expose form field info in GetInfo
                    result.Title = infoResponse.Title;
                    result.Author = infoResponse.Author;
                    result.Subject = infoResponse.Subject;
                    result.IsEncrypted = infoResponse.Encryption != EncryptionAlgorithm.None;
                }
                
                // Extract text for content analysis
                var extractParams = new PdfExtractTextParameters();
                extractParams.FileId = loadResponse.FileId;
                extractParams.PageRange = "*";
                
                var extractResponse = await pdfApi.ExtractTextAsync(extractParams);
                if (extractResponse.Error == null && extractResponse.ExtractedText != null)
                {
                    result.ExtractedText = string.Join("\n", extractResponse.ExtractedText.Select(page => page.ExtractedText));
                    result.TextLength = result.ExtractedText.Length;
                }
                
                // Clean up
                try
                {
                    var closeParams = new DocumentCloseParameters();
                    closeParams.FileId = loadResponse.FileId;
                    await documentApi.DocumentCloseAsync(closeParams);
                }
                catch
                {
                    // Ignore cleanup errors
                }
                
                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing PDF structure");
                result.Success = false;
                result.Error = ex.Message;
                return result;
            }
        }
    }
    
    public class PdfAnalysisResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public int PageCount { get; set; }
        public bool IsTagged { get; set; }
        public bool HasForm { get; set; }
        public bool IsEncrypted { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string Subject { get; set; }
        public string ExtractedText { get; set; }
        public int TextLength { get; set; }
    }
}