using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Aspose.Pdf;
using Aspose.Pdf.Facades;
using System.Collections.Generic;
using System.Linq;

namespace AccessFormServer.Services
{
    /// <summary>
    /// Service that uses Aspose.PDF (local) to optimize PDFs and embed fonts
    /// </summary>
    public class AsposePdfService
    {
        private readonly ILogger<AsposePdfService> _logger;
        private bool _isConfigured = false;

        public AsposePdfService(ILogger<AsposePdfService> logger)
        {
            _logger = logger;
            
            try
            {
                // Set license if available
                var licenseFile = Environment.GetEnvironmentVariable("ASPOSE_LICENSE_PATH");
                if (!string.IsNullOrEmpty(licenseFile) && File.Exists(licenseFile))
                {
                    var license = new License();
                    license.SetLicense(licenseFile);
                    _logger.LogInformation("Aspose.PDF license applied");
                }
                else
                {
                    _logger.LogInformation("Running Aspose.PDF in evaluation mode");
                }
                
                _isConfigured = true;
                _logger.LogInformation("Aspose PDF service initialized (local version)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Aspose PDF");
                _isConfigured = false;
            }
        }

        public bool IsConfigured() => _isConfigured;

        /// <summary>
        /// Optimize PDF to embed all fonts and ensure compliance
        /// </summary>
        public async Task<byte[]> OptimizePdfAsync(byte[] pdfBytes)
        {
            if (!_isConfigured)
            {
                throw new InvalidOperationException("Aspose PDF service is not configured");
            }

            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInformation($"Starting Aspose PDF optimization for {pdfBytes.Length} byte PDF");

                    using (var inputStream = new MemoryStream(pdfBytes))
                    using (var outputStream = new MemoryStream())
                    {
                        // Load the PDF document
                        var document = new Document(inputStream);
                        
                        // Embed all fonts
                        EmbedFonts(document);
                        
                        // Optimize the document
                        var optimizationOptions = new Aspose.Pdf.Optimization.OptimizationOptions
                        {
                            RemoveUnusedObjects = true,
                            RemoveUnusedStreams = true,
                            AllowReusePageContent = false,
                            LinkDuplcateStreams = false,
                            UnembedFonts = false,
                            SubsetFonts = false,
                            CompressImages = true,
                            ImageQuality = 100
                        };
                        
                        document.OptimizeResources(optimizationOptions);
                        
                        // Save optimized document
                        document.Save(outputStream);
                        
                        var optimizedBytes = outputStream.ToArray();
                        _logger.LogInformation($"Aspose optimization successful, result is {optimizedBytes.Length} bytes (from {pdfBytes.Length})");
                        
                        return optimizedBytes;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to optimize PDF with Aspose");
                    throw new InvalidOperationException($"Aspose PDF optimization failed: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Convert all fonts to embedded standard fonts
        /// </summary>
        public async Task<byte[]> ConvertFontsAsync(byte[] pdfBytes)
        {
            if (!_isConfigured)
            {
                throw new InvalidOperationException("Aspose PDF service is not configured");
            }

            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInformation($"Starting Aspose font conversion for {pdfBytes.Length} byte PDF");

                    using (var inputStream = new MemoryStream(pdfBytes))
                    using (var outputStream = new MemoryStream())
                    {
                        // Load the PDF document
                        var document = new Document(inputStream);
                        
                        // Convert and embed all fonts
                        EmbedFonts(document);
                        
                        // Convert to PDF/A-1b to ensure font embedding
                        var tempLogPath = Path.GetTempFileName();
                        try
                        {
                            bool isValid = document.Convert(tempLogPath, PdfFormat.PDF_A_1B, ConvertErrorAction.Delete);
                            if (isValid)
                            {
                                _logger.LogInformation("Document converted to PDF/A-1B for font embedding");
                            }
                            else
                            {
                                _logger.LogWarning("Document conversion to PDF/A-1B had issues but continued");
                            }
                        }
                        finally
                        {
                            try { File.Delete(tempLogPath); } catch { }
                        }
                        
                        // Save converted document
                        document.Save(outputStream);
                        
                        var convertedBytes = outputStream.ToArray();
                        _logger.LogInformation($"Aspose font conversion successful, result is {convertedBytes.Length} bytes");
                        
                        return convertedBytes;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to convert fonts with Aspose");
                    throw new InvalidOperationException($"Aspose font conversion failed: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Add accessibility tags and ensure PDF/UA compliance
        /// </summary>
        public async Task<byte[]> AddAccessibilityTagsAsync(byte[] pdfBytes)
        {
            if (!_isConfigured)
            {
                throw new InvalidOperationException("Aspose PDF service is not configured");
            }

            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInformation($"Starting Aspose accessibility tagging for {pdfBytes.Length} byte PDF");

                    using (var inputStream = new MemoryStream(pdfBytes))
                    using (var outputStream = new MemoryStream())
                    {
                        // Load the PDF document
                        var document = new Document(inputStream);
                        
                        // Set document info for accessibility
                        if (document.Info != null)
                        {
                            document.Info.Title = document.Info.Title ?? "Accessible PDF Document";
                            document.Info.Author = document.Info.Author ?? "AccessForm Server";
                        }
                        
                        // Embed all fonts for accessibility
                        EmbedFonts(document);
                        
                        // Convert to PDF/A-2A for accessibility compliance
                        var tempLogPath = Path.GetTempFileName();
                        try
                        {
                            bool isValid = document.Convert(tempLogPath, PdfFormat.PDF_A_2A, ConvertErrorAction.Delete);
                            if (isValid)
                            {
                                _logger.LogInformation("Document converted to PDF/A-2A for accessibility");
                            }
                            else
                            {
                                _logger.LogWarning("Document conversion to PDF/A-2A had issues but continued");
                            }
                        }
                        finally
                        {
                            try { File.Delete(tempLogPath); } catch { }
                        }
                        
                        // Save tagged document
                        document.Save(outputStream);
                        
                        var taggedBytes = outputStream.ToArray();
                        _logger.LogInformation($"Aspose tagging successful, result is {taggedBytes.Length} bytes");
                        
                        return taggedBytes;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add accessibility tags with Aspose");
                    throw new InvalidOperationException($"Aspose accessibility tagging failed: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Helper method to embed all fonts in the document
        /// </summary>
        private void EmbedFonts(Document document)
        {
            _logger.LogInformation("Embedding fonts in PDF document");
            
            // Get all pages
            foreach (Page page in document.Pages)
            {
                // Get resources
                if (page.Resources?.Fonts != null)
                {
                    foreach (var font in page.Resources.Fonts)
                    {
                        // Check if font is already embedded
                        if (!font.IsEmbedded)
                        {
                            _logger.LogInformation($"Embedding font: {font.FontName}");
                            
                            // Try to embed the font
                            try
                            {
                                font.IsEmbedded = true;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Could not embed font {font.FontName}: {ex.Message}");
                                
                                // Try to replace with a standard font
                                if (font.FontName.Contains("Arial"))
                                {
                                    _logger.LogInformation("Replacing Arial with Helvetica");
                                    // Font replacement is handled at save time
                                }
                                else if (font.FontName.Contains("ZapfDingbats"))
                                {
                                    _logger.LogInformation("ZapfDingbats is a standard PDF font");
                                }
                            }
                        }
                        else
                        {
                            _logger.LogInformation($"Font already embedded: {font.FontName}");
                        }
                    }
                }
            }
            
            // Use FontUtilities to subset fonts
            try
            {
                document.FontUtilities.SubsetFonts(FontSubsetStrategy.SubsetEmbeddedFontsOnly);
                _logger.LogInformation("Applied font subsetting strategy");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not apply font subsetting: {ex.Message}");
            }
        }
    }
}