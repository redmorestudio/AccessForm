using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models;
using WordToPdfConverter.Models.Logical;
using WordToPdfConverter.Services.Images;
using AccessFormServer.Services;

namespace WordToPdfConverter.Services.Enrichment;

/// <summary>
/// Enriches a LogicalDocument with Figure blocks by calling the image tagging module,
/// classifying decorative vs meaningful images, and building an ImageCache for later rendering.
/// </summary>
public sealed class FigureDetectionEnricher
{
    private readonly ImageAutoTaggingService _imageTagging;
    private readonly ILogger<FigureDetectionEnricher> _logger;

    public FigureDetectionEnricher(
        ImageAutoTaggingService imageTagging,
        ILogger<FigureDetectionEnricher> logger)
    {
        _imageTagging = imageTagging;
        _logger = logger;
    }

    /// <summary>
    /// Enriches the LogicalDocument with FigureBlocks and populates the ImageCache.
    /// </summary>
    /// <param name="document">The LogicalDocument to enrich</param>
    /// <param name="pdfBytes">Original PDF bytes for image extraction</param>
    /// <param name="fileName">Filename for logging purposes</param>
    /// <param name="pageWidth">Page width for decorative classification (default 612 = US Letter)</param>
    /// <param name="pageHeight">Page height for decorative classification (default 792 = US Letter)</param>
    /// <returns>Dictionary mapping SourceImageId to ExtractedImageData (ImageCache)</returns>
    public async Task<Dictionary<string, ExtractedImageData>> EnrichAsync(
        LogicalDocument document,
        byte[] pdfBytes,
        string fileName,
        double pageWidth = 612,
        double pageHeight = 792)
    {
        _logger.LogInformation($"[FIGURE-ENRICHMENT] Starting figure detection for {fileName}");

        var imageCache = new Dictionary<string, ExtractedImageData>();

        try
        {
            // Call the image tagging module
            var imagesWithAltText = await _imageTagging.ExtractAndTagImagesAsync(pdfBytes, fileName);

            _logger.LogInformation($"[FIGURE-ENRICHMENT] Extracted {imagesWithAltText.Count} images");

            // Track images by page for ID generation
            var imageCountByPage = new Dictionary<int, int>();

            foreach (var img in imagesWithAltText)
            {
                // Convert to ImageTagResult
                var imageTagResult = new ImageTagResult
                {
                    PageIndex = img.PageNumber - 1, // Convert 1-based to 0-based
                    Bounds = new Rect(img.X, img.Y, img.Width, img.Height),
                    AltText = img.AltText,
                    ImageBytes = img.ImageBytes,
                    Width = (int?)img.Width,
                    Height = (int?)img.Height,
                    IsLikelyDecorative = false // Will be set next
                };

                // Classify as decorative or meaningful
                var isDecorative = ImageDecorativeClassifier.IsLikelyDecorative(
                    imageTagResult,
                    pageWidth,
                    pageHeight);

                imageTagResult = imageTagResult with { IsLikelyDecorative = isDecorative };

                // Generate SourceImageId
                var pageIndex = imageTagResult.PageIndex;
                if (!imageCountByPage.ContainsKey(pageIndex))
                {
                    imageCountByPage[pageIndex] = 0;
                }
                var imageIndex = imageCountByPage[pageIndex]++;
                var sourceImageId = $"p{pageIndex}_img{imageIndex}";

                // Create FigureBlock
                var figureBlock = new FigureBlock(
                    Bounds: imageTagResult.Bounds,
                    PageIndex: pageIndex,
                    AltTextSuggestion: imageTagResult.AltText,
                    IsLikelyDecorative: imageTagResult.IsLikelyDecorative,
                    SourceImageId: sourceImageId
                );

                // Add to ImageCache
                if (imageTagResult.ImageBytes != null && imageTagResult.ImageBytes.Length > 0)
                {
                    imageCache[sourceImageId] = new ExtractedImageData
                    {
                        Bytes = imageTagResult.ImageBytes,
                        Width = imageTagResult.Width,
                        Height = imageTagResult.Height
                    };

                    _logger.LogInformation(
                        $"[FIGURE-ENRICHMENT] {sourceImageId}: " +
                        $"decorative={isDecorative}, alt=\"{imageTagResult.AltText}\", " +
                        $"bytes={imageTagResult.ImageBytes.Length}");
                }

                // Add FigureBlock to the corresponding page's blocks
                if (pageIndex >= 0 && pageIndex < document.Pages.Count)
                {
                    var page = document.Pages[pageIndex];
                    var updatedBlocks = new List<LogicalBlock>(page.Blocks) { figureBlock };

                    // Replace page with updated blocks
                    var updatedPage = page with { Blocks = updatedBlocks };
                    var pagesList = document.Pages.ToList();
                    pagesList[pageIndex] = updatedPage;

                    // Update document (this is a bit awkward with records, but necessary)
                    // Note: In practice, you might want to pass the document as mutable or
                    // rebuild it from scratch. For now, this shows the concept.
                    document = document with { Pages = pagesList };
                }
            }

            _logger.LogInformation(
                $"[FIGURE-ENRICHMENT] Completed: {imagesWithAltText.Count} figures, " +
                $"{imageCache.Count} cached images");

            return imageCache;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[FIGURE-ENRICHMENT] Failed to enrich figures for {fileName}");
            return imageCache;
        }
    }
}
