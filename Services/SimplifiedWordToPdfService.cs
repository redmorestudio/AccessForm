using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;
using Syncfusion.DocIORenderer;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using Syncfusion.Pdf.Interactive;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Simplified service that uses Syncfusion's built-in form field preservation
    /// This focuses on leveraging Syncfusion's capabilities without adding complexity
    /// </summary>
    public class SimplifiedWordToPdfService
    {
        private readonly ILogger<SimplifiedWordToPdfService> _logger;

        public SimplifiedWordToPdfService(ILogger<SimplifiedWordToPdfService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Converts Word to PDF using Syncfusion's built-in form preservation
        /// </summary>
        public async Task<byte[]> ConvertWithFormPreservation(byte[] wordBytes, string fileName)
        {
            _logger.LogInformation($"Starting simplified Word to PDF conversion for {fileName}");

            try
            {
                using (var inputStream = new MemoryStream(wordBytes))
                using (var wordDoc = new WordDocument(inputStream, FormatType.Docx))
                {
                    // Log what we found in the Word document
                    LogWordDocumentInfo(wordDoc);

                    using (var renderer = new DocIORenderer())
                    {
                        // Configure renderer with Syncfusion's recommended settings
                        renderer.Settings.PreserveFormFields = true;  // Preserve Word form fields as PDF form fields
                        renderer.Settings.AutoTag = true;             // Enable automatic tagging for accessibility
                        renderer.Settings.EmbedFonts = true;          // Embed fonts for consistency
                        renderer.Settings.EmbedCompleteFonts = false; // Subset fonts to reduce size
                        renderer.Settings.EnableAlternateChunks = true; // Support embedded content
                        renderer.Settings.OptimizeIdenticalImages = true; // Reduce file size

                        // Convert to PDF
                        using (var pdfDocument = renderer.ConvertToPDF(wordDoc))
                        {
                            // Set basic document info for accessibility
                            SetDocumentMetadata(pdfDocument, fileName);

                            // Log what was created
                            LogPdfDocumentInfo(pdfDocument);

                            // Save the PDF
                            using (var outputStream = new MemoryStream())
                            {
                                pdfDocument.Save(outputStream);
                                var pdfBytes = outputStream.ToArray();
                                
                                _logger.LogInformation($"Conversion complete. PDF size: {pdfBytes.Length:N0} bytes");
                                return pdfBytes;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to convert Word to PDF");
                throw;
            }
        }

        /// <summary>
        /// Converts Word to PDF and then reloads it to check/enhance form fields
        /// </summary>
        public async Task<ConversionResult> ConvertAndAnalyze(byte[] wordBytes, string fileName)
        {
            _logger.LogInformation($"Converting and analyzing {fileName}");

            // First, do the conversion
            var pdfBytes = await ConvertWithFormPreservation(wordBytes, fileName);

            // Now load the PDF to analyze what we got
            using (var pdfStream = new MemoryStream(pdfBytes))
            using (var loadedPdf = new PdfLoadedDocument(pdfStream))
            {
                var result = new ConversionResult
                {
                    PdfBytes = pdfBytes,
                    PageCount = loadedPdf.Pages.Count,
                    FormFieldCount = loadedPdf.Form?.Fields?.Count ?? 0,
                    HasForm = loadedPdf.Form != null
                };

                // Analyze form fields if present
                if (loadedPdf.Form?.Fields != null)
                {
                    foreach (PdfLoadedField field in loadedPdf.Form.Fields)
                    {
                        result.FieldDetails.Add(new FieldDetail
                        {
                            Name = field.Name ?? "Unnamed",
                            Type = GetFieldType(field),
                            ToolTip = field.ToolTip,
                            Required = field.Required,
                            ReadOnly = field.ReadOnly,
                            TabIndex = field.TabIndex
                        });
                    }
                }

                _logger.LogInformation($"Analysis complete: {result.PageCount} pages, {result.FormFieldCount} form fields");
                return result;
            }
        }

        private void LogWordDocumentInfo(WordDocument wordDoc)
        {
            try
            {
                _logger.LogInformation($"Word document has {wordDoc.Sections.Count} sections");
                
                // Check for form fields in Word
                int formFieldCount = 0;
                foreach (WSection section in wordDoc.Sections)
                {
                    foreach (WParagraph para in section.Paragraphs)
                    {
                        // Count any form-related items (this is simplified)
                        formFieldCount++; // Just count paragraphs for now
                    }
                }
                
                _logger.LogInformation($"Word document contains approximately {formFieldCount} paragraph items");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not analyze Word document: {ex.Message}");
            }
        }

        private void LogPdfDocumentInfo(PdfDocument pdfDoc)
        {
            try
            {
                _logger.LogInformation($"PDF has {pdfDoc.Pages.Count} pages");
                
                if (pdfDoc.Form != null)
                {
                    _logger.LogInformation($"PDF form created with {pdfDoc.Form.Fields.Count} fields");
                    
                    // Log first few fields for debugging
                    int count = 0;
                    foreach (PdfField field in pdfDoc.Form.Fields)
                    {
                        if (count++ < 5)
                        {
                            _logger.LogInformation($"  Field: {field.Name} (TabIndex: {field.TabIndex})");
                        }
                    }
                    
                    if (pdfDoc.Form.Fields.Count > 5)
                    {
                        _logger.LogInformation($"  ... and {pdfDoc.Form.Fields.Count - 5} more fields");
                    }
                }
                else
                {
                    _logger.LogInformation("No form was created in the PDF");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not analyze PDF document: {ex.Message}");
            }
        }

        private void SetDocumentMetadata(PdfDocument pdfDoc, string fileName)
        {
            try
            {
                var docInfo = pdfDoc.DocumentInformation;
                docInfo.Title = Path.GetFileNameWithoutExtension(fileName);
                docInfo.Subject = "Accessible Form Document";
                docInfo.Keywords = "form, accessible, Section 508, WCAG";
                docInfo.Language = "en-US";
                docInfo.Producer = "Syncfusion Essential PDF";
                
                // Set creation/modification dates
                docInfo.CreationDate = DateTime.Now;
                docInfo.ModificationDate = DateTime.Now;

                // Enable auto-tagging for accessibility
                pdfDoc.AutoTag = true;
                
                _logger.LogInformation("Document metadata set for accessibility");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not set all document metadata: {ex.Message}");
            }
        }

        private string GetFieldType(PdfLoadedField field)
        {
            return field switch
            {
                PdfLoadedTextBoxField => "TextBox",
                PdfLoadedCheckBoxField => "CheckBox",
                PdfLoadedRadioButtonListField => "RadioButton",
                PdfLoadedComboBoxField => "ComboBox",
                PdfLoadedListBoxField => "ListBox",
                PdfLoadedSignatureField => "Signature",
                _ => "Unknown"
            };
        }

        public class ConversionResult
        {
            public byte[] PdfBytes { get; set; }
            public int PageCount { get; set; }
            public int FormFieldCount { get; set; }
            public bool HasForm { get; set; }
            public List<FieldDetail> FieldDetails { get; set; } = new List<FieldDetail>();
        }

        public class FieldDetail
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public string ToolTip { get; set; }
            public bool Required { get; set; }
            public bool ReadOnly { get; set; }
            public int TabIndex { get; set; }
        }
    }
}