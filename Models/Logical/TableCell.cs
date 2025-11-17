namespace WordToPdfConverter.Models.Logical;

/// <summary>
/// A single table cell. Row/Column indices are 0-based within the table.
/// IsHeader distinguishes header cells (TH) from data cells (TD).
/// </summary>
public record TableCell(
    int RowIndex,
    int ColumnIndex,
    bool IsHeader,
    string Text);
