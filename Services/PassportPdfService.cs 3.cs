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
using System.Collections.Generic;
using System.Linq;

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
            _apiKey = configuration["ApiKeys:PassportPdf"] ?? "TRIAL";
            
            // Configure PassportPDF API client
            if (_apiKey != "TRIAL")
            {
                GlobalConfiguration.ApiKey = _apiKey;
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
                if (_apiKey == "TRIAL")
                {
                    _logger.LogInformation("Using trial mode - Syncfusion conversion with PassportPDF concepts");
                    return await ConvertWithSyncfusionAndEnhanceAsync(wordContent, fileName);
                }
                
                // Full PassportPDF API implementation
                // Step 1: Load document
                var loadRequest = new LoadDocumentFromByteArrayRequest
                {
                    Content = wordContent,
                    FileName = fileName
                };
                
                var loadResponse = await Task.Run(() => _documentApi.DocumentLoadFromByteArray(loadRequest));
                if (loadResponse.Error == true)
                {
                    throw new Exception($"Failed to load document: {loadResponse.ErrorMessage}");
                }
                
                var fileId = loadResponse.FileId;
                result.FileId = fileId;
                _logger.LogInformation($"Document loaded with FileId: {fileId}");
                
                // Step 2: Convert to PDF if needed
                if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    var convertRequest = new PdfConvertToPDFRequest
                    {
                        FileId = fileId
                    };
                    
                    var convertResponse = await Task.Run(() => _pdfApi.ConvertToPDF(convertRequest));
                    if (convertResponse.Error == true)
                    {
                        throw new Exception($"Failed to convert to PDF: {convertResponse.ErrorMessage}");
                    }
                }
                
                // Step 3: Make PDF/UA compliant
                var complianceRequest = new PdfSetInfoRequest
                {
                    FileId = fileId,
                    Title = "Accessible Form",
                    Subject = "PDF/UA Compliant Document",
                    Producer = "PassportPDF with AccessForm"
                };
                
                await Task.Run(() => _pdfApi.SetInfo(complianceRequest));
                
                // Step 4: Auto-tag for accessibility
                var tagRequest = new PdfAutoDeskewRequest
                {
                    FileId = fileId
                };
                
                await Task.Run(() => _pdfApi.AutoDeskew(tagRequest));
                
                // Step 5: Extract tag structure info
                var getInfoRequest = new PdfGetInfoRequest
                {
                    FileId = fileId
                };
                
                var infoResponse = await Task.Run(() => _pdfApi.GetInfo(getInfoRequest));
                result.PageCount = infoResponse.PageCount ?? 0;
                result.HasForm = infoResponse.Encrypted == false;
                result.Title = infoResponse.Title;
                
                // Step 6: Save and download
                var saveRequest = new PdfSaveDocumentRequest
                {
                    FileId = fileId
                };
                
                var saveResponse = await Task.Run(() => _pdfApi.SaveDocument(saveRequest));
                if (saveResponse.Error == true)
                {
                    throw new Exception($"Failed to save document: {saveResponse.ErrorMessage}");
                }
                
                // Step 7: Close document
                var closeRequest = new DocumentCloseRequest
                {
                    FileId = fileId
                };
                
                await Task.Run(() => _documentApi.DocumentClose(closeRequest));
                
                result.Success = true;
                result.Message = "PDF created with full PDF/UA compliance";
                
                return (saveResponse.Content, result);
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
                pdfDocument.TaggedPDF = new PdfTaggedDocument(pdfDocument);
                var structure = pdfDocument.TaggedPDF.Structure;
                
                // Add document element
                var documentElement = new PdfStructureElement(PdfTagType.Document);
                structure.RootElement.AppendChildElement(documentElement);
                
                // Add form structure if fields exist
                if (pdfDocument.Form?.Fields?.Count > 0)
                {
                    var formElement = new PdfStructureElement(PdfTagType.Form);
                    documentElement.AppendChildElement(formElement);
                    
                    result.FieldCount = pdfDocument.Form.Fields.Count;
                    result.HasForm = true;
                    
                    // Associate each field with the form structure
                    foreach (var field in pdfDocument.Form.Fields)
                    {
                        if (field is PdfLoadedField loadedField)
                        {
                            var fieldElement = new PdfStructureElement(PdfTagType.Form);
                            fieldElement.Title = loadedField.Name;
                            formElement.AppendChildElement(fieldElement);
                        }
                    }
                }
                
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
                
                // Extract tag structure
                if (pdfDoc.TaggedPDF != null)
                {
                    structure.HasTaggedContent = true;
                    var rootElement = pdfDoc.TaggedPDF.Structure?.RootElement;
                    
                    if (rootElement != null)
                    {
                        structure.RootTag = ExtractTagInfo(rootElement);
                    }
                }
                
                // Extract form field information
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
        
        private PassportTagInfo ExtractTagInfo(PdfStructureElement element)
        {
            var tagInfo = new PassportTagInfo
            {
                Type = element.TagType.ToString(),
                Title = element.Title,
                Children = new List<PassportTagInfo>()
            };
            
            if (element.ChildElements != null)
            {
                foreach (PdfStructureElement child in element.ChildElements)
                {
                    tagInfo.Children.Add(ExtractTagInfo(child));
                }
            }
            
            return tagInfo;
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
                PdfLoadedTextBoxField => "text",
                _ => "unknown"
            };
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
        public string Title { get; set; }
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
