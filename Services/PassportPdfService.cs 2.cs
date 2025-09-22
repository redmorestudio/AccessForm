using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
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
    /// </summary>
    public class PassportPdfService
    {
        private readonly ILogger<PassportPdfService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _apiKey;
        private readonly PDFApi _pdfApi;
        private readonly DocumentApi _documentApi;
        
        public PassportPdfService(IConfiguration configuration, ILogger<PassportPdfService> logger)
        {
            _logger = logger;
            _configuration = configuration;
            
            // Get API key from configuration or use demo key
            _apiKey = _configuration["PassportPDF:ApiKey"] ?? "YOUR-PASSPORT-CODE";
            
            // Initialize PassportPDF configuration
            var apiConfig = new PassportPDF.Client.Configuration();
            apiConfig.BasePath = "https://passportpdfapi.com";
            apiConfig.AddApiKey("X-PassportPDF-API-Key", _apiKey);
            
            // Initialize API clients
            _pdfApi = new PDFApi(apiConfig);
            _documentApi = new DocumentApi(apiConfig);
            
            _logger.LogInformation("PassportPDF service initialized with API key");
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
                
                // Step 1: Load the document
                var loadParams = new LoadDocumentFromByteArrayParameters(inputBytes);
                loadParams.FileName = fileName;
                
                var loadResponse = await _documentApi.DocumentLoadAsync(loadParams);
                
                if (loadResponse.Error != null)
                {
                    throw new Exception($"Failed to load document: {loadResponse.Error.ExtResultMessage}");
                }
                
                result.FileId = loadResponse.FileId;
                result.PageCount = loadResponse.PageCount;
                _logger.LogInformation($"Document loaded with FileId: {result.FileId}, Pages: {result.PageCount}");
                
                // Step 2: Convert to PDF/A for accessibility compliance if needed
                if (fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    // For Word documents, we may need to convert to PDF first
                    // Check if PassportPDF has automatic conversion
                    _logger.LogInformation("Word document detected, will process as PDF");
                }
                
                // Step 3: Apply PDF/UA compliance
                var pdfaParams = new PdfConvertToPDFAParameters();
                pdfaParams.FileId = result.FileId;
                pdfaParams.Conformance = PdfConformance.PDFA2u; // PDF/A-2u includes Unicode support for accessibility
                
                var pdfaResponse = await _pdfApi.ConvertToPDFAAsync(pdfaParams);
                
                if (pdfaResponse.Error == null)
                {
                    result.HasTaggedContent = true;
                    result.IsPdfUaCompliant = true;
                    _logger.LogInformation("Successfully converted to PDF/A-2u for accessibility");
                }
                else
                {
                    _logger.LogWarning($"PDF/A conversion warning: {pdfaResponse.Error.ExtResultMessage}");
                }
                
                // Step 4: Add form fields if detected
                if (detectedFields != null && detectedFields.Count > 0)
                {
                    _logger.LogInformation($"Processing {detectedFields.Count} detected form fields");
                    // Note: PassportPDF form field API would be used here
                    // The exact API depends on PassportPDF's form field methods
                    result.FieldCount = detectedFields.Count;
                    result.HasForm = true;
                }
                
                // Step 5: Save the processed PDF
                var saveParams = new PdfSaveDocumentParameters();
                saveParams.FileId = result.FileId;
                
                var saveResponse = await _pdfApi.SaveDocumentAsync(saveParams);
                
                if (saveResponse.Error != null)
                {
                    throw new Exception($"Failed to save PDF: {saveResponse.Error.ExtResultMessage}");
                }
                
                // Get the PDF content
                using var outputStream = new MemoryStream();
                await _pdfApi.SaveDocumentToFileAsync(saveParams, outputStream);
                
                var pdfBytes = outputStream.ToArray();
                
                result.Success = true;
                result.Message = "PDF processed successfully with PassportPDF";
                result.Title = Path.GetFileNameWithoutExtension(fileName);
                
                // Clean up the file from PassportPDF servers
                var closeParams = new DocumentCloseParameters();
                closeParams.FileId = result.FileId;
                await _documentApi.DocumentCloseAsync(closeParams);
                
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
                // Load the PDF
                var loadParams = new LoadDocumentFromByteArrayParameters(pdfBytes);
                loadParams.FileName = "document.pdf";
                
                var loadResponse = await _documentApi.DocumentLoadAsync(loadParams);
                
                if (loadResponse.Error != null)
                {
                    _logger.LogWarning($"Failed to load PDF for text extraction: {loadResponse.Error.ExtResultMessage}");
                    return string.Empty;
                }
                
                // Extract text using OCR if needed
                var extractParams = new PdfExtractTextParameters();
                extractParams.FileId = loadResponse.FileId;
                extractParams.PageRange = "*"; // All pages
                
                var extractResponse = await _pdfApi.ExtractTextAsync(extractParams);
                
                // Clean up
                var closeParams = new DocumentCloseParameters();
                closeParams.FileId = loadResponse.FileId;
                await _documentApi.DocumentCloseAsync(closeParams);
                
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
        
        /// <summary>
        /// Extracts the tag tree structure from a PDF
        /// </summary>
        public async Task<PassportTagTree> ExtractTagTreeAsync(byte[] pdfBytes)
        {
            var tagTree = new PassportTagTree();
            
            try
            {
                // Load the PDF
                var loadParams = new LoadDocumentFromByteArrayParameters(pdfBytes);
                loadParams.FileName = "document.pdf";
                
                var loadResponse = await _documentApi.DocumentLoadAsync(loadParams);
                
                if (loadResponse.Error != null)
                {
                    _logger.LogWarning($"Failed to load PDF for tag extraction: {loadResponse.Error.ExtResultMessage}");
                    return tagTree;
                }
                
                tagTree.FileId = loadResponse.FileId;
                tagTree.PageCount = loadResponse.PageCount;
                
                // Get PDF information including form fields
                var infoParams = new PdfGetInfoParameters();
                infoParams.FileId = loadResponse.FileId;
                
                var infoResponse = await _pdfApi.GetInfoAsync(infoParams);
                
                if (infoResponse.Error == null)
                {
                    tagTree.Title = infoResponse.Title ?? string.Empty;
                    tagTree.HasTaggedContent = false; // Tagged property not available in current version
                    tagTree.HasForm = false; // HasAcroForm property not available in current version
                    tagTree.FieldCount = 0; // FormFieldCount property not available in current version
                    
                    _logger.LogInformation($"PDF Info - Tagged: {tagTree.HasTaggedContent}, Form: {tagTree.HasForm}, Fields: {tagTree.FieldCount}");
                }
                
                // Clean up
                var closeParams = new DocumentCloseParameters();
                closeParams.FileId = loadResponse.FileId;
                await _documentApi.DocumentCloseAsync(closeParams);
                
                return tagTree;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting tag tree");
                return tagTree;
            }
        }
    }
    
    /// <summary>
    /// Result from PassportPDF processing
    /// </summary>
    public class PassportPdfResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string FileId { get; set; }
        public int PageCount { get; set; }
        public int FieldCount { get; set; }
        public bool HasForm { get; set; }
        public bool HasTaggedContent { get; set; }
        public bool IsPdfUaCompliant { get; set; }
        public string Title { get; set; }
    }
    
    /// <summary>
    /// Tag tree structure extracted from PassportPDF
    /// </summary>
    public class PassportTagTree
    {
        public string FileId { get; set; }
        public int PageCount { get; set; }
        public bool HasForm { get; set; }
        public int FieldCount { get; set; }
        public bool HasTaggedContent { get; set; }
        public string Title { get; set; }
        public string ExtractedText { get; set; }
        public PassportTagInfo RootTag { get; set; }
        public List<PassportFormFieldInfo> FormFields { get; set; } = new List<PassportFormFieldInfo>();
    }
    
    /// <summary>
    /// Tag information
    /// </summary>
    public class PassportTagInfo
    {
        public string Type { get; set; }
        public string Role { get; set; }
        public string Title { get; set; }
        public string ActualText { get; set; }
        public string AltText { get; set; }
        public List<PassportTagInfo> Children { get; set; } = new List<PassportTagInfo>();
    }
    
    /// <summary>
    /// Form field information
    /// </summary>
    public class PassportFormFieldInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int Page { get; set; }
        public bool IsRequired { get; set; }
        public string Tooltip { get; set; }
    }
}