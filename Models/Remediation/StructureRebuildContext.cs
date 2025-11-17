using WordToPdfConverter.Models.Layout;

namespace WordToPdfConverter.Models.Remediation;

/// <summary>
/// Context passed to the PDF structure writer containing all necessary data
/// for rebuilding the PDF with proper structure, layout, and images.
/// </summary>
public sealed class StructureRebuildContext
{
    /// <summary>
    /// Image cache mapping SourceImageId to ExtractedImageData.
    /// Used by the writer to draw figures at their correct positions.
    /// </summary>
    public IReadOnlyDictionary<string, ExtractedImageData> ImageCache { get; init; }
        = new Dictionary<string, ExtractedImageData>();

    /// <summary>
    /// Page layout plan containing drawing instructions for all pages.
    /// In Phase 3b this is minimal (primarily for figure placement).
    /// In Phase 3c this becomes more sophisticated with column-aware ordering.
    /// </summary>
    public PageLayoutPlan LayoutPlan { get; init; } = new();
}
