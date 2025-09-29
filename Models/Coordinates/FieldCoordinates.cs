using System;

namespace WordToPdfConverter.Models.Coordinates
{
    /// <summary>
    /// Base class for all coordinate types - provides type safety
    /// </summary>
    public abstract class FieldCoordinate
    {
        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }

        protected FieldCoordinate(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;

            // Validation
            if (width < 0)
                throw new ArgumentException($"Width cannot be negative: {width}");
            if (height < 0)
                throw new ArgumentException($"Height cannot be negative: {height}");
        }

        public abstract string CoordinateSystem { get; }
        public abstract string Origin { get; }

        public override string ToString()
        {
            return $"{GetType().Name}[X={X:F1}, Y={Y:F1}, W={Width:F1}, H={Height:F1}] ({Origin} origin)";
        }
    }

    /// <summary>
    /// PDF coordinates in points (72 DPI), bottom-left origin
    /// This is our canonical internal representation
    /// </summary>
    public class PdfCoordinate : FieldCoordinate
    {
        public PdfCoordinate(float x, float y, float width, float height)
            : base(x, y, width, height) { }

        public override string CoordinateSystem => "PDF Points (72 DPI)";
        public override string Origin => "Bottom-Left";

        /// <summary>
        /// Validate that coordinates are within page bounds
        /// </summary>
        public bool IsWithinPage(float pageWidth, float pageHeight)
        {
            return X >= 0 && Y >= 0 &&
                   X + Width <= pageWidth &&
                   Y + Height <= pageHeight;
        }
    }

    /// <summary>
    /// Display coordinates in pixels (150 DPI), top-left origin
    /// Used for UI rendering
    /// </summary>
    public class DisplayCoordinate : FieldCoordinate
    {
        public DisplayCoordinate(float x, float y, float width, float height)
            : base(x, y, width, height) { }

        public override string CoordinateSystem => "Display Pixels (150 DPI)";
        public override string Origin => "Top-Left";
    }

    /// <summary>
    /// Percentage-based coordinates (0-100), top-left origin
    /// Used by Claude Vision API
    /// </summary>
    public class PercentageCoordinate : FieldCoordinate
    {
        public PercentageCoordinate(float xPercent, float yPercent, float widthPercent, float heightPercent)
            : base(xPercent, yPercent, widthPercent, heightPercent)
        {
            // Validate percentages
            if (xPercent < 0 || xPercent > 100)
                throw new ArgumentException($"X percentage must be 0-100: {xPercent}");
            if (yPercent < 0 || yPercent > 100)
                throw new ArgumentException($"Y percentage must be 0-100: {yPercent}");
            if (widthPercent < 0 || widthPercent > 100)
                throw new ArgumentException($"Width percentage must be 0-100: {widthPercent}");
            if (heightPercent < 0 || heightPercent > 100)
                throw new ArgumentException($"Height percentage must be 0-100: {heightPercent}");
        }

        public override string CoordinateSystem => "Percentage (0-100)";
        public override string Origin => "Top-Left";
    }

    /// <summary>
    /// Raw Syncfusion coordinates - already in PDF format (bottom-left)
    /// but we track them separately for debugging
    /// </summary>
    public class SyncfusionCoordinate : PdfCoordinate
    {
        public SyncfusionCoordinate(float x, float y, float width, float height)
            : base(x, y, width, height) { }

        public override string CoordinateSystem => "Syncfusion (PDF Points)";
    }
}