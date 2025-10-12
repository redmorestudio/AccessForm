# Radio Button Field Implementation - Current Status

## Overview
This document describes the current state of radio button field handling in the PDF field editor and what remains to be implemented.

## What's Currently Working

### 1. Radio Button Detection & Storage
- Radio buttons are correctly detected from PDFs by Syncfusion
- Each radio button in a group has:
  - **Same `FieldName`**: Identifies the group (e.g., "PreferredMethodofCommunication")
  - **Unique `ButtonValue`**: Identifies the individual button within the group (e.g., "Mail", "Email")
- Radio button data is stored in the `EditableField` class with a `ButtonValue` property

### 2. Field Coordinate Display
- ✅ **FIXED**: Radio button overlays now display at correct positions
- The Y-coordinate inversion issue has been resolved
- Syncfusion returns coordinates in top-left format (not PDF standard bottom-left)
- Display formula: `topPercent = (field.Y / pageHeight) * 100` (no conversion needed)

### 3. Field Saving with Python Script
- Radio button coordinates are sent to the Python script `pdf_field_recreate.py`
- The `PdfFieldTagEditorService` includes `ButtonValue` in field updates
- See: `PdfFieldTagEditorService.cs` lines 39-52 for `FieldUpdate` class

## What Needs to Be Implemented

### 1. Fix Drag Speed (PRIORITY: HIGH)
**Current Issue**: When dragging fields, movement is at 0.5x speed instead of 1:1
- Moving mouse 100 pixels only moves field 50 pixels
- This is likely due to DPI scaling factor (150/72 = 2.08)

**Location to Fix**: `TagModificationModal.razor` - drag handling code
- Look for mouse movement delta calculations
- Remove or adjust any division by `ImageScale` or `DPI_SCALE`

### 2. Radio Button Group UI (PRIORITY: MEDIUM)
**Current Issue**: Creating radio button groups is cumbersome
- User must manually:
  1. Click "Add Field (Manual)"
  2. Set type to "radio"
  3. Enter the same group name for each button
  4. Enter different button values for each button
  5. Position each button individually

**Desired Improvement**: Add a streamlined workflow
- When adding a radio button, show option: "Add another button to this group"
- Automatically:
  - Copy the group name (FieldName)
  - Prompt only for the new ButtonValue
  - Place the new button near the previous one
  - Maintain consistent sizing

**Suggested UI Location**:
- In the field editor panel when a radio button is selected
- Add a button: "➕ Add Button to Group"
- Or: After adding a radio field, show a confirmation dialog with "Add Another?" option

## Technical Details

### Key Files
1. **Pages/TagModificationModal.razor** (lines 1640-1660)
   - Field overlay positioning
   - Drag handling code (needs drag speed fix)
   - Field editor UI (needs radio button UI enhancement)

2. **Services/PdfFieldTagEditorService.cs** (lines 39-52)
   - `FieldUpdate` class with `ButtonValue` property
   - Handles field modifications and Python script coordination

3. **Models/FieldDetectionConfig.cs** (lines 139-141)
   - `ButtonValue` property documentation
   - Explains radio button grouping mechanism

### Radio Button Grouping Logic
```csharp
// Radio buttons with same FieldName but different ButtonValue form a mutually exclusive group
// Example:
// FieldName: "PreferredMethodofCommunication"
//   - Button 1: ButtonValue = "Mail"
//   - Button 2: ButtonValue = "Email"
// Only one can be selected at a time
```

### Coordinate System (IMPORTANT)
- **Syncfusion coordinates**: Top-left origin (Y=0 at top, Y increases downward)
- **NOT standard PDF**: Bottom-left origin (this is documented but incorrect for Syncfusion)
- **Display formula**: Use Y directly without conversion
- **Page dimensions**: Default 612x792 points (US Letter)

## Testing Checklist
When implementing the remaining features:

### Drag Speed Fix
- [ ] Test dragging a field 100 pixels - it should move 100 pixels (not 50)
- [ ] Verify drag works on different zoom levels
- [ ] Test with both text fields and radio buttons

### Radio Button UI
- [ ] Can easily create a new radio button in existing group
- [ ] New button inherits group name automatically
- [ ] Can specify unique ButtonValue for new button
- [ ] New button appears near previous button
- [ ] Can save and the buttons work as a mutually exclusive group

## Code References

### Drag Handling
Location: `TagModificationModal.razor` around lines 1700-1750
```csharp
[JSInvokable]
public void OnGlobalMouseMove(double clientX, double clientY)
{
    if (isDraggingField && draggedField != null)
    {
        // ISSUE: Division by DPI_SCALE causes 0.5x movement
        // Look for: deltaX / ImageScale or similar
        // FIX: Remove the division or use direct pixel values
    }
}
```

### Field Position Calculation
Location: `TagModificationModal.razor:1645-1657`
```csharp
// CORRECT - DO NOT CHANGE
var leftPercent = (field.X / pageWidth) * 100;
var topPercent = (field.Y / pageHeight) * 100;  // No conversion needed!
```

## Questions for Next Session
1. Should the radio button UI be:
   - A button in the field editor panel?
   - A context menu option?
   - A modal dialog?

2. For positioning new radio buttons:
   - How far apart should they be placed? (suggest: 20-30 pixels)
   - Horizontal or vertical arrangement?
   - User-configurable spacing?

3. Drag speed:
   - Should it account for browser zoom level?
   - Should it work in PDF points or screen pixels?
