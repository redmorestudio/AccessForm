using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tagged;
using Microsoft.Extensions.Logging;

namespace AccessFormServer.Services;

/// <summary>
/// Service for adding AI-generated alt text to images in PDFs
/// </summary>
public class ImageAltTextService
{
    private readonly AnthropicService _anthropicService;
    private readonly ILogger<ImageAltTextService> _logger;

    public ImageAltTextService(
        AnthropicService anthropicService,
        ILogger<ImageAltTextService> logger)
    {
        _anthropicService = anthropicService;
        _logger = logger;
    }

    /// <summary>
    /// Add alt text to all images in the PDF using Claude Vision
    /// </summary>
    public async Task<ImageAltTextReport> AddAltTextToImagesAsync(Document document)
    {
        var report = new ImageAltTextReport();

        try
        {
            _logger.LogInformation("===== IMAGE ALT TEXT GENERATION =====");

            var taggedContent = document.TaggedContent;
            if (taggedContent == null || taggedContent.RootElement == null)
            {
                _logger.LogWarning("Document is not tagged - cannot add alt text to images");
                report.Warnings.Add("Document is not tagged");
                return report;
            }

            // Find all Figure elements (images)
            var figures = new List<FigureElement>();
            FindFigureElements(taggedContent.RootElement, figures);

            _logger.LogInformation($"Found {figures.Count} image(s) in document");
            report.TotalImages = figures.Count;

            foreach (var figure in figures)
            {
                try
                {
                    // Check if alt text already exists
                    if (!string.IsNullOrEmpty(figure.AlternativeText))
                    {
                        _logger.LogInformation($"Image already has alt text: '{figure.AlternativeText}'");
                        report.ImagesWithExistingAltText++;
                        continue;
                    }

                    // Try to extract the image and generate alt text
                    var altText = await GenerateAltTextForFigure(figure, document);

                    if (!string.IsNullOrEmpty(altText))
                    {
                        figure.AlternativeText = altText;
                        _logger.LogInformation($"✅ Generated alt text: '{altText}'");
                        report.ImagesProcessed++;
                    }
                    else
                    {
                        _logger.LogWarning("Failed to generate alt text for image");
                        report.ImagesFailed++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error processing image: {ex.Message}");
                    report.ImagesFailed++;
                    report.Warnings.Add($"Failed to process image: {ex.Message}");
                }
            }

            _logger.LogInformation($"===== ALT TEXT GENERATION SUMMARY =====");
            _logger.LogInformation($"Total images: {report.TotalImages}");
            _logger.LogInformation($"Images processed: {report.ImagesProcessed}");
            _logger.LogInformation($"Images with existing alt text: {report.ImagesWithExistingAltText}");
            _logger.LogInformation($"Images failed: {report.ImagesFailed}");

            if (report.Warnings.Any())
            {
                _logger.LogWarning($"Warnings ({report.Warnings.Count}):");
                foreach (var warning in report.Warnings)
                {
                    _logger.LogWarning($"  - {warning}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error during alt text generation: {ex.Message}");
            report.Warnings.Add($"Alt text generation error: {ex.Message}");
        }

        return report;
    }

    /// <summary>
    /// Recursively find all Figure elements in the structure tree
    /// </summary>
    private void FindFigureElements(Element element, List<FigureElement> figures)
    {
        if (element == null) return;

        if (element is FigureElement figure)
        {
            figures.Add(figure);
        }

        foreach (var child in element.ChildElements)
        {
            FindFigureElements(child, figures);
        }
    }

    /// <summary>
    /// Generate alt text for a specific figure element
    /// </summary>
    private async Task<string?> GenerateAltTextForFigure(FigureElement figure, Document document)
    {
        try
        {
            // Try to extract image bytes from the figure
            // This is tricky because Aspose doesn't provide direct access to image bytes from FigureElement
            // We need to find the associated XImage in the page resources

            // For now, we'll try a different approach: extract all images from the document
            // and match them to figures based on position

            foreach (Page page in document.Pages)
            {
                var imageExtractor = new Aspose.Pdf.Facades.PdfExtractor();
                imageExtractor.BindPdf(document);
                imageExtractor.StartPage = page.Number;
                imageExtractor.EndPage = page.Number;
                imageExtractor.ExtractImage();

                if (imageExtractor.HasNextImage())
                {
                    // Get first image (this is a simplification - should match by position)
                    var ms = new MemoryStream();
                    imageExtractor.GetNextImage(ms);
                    var imageBytes = ms.ToArray();

                    if (imageBytes.Length > 0)
                    {
                        _logger.LogInformation($"Analyzing image ({imageBytes.Length} bytes)...");
                        var altText = await _anthropicService.AnalyzeImageForAltText(imageBytes);
                        return altText;
                    }
                }
            }

            _logger.LogWarning("Could not extract image bytes for figure");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error generating alt text for figure: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// Report of image alt text generation operations
/// </summary>
public class ImageAltTextReport
{
    public int TotalImages { get; set; } = 0;
    public int ImagesProcessed { get; set; } = 0;
    public int ImagesWithExistingAltText { get; set; } = 0;
    public int ImagesFailed { get; set; } = 0;
    public List<string> Warnings { get; set; } = new List<string>();
}
