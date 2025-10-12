# PDF Input Field View Problems

## Current Issue
The PDF form fields are displaying with Y-coordinates mirrored/flipped vertically in the tag editor view. Fields that should appear at the top of the page are showing at the bottom, and vice versa.

## Root Cause Discovery
After extensive investigation, I found that fields are being rendered in TWO different ways:

1. **Client-side HTML overlays** - Using `GetFieldOverlayStyle()` in TagModificationModal.razor
2. **Server-side image drawing** - Using `/api/pdf-page-with-field-boxes` endpoint that draws fields directly on the PDF image

The actual rendering path being used is the **server-side drawing** when `ShowFieldBoxes` is true (which it is by default).

## What I've Tried

### 1. Client-Side Fixes (Wrong Path)
- Modified `GetFieldOverlayStyle()` in TagModificationModal.razor to flip Y-axis
- Added PageWidth/PageHeight properties to EditableField class
- Result: **No effect** because this code isn't being executed when fields are drawn server-side

### 2. Server-Side Fix (Right Path, Wrong Direction)
- Found the actual rendering code in `/api/pdf-page-with-field-boxes` endpoint in Program.cs
- Added Y-axis flip: `y = (pageHeight - y - height) * scaleFactor`
- Result: **Made it worse** - fields are now perfectly mirrored (exactly opposite of what we want)

### 3. Current State
- Just removed the Y-axis flip from server-side code
- Fields should now render with just DPI scaling: `y = y * scaleFactor`
- Not yet tested

## The Actual Coordinate Systems

### PDF (Syncfusion)
- Origin: Bottom-left (0,0)
- Y increases upward
- Units: Points (72 DPI)

### Display (Browser/Image)
- Origin: Top-left (0,0)
- Y increases downward
- Units: Pixels (150 DPI for rendered image)

## The Real Problem
The coordinates coming from PdfPreservationService are in PDF coordinates (bottom-left origin), but somewhere in the pipeline they're being treated as if they're already in top-left origin coordinates, causing the flip.

## Next Steps to Try

1. **Verify the actual coordinate system** of the data coming from PdfPreservationService
   - Log the raw coordinates for a known field (like "Applicant Name" which we know is near the top)
   - Compare with what we see visually

2. **Test without any transformation**
   - Already done - just using `y = y * scaleFactor`
   - Need to see if this fixes the mirroring

3. **Check if there's ANOTHER transformation happening**
   - The coordinates might be getting pre-transformed somewhere else
   - Need to trace from PdfPreservationService → sessionStorage → TagModificationModal → server endpoint

## Other Issues Found

### CSS Color Coding Not Working
- Field type classes (field-checkbox, field-text, etc.) are being applied
- But colors aren't showing because base .field-overlay class has hardcoded colors
- **Status**: Partially fixed but needs testing

### Checkbox Sizes
- Should be 15x15 pixels (7.2x7.2 PDF points)
- Were being overridden to 20x20 in multiple places
- **Status**: Fixed in PdfPreservationService

### Accept Changes Button Not Working
- **Status**: Not investigated yet

## The REAL Important Problems
You mentioned we should focus on more important problems. The field positioning is blocking the tag editor from being usable, but if there are more critical issues, we should prioritize those instead.

## Current Files Modified
- `/Program.cs` - Lines 2925-2927 (server-side field drawing)
- `/Pages/TagModificationModal.razor` - Lines 1625-1627 (client-side positioning - not actually used)
- `/Services/PdfPreservationService.cs` - Checkbox size normalization
- `/Models/FieldDetectionConfig.cs` - Added PageWidth/PageHeight properties