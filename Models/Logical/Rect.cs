namespace WordToPdfConverter.Models.Logical;

/// <summary>
/// Simple rectangle in PDF page coordinates (72 DPI, bottom-left origin).
/// Used to represent bounding boxes for all logical content blocks.
/// </summary>
public readonly record struct Rect(double X, double Y, double Width, double Height);
