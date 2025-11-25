using System.Collections.Generic;
using iText.Kernel.Pdf;

namespace WordToPdfConverter.Services.Pdf;

/// <summary>
/// Phase 6b: Marks PDF page content streams with MCID tags by inserting
/// /Span <</MCID n>> BDC ... EMC wrappers around content segments.
/// This enables Acrobat highlighting, PDFix content linking, and proper screen reader navigation.
/// </summary>
public interface IContentMcidMarker
{
    /// <summary>
    /// Rewrites page content streams to insert BDC/EMC MCID markers.
    /// Must be called before pdfDoc.Close().
    /// NOTE: This method modifies the targets dictionary to remove unsuccessful assignments.
    /// </summary>
    /// <param name="doc">The PDF document with structure tree already built</param>
    /// <param name="targets">Mutable dictionary mapping (pageIndex, mcid) to structure nodes. Unassigned targets will be removed.</param>
    void Apply(
        PdfDocument doc,
        Dictionary<(int pageIndex, int mcid), McidTarget> targets);
}
