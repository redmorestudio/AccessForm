namespace WordToPdfConverter.Models.Logical;

/// <summary>
/// Base type for all logical blocks on a page: headings, paragraphs, tables, figures, etc.
/// This is an abstract discriminated union that can be pattern-matched in C#.
/// </summary>
public abstract record LogicalBlock(Rect Bounds);
