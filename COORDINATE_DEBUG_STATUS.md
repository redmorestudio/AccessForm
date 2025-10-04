# PDF Field Coordinate Debugging - Current Status

## Problem Statement

When uploading **existing PDFs** (not Word→PDF conversions) and opening the tag editor modal, all form fields appear bunched together at the bottom-left corner at coordinates (0,0) instead of overlaying their actual positions in the PDF.

**Important Context**: This issue ONLY affects existing PDFs. Word→PDF conversion works perfectly fine with correct field positioning.

## Root Causes Identified and Fixed

### 1. Missing Text Field Handler (FIXED)
**Location**: `/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/Program.cs` lines 3487-3516

**Problem**: The `/api/extract-tag-structure` endpoint had an if/else chain handling checkboxes, signatures, radio buttons, and combo boxes, but was **missing** the `PdfLoadedTextBoxField` case. This caused all text fields to fall through to the `else` block which set default coordinates of (0, 0).

**Fix**: Added comprehensive text field handling:
```csharp
else if (f is PdfLoadedTextBoxField loadedTextField)
{
    // Get page number
    if (loadedTextField.Page != null)
    {
        for (int i = 0; i < pdfDoc.Pages.Count; i++)
        {
            if (pdfDoc.Pages[i] == loadedTextField.Page)
            {
                page = i + 1;
                break;
            }
        }
    }

    // Get the correct page height for this specific page
    if (page > 0 && page <= pdfDoc.Pages.Count)
    {
        pageHeight = pdfDoc.Pages[page - 1].Size.Height;
    }

    x = loadedTextField.Bounds.X;
    y = loadedTextField.Bounds.Y;
    width = loadedTextField.Bounds.Width;
    height = loadedTextField.Bounds.Height;

    logger.LogInformation($"[TEXTFIELD] '{f.Name}' on page {page} at X={x}, Y={y}, W={width}, H={height}");
}
```

### 2. JSON Property Name Case Sensitivity (FIXED)
**Location**: `/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/Program.cs` lines 3728-3735

**Problem**: The anonymous object created for JSON serialization used lowercase property names (`x`, `y`, `width`, `height`), but the C# `EditableField` model expected PascalCase (`X`, `Y`, `Width`, `Height`). This caused silent deserialization failures, defaulting all values to 0.

**Fix**: Changed all properties to PascalCase:
```csharp
var formFields = fieldList.Select((field, index) => new
{
    Name = GenerateHumanReadableName(field, index, fieldList.Count),
    TabOrder = index + 1,
    OriginalName = field.originalName,
    Type = field.type,
    Tooltip = field.tooltip,
    Page = field.page,
    X = field.x,        // Was: x
    Y = field.y,        // Was: y
    Width = field.width,    // Was: width
    Height = field.height   // Was: height
}).ToList();
```

### 3. Coordinate System Discovery (CRITICAL INSIGHT)
**Location**: `/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/Pages/TagModificationModal.razor` lines 1531-1543

**Discovery**: Syncfusion's `Bounds` property uses **TOP-LEFT origin** coordinate system, NOT the PDF specification's standard **bottom-left origin** coordinate system.

**Previous Wrong Assumption**: We initially tried converting from bottom-left to top-left, but this was backwards because Syncfusion already provides top-left coordinates.

**Final Fix**: Remove coordinate conversion entirely, only apply DPI scaling:
```csharp
private string GetFieldOverlayStyle(EditableField field)
{
    const float SCALE_FACTOR = 150.0f / 72.0f; // 150 DPI display / 72 DPI PDF

    // IMPORTANT: Syncfusion Bounds uses TOP-LEFT origin (not PDF's standard bottom-left)
    // No coordinate conversion needed - just scale from 72 DPI to 150 DPI
    var displayX = field.X * SCALE_FACTOR;
    var displayY = field.Y * SCALE_FACTOR;  // NO conversion - use as-is
    var displayWidth = field.Width * SCALE_FACTOR;
    var displayHeight = field.Height * SCALE_FACTOR;

    return $"left: {displayX}px; top: {displayY}px; width: {displayWidth}px; height: {displayHeight}px;";
}
```

## Key Technical Concepts

