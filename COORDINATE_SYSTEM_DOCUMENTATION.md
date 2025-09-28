# AccessForm Coordinate System Documentation

## Overview
This document defines the standard coordinate system used throughout the AccessForm application and how coordinates are transformed between different contexts.

## Coordinate Systems in Use

### 1. PDF Native Coordinates (Bottom-Left Origin)
- **Origin**: Bottom-left corner of the page (0, 0)
- **Y-Direction**: Increases UPWARD (bottom to top)
- **X-Direction**: Increases rightward (left to right)
- **Units**: Points (1 point = 1/72 inch)
- **Used by**: Syncfusion PDF library, PDF specifications
- **Example**: A field at Y=82 is near the BOTTOM of the page

### 2. Display/Screen Coordinates (Top-Left Origin)
- **Origin**: Top-left corner of the page (0, 0)
- **Y-Direction**: Increases DOWNWARD (top to bottom)
- **X-Direction**: Increases rightward (left to right)
- **Units**: Pixels (varies with DPI)
- **Used by**: HTML Canvas, Browser rendering, Image display
- **Example**: A field at Y=82 is near the TOP of the page

### 3. Internal Storage Format
- **Standard**: We store all coordinates in PDF native format (bottom-left origin)
- **Reason**: This matches the PDF specification and Syncfusion's expectations
- **Conversions**: Applied only at display/rendering time

## Conversion Formulas

### PDF to Display (for rendering on screen)
```csharp
displayX = pdfX * scaleFactor
displayY = (pageHeight - pdfY) * scaleFactor  // Invert Y coordinate
```

### Display to PDF (for click detection, user input)
```csharp
pdfX = displayX / scaleFactor
pdfY = pageHeight - (displayY / scaleFactor)  // Invert Y coordinate
```

### Scale Factor Calculation
```csharp
scaleFactor = displayDPI / 72.0  // 72 points per inch
// Typically: 150 DPI / 72 = 2.083x scale
```

## Component-Specific Implementations

### TagModificationModal.razor
- **Storage**: PDF coordinates (bottom-left)
- **Display**: Must convert to top-left for rendering
- **Issue**: Currently showing Y=82 as "near top" when it should be "near bottom"
- **Fix Required**: Invert Y coordinate when displaying/editing

### PdfCoordinateConverter.cs
- **Purpose**: Central utility for all coordinate conversions
- **Methods**:
  - `PdfToDisplay()`: Converts PDF coords to screen coords
  - `DisplayToPdf()`: Converts screen coords to PDF coords
  - `ScaleDimensions()`: Scales width/height based on DPI

### Field Detection Services

#### Syncfusion Field Detection
- **Output**: PDF native coordinates (bottom-left)
- **Storage**: Direct storage, no conversion needed

#### Claude Vision Detection
- **Output**: Percentage-based (0-100% of page dimensions)
- **Conversion**: Convert to PDF points, maintaining bottom-left origin
```csharp
pdfX = (percentX / 100) * pageWidth
pdfY = (percentY / 100) * pageHeight  // Already bottom-left based
```

#### Google Document AI
- **Output**: Normalized coordinates (0.0 to 1.0)
- **Conversion**: Similar to Claude Vision but using decimal fractions

### /api/pdf-page-with-field-boxes Endpoint
- **Input**: PDF coordinates from EditableFields
- **Processing**: Converts to display coordinates for image rendering
- **Current Bug**: Not properly inverting Y coordinate

## Testing Checklist

- [ ] Field at top of page has high Y value in PDF coords (e.g., Y=700 for 792pt page)
- [ ] Field at bottom of page has low Y value in PDF coords (e.g., Y=82)
- [ ] Click detection works correctly (clicks on visual field select correct field)
- [ ] Field navigation scrolls to correct position
- [ ] Field boxes render at correct positions
- [ ] Coordinate display in UI matches actual position

## Common Pitfalls

1. **Forgetting Y-axis inversion**: Most common error, causes fields to appear flipped vertically
2. **Mixed coordinate systems**: Storing display coords when PDF coords expected
3. **Scale factor errors**: Not accounting for DPI differences
4. **Page height variations**: Letter (792pt) vs A4 (842pt) have different heights
5. **Multi-page documents**: Y coordinate must account for page boundaries

## Debug Logging

Add these log statements to verify coordinate conversions:
```csharp
logger.LogInformation($"[COORD] PDF: ({pdfX}, {pdfY}) -> Display: ({displayX}, {displayY})");
logger.LogInformation($"[COORD] Page height: {pageHeight}pt, Scale: {scaleFactor}");
```

## Quick Reference

| Context | Origin | Y-Direction | Typical Y for "top" field | Typical Y for "bottom" field |
|---------|--------|-------------|---------------------------|------------------------------|
| PDF | Bottom-Left | Up ↑ | 700-750 | 50-100 |
| Display | Top-Left | Down ↓ | 50-100 | 700-750 |

## Implementation Status

- ✅ PdfCoordinateConverter utility exists
- ❌ TagModificationModal not inverting Y properly
- ❌ Field navigation using wrong coordinate system
- ⚠️ Inconsistent usage across services

Last Updated: 2024-01-28