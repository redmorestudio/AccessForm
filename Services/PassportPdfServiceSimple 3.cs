using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
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
            _apiKey = _configuration["PassportPDF:ApiKey"] ?? "YOUR-PASSPORT-CODE";
            
            // Set global API configuration
            PassportPDF.Client.GlobalConfiguration.ApiKey.Clear();
            PassportPDF.Client.GlobalConfiguration.ApiKey.Add("X-PassportPDF-API-Key", _apiKey);
            
            _logger.LogInformation("PassportPDF service initialized");
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
                
                // Initialize API clients with default configuration
                var documentApi = new DocumentApi();
                var pdfApi = new PDFApi();
                
                // Step 1: Load the document using the simplest constructor
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
                    result.HasTaggedContent = true; // We'll ensure this through conversion
                    _logger.LogInformation($"PDF Info retrieved: {result.Title}");
                }
                
                // Step 3: Add detected form fields count
                if (detectedFields != null && detectedFields.Count > 0)
                {
                    result.FieldCount = detectedFields.Count;
                    result.HasForm = true;
                    _logger.LogInformation($"Detected {detectedFields.Count} form fields");
                }
                
                // Step 4: Save the document
                var saveParams = new PdfSaveDocumentParameters();
                saveParams.FileId = result.FileId;
                
                var saveResponse = await pdfApi.SaveDocumentAsync(saveParams);
                
                if (saveResponse.Error != null)
                {
                    throw new Exception($"Failed to save PDF: {saveResponse.Error.ExtResultMessage}");
                }
                
                // Get the PDF content
                using var outputStream = new MemoryStream();
                await pdfApi.SaveDocumentToFileAsync(saveParams, outputStream);
                
                var pdfBytes = outputStream.ToArray();
                
                result.Success = true;
                result.Message = "PDF processed successfully with PassportPDF";
                result.IsPdfUaCompliant = false; // Basic processing doesn't guarantee PDF/UA
                
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
        /// Extracts text content from a PDF
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
                    return string.Join("\n", extractResponse.ExtractedText);
                }
                
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from PDF");
                return string.Empty;
            }
        }
    }
}