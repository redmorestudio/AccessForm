using iText.Kernel.Pdf.Tagging;
using WordToPdfConverter.Services.Remediation.Structure;

namespace WordToPdfConverter.Services.Pdf;

/// <summary>
/// Internal helper class for ITextMcidContentMarker.
/// Represents a structure node that needs to be linked to content via an MCID.
/// </summary>
public sealed class McidTarget
{
    /// <summary>
    /// The structure node that this MCID target represents.
    /// </summary>
    public required StructureNode Node { get; init; }

    /// <summary>
    /// The MCID assigned to this target (populated from Node.McidReferences).
    /// </summary>
    public int Mcid { get; init; }

    /// <summary>
    /// PHASE 6G: The PDF structure element for this target (used to create MCR links after content marking).
    /// </summary>
    public PdfStructElem? StructElem { get; init; }
}
