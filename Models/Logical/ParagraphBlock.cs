namespace WordToPdfConverter.Models.Logical;

/// <summary>
/// A semantic paragraph (one or more visual text runs merged together).
/// Represents body text content in the document.
/// </summary>
public record ParagraphBlock(
    Rect Bounds,
    string Text
) : LogicalBlock(Bounds);
