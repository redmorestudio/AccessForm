using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PassportPDF.Api;
using PassportPDF.Client;
using PassportPDF.Model;
using WordToPdfConverter.Models;

namespace AccessFormServer.Services
{
    /// <summary>
    /// Service for creating accessible PDFs using PassportPDF API
    /// Focuses on PDF/A-2u conversion for accessibility compliance
    /// </summary>
    public class PassportPdfService
    {
        private readonly ILogger<PassportPdfService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _apiKey;
        
        public PassportPdfService(IConfiguration configuration, ILogger<PassportPdfService> logger)
        {
            _logger = logger;
            _configuration = configuration;
            
            // Get API key from configuration or use demo key
            _apiKey = _configuration["ApiKeys:PassportPdf"] ?? "YOUR-PASSPORT-CODE";
            
            // Set the API key globally for all PassportPDF operations
            GlobalConfiguration.ApiKey = _apiKey;
            
            _logger.LogInformation($"PassportPDF service initialized with API key: {_apiKey.Substring(0, 5)}...");
        }
        
        /// <summary>
        /// Converts a PDF to PDF/A-2u for accessibility compliance
        /// </summary>
        public async Task<byte[]> ConvertToPdfAAsync(byte[] pdfBytes, string fileName)
        {
            try
            {
                _logger.LogInformation($"Starting PDF/A conversion for {fileName}");
                
                // Initialize the APIs
                var documentApi = new DocumentApi();
                var pdfApi = new PDFApi();
                
                // Step 1: Load the PDF document
                _logger.LogInformation("Loading document into PassportPDF...");
                
                using var inputStream = new MemoryStream(pdfBytes);
                var loadParams = new LoadDocumentFromByteArrayParameters(pdfBytes);
                loadParams.FileName = fileName;
                
                var loadResponse = await documentApi.DocumentLoadAsync(loadParams);
                
                if (loadResponse.Error != null)
                {
                    _logger.LogError($"Failed to load document: {loadResponse.Error.ExtResultMessage}");
                    throw new Exception($"Failed to load document: {loadResponse.Error.ExtResultMessage}");
                }
                
                _logger.LogInformation($"Document loaded with FileId: {loadResponse.FileId}");
                
                // Step 2: Convert to PDF/A-2u (Unicode support for accessibility)
                _logger.LogInformation("Converting to PDF/A-2u for accessibility compliance...");
                
                var convertParams = new PdfConvertToPDFAParameters(loadResponse.FileId);
                convertParams.Conformance = PdfAConformance.PDFA2u;
                
                var convertResponse = await pdfApi.ConvertToPDFAAsync(convertParams);
                
                if (convertResponse.Error != null)
                {
                    _logger.LogWarning($"PDF/A conversion warning: {convertResponse.Error.ExtResultMessage}");
                    // Continue even with warnings - some PDFs may have minor issues
                }
                else
                {
                    _logger.LogInformation("Successfully converted to PDF/A-2u");
                }
                
                // Step 3: Save the converted PDF
                _logger.LogInformation("Downloading converted PDF...");
                
                var saveParams = new PdfSaveDocumentParameters(loadResponse.FileId);
                
                var saveResponse = await pdfApi.SaveDocumentAsync(saveParams);
                
                if (saveResponse.Error != null)
                {
                    _logger.LogError($"Failed to save PDF: {saveResponse.Error.ExtResultMessage}");
                    throw new Exception($"Failed to save PDF: {saveResponse.Error.ExtResultMessage}");
                }
                
                // Step 4: Clean up - close the document on PassportPDF servers
                try
                {
                    var closeParams = new DocumentCloseParameters(loadResponse.FileId);
                    await documentApi.DocumentCloseAsync(closeParams);
                    _logger.LogInformation("Document closed on PassportPDF servers");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to close document: {ex.Message}");
                    // Not critical if close fails
                }
                
                _logger.LogInformation($"PDF/A conversion complete. Output size: {saveResponse.Data?.Length ?? 0} bytes");
                
                return saveResponse.Data ?? pdfBytes; // Return original if conversion somehow fails
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PassportPDF conversion error");
                throw;
            }
        }
        
        /// <summary>
        /// Validates if a PDF is PDF/A compliant
        /// </summary>
        public async Task<PdfAValidationResult> ValidatePdfAAsync(byte[] pdfBytes)
        {
            try
            {
                var documentApi = new DocumentApi();
                var pdfApi = new PDFApi();
                
                // Load the document
                var loadParams = new LoadDocumentFromByteArrayParameters(pdfBytes);
                loadParams.FileName = "validation.pdf";
                
                var loadResponse = await documentApi.DocumentLoadAsync(loadParams);
                
                if (loadResponse.Error != null)
                {
                    return new PdfAValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = loadResponse.Error.ExtResultMessage
                    };
                }
                
                // Validate PDF/A compliance
                var validateParams = new PdfValidatePDFAParameters(loadResponse.FileId);
                validateParams.Conformance = PdfAValidationConformance.Autodetect;
                
                var validateResponse = await pdfApi.ValidatePDFAAsync(validateParams);
                
                // Clean up
                try
                {
                    var closeParams = new DocumentCloseParameters(loadResponse.FileId);
                    await documentApi.DocumentCloseAsync(closeParams);
                }
                catch { }
                
                if (validateResponse.Error != null)
                {
                    return new PdfAValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = validateResponse.Error.ExtResultMessage
                    };
                }
                
                return new PdfAValidationResult
                {
                    IsValid = validateResponse.Error == null,
                    ConformanceLevel = validateResponse.Conformance.ToString() ?? "Unknown",
                    ValidationDetails = validateResponse.Error?.ExtResultMessage ?? ""
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PDF/A validation error");
                return new PdfAValidationResult
                {
                    IsValid = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        
        /// <summary>
        /// Extracts text from a PDF using OCR if needed
        /// </summary>
        public async Task<string> ExtractTextWithOcrAsync(byte[] pdfBytes)
        {
            try
            {
                var documentApi = new DocumentApi();
                var pdfApi = new PDFApi();
                
                // Load the document
                var loadParams = new LoadDocumentFromByteArrayParameters(pdfBytes);
                loadParams.FileName = "ocr.pdf";
                
                var loadResponse = await documentApi.DocumentLoadAsync(loadParams);
                
                if (loadResponse.Error != null)
                {
                    _logger.LogError($"Failed to load PDF for OCR: {loadResponse.Error.ExtResultMessage}");
                    return string.Empty;
                }
                
                // Perform OCR on all pages
                var ocrParams = new PdfOCRParameters(loadResponse.FileId, "*");
                ocrParams.Language = "eng";  // English
                
                var ocrResponse = await pdfApi.OCRAsync(ocrParams);
                
                // Extract text after OCR
                var extractParams = new PdfExtractTextParameters(loadResponse.FileId, "*");
                
                var extractResponse = await pdfApi.ExtractTextAsync(extractParams);
                
                // Clean up
                try
                {
                    var closeParams = new DocumentCloseParameters(loadResponse.FileId);
                    await documentApi.DocumentCloseAsync(closeParams);
                }
                catch { }
                
                if (extractResponse.Error == null && extractResponse.ExtractedText != null)
                {
                    return string.Join("\n", extractResponse.ExtractedText);
                }
                
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text with OCR");
                return string.Empty;
            }
        }
    }
    
    /// <summary>
    /// Result of PDF/A validation
    /// </summary>
    public class PdfAValidationResult
    {
        public bool IsValid { get; set; }
        public string ConformanceLevel { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public string ValidationDetails { get; set; } = "";
    }
}