using System;
using Microsoft.Extensions.Logging;

namespace WordToPdfConverter.Models
{
    /// <summary>
    /// Universal coordinate system for field positions.
    /// All coordinates are stored in PDF coordinate system (bottom-left origin, in points).
    /// This is the "system of truth" for all field positioning.
    /// </summary>
    public class UniversalFieldCoordinates
    {
        // Core PDF coordinates (bottom-left origin, Y increases upward)
        public float PdfX { get; private set; }
        public float PdfY { get; private set; }
        public float PdfWidth { get; private set; }
        public float PdfHeight { get; private set; }
        public int PageNumber { get; private set; }

        // Page dimensions for conversions
        public float PageWidth { get; private set; }
        public float PageHeight { get; private set; }

        // Metadata
        public string SourceSystem { get; private set; }
        public DateTime CreatedAt { get; private set; }

        // Standard DPI values
        public const float PDF_DPI = 72f;
        public const float DISPLAY_DPI = 150f;
        public const float SCALE_FACTOR = DISPLAY_DPI / PDF_DPI;

        /// <summary>
        /// Create universal coordinates from PDF coordinates (bottom-left origin)
        /// </summary>
        public static UniversalFieldCoordinates FromPdfCoordinates(
            float x, float y, float width, float height,
            int pageNumber, float pageWidth, float pageHeight,
            string sourceSystem)
        {
            return new UniversalFieldCoordinates
            {
                PdfX = x,
                PdfY = y,
                PdfWidth = width,
                PdfHeight = height,
                PageNumber = pageNumber,
                PageWidth = pageWidth,
                PageHeight = pageHeight,
                SourceSystem = sourceSystem,
                CreatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Create universal coordinates from display/image coordinates (top-left origin)
        /// </summary>
        public static UniversalFieldCoordinates FromDisplayCoordinates(
            float displayX, float displayY, float displayWidth, float displayHeight,
            int pageNumber, float pageWidth, float pageHeight,
            string sourceSystem)
        {
            // Convert from top-left to bottom-left origin
            float pdfX = displayX;
            float pdfY = pageHeight - displayY - displayHeight; // Flip Y axis

            return new UniversalFieldCoordinates
            {
                PdfX = pdfX,
                PdfY = pdfY,
                PdfWidth = displayWidth,
                PdfHeight = displayHeight,
                PageNumber = pageNumber,
                PageWidth = pageWidth,
                PageHeight = pageHeight,
                SourceSystem = sourceSystem,
                CreatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Create universal coordinates from percentage values (0-100)
        /// </summary>
        public static UniversalFieldCoordinates FromPercentageCoordinates(
            float xPercent, float yPercent, float widthPercent, float heightPercent,
            int pageNumber, float pageWidth, float pageHeight,
            string sourceSystem, bool isTopLeftOrigin = true)
        {
            // Convert percentages to PDF points
            float x = (xPercent / 100f) * pageWidth;
            float width = (widthPercent / 100f) * pageWidth;
            float height = (heightPercent / 100f) * pageHeight;

            float y;
            if (isTopLeftOrigin)
            {
                // Convert from top-left percentage to bottom-left PDF
                float yTopLeft = (yPercent / 100f) * pageHeight;
                y = pageHeight - yTopLeft - height;
            }
            else
            {
                // Already in bottom-left origin
                y = (yPercent / 100f) * pageHeight;
            }

            return new UniversalFieldCoordinates
            {
                PdfX = x,
                PdfY = y,
                PdfWidth = width,
                PdfHeight = height,
                PageNumber = pageNumber,
                PageWidth = pageWidth,
                PageHeight = pageHeight,
                SourceSystem = sourceSystem,
                CreatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Get coordinates in display/screen coordinate system (top-left origin)
        /// </summary>
        public (float x, float y, float width, float height) ToDisplayCoordinates()
        {
            // Convert from bottom-left to top-left origin
            float displayX = PdfX;
            float displayY = PageHeight - PdfY - PdfHeight; // Flip Y axis

            return (displayX, displayY, PdfWidth, PdfHeight);
        }

        /// <summary>
        /// Get coordinates as percentages (0-100)
        /// </summary>
        public (float xPercent, float yPercent, float widthPercent, float heightPercent) ToPercentageCoordinates(bool asTopLeftOrigin = true)
        {
            float xPercent = (PdfX / PageWidth) * 100f;
            float widthPercent = (PdfWidth / PageWidth) * 100f;
            float heightPercent = (PdfHeight / PageHeight) * 100f;

            float yPercent;
            if (asTopLeftOrigin)
            {
                // Convert to top-left origin percentage
                float yTopLeft = PageHeight - PdfY - PdfHeight;
                yPercent = (yTopLeft / PageHeight) * 100f;
            }
            else
            {
                // Keep in bottom-left origin
                yPercent = (PdfY / PageHeight) * 100f;
            }

            return (xPercent, yPercent, widthPercent, heightPercent);
        }

        /// <summary>
        /// Get coordinates with DPI scaling for display
        /// </summary>
        public (float x, float y, float width, float height) ToScaledDisplayCoordinates()
        {
            var (displayX, displayY, displayWidth, displayHeight) = ToDisplayCoordinates();
            return (
                displayX * SCALE_FACTOR,
                displayY * SCALE_FACTOR,
                displayWidth * SCALE_FACTOR,
                displayHeight * SCALE_FACTOR
            );
        }

        /// <summary>
        /// Validate that coordinates are within page bounds
        /// </summary>
        public bool IsValid(ILogger logger = null)
        {
            bool valid = true;

            if (PdfX < 0 || PdfX > PageWidth)
            {
                logger?.LogWarning($"[COORD-VALIDATION] X coordinate {PdfX} is outside page bounds (0-{PageWidth})");
                valid = false;
            }

            if (PdfY < 0 || PdfY > PageHeight)
            {
                logger?.LogWarning($"[COORD-VALIDATION] Y coordinate {PdfY} is outside page bounds (0-{PageHeight})");
                valid = false;
            }

            if (PdfX + PdfWidth > PageWidth)
            {
                logger?.LogWarning($"[COORD-VALIDATION] Field extends beyond right edge: X({PdfX}) + Width({PdfWidth}) > PageWidth({PageWidth})");
                valid = false;
            }

            if (PdfY + PdfHeight > PageHeight)
            {
                logger?.LogWarning($"[COORD-VALIDATION] Field extends beyond top edge: Y({PdfY}) + Height({PdfHeight}) > PageHeight({PageHeight})");
                valid = false;
            }

            if (PdfWidth <= 0 || PdfHeight <= 0)
            {
                logger?.LogWarning($"[COORD-VALIDATION] Invalid dimensions: Width={PdfWidth}, Height={PdfHeight}");
                valid = false;
            }

            return valid;
        }

        /// <summary>
        /// Apply field-type specific corrections to dimensions
        /// </summary>
        public void ApplyFieldTypeCorrections(string fieldType, ILogger logger = null)
        {
            switch (fieldType?.ToLower())
            {
                case "checkbox":
                case "radio":
                    // Ensure square dimensions for checkboxes/radios
                    if (Math.Abs(PdfWidth - PdfHeight) > 5)
                    {
                        float size = Math.Min(PdfWidth, PdfHeight);
                        size = Math.Max(15, Math.Min(25, size)); // Clamp between 15-25 points
                        logger?.LogInformation($"[COORD-CORRECTION] Normalizing {fieldType} to square: {size}x{size}");
                        PdfWidth = size;
                        PdfHeight = size;
                    }
                    break;

                case "text":
                case "date":
                case "email":
                case "phone":
                    // Ensure minimum dimensions for text fields
                    if (PdfWidth < 50)
                    {
                        logger?.LogInformation($"[COORD-CORRECTION] Expanding text field width from {PdfWidth} to 150");
                        PdfWidth = 150;
                    }
                    if (PdfHeight < 15)
                    {
                        logger?.LogInformation($"[COORD-CORRECTION] Expanding text field height from {PdfHeight} to 20");
                        PdfHeight = 20;
                    }
                    break;

                case "signature":
                    // Ensure reasonable signature field size
                    if (PdfWidth < 100)
                    {
                        logger?.LogInformation($"[COORD-CORRECTION] Expanding signature field width from {PdfWidth} to 200");
                        PdfWidth = 200;
                    }
                    if (PdfHeight < 40)
                    {
                        logger?.LogInformation($"[COORD-CORRECTION] Expanding signature field height from {PdfHeight} to 60");
                        PdfHeight = 60;
                    }
                    break;
            }
        }

        /// <summary>
        /// Check if two coordinate sets match within tolerance
        /// </summary>
        public bool Matches(UniversalFieldCoordinates other, float tolerance = 10f)
        {
            if (other == null) return false;
            if (PageNumber != other.PageNumber) return false;

            return Math.Abs(PdfX - other.PdfX) <= tolerance &&
                   Math.Abs(PdfY - other.PdfY) <= tolerance &&
                   Math.Abs(PdfWidth - other.PdfWidth) <= tolerance &&
                   Math.Abs(PdfHeight - other.PdfHeight) <= tolerance;
        }

        /// <summary>
        /// Clone with optional adjustments
        /// </summary>
        public UniversalFieldCoordinates Clone(float? xOffset = null, float? yOffset = null)
        {
            return new UniversalFieldCoordinates
            {
                PdfX = PdfX + (xOffset ?? 0),
                PdfY = PdfY + (yOffset ?? 0),
                PdfWidth = PdfWidth,
                PdfHeight = PdfHeight,
                PageNumber = PageNumber,
                PageWidth = PageWidth,
                PageHeight = PageHeight,
                SourceSystem = SourceSystem,
                CreatedAt = CreatedAt
            };
        }

        public override string ToString()
        {
            return $"[Universal Coords: PDF({PdfX:F1}, {PdfY:F1}, {PdfWidth:F1}x{PdfHeight:F1}) Page:{PageNumber} Source:{SourceSystem}]";
        }
    }
}