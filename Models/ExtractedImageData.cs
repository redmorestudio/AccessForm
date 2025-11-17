namespace WordToPdfConverter.Models;

/// <summary>
/// Represents extracted image data to be cached for later rendering in the PDF writer.
/// This is stored in the ImageCache and referenced by SourceImageId.
/// </summary>
public sealed class ExtractedImageData
{
    /// <summary>
    /// Raw image bytes (PNG, JPEG, etc.)
    /// </summary>
    public byte[] Bytes { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// Image width in pixels (optional, for layout calculations)
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// Image height in pixels (optional, for layout calculations)
    /// </summary>
    public int? Height { get; init; }
}
