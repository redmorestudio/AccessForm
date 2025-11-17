using System.Collections.Generic;

namespace WordToPdfConverter.Models.Logical;

/// <summary>
/// A single row within a table, containing a set of cells.
/// Cells are ordered left-to-right by ColumnIndex.
/// </summary>
public record TableRow(IReadOnlyList<TableCell> Cells);
