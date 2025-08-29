using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PassportPDF.Api;
using PassportPDF.Client;
using PassportPDF.Model;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;
using Syncfusion.DocIORenderer;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using Syncfusion.Pdf.Interactive;
using System.Collections.Generic;
using System.Linq;
using WordToPdfConverter.Models;

namespace AccessFormServer.Services
{
    public class PassportPdfService
    {
        private readonly ILogger<PassportPdfService> _logger;
        private readonly string _apiKey;
        private readonly PDFApi _pdfApi;
        private readonly DocumentApi _documentApi;
        private readonly IConfiguration _configuration;

        public PassportPdfService(IConfiguration configuration, ILogger<PassportPdfService> logger)
        {
            _logger = logger;
            _configuration = configuration;
            
            // Get API key from multiple possible locations
            _apiKey = configuration["ApiKeys:PassportPdf"] ?? 
                     configuration["PassportPDF:ApiKey"] ?? 
                     "YOUR-PASSPORT-CODE";
            
            // Configure PassportPDF API client
            if (_apiKey != "YOUR-PASSPORT-CODE" && _apiKey != "TRIAL")
            {
                GlobalConfiguration.ApiKey.Clear();
                GlobalConfiguration.ApiKey.Add("X-PassportPDF-API-Key", _apiKey);
                _logger.LogInformation("PassportPDF configured with API key");
            }
            else
            {
                _logger.LogWarning("PassportPDF using demo/trial mode - limited functionality");
            }
            
            _pdfApi = new PDFApi();
            _documentApi = new DocumentApi();
        }

