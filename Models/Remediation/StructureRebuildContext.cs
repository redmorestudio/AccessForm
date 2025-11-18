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
    /// Phase 6b Pipeline Integration: Changed to { get; set; } to allow shared context updates.
    /// </summary>
    public IReadOnlyDictionary<string, ExtractedImageData> ImageCache { get; set; }
        = new Dictionary<string, ExtractedImageData>();

    /// <summary>
    /// Page layout plan containing drawing instructions for all pages.
    /// In Phase 3b this is minimal (primarily for figure placement).
    /// In Phase 3c this becomes more sophisticated with column-aware ordering.
    /// Phase 6b Pipeline Integration: Changed to { get; set; } to allow shared context updates.
    /// </summary>
    public PageLayoutPlan LayoutPlan { get; set; } = new();

    /// <summary>
    /// Phase 6b Pipeline Integration: Tracks whether structure rebuild has been executed.
    /// Used to prevent secondary rebuilds that would overwrite MCID markers.
    /// </summary>
    public bool StructureRebuildExecuted { get; set; }

    /// <summary>
    /// Phase 6b Pipeline Integration: Tracks whether MCID content stream rewrite has been executed.
    /// Used to write-protect the PDF against accidental overwrites of BDC/EMC markers.
    /// </summary>
    public bool McidContentRewriteExecuted { get; set; }
}
