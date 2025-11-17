using Syncfusion.Drawing;
using WordToPdfConverter.Services.Remediation.Structure;

namespace WordToPdfConverter.Models.Layout;

/// <summary>
/// A single drawing instruction linking a StructureNode to its target bounds on a page.
/// Phase 3b: Used for positioning figures.
/// Phase 3c: Extended with column and region information for reading order.
/// </summary>
public sealed class DrawInstruction
{
    /// <summary>
    /// The structure node to render
    /// </summary>
    public StructureNode Node { get; set; } = default!;

    /// <summary>
    /// Target bounding rectangle for this node (in PDF coordinates)
    /// </summary>
    public RectangleF TargetBounds { get; set; }

    /// <summary>
    /// Column index for this node (0-based, left to right).
    /// Used for multi-column layout ordering.
    /// </summary>
    public int ColumnIndex { get; set; }

    /// <summary>
    /// Region classification: "header", "body", "sidebar", or "footer".
    /// Used for reading order synthesis.
    /// </summary>
    public string Region { get; set; } = "body";
}
