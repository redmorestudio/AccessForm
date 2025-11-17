using System.Collections.Generic;

namespace WordToPdfConverter.Models.Logical;

/// <summary>
/// A table block representing a logical table on the page.
/// Contains rows which contain cells, providing full table structure for accessibility.
/// </summary>
public record TableBlock(
    Rect Bounds,
    IReadOnlyList<TableRow> Rows
) : LogicalBlock(Bounds);
