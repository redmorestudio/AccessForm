namespace WordToPdfConverter.Models;

/// <summary>
/// Normalized result from the image tagging module.
/// Contains extracted image information, AI-generated alt text, and decorative classification.
/// </summary>
public sealed record ImageTagResult
{
    /// <summary>
    /// Zero-based page index where this image appears
    /// </summary>
    public int PageIndex { get; init; }

    /// <summary>
    /// Bounding rectangle of the image in the unified coordinate system
    /// </summary>
    public Logical.Rect Bounds { get; init; }

    /// <summary>
    /// AI-generated or existing alt text for the image
    /// </summary>
    public string? AltText { get; init; }

    /// <summary>
    /// Whether this image is likely decorative (should be tagged as artifact)
    /// </summary>
    public bool IsLikelyDecorative { get; init; }

    /// <summary>
    /// Raw image bytes (PNG, JPEG, etc.)
    /// </summary>
    public byte[]? ImageBytes { get; init; }

    /// <summary>
    /// Image width in pixels (if available)
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// Image height in pixels (if available)
    /// </summary>
    public int? Height { get; init; }
}
