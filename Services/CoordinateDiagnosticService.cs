using System;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models;

namespace AccessFormServer.Services
{
    /// <summary>
    /// Diagnostic service for tracking and debugging coordinate conversions
    /// Helps identify issues with field placement by logging all coordinate transformations
    /// </summary>
    public class CoordinateDiagnosticService
    {
        private readonly ILogger<CoordinateDiagnosticService> _logger;

        public CoordinateDiagnosticService(ILogger<CoordinateDiagnosticService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Log field placement with full coordinate details
        /// </summary>
        public void LogFieldPlacement(string fieldName, float x, float y, float width, float height,
            float pageHeight, int pageNumber, string source)
        {
            // Calculate display coordinates (top-left origin)
            var displayY = pageHeight - y - height;

            _logger.LogInformation($"[COORD-DIAG] Field Placement");
            _logger.LogInformation($"  Field: {fieldName}");
            _logger.LogInformation($"  Source: {source}");
            _logger.LogInformation($"  Page: {pageNumber} (Height: {pageHeight}pt)");
            _logger.LogInformation($"  PDF Coords (bottom-left): X={x:F2}, Y={y:F2}, W={width:F2}, H={height:F2}");
            _logger.LogInformation($"  Display Coords (top-left): X={x:F2}, Y={displayY:F2}");
            _logger.LogInformation($"  Position: {GetPositionDescription(y, pageHeight)}");

            // Validate bounds
            if (x < 0 || y < 0 || x + width > 612 || y + height > pageHeight)
            {
                _logger.LogWarning($"  ⚠️ WARNING: Field extends outside page bounds!");
            }
        }

        /// <summary>
        /// Log a coordinate conversion operation
        /// </summary>
        public void LogCoordinateConversion(string operation, string fromSystem, string toSystem,
            float inputX, float inputY, float outputX, float outputY, float pageHeight)
        {
            _logger.LogInformation($"[COORD-CONVERT] {operation}");
            _logger.LogInformation($"  From: {fromSystem} ({inputX:F2}, {inputY:F2})");
            _logger.LogInformation($"  To: {toSystem} ({outputX:F2}, {outputY:F2})");
            _logger.LogInformation($"  Page Height: {pageHeight:F2}pt");
            _logger.LogInformation($"  Delta: ΔX={outputX - inputX:F2}, ΔY={outputY - inputY:F2}");
        }

        /// <summary>
        /// Log field drag operation
        /// </summary>
        public void LogFieldDrag(string fieldName, float startX, float startY, float endX, float endY,
            float deltaX, float deltaY, string coordinateSystem)
        {
            _logger.LogInformation($"[COORD-DRAG] Field: {fieldName}");
            _logger.LogInformation($"  System: {coordinateSystem}");
            _logger.LogInformation($"  Start: ({startX:F2}, {startY:F2})");
            _logger.LogInformation($"  End: ({endX:F2}, {endY:F2})");
            _logger.LogInformation($"  Delta: ({deltaX:F2}, {deltaY:F2})");

            // Check if drag direction seems inverted
            if ((deltaY > 0 && endY < startY) || (deltaY < 0 && endY > startY))
            {
                _logger.LogWarning($"  ⚠️ WARNING: Y-axis movement may be inverted!");
            }
        }

        /// <summary>
        /// Validate UniversalFieldCoordinates and log any issues
        /// </summary>
        public bool ValidateAndLogCoordinates(UniversalFieldCoordinates coords, string fieldName)
        {
            _logger.LogInformation($"[COORD-VALIDATE] Field: {fieldName}");

            bool isValid = coords.IsValid(_logger);

            if (isValid)
            {
                _logger.LogInformation($"  ✓ Coordinates valid: {coords}");
            }
            else
            {
                _logger.LogError($"  ✗ Coordinates INVALID: {coords}");
            }

            // Log all representations for debugging
            var (pdfX, pdfY, pdfW, pdfH) = (coords.PdfX, coords.PdfY, coords.PdfWidth, coords.PdfHeight);
            var (displayX, displayY, displayW, displayH) = coords.ToDisplayCoordinates();
            var (pctX, pctY, pctW, pctH) = coords.ToPercentageCoordinates();

            _logger.LogInformation($"  PDF: ({pdfX:F2}, {pdfY:F2}, {pdfW:F2}×{pdfH:F2})");
            _logger.LogInformation($"  Display: ({displayX:F2}, {displayY:F2}, {displayW:F2}×{displayH:F2})");
            _logger.LogInformation($"  Percent: ({pctX:F2}%, {pctY:F2}%, {pctW:F2}%×{pctH:F2}%)");

            return isValid;
        }

        /// <summary>
        /// Compare two sets of coordinates and log differences
        /// </summary>
        public void CompareCoordinates(string label1, float x1, float y1, string label2, float x2, float y2)
        {
            var deltaX = x2 - x1;
            var deltaY = y2 - y1;
            var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

            _logger.LogInformation($"[COORD-COMPARE]");
            _logger.LogInformation($"  {label1}: ({x1:F2}, {y1:F2})");
            _logger.LogInformation($"  {label2}: ({x2:F2}, {y2:F2})");
            _logger.LogInformation($"  Difference: ΔX={deltaX:F2}, ΔY={deltaY:F2}, Distance={distance:F2}pt");

            if (distance > 50)
            {
                _logger.LogWarning($"  ⚠️ WARNING: Large coordinate difference detected!");
            }
        }

        /// <summary>
        /// Log scale factor calculations
        /// </summary>
        public void LogScaleFactors(float pdfDpi, float displayDpi)
        {
            var scaleFactor = displayDpi / pdfDpi;
            _logger.LogInformation($"[COORD-SCALE]");
            _logger.LogInformation($"  PDF DPI: {pdfDpi}");
            _logger.LogInformation($"  Display DPI: {displayDpi}");
            _logger.LogInformation($"  Scale Factor: {scaleFactor:F6}");
            _logger.LogInformation($"  Example: 100pt PDF = {100 * scaleFactor:F2}px Display");
        }

        /// <summary>
        /// Log Claude Vision percentage-based coordinates
        /// </summary>
        public void LogClaudeVisionCoordinates(float xPct, float yPct, float wPct, float hPct,
            float pageWidth, float pageHeight)
        {
            var x = (xPct / 100f) * pageWidth;
            var y = (yPct / 100f) * pageHeight;
            var w = (wPct / 100f) * pageWidth;
            var h = (hPct / 100f) * pageHeight;

            // Claude uses top-left origin
            var pdfY = pageHeight - y - h;

            _logger.LogInformation($"[COORD-CLAUDE]");
            _logger.LogInformation($"  Input (top-left %): X={xPct:F2}%, Y={yPct:F2}%, W={wPct:F2}%, H={hPct:F2}%");
            _logger.LogInformation($"  Page Size: {pageWidth:F2}×{pageHeight:F2}pt");
            _logger.LogInformation($"  Display (top-left): X={x:F2}, Y={y:F2}, W={w:F2}, H={h:F2}");
            _logger.LogInformation($"  PDF (bottom-left): X={x:F2}, Y={pdfY:F2}, W={w:F2}, H={h:F2}");
        }

        /// <summary>
        /// Get human-readable position description
        /// </summary>
        private string GetPositionDescription(float pdfY, float pageHeight)
        {
            var fromBottom = pdfY;
            var fromTop = pageHeight - pdfY;

            if (fromBottom < 100)
                return $"Near bottom (Y={pdfY:F0}pt from bottom)";
            else if (fromTop < 100)
                return $"Near top (Y={pdfY:F0}pt from bottom, {fromTop:F0}pt from top)";
            else
                return $"Middle (Y={pdfY:F0}pt from bottom, {fromTop:F0}pt from top)";
        }

        /// <summary>
        /// Create a detailed diagnostic report for a field
        /// </summary>
        public string CreateFieldDiagnosticReport(string fieldName, float x, float y, float width, float height,
            float pageWidth, float pageHeight, int pageNumber, string source)
        {
            var displayY = pageHeight - y - height;
            var pctX = (x / pageWidth) * 100;
            var pctY = (y / pageHeight) * 100;
            var displayPctY = (displayY / pageHeight) * 100;

            return $@"
═══════════════════════════════════════════════════════════
FIELD COORDINATE DIAGNOSTIC REPORT
═══════════════════════════════════════════════════════════
Field Name: {fieldName}
Source: {source}
Page: {pageNumber} ({pageWidth:F0}×{pageHeight:F0}pt)

PDF COORDINATES (Bottom-Left Origin):
  Position: ({x:F2}, {y:F2})
  Size: {width:F2} × {height:F2}
  Percentages: {pctX:F1}% from left, {pctY:F1}% from bottom

DISPLAY COORDINATES (Top-Left Origin):
  Position: ({x:F2}, {displayY:F2})
  Size: {width:F2} × {height:F2}
  Percentages: {pctX:F1}% from left, {displayPctY:F1}% from top

POSITION: {GetPositionDescription(y, pageHeight)}

VALIDATION:
  {(x >= 0 && x + width <= pageWidth ? "✓" : "✗")} Horizontal bounds OK
  {(y >= 0 && y + height <= pageHeight ? "✓" : "✗")} Vertical bounds OK
  {(width > 0 && height > 0 ? "✓" : "✗")} Positive dimensions
═══════════════════════════════════════════════════════════";
        }
    }
}
