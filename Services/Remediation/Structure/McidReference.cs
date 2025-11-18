namespace WordToPdfConverter.Services.Remediation.Structure;

/// <summary>
/// Represents a Marked Content ID (MCID) reference for a structure node.
/// MCIDs link structure tree elements to actual content in PDF page streams.
/// </summary>
public sealed class McidReference
{
    /// <summary>
    /// Zero-based page index where this MCID appears.
    /// </summary>
    public int PageIndex { get; init; }

    /// <summary>
    /// Marked Content ID value assigned to this content segment.
    /// MCIDs are page-scoped integers that appear in BDC/EMC operators in the content stream.
    /// </summary>
    public int Mcid { get; init; }
}
