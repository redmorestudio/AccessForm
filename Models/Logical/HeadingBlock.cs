namespace WordToPdfConverter.Models.Logical;

/// <summary>
/// A heading (H1, H2, etc.) on the page.
/// Headings provide document structure and navigation for screen readers.
/// </summary>
public record HeadingBlock(
    Rect Bounds,
    int Level,      // 1-6 corresponding to H1-H6
    string Text
) : LogicalBlock(Bounds);
