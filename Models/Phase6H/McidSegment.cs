namespace WordToPdfConverter.Models.Phase6H;

/// <summary>
/// Phase 6H: Represents a single content segment that should be wrapped with a specific MCID.
/// This model is sent to the external MCID rewriter microservice.
/// </summary>
public sealed class McidSegment
{
    /// <summary>
    /// 1-based page index (matching PDF page numbering).
    /// </summary>
    public int PageIndex { get; set; }

    /// <summary>
    /// The MCID value to apply to this segment.
    /// </summary>
    public int Mcid { get; set; }

    /// <summary>
    /// Optional role (e.g., "P", "H1", "TD", "TH", "Figure") for debugging/validation.
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Bounding box X coordinate in PDF user units (72 dpi).
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Bounding box Y coordinate in PDF user units (72 dpi).
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Bounding box width in PDF user units (72 dpi).
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Bounding box height in PDF user units (72 dpi).
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Optional ordering index (e.g., reading order position) if geometry is ambiguous.
    /// </summary>
    public int? SequenceIndex { get; set; }
}
