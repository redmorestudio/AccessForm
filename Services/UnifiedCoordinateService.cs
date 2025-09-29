using System;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.Coordinates;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Centralized service for all coordinate conversions - the SINGLE source of truth
    /// Ensures no contamination between different detection modes
    /// </summary>
    public class UnifiedCoordinateService
    {
        private readonly ILogger<UnifiedCoordinateService> _logger;

        // Standard page dimensions
        private const float PDF_DPI = 72f;
        private const float DISPLAY_DPI = 150f;

        public UnifiedCoordinateService(ILogger<UnifiedCoordinateService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Convert percentage coordinates (0-100, top-left) to PDF coordinates (points, bottom-left)
        /// Used for Claude Vision results
        /// </summary>
        public PdfCoordinate ConvertPercentageToPdf(PercentageCoordinate percent, float pageWidth, float pageHeight)
        {
            _logger.LogDebug($"Converting percentage to PDF: {percent}");
            _logger.LogDebug($"   Page dimensions: {pageWidth}x{pageHeight} PDF points");

            // Step 1: Convert percentages to PDF points (still top-left origin)
            float xPdf = (percent.X / 100f) * pageWidth;
            float yPdfTopLeft = (percent.Y / 100f) * pageHeight;
            float widthPdf = (percent.Width / 100f) * pageWidth;
            float heightPdf = (percent.Height / 100f) * pageHeight;

            _logger.LogDebug($"   Step 1 - To PDF points (top-left): X={xPdf:F1}, Y={yPdfTopLeft:F1}, W={widthPdf:F1}, H={heightPdf:F1}");

            // Step 2: Flip Y-axis from top-left to bottom-left
            float yPdfBottomLeft = pageHeight - yPdfTopLeft - heightPdf;

            _logger.LogDebug($"   Step 2 - Flip to bottom-left: Y={yPdfBottomLeft:F1} (was {yPdfTopLeft:F1})");

            var result = new PdfCoordinate(xPdf, yPdfBottomLeft, widthPdf, heightPdf);
            _logger.LogDebug($"   Result: {result}");

            return result;
        }

        /// <summary>
        /// Convert PDF coordinates (points, bottom-left) to display coordinates (pixels, top-left)
        /// Used for UI rendering
        /// </summary>
        public DisplayCoordinate ConvertPdfToDisplay(PdfCoordinate pdf, float pageHeight)
        {
            _logger.LogDebug($"Converting PDF to display: {pdf}");

            // Step 1: Scale from 72 DPI to 150 DPI
            float scaleFactor = DISPLAY_DPI / PDF_DPI;
            float xDisplay = pdf.X * scaleFactor;
            float widthDisplay = pdf.Width * scaleFactor;
            float heightDisplay = pdf.Height * scaleFactor;

            // Step 2: Flip Y-axis from bottom-left to top-left
            // First scale the page height, then flip
            float pageHeightDisplay = pageHeight * scaleFactor;
            float yBottomDisplay = pdf.Y * scaleFactor;
            float yTopDisplay = pageHeightDisplay - yBottomDisplay - heightDisplay;

            _logger.LogDebug($"   Scale factor: {scaleFactor:F2}x (72→150 DPI)");
            _logger.LogDebug($"   Page height: {pageHeight:F1} PDF → {pageHeightDisplay:F1} display");
            _logger.LogDebug($"   Y-flip: {yBottomDisplay:F1} bottom → {yTopDisplay:F1} top");

            var result = new DisplayCoordinate(xDisplay, yTopDisplay, widthDisplay, heightDisplay);
            _logger.LogDebug($"   Result: {result}");

            return result;
        }

        /// <summary>
        /// Convert display coordinates (pixels, top-left) to PDF coordinates (points, bottom-left)
        /// Used when processing UI interactions
        /// </summary>
        public PdfCoordinate ConvertDisplayToPdf(DisplayCoordinate display, float pageHeight)
        {
            _logger.LogDebug($"Converting display to PDF: {display}");

            // Step 1: Scale from 150 DPI to 72 DPI
            float scaleFactor = PDF_DPI / DISPLAY_DPI;
            float xPdf = display.X * scaleFactor;
            float widthPdf = display.Width * scaleFactor;
            float heightPdf = display.Height * scaleFactor;

            // Step 2: Flip Y-axis from top-left to bottom-left
            float pageHeightPdf = pageHeight; // Already in PDF points
            float yTopPdf = display.Y * scaleFactor;
            float yBottomPdf = pageHeightPdf - yTopPdf - heightPdf;

            _logger.LogDebug($"   Scale factor: {scaleFactor:F2}x (150→72 DPI)");
            _logger.LogDebug($"   Y-flip: {yTopPdf:F1} top → {yBottomPdf:F1} bottom");

            var result = new PdfCoordinate(xPdf, yBottomPdf, widthPdf, heightPdf);
            _logger.LogDebug($"   Result: {result}");

            return result;
        }

        /// <summary>
        /// Convert PDF coordinates to percentage coordinates
        /// Used for sending to Claude Vision API
        /// </summary>
        public PercentageCoordinate ConvertPdfToPercentage(PdfCoordinate pdf, float pageWidth, float pageHeight)
        {
            _logger.LogDebug($"Converting PDF to percentage: {pdf}");
            _logger.LogDebug($"   Page dimensions: {pageWidth}x{pageHeight} PDF points");

            // Step 1: Flip Y-axis from bottom-left to top-left
            float yTopLeft = pageHeight - pdf.Y - pdf.Height;

            // Step 2: Convert to percentages
            float xPercent = (pdf.X / pageWidth) * 100f;
            float yPercent = (yTopLeft / pageHeight) * 100f;
            float widthPercent = (pdf.Width / pageWidth) * 100f;
            float heightPercent = (pdf.Height / pageHeight) * 100f;

            _logger.LogDebug($"   Y-flip: {pdf.Y:F1} bottom → {yTopLeft:F1} top");
            _logger.LogDebug($"   Percentages: X={xPercent:F1}%, Y={yPercent:F1}%, W={widthPercent:F1}%, H={heightPercent:F1}%");

            var result = new PercentageCoordinate(xPercent, yPercent, widthPercent, heightPercent);
            _logger.LogDebug($"   Result: {result}");

            return result;
        }

        /// <summary>
        /// Validate and normalize Syncfusion coordinates (already in PDF format)
        /// </summary>
        public PdfCoordinate NormalizeSyncfusionCoordinate(SyncfusionCoordinate syncfusion)
        {
            _logger.LogDebug($"Normalizing Syncfusion coordinate: {syncfusion}");

            // Syncfusion coordinates are already in PDF format (bottom-left, 72 DPI)
            // Just convert to standard PdfCoordinate type
            var result = new PdfCoordinate(syncfusion.X, syncfusion.Y, syncfusion.Width, syncfusion.Height);

            _logger.LogDebug($"   Normalized: {result}");

            return result;
        }

        /// <summary>
        /// Convert raw coordinates to PDF based on source type
        /// This is the MAIN entry point for all field detection results
        /// </summary>
        public PdfCoordinate ConvertToCanonicalPdf(
            float x, float y, float width, float height,
            CoordinateSource source,
            float pageWidth, float pageHeight)
        {
            _logger.LogInformation($"Converting to canonical PDF from {source}");
            _logger.LogInformation($"   Raw input: X={x:F1}, Y={y:F1}, W={width:F1}, H={height:F1}");
            _logger.LogInformation($"   Page: {pageWidth:F1}x{pageHeight:F1}");

            PdfCoordinate result;

            switch (source)
            {
                case CoordinateSource.Syncfusion:
                    // Syncfusion already provides PDF coordinates
                    var syncCoord = new SyncfusionCoordinate(x, y, width, height);
                    result = NormalizeSyncfusionCoordinate(syncCoord);
                    _logger.LogInformation($"   Syncfusion -> PDF: {result}");
                    break;

                case CoordinateSource.ClaudeVision:
                    // Claude Vision provides percentage coordinates
                    var percentCoord = new PercentageCoordinate(x, y, width, height);
                    result = ConvertPercentageToPdf(percentCoord, pageWidth, pageHeight);
                    _logger.LogInformation($"   Claude Vision -> PDF: {result}");
                    break;

                case CoordinateSource.Display:
                    // Display coordinates from UI
                    var displayCoord = new DisplayCoordinate(x, y, width, height);
                    result = ConvertDisplayToPdf(displayCoord, pageHeight);
                    _logger.LogInformation($"   Display -> PDF: {result}");
                    break;

                default:
                    throw new ArgumentException($"Unknown coordinate source: {source}");
            }

            // Validate result is within page bounds
            if (!result.IsWithinPage(pageWidth, pageHeight))
            {
                _logger.LogWarning($"   WARNING: Result outside page bounds! Page: {pageWidth:F1}x{pageHeight:F1}, Field: {result}");
            }

            return result;
        }

        /// <summary>
        /// Log detailed coordinate diagnostics for debugging
        /// </summary>
        public void LogCoordinateDiagnostics(string context, PdfCoordinate coord, float pageWidth, float pageHeight)
        {
            _logger.LogInformation($"COORDINATE DIAGNOSTICS - {context}");
            _logger.LogInformation($"   PDF (canonical): {coord}");

            var display = ConvertPdfToDisplay(coord, pageHeight);
            _logger.LogInformation($"   Display (UI): {display}");

            var percent = ConvertPdfToPercentage(coord, pageWidth, pageHeight);
            _logger.LogInformation($"   Percentage: {percent}");

            _logger.LogInformation($"   Within bounds: {coord.IsWithinPage(pageWidth, pageHeight)}");
            _logger.LogInformation($"   Page: {pageWidth:F1}x{pageHeight:F1} PDF points");
        }
    }

    /// <summary>
    /// Enum to identify the source of coordinates
    /// </summary>
    public enum CoordinateSource
    {
        Syncfusion,
        ClaudeVision,
        Display,
        Unknown
    }
}