# Field Resizing Feature - TODO

## Requirements
1. Resize individual fields by dragging corners
   - Left/right (change width)
   - Up/down (change height)
   - Diagonal (change both width and height proportionally)

2. Multi-select field resizing
   - Select multiple fields
   - Resize all simultaneously

## Implementation Plan

### Frontend (Pages/Index.razor)

#### 1. Add Resize Handles to Fields
```razor
<!-- Add to each field's render -->
<div class="field-box" @onclick="() => SelectField(field)">
    <div class="field-content">{field.NewName}</div>

    <!-- Resize handles (8 total: corners + sides) -->
    <div class="resize-handle resize-nw" @onmousedown="(e) => StartResize(e, field, 'nw')"></div>
    <div class="resize-handle resize-n" @onmousedown="(e) => StartResize(e, field, 'n')"></div>
    <div class="resize-handle resize-ne" @onmousedown="(e) => StartResize(e, field, 'ne')"></div>
    <div class="resize-handle resize-e" @onmousedown="(e) => StartResize(e, field, 'e')"></div>
    <div class="resize-handle resize-se" @onmousedown="(e) => StartResize(e, field, 'se')"></div>
    <div class="resize-handle resize-s" @onmousedown="(e) => StartResize(e, field, 's')"></div>
    <div class="resize-handle resize-sw" @onmousedown="(e) => StartResize(e, field, 'sw')"></div>
    <div class="resize-handle resize-w" @onmousedown="(e) => StartResize(e, field, 'w')"></div>
</div>
```

#### 2. CSS for Resize Handles
```css
.resize-handle {
    position: absolute;
    width: 8px;
    height: 8px;
    background: #4A90E2;
    border: 1px solid white;
    z-index: 10;
}

.resize-nw { top: -4px; left: -4px; cursor: nw-resize; }
.resize-n { top: -4px; left: 50%; transform: translateX(-50%); cursor: n-resize; }
.resize-ne { top: -4px; right: -4px; cursor: ne-resize; }
.resize-e { top: 50%; right: -4px; transform: translateY(-50%); cursor: e-resize; }
.resize-se { bottom: -4px; right: -4px; cursor: se-resize; }
.resize-s { bottom: -4px; left: 50%; transform: translateX(-50%); cursor: s-resize; }
.resize-sw { bottom: -4px; left: -4px; cursor: sw-resize; }
.resize-w { top: 50%; left: -4px; transform: translateY(-50%); cursor: w-resize; }
```

#### 3. Resize Logic
```csharp
private FieldUpdate? resizingField = null;
private string resizeDirection = "";
private double resizeStartX = 0;
private double resizeStartY = 0;
private double originalWidth = 0;
private double originalHeight = 0;
private double originalX = 0;
private double originalY = 0;

private void StartResize(MouseEventArgs e, FieldUpdate field, string direction)
{
    resizingField = field;
    resizeDirection = direction;
    resizeStartX = e.ClientX;
    resizeStartY = e.ClientY;
    originalWidth = field.Width;
    originalHeight = field.Height;
    originalX = field.X;
    originalY = field.Y;

    // Prevent event bubbling
    e.StopPropagation();
}

private async Task HandleMouseMove(MouseEventArgs e)
{
    if (resizingField == null) return;

    double deltaX = e.ClientX - resizeStartX;
    double deltaY = e.ClientY - resizeStartY;

    // Convert to PDF coordinates (account for zoom)
    deltaX = deltaX / zoomLevel;
    deltaY = deltaY / zoomLevel;

    switch (resizeDirection)
    {
        case "se": // Bottom-right corner
            resizingField.Width = Math.Max(20, originalWidth + deltaX);
            resizingField.Height = Math.Max(10, originalHeight + deltaY);
            break;

        case "nw": // Top-left corner
            resizingField.X = originalX + deltaX;
            resizingField.Y = originalY + deltaY;
            resizingField.Width = Math.Max(20, originalWidth - deltaX);
            resizingField.Height = Math.Max(10, originalHeight - deltaY);
            break;

        case "ne": // Top-right corner
            resizingField.Y = originalY + deltaY;
            resizingField.Width = Math.Max(20, originalWidth + deltaX);
            resizingField.Height = Math.Max(10, originalHeight - deltaY);
            break;

        case "sw": // Bottom-left corner
            resizingField.X = originalX + deltaX;
            resizingField.Width = Math.Max(20, originalWidth - deltaX);
            resizingField.Height = Math.Max(10, originalHeight + deltaY);
            break;

        case "e": // Right side
            resizingField.Width = Math.Max(20, originalWidth + deltaX);
            break;

        case "w": // Left side
            resizingField.X = originalX + deltaX;
            resizingField.Width = Math.Max(20, originalWidth - deltaX);
            break;

        case "s": // Bottom side
            resizingField.Height = Math.Max(10, originalHeight + deltaY);
            break;

        case "n": // Top side
            resizingField.Y = originalY + deltaY;
            resizingField.Height = Math.Max(10, originalHeight - deltaY);
            break;
    }

    StateHasChanged();
}

private void StopResize()
{
    resizingField = null;
    resizeDirection = "";
}
```

#### 4. Multi-Select Resizing
```csharp
private List<FieldUpdate> selectedFields = new();

private void HandleMultiSelectResize(MouseEventArgs e)
{
    if (selectedFields.Count == 0) return;

    double deltaX = e.ClientX - resizeStartX;
    double deltaY = e.ClientY - resizeStartY;

    deltaX = deltaX / zoomLevel;
    deltaY = deltaY / zoomLevel;

    // Calculate scale factors
    double widthScale = (originalWidth + deltaX) / originalWidth;
    double heightScale = (originalHeight + deltaY) / originalHeight;

    foreach (var field in selectedFields)
    {
        // Apply proportional resize to all selected fields
        field.Width *= widthScale;
        field.Height *= heightScale;
    }
}
```

## Files to Modify
- `Pages/Index.razor` - Add resize UI and logic
- `Pages/Index.razor.css` - Add resize handle styles

## Testing
1. Single field resize - all 8 directions
2. Multi-select resize
3. Min/max size constraints
4. Coordinate accuracy after resize

## Priority
High - Critical UX feature for field editing