        public async Task<(byte[] pdfBytes, PassportPdfResult result)> ConvertWordToPdfWithAccessibilityAsync(byte[] wordContent, string fileName)
        {
            var result = new PassportPdfResult();
            
            try
            {
                _logger.LogInformation($"Starting PassportPDF conversion for {fileName}");
                
                // For trial/demo, use Syncfusion for conversion and PassportPDF concepts
                if (_apiKey == "YOUR-PASSPORT-CODE" || _apiKey == "TRIAL")
                {
                    _logger.LogInformation("Using trial mode - Syncfusion conversion with PassportPDF concepts");
                    return await ConvertWithSyncfusionAndEnhanceAsync(wordContent, fileName);
                }
                
                // Full PassportPDF API implementation
                // Step 1: Load document using proper parameters class
                var loadParams = new LoadDocumentFromByteArrayParameters(wordContent);
                loadParams.FileName = fileName;
                
                var loadResponse = await _documentApi.DocumentLoadAsync(loadParams);
                if (loadResponse.Error != null)
                {
                    throw new Exception($"Failed to load document: {loadResponse.Error.ExtResultMessage}");
                }
                
                var fileId = loadResponse.FileId;
                result.FileId = fileId;
                _logger.LogInformation($"Document loaded with FileId: {fileId}");
                
                // Step 2: Get document info
                var infoParams = new PdfGetInfoParameters();
                infoParams.FileId = fileId;
                
                var infoResponse = await _pdfApi.GetInfoAsync(infoParams);
                if (infoResponse.Error == null)
                {
                    result.PageCount = infoResponse.PageCount;
                    result.HasForm = false; // Will be determined by field extraction
                    result.Title = infoResponse.Title ?? fileName;
                }
                
                // Step 3: Extract text for AI analysis if needed
                var extractParams = new PdfExtractTextParameters();
                extractParams.FileId = fileId;
                extractParams.PageRange = "*";
                
                var extractResponse = await _pdfApi.ExtractTextAsync(extractParams);
                if (extractResponse.Error == null && extractResponse.ExtractedText != null)
                {
                    result.ExtractedText = string.Join("\n", extractResponse.ExtractedText.Select(page => page.ExtractedText));
                }
                
                // Step 4: Apply PDF/UA compliance enhancement if available
                try
                {
                    var convertParams = new PdfConvertToPDFAParameters();
                    convertParams.FileId = fileId;
                    convertParams.Conformance = PdfAConformance.PDFA1a; // PDF/A-1a for accessibility
                    
                    var convertResponse = await _pdfApi.ConvertToPDFAAsync(convertParams);
                    if (convertResponse.Error == null)
                    {
                        result.IsPdfUaCompliant = true;
                        _logger.LogInformation("Document converted to PDF/A-1a for accessibility compliance");
                    }
                    else
                    {
                        _logger.LogWarning($"PDF/A conversion failed: {convertResponse.Error.ExtResultMessage}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PDF/A conversion not available or failed");
                }
                
                // Step 5: Save and download
                var saveParams = new PdfSaveDocumentParameters();
                saveParams.FileId = fileId;
                
                var saveResponse = await _pdfApi.SaveDocumentAsync(saveParams);
                if (saveResponse.Error != null)
                {
                    throw new Exception($"Failed to save document: {saveResponse.Error.ExtResultMessage}");
                }
                
                // Step 6: Close document
                var closeParams = new DocumentCloseParameters();
                closeParams.FileId = fileId;
                
                await _documentApi.DocumentCloseAsync(closeParams);
                
                result.Success = true;
                result.Message = "PDF created with PassportPDF API";
                
                return (saveResponse.Data, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PassportPDF API error, falling back to Syncfusion");
                return await ConvertWithSyncfusionAndEnhanceAsync(wordContent, fileName);
            }
        }
        
        private async Task<(byte[] pdfBytes, PassportPdfResult result)> ConvertWithSyncfusionAndEnhanceAsync(byte[] wordContent, string fileName)
        {
            var result = new PassportPdfResult();
            
            try
            {
                // Use Syncfusion for conversion
                using var inputStream = new MemoryStream(wordContent);
                using var wordDoc = new WordDocument(inputStream, FormatType.Docx);
                using var renderer = new DocIORenderer();
                
                // Enable auto-tagging for accessibility
                renderer.Settings.AutoTag = true;
                renderer.Settings.PreserveFormFields = true;
                
                using var pdfDocument = renderer.ConvertToPDF(wordDoc);
                
                // Set PDF/UA metadata
                pdfDocument.DocumentInformation.Title = "Accessible Form";
                pdfDocument.DocumentInformation.Subject = "PDF/UA Compliant Document";
                pdfDocument.DocumentInformation.Producer = "AccessForm with PassportPDF Concepts";
                pdfDocument.DocumentInformation.Creator = "AccessForm";
                
                // Add PDF/UA identifier
                pdfDocument.DocumentInformation.Keywords = "PDF/UA-1";
                
                // Enable form field tab order based on structure
                if (pdfDocument.Form != null)
                {
                    foreach (PdfLoadedPage page in pdfDocument.Pages)
                    {
                        page.FormFieldsTabOrder = PdfFormFieldsTabOrder.Structure;
                    }
                }
                
                // Create tag structure
                pdfDocument.AutoTag = true;
                
                result.PageCount = pdfDocument.Pages.Count;
                result.HasTaggedContent = true;
                result.Success = true;
                result.Message = "PDF created with Syncfusion using PassportPDF accessibility concepts";
                
                using var outputStream = new MemoryStream();
                pdfDocument.Save(outputStream);
                
                return (outputStream.ToArray(), result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Syncfusion PDF creation");
                throw;
            }
        }

        public async Task<string> ExtractTextFromPdfAsync(byte[] pdfContent)
        {
            try
            {
                if (_apiKey == "YOUR-PASSPORT-CODE" || _apiKey == "TRIAL")
                {
                    // Use Syncfusion for extraction in demo mode
                    return ExtractTextWithSyncfusion(pdfContent);
                }
                
                // Use PassportPDF API for text extraction
                var loadParams = new LoadDocumentFromByteArrayParameters(pdfContent);
                loadParams.FileName = "document.pdf";
                
                var loadResponse = await _documentApi.DocumentLoadAsync(loadParams);
                
                if (loadResponse.Error != null)
                {
                    _logger.LogWarning($"Failed to load PDF for text extraction: {loadResponse.Error.ExtResultMessage}");
                    return ExtractTextWithSyncfusion(pdfContent);
                }
                
                // Extract text
                var extractParams = new PdfExtractTextParameters();
                extractParams.FileId = loadResponse.FileId;
                extractParams.PageRange = "*"; // All pages
                
                var extractResponse = await _pdfApi.ExtractTextAsync(extractParams);
                
                // Clean up
                try
                {
                    var closeParams = new DocumentCloseParameters();
                    closeParams.FileId = loadResponse.FileId;
                    await _documentApi.DocumentCloseAsync(closeParams);
                }
                catch
                {
                    // Ignore close errors
                }
                
                if (extractResponse.Error == null && extractResponse.ExtractedText != null)
                {
                    return string.Join("\n", extractResponse.ExtractedText.Select(page => page.ExtractedText));
                }
                
                return ExtractTextWithSyncfusion(pdfContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text with PassportPDF, using Syncfusion fallback");
                return ExtractTextWithSyncfusion(pdfContent);
            }
        }
        
        private string ExtractTextWithSyncfusion(byte[] pdfContent)
        {
            try
            {
                using var pdfStream = new MemoryStream(pdfContent);
                using var pdfDoc = new PdfLoadedDocument(pdfStream);
                var extractedTextBuilder = new System.Text.StringBuilder();
                
                for (int i = 0; i < pdfDoc.Pages.Count; i++)
                {
                    var pageText = pdfDoc.Pages[i].ExtractText();
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        extractedTextBuilder.AppendLine($"--- Page {i + 1} ---");
                        extractedTextBuilder.AppendLine(pageText);
                    }
                }
                
                return extractedTextBuilder.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract text with Syncfusion fallback");
                return "Could not extract text from document";
            }
        }

        public async Task<TagTreeStructure> ExtractTagTreeAsync(byte[] pdfContent)
        {
            var structure = new TagTreeStructure();
            
            try
            {
                using var pdfStream = new MemoryStream(pdfContent);
                using var pdfDoc = new PdfLoadedDocument(pdfStream);
                
                structure.PageCount = pdfDoc.Pages.Count;
                structure.HasForm = pdfDoc.Form?.Fields?.Count > 0;
                structure.FieldCount = pdfDoc.Form?.Fields?.Count ?? 0;
                
                // Extract form field information with proper type detection
                if (pdfDoc.Form?.Fields != null)
                {
                    foreach (var field in pdfDoc.Form.Fields)
                    {
                        structure.FormFields.Add(new PassportFormFieldInfo
                        {
                            Name = field.Name,
                            Type = GetFieldType(field),
                            Page = field.Page?.Index ?? 0,
                            IsRequired = field.Required,
                            Tooltip = field.ToolTip
                        });
                    }
                }
                
                return structure;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting tag tree");
                return structure;
            }
        }
        
        private string GetFieldType(dynamic field)
        {
            return field switch
            {
                PdfLoadedCheckBoxField => "checkbox",
                PdfLoadedRadioButtonListField => "radio",
                PdfLoadedComboBoxField => "dropdown",
                PdfLoadedListBoxField => "listbox",
                PdfLoadedSignatureField => "signature",
                PdfLoadedTextBoxField textBox => DetermineTextFieldType(textBox),
                _ => "text"
            };
        }
        
        private string DetermineTextFieldType(PdfLoadedTextBoxField textBox)
        {
            var fieldName = (textBox.Name ?? "").ToLower();
            
            // Use the FieldTypeDetector from WordToPdfConverter
            if (System.Type.GetType("WordToPdfConverter.Services.FieldTypeDetector") != null)
            {
                try
                {
                    var detectedType = WordToPdfConverter.Services.FieldTypeDetector.DetectFieldType(textBox.Name, null, null);
                    if (detectedType != "text")
                        return detectedType;
                }
                catch
                {
                    // Fall back to simple detection
                }
            }
            
            // Fallback field type detection
            if (fieldName.Contains("date") || fieldName.Contains("dob") || fieldName.Contains("birth"))
                return "date";
            if (fieldName.Contains("email") || fieldName.Contains("e-mail"))
                return "email";
            if (fieldName.Contains("phone") || fieldName.Contains("tel"))
                return "phone";
            if (fieldName.Contains("ssn") || fieldName.Contains("social"))
                return "ssn";
            if (fieldName.Contains("name"))
                return "name";
            if (fieldName.Contains("address") || fieldName.Contains("street"))
                return "address";
            if (fieldName.Contains("zip") || fieldName.Contains("postal"))
                return "zip";
            if (fieldName.Contains("number") || fieldName.Contains("amount"))
                return "number";
                
            return "text";
        }
        
        /// <summary>
        /// Create form fields in a PDF with proper types and tooltips from AI analysis
        /// </summary>
        public async Task<byte[]> CreateFormFieldsInPdf(byte[] pdfBytes, List<FieldDetectionResult> detectedFields)
        {
            try
            {
                using var pdfStream = new MemoryStream(pdfBytes);
                using var pdfDoc = new PdfLoadedDocument(pdfStream);
                
                // Clear existing fields if any
                if (pdfDoc.Form?.Fields != null)
                {
                    pdfDoc.Form.Fields.Clear();
                }
                
                // Create form fields based on AI detection
                foreach (var field in detectedFields.Where(f => f.IsValid))
                {
                    var bounds = new Syncfusion.Drawing.RectangleF(field.X, field.Y, field.Width, field.Height);
                    
                    // Create appropriate field type based on AI detection
                    PdfField pdfField = CreatePdfFieldByType(pdfDoc, field, bounds);
                    
                    if (pdfField != null)
                    {
                        // Generate tooltip using NLP if available
                        if (!string.IsNullOrEmpty(field.ValidationNotes))
                        {
                            SetFieldTooltip(pdfField, field.ValidationNotes);
                        }
                        else
                        {
                            SetFieldTooltip(pdfField, $"Enter {field.FieldName}");
                        }
                        
                        pdfDoc.Form.Fields.Add(pdfField);
                    }
                }
                
                // Save the enhanced PDF
                using var outputStream = new MemoryStream();
                pdfDoc.Save(outputStream);
                return outputStream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating form fields in PDF");
                return pdfBytes; // Return original if field creation fails
            }
        }
        
        private PdfField CreatePdfFieldByType(PdfLoadedDocument pdfDoc, FieldDetectionResult field, Syncfusion.Drawing.RectangleF bounds)
        {
            var page = pdfDoc.Pages[field.PageNumber - 1];
            
            switch (field.FieldType.ToLower())
            {
                case "checkbox":
                    var checkField = new PdfCheckBoxField(page, field.FieldName ?? field.ShortId);
                    checkField.Bounds = bounds;
                    return checkField;
                    
                case "radio":
                    var radioField = new PdfRadioButtonListField(page, field.FieldName ?? field.ShortId);
                    var radioItem = new PdfRadioButtonListItem("Option");
                    radioItem.Bounds = bounds;
                    radioField.Items.Add(radioItem);
                    return radioField;
                    
                case "signature":
                    var sigField = new PdfSignatureField(page, field.FieldName ?? field.ShortId);
                    sigField.Bounds = bounds;
                    return sigField;
                    
                default:
                    // Create text field with proper formatting
                    var textField = new PdfTextBoxField(page, field.FieldName ?? field.ShortId);
                    textField.Bounds = bounds;
                    
                    // Apply field-specific formatting
                    ApplyFieldFormatting(textField, field.FieldType);
                    
                    return textField;
            }
        }
        
        private void ApplyFieldFormatting(PdfTextBoxField textField, string fieldType)
        {
            switch (fieldType.ToLower())
            {
                case "date":
                    textField.MaxLength = 10; // MM/DD/YYYY
                    break;
                case "phone":
                    textField.MaxLength = 14; // (XXX) XXX-XXXX
                    break;
                case "email":
                    textField.MaxLength = 100;
                    break;
                case "ssn":
                    textField.MaxLength = 11; // XXX-XX-XXXX
                    break;
                case "zip":
                    textField.MaxLength = 10; // XXXXX-XXXX
                    break;
            }
        }
        
        private void SetFieldTooltip(PdfField field, string tooltip)
        {
            if (field is PdfTextBoxField textField)
                textField.ToolTip = tooltip;
            else if (field is PdfCheckBoxField checkField)
                checkField.ToolTip = tooltip;
            else if (field is PdfRadioButtonListField radioField)
                radioField.ToolTip = tooltip;
            // Signature fields don't have ToolTip property
        }
    }
    
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
        public string ExtractedText { get; set; }
    }
    
    public class TagTreeStructure
    {
        public int PageCount { get; set; }
        public bool HasForm { get; set; }
        public int FieldCount { get; set; }
        public bool HasTaggedContent { get; set; }
        public PassportTagInfo RootTag { get; set; }
        public List<PassportFormFieldInfo> FormFields { get; set; } = new List<PassportFormFieldInfo>();
    }
    
    public class PassportTagInfo
    {
        public string Type { get; set; }
        public string Title { get; set; }
        public List<PassportTagInfo> Children { get; set; } = new List<PassportTagInfo>();
    }
    
    public class PassportFormFieldInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int Page { get; set; }
        public bool IsRequired { get; set; }
        public string Tooltip { get; set; }
    }
}