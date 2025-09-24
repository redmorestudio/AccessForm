using System;
using Microsoft.Extensions.Logging;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Utility class for standardized PDF coordinate conversion
    /// </summary>
    public static class PdfCoordinateConverter
    {
        // Standard DPI values
        public const float PDF_DPI = 72f;
        public const float DISPLAY_DPI = 150f;
        public const float SCALE_FACTOR = DISPLAY_DPI / PDF_DPI; // 2.083...

        // Standard PDF page dimensions in points
        public const float STANDARD_PAGE_WIDTH = 612f;  // US Letter width
        public const float STANDARD_PAGE_HEIGHT = 792f; // US Letter height

        /// <summary>
        /// Convert PDF coordinates (bottom-left origin) to display coordinates (top-left origin)
        /// </summary>
        public static (float x, float y) PdfToDisplay(float pdfX, float pdfY, float pageHeight)
        {
            var displayX = pdfX * SCALE_FACTOR;
            var displayY = (pageHeight - pdfY) * SCALE_FACTOR;
            return (displayX, displayY);
        }

        /// <summary>
        /// Convert display coordinates (top-left origin) to PDF coordinates (bottom-left origin)
        /// </summary>
        public static (float x, float y) DisplayToPdf(float displayX, float displayY, float pageHeight)
        {
            var pdfX = displayX / SCALE_FACTOR;
            var pdfY = pageHeight - (displayY / SCALE_FACTOR);
            return (pdfX, pdfY);
        }

        /// <summary>
        /// Scale PDF dimensions to display dimensions
        /// </summary>
        public static (float width, float height) ScaleDimensions(float pdfWidth, float pdfHeight)
        {
            return (pdfWidth * SCALE_FACTOR, pdfHeight * SCALE_FACTOR);
        }

        /// <summary>
        /// Determine which page a Y coordinate belongs to in a multi-page document (for absolute coordinates)
        /// </summary>
        public static int CalculatePageFromY(float yCoordinate, PdfLoadedDocument document, ILogger logger = null)
        {
            if (document?.Pages == null || document.Pages.Count == 0)
            {
                logger?.LogWarning($"Document has no pages, defaulting to page 1 for Y={yCoordinate}");
                return 1;
            }

            // Get actual page heights instead of assuming standard
            float cumulativeHeight = 0;

            for (int i = 0; i < document.Pages.Count; i++)
            {
                var page = document.Pages[i];
                var pageHeight = page.Size.Height;

                // Check if this Y coordinate falls within this page
                var pageStartY = cumulativeHeight;
                var pageEndY = cumulativeHeight + pageHeight;

                logger?.LogDebug($"Page {i + 1}: height={pageHeight}, range=[{pageStartY}, {pageEndY}], checking Y={yCoordinate}");

                // Handle both positive coordinates (from page 1) and negative coordinates (multi-page offset)
                if (yCoordinate >= 0)
                {
                    // Standard positive coordinates - check if it falls within this page's range
                    if (yCoordinate >= pageStartY && yCoordinate <= pageEndY)
                    {
                        logger?.LogInformation($"Y={yCoordinate} falls on page {i + 1} (range: {pageStartY}-{pageEndY})");
                        return i + 1;
                    }
                }
                else
                {
                    // Negative coordinates indicate offset from a multi-page conversion
                    // Convert to positive equivalent and check
                    var absoluteY = Math.Abs(yCoordinate);
                    if (absoluteY <= pageHeight && i == 1) // Usually page 2 for simple negative offsets
                    {
                        logger?.LogInformation($"Negative Y={yCoordinate} (abs={absoluteY}) assigned to page 2");
                        return 2;
                    }
                    // For more complex negative offsets, calculate based on cumulative height
                    else if (absoluteY >= cumulativeHeight && absoluteY <= pageEndY)
                    {
                        logger?.LogInformation($"Complex negative Y={yCoordinate} assigned to page {i + 1}");
                        return i + 1;
                    }
                }

                cumulativeHeight += pageHeight;
            }

            // If we couldn't determine the page, use a heuristic
            if (yCoordinate < 0)
            {
                // Negative coordinates typically indicate page 2 or beyond
                var calculatedPage = Math.Min(document.Pages.Count,
                    Math.Max(2, (int)(Math.Abs(yCoordinate) / STANDARD_PAGE_HEIGHT) + 2));
                logger?.LogWarning($"Could not determine page for Y={yCoordinate}, using heuristic: page {calculatedPage}");
                return calculatedPage;
            }
            else
            {
                // Positive coordinates - calculate based on standard page height
                var calculatedPage = Math.Min(document.Pages.Count,
                    Math.Max(1, (int)(yCoordinate / STANDARD_PAGE_HEIGHT) + 1));
                logger?.LogWarning($"Could not determine page for Y={yCoordinate}, using heuristic: page {calculatedPage}");
                return calculatedPage;
            }
        }

        /// <summary>
        /// Find which page contains a field using page-relative coordinates
        /// (for use with Syncfusion field bounds which are page-relative)
        /// </summary>
        public static int FindPageContainingField(PdfLoadedField field, PdfLoadedDocument document, ILogger logger = null)
        {
            if (document?.Pages == null || document.Pages.Count == 0)
            {
                logger?.LogWarning($"Document has no pages, defaulting to page 1 for field '{field.Name}'");
                return 1;
            }

            // Try to use the field's Page property first if available
            try
            {
                if (field.Page != null)
                {
                    // Find which page index this corresponds to
                    for (int i = 0; i < document.Pages.Count; i++)
                    {
                        if (ReferenceEquals(field.Page, document.Pages[i]))
                        {
                            var pageNum = i + 1;
                            logger?.LogDebug($"Field '{field.Name}' found on page {pageNum} via Page property");
                            return pageNum;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogDebug($"Could not use Page property for field '{field.Name}': {ex.Message}");
            }

            // Enhanced fallback: Check each page for fields and determine which page this field belongs to
            // by analyzing the field bounds and page geometry
            float fieldY = 0;
            try
            {
                if (field is PdfLoadedTextBoxField textField)
                {
                    fieldY = textField.Bounds.Y;
                }
                else if (field is PdfLoadedCheckBoxField checkField)
                {
                    fieldY = checkField.Bounds.Y;
                }
                else if (field is PdfLoadedRadioButtonListField radioField && radioField.Items?.Count > 0)
                {
                    fieldY = radioField.Items[0].Bounds.Y;
                }
                else if (field is PdfLoadedSignatureField sigField)
                {
                    fieldY = sigField.Bounds.Y;
                }
                else if (field is PdfLoadedComboBoxField comboField)
                {
                    fieldY = comboField.Bounds.Y;
                }

                logger?.LogDebug($"Field '{field.Name}' has Y coordinate: {fieldY}");

                // For multi-page documents with page-relative coordinates:
                // - Page 1 fields: Y usually ranges from 0-792 (most values > 100)
                // - Page 2 fields: Y usually ranges from 0-792 but are typically smaller values (often < 100)
                // This is because page 2 fields start from the top of page 2
                if (document.Pages.Count > 1)
                {
                    // If Y coordinate is very small (< 100) and we have multiple pages,
                    // it's likely a page 2+ field
                    if (fieldY < 100 && fieldY >= 0)
                    {
                        // Determine which page based on Y coordinate ranges
                        // This is a heuristic but works for most multi-page forms
                        int estimatedPage = 2;
                        logger?.LogInformation($"Field '{field.Name}' with Y={fieldY} estimated to be on page {estimatedPage} (small Y coordinate)");
                        return Math.Min(estimatedPage, document.Pages.Count);
                    }
                    else
                    {
                        // Larger Y coordinates are typically page 1
                        logger?.LogInformation($"Field '{field.Name}' with Y={fieldY} estimated to be on page 1 (large Y coordinate)");
                        return 1;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogDebug($"Could not determine coordinates for field '{field.Name}': {ex.Message}");
            }

            logger?.LogWarning($"Could not determine page for field '{field.Name}', defaulting to page 1");
            return 1;
        }

        /// <summary>
        /// Get the actual page height for a specific page
        /// </summary>
        public static float GetPageHeight(PdfLoadedDocument document, int pageNumber)
        {
            if (document?.Pages == null || pageNumber < 1 || pageNumber > document.Pages.Count)
            {
                return STANDARD_PAGE_HEIGHT;
            }

            return document.Pages[pageNumber - 1].Size.Height;
        }

        /// <summary>
        /// Validate and correct field bounds to ensure they're within reasonable limits
        /// </summary>
        public static (float x, float y, float width, float height) ValidateFieldBounds(
            float x, float y, float width, float height, string fieldType = "text", ILogger logger = null)
        {
            var correctedX = Math.Max(0, x);
            var correctedY = Math.Max(0, y);
            var correctedWidth = width;
            var correctedHeight = height;

            // Apply field type specific corrections
            switch (fieldType?.ToLower())
            {
                case "checkbox":
                case "radio":
                    // Checkboxes should be square and reasonable size
                    if (correctedWidth > correctedHeight * 2 || correctedWidth < 15 || correctedHeight < 15)
                    {
                        correctedWidth = 20;
                        correctedHeight = 20;
                        logger?.LogInformation($"Corrected {fieldType} dimensions to {correctedWidth}x{correctedHeight}");
                    }
                    break;

                default:
                    // Text fields need minimum dimensions
                    if (correctedWidth < 50)
                    {
                        correctedWidth = 150;
                        logger?.LogInformation($"Corrected field width from {width} to {correctedWidth}");
                    }
                    if (correctedHeight < 15)
                    {
                        correctedHeight = 20;
                        logger?.LogInformation($"Corrected field height from {height} to {correctedHeight}");
                    }
                    break;
            }

            return (correctedX, correctedY, correctedWidth, correctedHeight);
        }
    }
}