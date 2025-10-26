# AccessForm GUI Architecture - Modals Documentation

**Last Updated:** 2025-10-26

## Overview

This document provides detailed documentation for modal components in AccessForm, including the new remediation modal with real-time progress tracking. The application uses Blazor Server with Bootstrap modals and custom JavaScript integration.

**Related Documentation:**
- [ARCHITECTURE-GUI-PAGES.md](ARCHITECTURE-GUI-PAGES.md) - Main page components documentation
- [ARCHITECTURE.md](ARCHITECTURE.md) - Main processing paths
- [ARCHITECTURE-SERVICES.md](ARCHITECTURE-SERVICES.md) - Service class details
- [ARCHITECTURE-API.md](ARCHITECTURE-API.md) - API endpoints

---

## Table of Contents

1. [Remediation Modal (NEW)](#remediation-modal-new)
2. [Tag Tree Viewer Modal](#tag-tree-viewer-modal)
3. [Field Editor Modal](#field-editor-modal)
4. [Tag Modification Modal](#tag-modification-modal)
5. [Cascade Correction Panel](#cascade-correction-panel)

---

## Remediation Modal (NEW)

**Implementation**: JavaScript overlay in `wwwroot/js/accessform.js`
**Trigger**: Click "🎆 Browse Files (PassportPDF)" button
**Purpose**: Show real-time progress during closed-loop PDF/UA remediation
**Added**: 2025-10-26 (based on git commit history)

### Overview

The Remediation Modal is a full-screen processing overlay that appears during PassportPDF closed-loop remediation. It provides:
- Real-time progress checklist with 11 remediation steps
- Live processing timer
- Action buttons for downloading current state or canceling
- Visual feedback with checkboxes, spinners, and status indicators

### Modal Structure

**Location**: Dynamically created JavaScript overlay
**Style**: Fixed position, full-screen with semi-transparent backdrop
**Z-Index**: 9999 (highest layer)

```javascript
// Created in accessform.js triggerFileInputWithPassport function
const overlay = document.createElement('div');
overlay.id = 'processing-overlay';
overlay.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;' +
                        'background:rgba(0,0,0,0.5);display:flex;' +
                        'align-items:center;justify-content:center;z-index:9999';
```

---

### HTML Structure

```html
<div id="processing-overlay" style="position:fixed;...">
    <div style="background:white;padding:30px;border-radius:10px;
                text-align:center;max-width:600px;max-height:80vh;overflow-y:auto">
        <!-- Spinner -->
        <div class="spinner-border text-primary mb-3"
             style="width:3rem;height:3rem" role="status">
            <span class="visually-hidden">Processing...</span>
        </div>

        <!-- Title -->
        <h4>Processing PDF with Closed-Loop Remediation</h4>

        <!-- Timer -->
        <div class="fs-1 fw-bold text-primary mt-3" id="timer-display">0:00</div>
        <p class="text-muted mt-2" id="current-phase-text">
            Validating compliance and fixing errors...
        </p>

        <!-- Processing Steps Checklist -->
        <div class="mt-4" style="text-align:left">
            <h6 class="mb-3">Progress:</h6>
            <div id="processing-steps-list">
                <!-- Steps rendered dynamically (see below) -->
            </div>
        </div>

        <!-- Action Buttons -->
        <div class="mt-4" style="display:flex;gap:10px;justify-content:center">
            <button id="download-current-btn" class="btn btn-secondary"
                    style="display:none">
                Download Current PDF
            </button>
            <button id="cancel-processing-btn" class="btn btn-danger"
                    style="display:none">
                Cancel Processing
            </button>
        </div>
    </div>
</div>
```

---

### Processing Steps Checklist

**Total Steps**: 11 core remediation phases
**Update Method**: Server-sent events or polling (implementation TBD)
**Visual States**: Unchecked (⚪), Active (spinner), Complete (✓)

#### Step List

Each step is rendered with this structure:

```html
<div class="processing-step" data-step="Validating PDF"
     style="margin-bottom:8px">
    <input type="checkbox" disabled style="margin-right:8px">
    <span>Initial Validation</span>
</div>
```

**Complete Step List**:

1. **Initial Validation**
   - Maps to: "Validating PDF"
   - Purpose: Initial PDF/UA compliance check
   - Duration: ~5-10 seconds

2. **Whitespace Cleanup**
   - Maps to: "Whitespace Cleanup"
   - Purpose: Remove empty text nodes and whitespace artifacts
   - Duration: ~3-5 seconds

3. **Content Remediation**
   - Maps to: "Content Remediation"
   - Purpose: Fix content stream issues
   - Duration: ~10-15 seconds

4. **Structure Enhancement**
   - Maps to: "Structure Enhancement"
   - Purpose: Improve document structure tree
   - Duration: ~10-15 seconds

5. **Form Field Remediation**
   - Maps to: "Form Field Remediation"
   - Purpose: Fix form field accessibility
   - Duration: ~5-10 seconds

6. **Link Structure Fixes**
   - Maps to: "Link Structure Fixes"
   - Purpose: Fix hyperlink accessibility
   - Duration: ~5-10 seconds

7. **Font & PDF/A Conversion**
   - Maps to: "Font Fixes"
   - Purpose: Embed fonts and convert to PDF/A-2u
   - Duration: ~15-20 seconds

8. **Table and List Structure**
   - Maps to: "Table and List Structure Fixes"
   - Purpose: Fix table and list tag structure
   - Duration: ~10-15 seconds

9. **Alternative Text**
   - Maps to: "Alternative Text"
   - Purpose: Add alt text to images
   - Duration: ~5-10 seconds

10. **GPT Fallback**
    - Maps to: "GPT-Powered Remediation"
    - Purpose: Use AI for remaining issues
    - Duration: ~20-30 seconds

11. **Metadata Finalization**
    - Maps to: "Metadata Finalization"
    - Purpose: Set PDF metadata and finalize
    - Duration: ~3-5 seconds

---

### Timer Implementation

**Update Frequency**: Every 1 second
**Format**: M:SS (e.g., "0:45", "2:15")
**Display**: Large bold text with stopwatch emoji

```javascript
// Start timer when processing begins
const startTime = Date.now();
const timerInterval = setInterval(() => {
    const elapsed = Math.floor((Date.now() - startTime) / 1000);
    const minutes = Math.floor(elapsed / 60);
    const seconds = elapsed % 60;
    const display = document.getElementById('timer-display');
    if (display) {
        display.textContent = `${minutes}:${seconds.toString().padStart(2, '0')}`;
    }
}, 1000);
```

**Cleanup**:
```javascript
clearInterval(timerInterval);
```

---

### Action Buttons

#### Download Current PDF Button

**ID**: `download-current-btn`
**Label**: "Download Current PDF"
**Class**: `btn btn-secondary`
**Visibility**: Hidden initially, shown after 3 seconds
**Purpose**: Allow user to download intermediate PDF state

```javascript
// Show buttons after 3 seconds
setTimeout(() => {
    const downloadBtn = document.getElementById('download-current-btn');
    const cancelBtn = document.getElementById('cancel-processing-btn');
    if (downloadBtn) downloadBtn.style.display = 'inline-block';
    if (cancelBtn) cancelBtn.style.display = 'inline-block';
}, 3000);

// Wire up download functionality
const downloadBtn = document.getElementById('download-current-btn');
if (downloadBtn) {
    downloadBtn.onclick = () => {
        if (window.accessForm._currentProcessingPdf) {
            // Download the current PDF
            const blob = new Blob([window.accessForm._currentProcessingPdf],
                                 { type: 'application/pdf' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = 'current-processing-state.pdf';
            a.click();
            URL.revokeObjectURL(url);
        } else {
            alert('Current PDF not available yet');
        }
    };
}
```

#### Cancel Processing Button

**ID**: `cancel-processing-btn`
**Label**: "Cancel Processing"
**Class**: `btn btn-danger`
**Visibility**: Hidden initially, shown after 3 seconds
**Purpose**: Allow user to abort long-running remediation

```javascript
// Wire up cancel functionality
const cancelBtn = document.getElementById('cancel-processing-btn');
if (cancelBtn) {
    cancelBtn.onclick = () => {
        if (confirm('Are you sure you want to cancel processing?')) {
            // Clear intervals
            clearInterval(timerInterval);
            if (window.accessForm._progressInterval) {
                clearInterval(window.accessForm._progressInterval);
            }

            // Remove overlay
            const overlayToRemove = document.getElementById('processing-overlay');
            if (overlayToRemove) {
                overlayToRemove.remove();
            }

            // Call cancel endpoint if session exists
            if (result && result.sessionId) {
                fetch(`/api/cancel/${result.sessionId}`, { method: 'POST' })
                    .catch(err => console.error('Error canceling:', err));
            }
        }
    };
}
```

---

### Progress Update Mechanism

**Method**: Real-time progress updates (implementation via server events or polling)
**Update Target**: Checkboxes in `#processing-steps-list`
**Update Frequency**: As each phase completes

#### Step Mapping

The UI step names map to backend phase names:

```javascript
const stepMapping = {
    "Validating PDF": "Initial Validation",
    "Initial validation": "Initial Validation",
    "Whitespace Cleanup": "Whitespace Cleanup",
    "Content Remediation": "Content Remediation",
    "Structure Enhancement": "Structure Enhancement",
    "Form Field Remediation": "Form Field Remediation",
    "Table and List Structure Fixes": "Table and List Structure",
    "Font Fixes & PDF/A Conversion": "Font & PDF/A Conversion",
    "Alternative Text for Images": "Alternative Text",
    "Post-Remediation Structural Cleanup": "Post-remediation cleanup",
    "GPT-Powered Remediation": "GPT Fallback",
    "GPT Fallback": "GPT Fallback",
    "Metadata Finalization": "Metadata Finalization",
    "Final validation": "Final validation",
    "Generating report": "Generating report"
};
```

#### Update Logic (Conceptual)

```javascript
function updateStep(stepName, isComplete) {
    const steps = document.querySelectorAll('.processing-step');
    steps.forEach(step => {
        const stepAttr = step.getAttribute('data-step');
        if (stepAttr === stepName || stepMapping[stepAttr] === stepName) {
            const checkbox = step.querySelector('input[type="checkbox"]');
            if (isComplete) {
                checkbox.checked = true;
                checkbox.style.accentColor = '#28a745'; // Green
            } else {
                // Show spinner for active step
                checkbox.style.opacity = '0.5';
            }
        }
    });
}
```

---

### Modal Lifecycle

#### 1. Modal Creation

**Trigger**: User clicks "🎆 Browse Files (PassportPDF)" and selects PDF file

```javascript
triggerFileInputWithPassport: function (inputId) {
    const input = document.getElementById(inputId);
    if (input) {
        input.onchange = async function (event) {
            const files = event.target.files;
            if (files && files.length > 0) {
                const file = files[0];

                // Create processing overlay
                const overlay = document.createElement('div');
                overlay.id = 'processing-overlay';
                // ... (set styles and innerHTML)
                document.body.appendChild(overlay);

                // Start timer
                const startTime = Date.now();
                const timerInterval = setInterval(() => {
                    // Update timer display
                }, 1000);

                // ... continue with processing
            }
        };
        input.click();
    }
}
```

#### 2. Processing Phase

**Duration**: Variable (typically 1-5 minutes)
**Updates**: Real-time checklist updates as each phase completes
**User Actions**: Can download current PDF or cancel at any time

#### 3. Modal Cleanup

**Trigger**: Processing completes (success or error) or user cancels

```javascript
// On completion or error
clearInterval(timerInterval);
const overlayToRemove = document.getElementById('processing-overlay');
if (overlayToRemove) {
    overlayToRemove.remove();
}

// Show results in Blazor component
if (result) {
    const fileData = {
        name: file.name,
        size: file.size,
        type: file.type,
        lastModified: file.lastModified,
        data: ''
    };

    if (window.accessForm.dotNetHelper) {
        await window.accessForm.dotNetHelper.invokeMethodAsync(
            'OnFileProcessedDirectly',
            fileData,
            JSON.stringify(result)
        );
    }
}
```

---

### Integration with Backend

**API Endpoint**: `/api/process-with-passportpdf-auto`
**Method**: POST
**Content-Type**: multipart/form-data

**Request Parameters**:
```javascript
const formData = new FormData();
formData.append('file', file);
formData.append('useSyncfusion', String(useSyncfusion));
formData.append('useGoogle', String(useGoogle));
formData.append('useClaudeVision', String(useClaudeVision));
formData.append('useClaudeValidation', String(useClaudeValidation));
formData.append('useGroqValidation', String(useGroqValidation));
formData.append('useMultiStageValidation', String(useMultiStageValidation));
formData.append('useSignatureDetection', String(useSignatureDetection));
formData.append('useAsposeFontEmbed', String(useAsposeFontEmbed));
```

**Response**:
```json
{
    "success": true,
    "sessionId": "session-guid",
    "normalPdfBase64": "...",
    "accessiblePdfBase64": "...",
    "validation": {
        "enabled": true,
        "compliant": true,
        "iterations": 3,
        "duration": "2m 15s",
        "initialViolations": 45,
        "finalViolations": 0,
        "violationsFixed": 45,
        "complianceImprovement": 100.0,
        "exitReason": "Compliant",
        "recommendations": [],
        "iterationHistory": [...]
    }
}
```

---

### Styling

**Container**:
```css
#processing-overlay {
    position: fixed;
    top: 0;
    left: 0;
    width: 100%;
    height: 100%;
    background: rgba(0, 0, 0, 0.5);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 9999;
}
```

**Modal Content**:
```css
.modal-content {
    background: white;
    padding: 30px;
    border-radius: 10px;
    text-align: center;
    max-width: 600px;
    max-height: 80vh;
    overflow-y: auto;
}
```

**Processing Steps**:
```css
.processing-step {
    margin-bottom: 8px;
    display: flex;
    align-items: center;
}

.processing-step input[type="checkbox"] {
    margin-right: 8px;
}

.processing-step input[type="checkbox"]:checked {
    accent-color: #28a745;
}
```

---

## Tag Tree Viewer Modal

**Trigger**: Click "🔍 View Tag Structure" button
**Purpose**: Display PDF structure tree in hierarchical view
**Implementation**: Bootstrap modal with JavaScript content loading

### Modal Structure

```html
<div class="modal fade" id="tagTreeModal" tabindex="-1"
     aria-labelledby="tagTreeModalLabel" aria-hidden="true">
    <div class="modal-dialog modal-xl modal-dialog-scrollable">
        <div class="modal-content">
            <div class="modal-header bg-primary text-white">
                <h5 class="modal-title" id="tagTreeModalLabel">
                    <i class="bi bi-diagram-3"></i> PDF Tag Structure Viewer
                </h5>
                <button type="button" class="btn-close btn-close-white"
                        data-bs-dismiss="modal" aria-label="Close"></button>
            </div>
            <div class="modal-body" id="tagTreeModalBody">
                <!-- Content loaded dynamically via JavaScript -->
            </div>
            <div class="modal-footer">
                <button type="button" class="btn btn-secondary"
                        data-bs-dismiss="modal">Close</button>
                <button type="button" class="btn btn-primary" @onclick="ExportTagTree">
                    <i class="bi bi-download"></i> Export as JSON
                </button>
            </div>
        </div>
    </div>
</div>
```

---

### JavaScript Integration

**Opening the Modal**:
```csharp
private async Task ViewTagTree(string pdfBase64, string fileName)
{
    try
    {
        // Extract tag structure from PDF
        var tagStructure = await PdfTagExtractor.ExtractTagTree(pdfBase64);
        var tagTreeJson = JsonSerializer.Serialize(tagStructure);

        // Store for export
        currentTagTreeData = tagStructure;
        currentTagTreeFileName = fileName;

        // Load into modal and show
        await JSRuntime.InvokeVoidAsync("showTagTree", tagTreeJson);
    }
    catch (Exception ex)
    {
        await JSRuntime.InvokeVoidAsync("alert",
            $"Error loading tag structure: {ex.Message}");
    }
}
```

**JavaScript Function**:
```javascript
window.showTagTree = function(tagTreeJson) {
    const tagTree = JSON.parse(tagTreeJson);
    const modalBody = document.getElementById('tagTreeModalBody');

    // Render hierarchical tree view
    modalBody.innerHTML = renderTagTree(tagTree);

    // Show modal
    const modal = new bootstrap.Modal(document.getElementById('tagTreeModal'));
    modal.show();
};

function renderTagTree(node, level = 0) {
    const indent = '  '.repeat(level);
    let html = `<div style="margin-left: ${level * 20}px;">`;

    // Render node
    html += `<div class="tag-node">`;
    html += `<span class="tag-type">${node.type}</span>`;
    if (node.content) {
        html += ` <span class="tag-content">${node.content}</span>`;
    }
    html += `</div>`;

    // Render children
    if (node.children) {
        node.children.forEach(child => {
            html += renderTagTree(child, level + 1);
        });
    }

    html += `</div>`;
    return html;
}
```

---

### Export Functionality

**Button**: "Export as JSON"
**Action**: Download tag structure as JSON file

```csharp
private async Task ExportTagTree()
{
    if (currentTagTreeData == null) return;

    try
    {
        var json = JsonSerializer.Serialize(currentTagTreeData,
            new JsonSerializerOptions { WriteIndented = true });

        var fileName = $"{Path.GetFileNameWithoutExtension(currentTagTreeFileName)}_tagtree.json";

        await JSRuntime.InvokeVoidAsync("downloadTextFile", json, fileName);
    }
    catch (Exception ex)
    {
        await JSRuntime.InvokeVoidAsync("alert",
            $"Error exporting tag tree: {ex.Message}");
    }
}
```

---

## Field Editor Modal

**Location**: `Pages/FieldEditorModal.razor`
**Route**: `/field-editor-modal`
**Purpose**: Full-screen modal for advanced field editing with drag-and-drop

### Modal Structure

**Layout**: Fixed position full-screen modal with three sections

```html
<div class="field-editor-modal">
    <div class="field-editor-content">
        <!-- Header -->
        <div class="field-editor-header">
            <h3>PDF Form Field Editor</h3>
            <button class="btn-close" @onclick="Close"></button>
        </div>

        <!-- Body (scrollable) -->
        <div class="field-editor-body">
            <!-- Field table here -->
        </div>

        <!-- Footer -->
        <div class="field-editor-footer">
            <div>
                <span class="badge bg-info">@Fields.Count fields</span>
                <span class="badge bg-warning ms-2" *ngIf="HasChanges">
                    Unsaved changes
                </span>
            </div>
            <div>
                <button class="btn btn-secondary" @onclick="Cancel">Cancel</button>
                <button class="btn btn-primary" @onclick="SaveAndClose">
                    Save Changes
                </button>
            </div>
        </div>
    </div>
</div>
```

---

### Field Table

**Sticky Header**:
```css
.field-table th {
    background: #4a90e2;
    color: white;
    padding: 10px 8px;
    text-align: left;
    position: sticky;
    top: 0;
    z-index: 10;
}
```

**Columns**:
1. **#** - Index
2. **Field Name** - Text input
3. **Type** - Select dropdown
4. **Page** - Number input
5. **Position** - X/Y with increment/decrement buttons
6. **Size** - Width/Height with increment/decrement buttons
7. **Tooltip** - Textarea
8. **Actions** - Delete button

**Position Controls**:
```html
<div class="position-controls">
    <button class="position-btn" @onclick="() => AdjustPosition(field, 'x', -1)">
        ◄
    </button>
    <input type="number" @bind="field.X" step="0.5" />
    <button class="position-btn" @onclick="() => AdjustPosition(field, 'x', 1)">
        ►
    </button>

    <button class="position-btn" @onclick="() => AdjustPosition(field, 'y', -1)">
        ▲
    </button>
    <input type="number" @bind="field.Y" step="0.5" />
    <button class="position-btn" @onclick="() => AdjustPosition(field, 'y', 1)">
        ▼
    </button>
</div>
```

**Adjustment Logic**:
```csharp
private void AdjustPosition(FieldDetectionResult field, string axis, float delta)
{
    if (axis == "x")
        field.X += delta;
    else if (axis == "y")
        field.Y += delta;

    MarkAsChanged(field);
}
```

---

### Quick Actions Bar

```html
<div class="quick-actions">
    <button class="quick-action-btn" @onclick="AlignLeft">
        <i class="bi bi-align-start"></i> Align Left
    </button>
    <button class="quick-action-btn" @onclick="AlignTop">
        <i class="bi bi-align-top"></i> Align Top
    </button>
    <button class="quick-action-btn" @onclick="DistributeHorizontally">
        <i class="bi bi-distribute-horizontal"></i> Distribute H
    </button>
    <button class="quick-action-btn" @onclick="DistributeVertically">
        <i class="bi bi-distribute-vertical"></i> Distribute V
    </button>
    <button class="quick-action-btn" @onclick="SameSize">
        <i class="bi bi-arrows-angle-expand"></i> Same Size
    </button>
</div>
```

**Quick Actions**:
- **Align Left**: Align selected fields to leftmost X
- **Align Top**: Align selected fields to topmost Y
- **Distribute Horizontally**: Even horizontal spacing
- **Distribute Vertically**: Even vertical spacing
- **Same Size**: Make all selected fields same width/height

---

## Tag Modification Modal

**Location**: `Pages/TagModificationModal.razor`
**Purpose**: Combined tag structure viewer and field editor with drag-and-drop

### Window Layout

**Draggable Window**: Fixed position with resize handles

```html
<div class="tag-modification-window @(IsVisible ? "show" : "hide")"
     style="position: fixed; left: 20px; top: 50px; width: 1200px;
            height: calc(100vh - 100px);">
    <div class="window-container">
        <!-- Header (draggable) -->
        <div class="window-header" id="tagModWindowHeader" style="cursor: move;">
            <h5>
                <i class="fas fa-tags me-2"></i> Tag Structure & Field Editor
            </h5>
            <button type="button" class="btn-close btn-close-white"
                    @onclick="Cancel"></button>
        </div>

        <!-- Body (two columns) -->
        <div class="window-body" style="display: flex;">
            <!-- Left: PDF Preview -->
            <div class="col-md-6">
                <!-- Preview here -->
            </div>

            <!-- Right: Field List -->
            <div class="col-md-6">
                <!-- Field list here -->
            </div>
        </div>
    </div>
</div>
```

---

### Left Panel: PDF Preview with Draggable Fields

**Page Navigation**:
```html
<div class="pdf-preview-controls mb-2">
    <button class="btn btn-sm btn-secondary" @onclick="PreviousPage"
            disabled="@(CurrentPage <= 1)">
        <i class="fas fa-chevron-left"></i> Previous
    </button>
    <span class="mx-2">Page @CurrentPage of @TotalPages</span>
    <button class="btn btn-sm btn-secondary" @onclick="NextPage"
            disabled="@(CurrentPage >= TotalPages)">
        Next <i class="fas fa-chevron-right"></i>
    </button>
</div>
```

---

**Interactive PDF Canvas**:
```html
<div class="pdf-page-container" style="position: relative; display: inline-block;">
    <img src="@($"data:image/png;base64,{CurrentPageImage}")"
         class="pdf-page-image"
         @onmousedown="HandleImageMouseDown" />

    <!-- Draggable field overlays -->
    @if (ShowFieldBoxes)
    {
        @foreach (var field in CurrentPageFields)
        {
            <div class="field-overlay @GetFieldClass(field.Type)
                      @(SelectedField == field ? "selected" : "")"
                 id="field-@field.OriginalIndex"
                 @onmousedown="e => HandleFieldMouseDown(e, field)"
                 @onmousedown:stopPropagation="true"
                 style="@GetFieldOverlayStyle(field)">
                <div class="field-label">@field.Name</div>
                <!-- Resize handles -->
                <div class="resize-handle-nw"></div>
                <div class="resize-handle-ne"></div>
                <div class="resize-handle-sw"></div>
                <div class="resize-handle-se"></div>
            </div>
        }
    }

    <!-- Drawing rectangle for new field -->
    @if (isDrawingNewField)
    {
        <div class="field-overlay drawing" style="@GetDrawingRectangleStyle()">
        </div>
    }
</div>
```

---

**Drag & Drop Logic**:
```csharp
private bool isDragging = false;
private FieldData? draggedField = null;
private float dragStartX, dragStartY;
private float dragOffsetX, dragOffsetY;

private void HandleFieldMouseDown(MouseEventArgs e, FieldData field)
{
    isDragging = true;
    draggedField = field;
    SelectedField = field;

    // Calculate offset from field origin to mouse position
    dragStartX = (float)e.ClientX;
    dragStartY = (float)e.ClientY;
    dragOffsetX = dragStartX - field.X;
    dragOffsetY = dragStartY - field.Y;

    await JSRuntime.InvokeVoidAsync("setupDragHandlers",
        DotNetObjectReference.Create(this));
}

[JSInvokable]
public void HandleMouseMove(float clientX, float clientY)
{
    if (!isDragging || draggedField == null) return;

    // Update field position
    draggedField.X = clientX - dragOffsetX;
    draggedField.Y = clientY - dragOffsetY;

    MarkAsChanged(draggedField);
    StateHasChanged();
}

[JSInvokable]
public void HandleMouseUp()
{
    isDragging = false;
    draggedField = null;
}
```

**JavaScript Interop**:
```javascript
window.setupDragHandlers = (dotnetHelper) => {
    document.addEventListener('mousemove', (e) => {
        dotnetHelper.invokeMethodAsync('HandleMouseMove', e.clientX, e.clientY);
    });

    document.addEventListener('mouseup', () => {
        dotnetHelper.invokeMethodAsync('HandleMouseUp');
    });
};
```

---

**Resize Handles**:
```css
.resize-handle-nw, .resize-handle-ne, .resize-handle-sw, .resize-handle-se {
    position: absolute;
    width: 8px;
    height: 8px;
    background: white;
    border: 1px solid #333;
}

.resize-handle-nw { top: -4px; left: -4px; cursor: nw-resize; }
.resize-handle-ne { top: -4px; right: -4px; cursor: ne-resize; }
.resize-handle-sw { bottom: -4px; left: -4px; cursor: sw-resize; }
.resize-handle-se { bottom: -4px; right: -4px; cursor: se-resize; }
```

---

### Right Panel: Field List with Sorting

**Sort Controls**:
```html
<div class="btn-group btn-group-sm">
    <button class="btn btn-outline-secondary @(sortBy == "original" ? "active" : "")"
            @onclick="@(() => SortFields("original"))">
        <i class="fas fa-sort-numeric-down"></i> Original Order
    </button>
    <button class="btn btn-outline-secondary @(sortBy == "name" ? "active" : "")"
            @onclick="@(() => SortFields("name"))">
        <i class="fas fa-sort-alpha-down"></i> Name
    </button>
    <button class="btn btn-outline-secondary @(sortBy == "type" ? "active" : "")"
            @onclick="@(() => SortFields("type"))">
        <i class="fas fa-tags"></i> Type
    </button>
    <button class="btn btn-outline-secondary @(sortBy == "page" ? "active" : "")"
            @onclick="@(() => SortFields("page"))">
        <i class="fas fa-file"></i> Page
    </button>
</div>
```

**Sorting Logic**:
```csharp
private void SortFields(string sortType)
{
    sortBy = sortType;

    switch (sortType)
    {
        case "original":
            EditableFields = EditableFields.OrderBy(f => f.OriginalIndex).ToList();
            break;
        case "name":
            EditableFields = EditableFields.OrderBy(f => f.Name).ToList();
            break;
        case "type":
            EditableFields = EditableFields.OrderBy(f => f.Type)
                                           .ThenBy(f => f.Name).ToList();
            break;
        case "page":
            EditableFields = EditableFields.OrderBy(f => f.PageNumber)
                                           .ThenBy(f => f.Y).ToList();
            break;
    }

    StateHasChanged();
}
```

---

**Field List Items**:
```html
@foreach (var field in EditableFields)
{
    <div class="field-item @(SelectedField == field ? "selected" : "")"
         @onclick="() => SelectAndFocusField(field)">
        <div class="field-item-header">
            <span class="field-type-badge @GetFieldTypeClass(field.Type)">
                @GetFieldTypeIcon(field.Type)
            </span>
            <strong>@field.Name</strong>
            <span class="badge bg-secondary ms-auto">Page @field.PageNumber</span>
        </div>
        <div class="field-item-body">
            <small class="text-muted">
                Position: (@Math.Round(field.X, 1), @Math.Round(field.Y, 1))
                Size: @Math.Round(field.Width, 1) × @Math.Round(field.Height, 1)
            </small>
        </div>
        <div class="field-item-actions">
            <button class="btn btn-sm btn-outline-primary"
                    @onclick="() => EditField(field)"
                    @onclick:stopPropagation="true">
                <i class="fas fa-edit"></i> Edit
            </button>
            <button class="btn btn-sm btn-outline-danger"
                    @onclick="() => DeleteField(field)"
                    @onclick:stopPropagation="true">
                <i class="fas fa-trash"></i> Delete
            </button>
        </div>
    </div>
}
```

**Field Type Icons**:
```csharp
private string GetFieldTypeIcon(string type) => type.ToLower() switch
{
    "textbox" => "📝",
    "checkbox" => "☑️",
    "radiobutton" => "🔘",
    "combobox" => "📋",
    "signature" => "✍️",
    _ => "❓"
};
```

---

## Cascade Correction Panel

**Location**: `Components/CascadeCorrectionPanel.razor`
**Purpose**: AI-powered interactive field correction with natural language commands

### Panel Structure

**Fixed Position Panel**: Right side of screen

```html
<div class="cascade-correction-panel @(IsVisible ? "show" : "hide")"
     style="position: fixed; right: 20px; top: 50px; width: 550px;
            height: calc(100vh - 100px);">
    <div class="card" style="height: 100%;">
        <div class="card-header bg-warning text-dark" id="cascadeWindowHeader"
             style="cursor: move;">
            <h5>
                <i class="bi bi-exclamation-triangle"></i> Field Cascade Correction
            </h5>
            <button type="button" class="btn-close" @onclick="Close"></button>
        </div>
        <div class="card-body" style="overflow-y: auto;">
            <!-- Content here -->
        </div>
    </div>
</div>
```

**Draggable**: Header acts as drag handle

---

### Command Input

```html
<div class="mb-3">
    <div class="input-group">
        <input type="text" class="form-control font-monospace"
               @bind="CommandInput"
               @onkeypress="@(async (e) => {
                   if (e.Key == "Enter") await ExecuteCommand();
               })"
               placeholder="Enter command: phantom 2, cascade 3, rename 5 'New Name',
                           auto, preview, reset">
        <button class="btn btn-primary" @onclick="ExecuteCommand">Execute</button>
        <button class="btn btn-secondary" @onclick="RefreshTable">Refresh</button>
    </div>
    <small class="text-muted">
        Commands: phantom, cascade, rename, swap, auto, preview, reset, undo
    </small>
</div>
```

**Supported Commands**:
- `phantom [index]` - Mark field as phantom (should be on different page)
- `cascade [index]` - Cascade field down by one page
- `rename [index] 'New Name'` - Rename field
- `swap [index1] [index2]` - Swap two fields
- `auto` - Auto-detect and fix phantom/cascade issues
- `preview` - Show preview of changes
- `reset` - Reset all changes
- `undo` - Undo last command

---

### Quick Action Buttons

```html
<div class="btn-group mb-3" role="group">
    <button class="btn btn-sm btn-outline-danger" @onclick="ExecuteAutoCommand">
        <i class="bi bi-magic"></i> Auto Detect
    </button>
    <button class="btn btn-sm btn-outline-info" @onclick="ExecutePreviewCommand">
        <i class="bi bi-eye"></i> Preview
    </button>
    <button class="btn btn-sm btn-outline-warning" @onclick="ExecuteResetCommand">
        <i class="bi bi-arrow-counterclockwise"></i> Reset
    </button>
    <button class="btn btn-sm btn-outline-secondary" @onclick="ExecuteUndoCommand">
        <i class="bi bi-arrow-90deg-left"></i> Undo
    </button>
</div>
```

**Button Actions**:
- **Auto Detect**: Analyzes all fields and suggests corrections
- **Preview**: Shows what changes will be applied
- **Reset**: Reverts to original field state
- **Undo**: Reverts last command only

---

### Field Table Display

```html
<div class="field-table-container" style="max-height: 400px; overflow-y: auto;">
    <pre class="bg-dark text-light p-2 rounded font-monospace"
         style="font-size: 0.85em;">
        @FieldTableDisplay
    </pre>
</div>
```

**Table Format**:
```
Index | Field Name            | Page | Status
------|----------------------|------|--------
1     | EmployeeName         | 1    | OK
2     | EmployeeAddress      | 2    | PHANTOM (should be page 1)
3     | SignatureDate        | 1    | CASCADE (should be page 2)
4     | AgreementCheckbox    | 2    | OK
```

**Status Indicators**:
- `OK` - Field is on correct page
- `PHANTOM` - Field appears on wrong page (page number too high)
- `CASCADE` - Field should cascade to next page

---

### Command Output

```html
@if (!string.IsNullOrEmpty(CommandOutput))
{
    <div class="mt-3">
        <h6>Command Result:</h6>
        <pre class="bg-light p-2 rounded font-monospace" style="font-size: 0.85em;">
            @CommandOutput
        </pre>
    </div>
}
```

**Example Outputs**:
```
> auto
Detected 3 phantom fields: [2, 5, 8]
Detected 2 cascade fields: [3, 7]
Applied corrections to 5 fields.

> cascade 3
Field 3 'SignatureDate' cascaded from page 1 to page 2.

> rename 5 'Employee_Email_Address'
Field 5 renamed from 'Email' to 'Employee_Email_Address'.
```

---

### Apply Changes

```html
<div class="mt-3">
    <button class="btn btn-success" @onclick="ApplyCorrections">
        <i class="bi bi-check-circle"></i> Apply Corrections
    </button>
    <button class="btn btn-secondary ms-2" @onclick="Close">Cancel</button>
</div>
```

**Apply Corrections**:
1. Sends corrected fields to API
2. Returns updated PDF
3. Closes panel
4. Refreshes parent component with new field data

---

## Common Modal Workflows

### Workflow 1: View PDF Tag Structure

1. **Process document** (get accessible PDF)
2. **Click "🔍 View Tag Structure"** on results card
3. **Modal opens** showing hierarchical tree
4. **Navigate tree**: Expand/collapse nodes
5. **Optional**: Click "Export as JSON" to download structure
6. **Close modal**

---

### Workflow 2: Edit Field Properties

1. **Process document** (get field detection results)
2. **Click "✏️ Edit Fields & Tags"** on results card
3. **Field editor modal opens**
4. **Select field** from list or by clicking overlay
5. **Edit properties**: Name, position, size, tooltip
6. **Use quick actions**: Align, distribute, same size
7. **Click "Save Changes"**
8. **Modal closes**, parent receives updated fields

---

### Workflow 3: Cascade Correction

1. **Process document** (get field detection results)
2. **Notice phantom fields** (fields on wrong pages)
3. **Open cascade correction panel**
4. **Run "Auto Detect"** to identify issues
5. **Review suggestions** in field table
6. **Execute corrections**:
   - `auto` - Apply all suggestions
   - `phantom [index]` - Fix specific field
   - `cascade [index]` - Move field to next page
7. **Preview changes**
8. **Click "Apply Corrections"**
9. **Download corrected PDF**

---

### Workflow 4: Closed-Loop PDF Remediation

1. **Click "🎆 Browse Files (PassportPDF)"** on home page
2. **Select PDF file**
3. **Remediation modal appears**
4. **Watch real-time progress**:
   - Timer counts up
   - Checklist items complete
   - Current phase displayed
5. **Optional**: Download intermediate PDF at any point
6. **Optional**: Cancel if taking too long
7. **On completion**: Modal closes, results displayed
8. **Review validation results**:
   - Compliance status
   - Violations fixed/remaining
   - Iteration history
   - Detailed violation lists

---

## Accessibility Considerations

### Keyboard Support

**All Modals**:
- `Escape` - Close modal
- `Tab` / `Shift+Tab` - Navigate focusable elements
- `Enter` - Activate focused button

**Field Editor**:
- `Arrow keys` - Navigate table cells
- `F2` - Start editing selected cell
- `Escape` - Cancel editing

### ARIA Attributes

**Modal Dialogs**:
```html
<div role="dialog" aria-modal="true" aria-labelledby="modalTitle">
    <h5 id="modalTitle">Modal Title</h5>
    <!-- Content -->
</div>
```

**Processing Steps**:
```html
<div role="status" aria-live="polite" aria-atomic="true">
    <span class="visually-hidden">Processing step: @currentStep</span>
</div>
```

---

## Styling & Theming

### Modal Overlay

```css
.modal-backdrop {
    background-color: rgba(0, 0, 0, 0.5);
    backdrop-filter: blur(3px);
}
```

### Modal Content

```css
.modal-content {
    border-radius: 15px;
    box-shadow: 0 10px 40px rgba(0, 0, 0, 0.3);
}

.modal-header {
    background-color: #4a90e2;
    color: white;
    border-radius: 15px 15px 0 0;
}
```

### Processing Modal

```css
#processing-overlay {
    animation: fadeIn 0.3s ease;
}

@keyframes fadeIn {
    from { opacity: 0; }
    to { opacity: 1; }
}

.processing-step {
    transition: all 0.3s ease;
}

.processing-step.active-step {
    background: rgba(13, 110, 253, 0.1);
    border-left: 3px solid #0d6efd;
}
```

---

**End of ARCHITECTURE-GUI-MODALS.md**

**See also:**
- **[ARCHITECTURE-GUI-PAGES.md](ARCHITECTURE-GUI-PAGES.md)** - Main page components documentation
- **[ARCHITECTURE.md](ARCHITECTURE.md)** - Main processing paths
- **[ARCHITECTURE-SERVICES.md](ARCHITECTURE-SERVICES.md)** - Service class details
- **[ARCHITECTURE-API.md](ARCHITECTURE-API.md)** - API endpoints
