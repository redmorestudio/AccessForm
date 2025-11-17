namespace WordToPdfConverter.Models.Logical;

/// <summary>
/// A figure or image on the page. May be decorative or have alt text.
/// AltTextSuggestion is provided by AI analysis, IsLikelyDecorative indicates
/// whether this should be tagged as an artifact vs. meaningful content.
/// </summary>
public record FigureBlock(
    Rect Bounds,
    int PageIndex,
    string? AltTextSuggestion,
    bool IsLikelyDecorative,
    string? SourceImageId
) : LogicalBlock(Bounds);
