using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Pdf.Parsing;
using WordToPdfConverter.Models;

namespace AccessFormServer.Services
{
    /// <summary>
    /// Extracts images from PDFs and generates alt-text using Claude Vision
    /// The "slicker than goose shit" auto-tagging system
    /// </summary>
    public class ImageAutoTaggingService
    {
        private readonly ILogger<ImageAutoTaggingService> _logger;
        private readonly AnthropicService _anthropicService;

        public ImageAutoTaggingService(
            ILogger<ImageAutoTaggingService> logger,
            AnthropicService anthropicService)
        {
            _logger = logger;
            _anthropicService = anthropicService;
        }

        /// <summary>
        /// Extract all images from PDF and generate alt-text with Claude Vision
        /// This is the "slicker than goose shit" approach that works with the existing infrastructure
        /// </summary>
        public async Task<List<ImageWithAltText>> ExtractAndTagImagesAsync(byte[] pdfBytes, string fileName)
        {
            var results = new List<ImageWithAltText>();

            try
            {
                _logger.LogInformation($"Starting image extraction and auto-tagging for {fileName}");

                using var stream = new MemoryStream(pdfBytes);
                using var pdfDoc = new PdfLoadedDocument(stream);

                // Convert each page to image and analyze with Claude Vision
                for (int pageIndex = 0; pageIndex < pdfDoc.Pages.Count; pageIndex++)
                {
                    var pageNumber = pageIndex + 1;

                    try
                    {
                        var options = new PDFtoImage.RenderOptions
                        {
                            Dpi = 150,
                            AntiAliasing = PDFtoImage.PdfAntiAliasing.All
                        };

                        using var bitmap = PDFtoImage.Conversion.ToImage(pdfBytes, pageIndex, options: options);

                        // Convert bitmap to bytes using SKImage encoding
                        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
                        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 80);
                        var imageBytes = data.ToArray();

                        var imageData = new ImageWithAltText
                        {
                            PageNumber = pageNumber,
                            ImageIndex = 0, // Whole page as single image
                            ImageBytes = imageBytes,
                            ImageFormat = "png",
                            Width = bitmap.Width,
                            Height = bitmap.Height,
                            X = 0,
                            Y = 0
                        };

                        // Generate alt-text with Claude Vision for the entire page
                        // Claude can identify and describe any images it finds on the page
                        imageData.AltText = await GenerateAltTextWithClaude(imageData);

                        // Only add if Claude found images on the page
                        if (!string.IsNullOrEmpty(imageData.AltText) &&
                            !imageData.AltText.Contains("no images") &&
                            !imageData.AltText.Contains("No images"))
                        {
                            results.Add(imageData);
                            _logger.LogInformation($"Found images on page {pageNumber}: {imageData.AltText}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to analyze page {pageNumber} for images");
                    }
                }

                _logger.LogInformation($"Extracted {results.Count} pages with images from {fileName}");
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to extract and tag images from {fileName}");
                return results;
            }
        }

        /// <summary>
        /// Extract images from a single PDF page by converting page to image and using Claude Vision
        /// This is the "slicker than goose shit" approach that works with the existing infrastructure
        /// </summary>
        private async Task<List<ImageWithAltText>> ExtractImagesFromPage(PdfLoadedPage page, int pageNumber)
        {
            var images = new List<ImageWithAltText>();

            try
            {
                // For now, return empty list as a placeholder
                // The image extraction will be handled at the document level using byte arrays
                _logger.LogInformation($"Image extraction for page {pageNumber} is handled at document level");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to analyze page {pageNumber} for images");
            }

            return images;
        }

        /// <summary>
        /// Generate alt-text using Claude Vision
        /// </summary>
        private async Task<string> GenerateAltTextWithClaude(ImageWithAltText imageData)
        {
            try
            {
                _logger.LogInformation($"Generating alt-text for image {imageData.ImageIndex + 1} on page {imageData.PageNumber}");

                var altText = await _anthropicService.AnalyzeImageForAltText(imageData.ImageBytes);

                // Clean up the response
                altText = altText?.Trim()?.Trim('"')?.Trim() ?? "Image";

                // Fallback if Claude didn't respond properly
                if (string.IsNullOrEmpty(altText) || altText.Length < 3)
                {
                    altText = $"Image on page {imageData.PageNumber}";
                }

                return altText;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate alt-text with Claude, using fallback");
                return $"Image on page {imageData.PageNumber}";
            }
        }

        /// <summary>
        /// Determine image format from bytes
        /// </summary>
        private string DetermineImageFormat(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length < 4)
                return "unknown";

            // Check for common image format headers
            if (imageBytes[0] == 0xFF && imageBytes[1] == 0xD8)
                return "jpeg";

            if (imageBytes[0] == 0x89 && imageBytes[1] == 0x50 && imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
                return "png";

            if (imageBytes[0] == 0x47 && imageBytes[1] == 0x49 && imageBytes[2] == 0x46)
                return "gif";

            if (imageBytes[0] == 0x42 && imageBytes[1] == 0x4D)
                return "bmp";

            return "unknown";
        }

        /// <summary>
        /// Apply generated alt-text to PDF structure (for manual review/editing)
        /// </summary>
        public async Task<byte[]> ApplyAltTextToPdfAsync(byte[] pdfBytes, List<ImageWithAltText> imagesWithAltText)
        {
            try
            {
                using var stream = new MemoryStream(pdfBytes);
                using var pdfDoc = new PdfLoadedDocument(stream);

                // This would integrate with the PDF structure tagging system
                // For now, we'll store the alt-text in document metadata for manual application

                var altTextSummary = string.Join("; ", imagesWithAltText.Select(img =>
                    $"Page {img.PageNumber} Image {img.ImageIndex + 1}: {img.AltText}"));

                // Add to document metadata
                if (pdfDoc.DocumentInformation != null)
                {
                    pdfDoc.DocumentInformation.Subject += $" [Auto-generated Alt-text: {altTextSummary}]";
                }

                using var outputStream = new MemoryStream();
                pdfDoc.Save(outputStream);
                return outputStream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply alt-text to PDF");
                return pdfBytes; // Return original on failure
            }
        }
    }

    /// <summary>
    /// Represents an image extracted from PDF with generated alt-text
    /// </summary>
    public class ImageWithAltText
    {
        public int PageNumber { get; set; }
        public int ImageIndex { get; set; }
        public byte[] ImageBytes { get; set; }
        public string ImageFormat { get; set; }
        public string AltText { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public bool IsManuallyEdited { get; set; } = false;
        public string OriginalAltText { get; set; } // For tracking changes
    }
}