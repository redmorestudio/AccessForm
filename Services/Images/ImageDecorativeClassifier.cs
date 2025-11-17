using WordToPdfConverter.Models;
using WordToPdfConverter.Models.Logical;

namespace WordToPdfConverter.Services.Images;

/// <summary>
/// Classifies images as decorative or meaningful using heuristics.
/// Decorative images should be tagged as artifacts and hidden from screen readers.
/// Meaningful images should have alt text and be included in reading order.
/// </summary>
public static class ImageDecorativeClassifier
{
    /// <summary>
    /// Determines if an image is likely decorative based on size, position, and alt text cues.
    /// </summary>
    /// <param name="image">The image tagging result to classify</param>
    /// <param name="pageWidth">Width of the page containing the image</param>
    /// <param name="pageHeight">Height of the page containing the image</param>
    /// <returns>True if the image is likely decorative, false if meaningful</returns>
    public static bool IsLikelyDecorative(ImageTagResult image, double pageWidth, double pageHeight)
    {
        int decorativeSignals = 0;

        // Rule 1: Size check - small images are often decorative
        var widthPercent = image.Bounds.Width / pageWidth;
        var heightPercent = image.Bounds.Height / pageHeight;

        if (widthPercent < 0.05 && heightPercent < 0.05)
        {
            decorativeSignals++;
        }

        // Rule 2: Header/footer position check
        var yPercent = image.Bounds.Y / pageHeight;
        var isInHeaderOrFooter = yPercent < 0.15 || yPercent > 0.85;

        if (isInHeaderOrFooter && (widthPercent < 0.10 || heightPercent < 0.10))
        {
            decorativeSignals++;
        }

        // Rule 3: Check alt text for decorative cues
        if (!string.IsNullOrWhiteSpace(image.AltText))
        {
            var altLower = image.AltText.ToLowerInvariant();

            var decorativeCues = new[]
            {
                "logo",
                "icon",
                "watermark",
                "divider",
                "bullet",
                "decoration",
                "decorative"
            };

            if (decorativeCues.Any(cue => altLower.Contains(cue)))
            {
                decorativeSignals++;
            }

            // Rule 4: Content cues override ALL other signals (force meaningful)
            var contentCues = new[]
            {
                "map",
                "chart",
                "diagram",
                "graph",
                "figure",
                "photo",
                "illustration",
                "screenshot",
                "infographic"
            };

            if (contentCues.Any(cue => altLower.Contains(cue)))
            {
                // Force meaningful - override all decorative signals
                return false;
            }
        }

        // Rule 5: Tie-breaking - two or more decorative signals = decorative
        return decorativeSignals >= 2;
    }
}