### Coordinate Systems
- **PDF Specification Standard**: Bottom-left origin (0,0) at lower-left corner
- **Syncfusion Bounds Property**: TOP-LEFT origin (0,0) at upper-left corner (non-standard!)
- **HTML/CSS**: Top-left origin (0,0) at upper-left corner

### DPI Scaling
- **PDF Points**: 72 DPI
- **Display**: 150 DPI
- **Scale Factor**: 150/72 = 2.0833...

### Field Type Handling in Syncfusion
The base `PdfLoadedField` class doesn't expose the `Bounds` property. You must cast to specific types:
- `PdfLoadedTextBoxField` - text input fields
- `PdfLoadedCheckBoxField` - checkboxes
- `PdfLoadedRadioButtonListField` - radio button groups
- `PdfLoadedComboBoxField` - dropdown lists
- `PdfLoadedSignatureField` - signature fields
- `PdfLoadedListBoxField` - list boxes

## Current Testing Status

**Last User Observation**: After the final coordinate fix, fields should now render in correct positions. User needs to:
1. Hard-refresh browser (Cmd+Shift+R)
2. Upload the existing PDF
3. Verify field overlay boxes appear in correct positions

## Files Modified

### 1. Program.cs
- Added missing `PdfLoadedTextBoxField` case (lines 3487-3516)
- Fixed JSON property names to PascalCase (lines 3728-3735)
- Added comprehensive logging throughout

### 2. TagModificationModal.razor
- Fixed `GetFieldOverlayStyle()` to remove incorrect coordinate conversion (lines 1531-1543)
- Only applies DPI scaling now

### 3. PdfPreservationService.cs
- Added `using Syncfusion.Drawing;` for RectangleF type
- Modified `GetExistingFields()` to cast to specific field types for Bounds access
- Added logging for detected field coordinates

## Debugging Logs to Watch

When testing, look for these log patterns:

```
[TEXTFIELD] 'FieldName' on page 1 at X=689.211, Y=328.853, W=200.5, H=25.0
```

If you see:
```
[PAGE FIX] Unknown field type 'PdfLoadedTextBoxField' - using defaults
Pos: (0.00,0.00), Size: 100.00x20.00
```

This indicates the text field case is not being hit (should not happen with current code).

## What Works vs What Doesn't

### ✅ Working (Confirmed)
- Word→PDF conversion with correct field positioning
- Reading field coordinates from existing PDFs (coordinates appear in UI)
- JSON serialization/deserialization of coordinates

### ❓ Testing Required
- Field overlay rendering in correct positions for existing PDFs (latest fix just applied)

## Next Steps if Issue Persists

If fields still appear in wrong positions after hard refresh:

1. **Check Browser Console** - Look for JavaScript errors
2. **Check Server Logs** - Verify coordinates are being read correctly
3. **Inspect Element** - Check the actual CSS `style` attribute on field overlay divs
4. **Test with Multiple PDFs** - Verify it's not a PDF-specific issue
5. **Compare Coordinates** - Use PDF viewer to get expected coordinates vs what we're rendering

## Additional Context

### Why This Only Affects Existing PDFs
Word→PDF conversion creates fields using a different code path that may use different coordinate systems or API calls. The existing PDF path specifically uses Syncfusion's `PdfLoadedDocument` and `PdfLoadedField` APIs which have the top-left coordinate peculiarity.

### Accessibility Pipeline
User also requested full accessibility remediation (font replacement, ZapfDingbats→Unicode conversion) which is handled by:
- `AccessibilityService.MakeAccessible()`
- `AccessibilityRetrofitService.RetrofitAccessibility()`
- `PdfAccessibilityEnhancer.EnhanceAccessibility()`

This was implemented in `PdfPreservationService.ProcessExistingPdfAsync()` but is tangential to the coordinate issue.

## Development Environment

- **Platform**: macOS (Darwin 25.0.0)
- **Server**: ASP.NET Core with Blazor
- **PDF Libraries**: Syncfusion PDF, PyMuPDF (for some operations)
- **Current Branch**: feature/cherry-pick-improvements
- **Server URL**: http://localhost:5001

## Quick Reference: Variable Names Used

Due to scope conflicts in Program.cs, we settled on these variable names:
- `loadedTextField` - for PdfLoadedTextBoxField (line 3487+)
- Other field types use their own unique names to avoid conflicts
