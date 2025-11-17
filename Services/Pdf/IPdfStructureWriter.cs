using WordToPdfConverter.Models.Remediation;
using WordToPdfConverter.Services.Remediation.Structure;

namespace WordToPdfConverter.Services.Pdf;

/// <summary>
/// Applies a StructureTree to a PDF and returns a new tagged PDF.
/// This interface abstracts the PDF backend (iText7, Syncfusion, pikepdf, etc.)
/// allowing us to swap implementations without changing the remediation pipeline.
/// </summary>
public interface IPdfStructureWriter
{
    /// <summary>
    /// Rewrites the provided PDF bytes with a new tag structure.
    /// The original PDF content is preserved, only the StructTreeRoot is replaced.
    /// </summary>
    /// <param name="originalPdf">Original PDF bytes.</param>
    /// <param name="tree">Structure tree describing the desired tag structure.</param>
    /// <param name="context">Optional rebuild context containing ImageCache and LayoutPlan.</param>
    /// <returns>New PDF bytes with tags updated.</returns>
    byte[] Rewrite(byte[] originalPdf, StructureTree tree, StructureRebuildContext? context = null);
}
