using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PassportPDF.Api;
using PassportPDF.Client;
using PassportPDF.Model;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Interactive;
using Syncfusion.Pdf.Parsing;
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
        /// Converts to PDF/A while preserving field names by extracting and re-adding fields
        /// </summary>
        public async Task<byte[]> ConvertToPdfAPreservingFieldsAsync(byte[] pdfBytes, string fileName)
        {
            try
            {
                _logger.LogInformation($"Starting PDF/A conversion with field preservation for {fileName}");
                
                // Step 1: Extract field metadata from original PDF
                var fieldMetadata = new List<FieldMetadata>();
                
                using (var stream = new MemoryStream(pdfBytes))
                using (var pdfDoc = new PdfLoadedDocument(stream))
                {
                    if (pdfDoc.Form != null)
                    {
                        foreach (PdfLoadedField field in pdfDoc.Form.Fields)
                        {
                            var metadata = new FieldMetadata
                            {
                                Name = field.Name,
                                ToolTip = field.ToolTip,
                                Required = field.Required
                            };
                            
                            // Get bounds and page based on field type
                            if (field is PdfLoadedTextBoxField textField)
                            {
                                metadata.FieldType = "text";
                                metadata.Bounds = textField.Bounds;

                                // PRIORITY 1: Extract page number from DefaultValue (Claude Vision's detection)
                                var defaultValuePage = AccessFormServer.Services.FieldTooltipGenerator.ExtractPageFromDefaultValue(textField.DefaultValue);
                                if (defaultValuePage > 0)
                                {
                                    metadata.PageIndex = defaultValuePage - 1; // Convert to 0-based
                                    _logger.LogInformation($"[DEFAULTVALUE-PAGE] Text field '{field.Name}' using page {defaultValuePage} from Claude Vision DefaultValue: '{textField.DefaultValue}'");
                                    // Clear the DefaultValue after extracting page info
                                    textField.DefaultValue = "";
                                }
                                else
                                {
                                    // FALLBACK 1: Extract page number from tooltip (legacy)
                                    var tooltipPage = AccessFormServer.Services.FieldTooltipGenerator.ExtractPageFromTooltip(textField.ToolTip);
                                    if (tooltipPage > 0)
                                    {
                                        metadata.PageIndex = tooltipPage - 1; // Convert to 0-based
                                        _logger.LogInformation($"[TOOLTIP-PAGE] Text field '{field.Name}' using page {tooltipPage} from legacy tooltip: '{textField.ToolTip}'");
                                    }
                                    else
                                    {
                                        // FALLBACK 2: Use coordinate detection
                                        metadata.PageIndex = WordToPdfConverter.Services.PdfCoordinateConverter.FindPageContainingField(textField, pdfDoc, _logger) - 1; // Convert to 0-based
                                        _logger.LogInformation($"[COORDINATE-FALLBACK] Text field '{field.Name}' using page {metadata.PageIndex + 1} from coordinate detection");
                                    }
                                }
                            }
                            else if (field is PdfLoadedCheckBoxField checkField)
                            {
                                metadata.FieldType = "checkbox";
                                metadata.Bounds = checkField.Bounds;

                                // PRIORITY 1: Extract page number from ToolTip (Claude Vision stores page info here for checkboxes)
                                var tooltipPage = AccessFormServer.Services.FieldTooltipGenerator.ExtractPageFromTooltip(checkField.ToolTip);
                                if (tooltipPage > 0)
                                {
                                    metadata.PageIndex = tooltipPage - 1; // Convert to 0-based
                                    _logger.LogInformation($"[TOOLTIP-PAGE] Checkbox field '{field.Name}' using page {tooltipPage} from Claude Vision tooltip");
                                }
                                else
                                {
                                    // FALLBACK: Use coordinate detection
                                    metadata.PageIndex = WordToPdfConverter.Services.PdfCoordinateConverter.FindPageContainingField(checkField, pdfDoc, _logger) - 1; // Convert to 0-based
                                    _logger.LogInformation($"[COORDINATE-FALLBACK] Checkbox field '{field.Name}' using page {metadata.PageIndex + 1} from coordinate detection");
                                }
                            }
                            else if (field is PdfLoadedRadioButtonListField radioField)
                            {
                                metadata.FieldType = "radio";
                                if (radioField.Items.Count > 0)
                                {
                                    metadata.Bounds = radioField.Items[0].Bounds;

                                    // PRIORITY 1: Extract page number from tooltip (Claude Vision's detection)
                                    var tooltipPage = AccessFormServer.Services.FieldTooltipGenerator.ExtractPageFromTooltip(radioField.ToolTip);
                                    if (tooltipPage > 0)
                                    {
                                        metadata.PageIndex = tooltipPage - 1; // Convert to 0-based
                                        _logger.LogInformation($"[TOOLTIP-PAGE] Radio field '{field.Name}' using page {tooltipPage} from Claude Vision tooltip: '{radioField.ToolTip}'");
                                    }
                                    else
                                    {
                                        // FALLBACK: Use coordinate detection
                                        metadata.PageIndex = WordToPdfConverter.Services.PdfCoordinateConverter.FindPageContainingField(radioField, pdfDoc, _logger) - 1; // Convert to 0-based
                                        _logger.LogInformation($"[COORDINATE-FALLBACK] Radio field '{field.Name}' using page {metadata.PageIndex + 1} from coordinate detection");
                                    }
                                }
                            }
                            else if (field is PdfLoadedSignatureField sigField)
                            {
                                metadata.FieldType = "signature";
                                metadata.Bounds = sigField.Bounds;

                                // PRIORITY 1: Extract page number from tooltip (Claude Vision's detection)
                                var tooltipPage = AccessFormServer.Services.FieldTooltipGenerator.ExtractPageFromTooltip(sigField.ToolTip);
                                if (tooltipPage > 0)
                                {
                                    metadata.PageIndex = tooltipPage - 1; // Convert to 0-based
                                    _logger.LogInformation($"[TOOLTIP-PAGE] Signature field '{field.Name}' using page {tooltipPage} from Claude Vision tooltip: '{sigField.ToolTip}'");
                                }
                                else
                                {
                                    // FALLBACK: Use coordinate detection
                                    metadata.PageIndex = WordToPdfConverter.Services.PdfCoordinateConverter.FindPageContainingField(sigField, pdfDoc, _logger) - 1; // Convert to 0-based
                                    _logger.LogInformation($"[COORDINATE-FALLBACK] Signature field '{field.Name}' using page {metadata.PageIndex + 1} from coordinate detection");
                                }
                            }
                            
                            fieldMetadata.Add(metadata);
                            _logger.LogInformation($"[FIELD EXTRACT] Field: {metadata.Name} ({metadata.FieldType}) on page {metadata.PageIndex}");
                        }
                        
                        _logger.LogInformation($"Extracted {fieldMetadata.Count} fields from original PDF");
                    }
                }
                
                // Step 2: Remove all fields from PDF before conversion
                byte[] fieldlessPdf;
                using (var stream = new MemoryStream(pdfBytes))
                using (var pdfDoc = new PdfLoadedDocument(stream))
                {
                    // Remove all form fields
                    if (pdfDoc.Form != null)
                    {
                        pdfDoc.Form.Fields.Clear();
                        _logger.LogInformation("Removed all fields from PDF");
                    }
                    
                    using (var outputStream = new MemoryStream())
                    {
                        pdfDoc.Save(outputStream);
                        fieldlessPdf = outputStream.ToArray();
                    }
                }
                
                // Step 3: Convert fieldless PDF to PDF/A
                var pdfABytes = await ConvertToPdfAAsync(fieldlessPdf, fileName);
                _logger.LogInformation("Converted to PDF/A-2u");
                
                // Step 4: Re-add fields with preserved names to PDF/A document
                if (fieldMetadata.Count > 0)
                {
                    using (var stream = new MemoryStream(pdfABytes))
                    using (var pdfDoc = new PdfLoadedDocument(stream))
                    {
                        // Ensure form exists
                        if (pdfDoc.Form == null)
                        {
                            pdfDoc.CreateForm();
                        }
                        
                        // Re-add each field with preserved metadata
                        foreach (var metadata in fieldMetadata)
                        {
                            if (metadata.PageIndex < 0 || metadata.PageIndex >= pdfDoc.Pages.Count)
                            {
                                _logger.LogWarning($"[FIELD RE-ADD] Field {metadata.Name} has invalid page index {metadata.PageIndex} (total pages: {pdfDoc.Pages.Count})");
                                continue;
                            }

                            var page = pdfDoc.Pages[metadata.PageIndex];
                            _logger.LogInformation($"[FIELD RE-ADD] Adding field: {metadata.Name} ({metadata.FieldType}) to page {metadata.PageIndex}");

                            switch (metadata.FieldType)
                            {
                                case "checkbox":
                                    var checkbox = new PdfCheckBoxField(page, metadata.Name);
                                    checkbox.Bounds = metadata.Bounds;
                                    checkbox.ToolTip = metadata.ToolTip;
                                    checkbox.Required = metadata.Required;
                                    pdfDoc.Form.Fields.Add(checkbox);
                                    break;
                                    
                                case "radio":
                                    var radio = new PdfRadioButtonListField(page, metadata.Name);
                                    radio.ToolTip = metadata.ToolTip;
                                    radio.Required = metadata.Required;
                                    var radioItem = new PdfRadioButtonListItem("Option");
                                    radioItem.Bounds = metadata.Bounds;
                                    radio.Items.Add(radioItem);
                                    pdfDoc.Form.Fields.Add(radio);
                                    break;
                                    
                                case "signature":
                                    var signature = new PdfSignatureField(page, metadata.Name);
                                    signature.Bounds = metadata.Bounds;
                                    pdfDoc.Form.Fields.Add(signature);
                                    break;
                                    
                                default: // text
                                    var textField = new PdfTextBoxField(page, metadata.Name);
                                    textField.Bounds = metadata.Bounds;
                                    textField.ToolTip = metadata.ToolTip;
                                    textField.Required = metadata.Required;
                                    pdfDoc.Form.Fields.Add(textField);
                                    break;
                            }
                        }
                        
                        _logger.LogInformation($"Re-added {fieldMetadata.Count} fields to PDF/A document");
                        
                        using (var outputStream = new MemoryStream())
                        {
                            pdfDoc.Save(outputStream);
                            return outputStream.ToArray();
                        }
                    }
                }
                
                return pdfABytes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PDF/A conversion with field preservation");
                // Fallback: return original
                return pdfBytes;
            }
        }
        
        
        private class FieldMetadata
        {
            public string Name { get; set; } = "";
            public string FieldType { get; set; } = "";
            public RectangleF Bounds { get; set; }
            public int PageIndex { get; set; }
            public string ToolTip { get; set; } = "";
            public bool Required { get; set; }
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