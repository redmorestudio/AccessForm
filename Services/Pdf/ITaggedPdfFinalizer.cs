using WordToPdfConverter.Models.Layout;
using WordToPdfConverter.Models.Logical;
using WordToPdfConverter.Models.Remediation;
using WordToPdfConverter.Services.Remediation.Structure;

namespace WordToPdfConverter.Services.Pdf;

/// <summary>
/// Phase 6b Pipeline Integration: Orchestrates the final tagged PDF generation.
///
/// This is the single high-level entrypoint responsible for:
/// 1. Building the final PDF structure tree
/// 2. Allocating MCIDs to content nodes (Phase 6)
/// 3. Rewriting content streams with BDC/EMC markers (Phase 6b)
/// 4. Ensuring exactly ONE structure rebuild per remediation run
///
/// This finalizer is called once at the end of the remediation pipeline
/// after all semantic fixers have modified LogicalDocument, StructureTree, and LayoutPlan.
/// </summary>
public interface ITaggedPdfFinalizer
{
    /// <summary>
    /// Finalizes the PDF with complete tag structure, MCID assignment, and content marking.
    ///
    /// This method:
    /// - Rebuilds PDF structure from StructureTree
    /// - Assigns MCIDs to leaf content nodes
    /// - Rewrites content streams with BDC/EMC markers
    /// - Sets context flags to prevent secondary rebuilds
    /// - Returns fully tagged, accessible PDF bytes
    ///
    /// IMPORTANT: This should be called exactly once per remediation run,
    /// after all fixers have completed their modifications.
    /// </summary>
    /// <param name="originalPdf">Original PDF bytes (or current state)</param>
    /// <param name="logical">Logical document representation</param>
    /// <param name="structure">Structure tree to write to PDF</param>
    /// <param name="layoutPlan">Page layout plan (required for Phase 6b)</param>
    /// <param name="context">Rebuild context containing image cache and execution flags</param>
    /// <returns>Final tagged PDF bytes with MCID markers</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if MCID content rewrite is enabled but layoutPlan is null
    /// </exception>
    byte[] FinalizeTaggedPdf(
        byte[] originalPdf,
        LogicalDocument logical,
        StructureTree structure,
        PageLayoutPlan layoutPlan,
        StructureRebuildContext context);
}
