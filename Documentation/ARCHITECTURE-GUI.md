# AccessForm GUI Architecture - Component Documentation

**Last Updated:** 2025-10-12

## Overview

This document provides detailed documentation for all GUI components in AccessForm. The application uses Blazor Server for server-side rendering with real-time SignalR communication.

**GUI Components:**
- [Index Page (Main Upload Interface)](#index-page-main-upload-interface)
- [Field Editor](#field-editor)
- [Field Editor Modal](#field-editor-modal)
- [Tag Modification Modal](#tag-modification-modal)
- [Cascade Correction Panel](#cascade-correction-panel)

---

## Index Page (Main Upload Interface)

**Location**: `Pages/Index.razor`
**Route**: `/` (home page)
**Purpose**: Primary document upload and processing interface

### Layout Overview

The page consists of four main sections:

1. **Field Detection Options** - Configure AI services
2. **Accessibility Services** - Configure post-processing options
3. **Drag & Drop Zone** - File upload area
4. **Results Section** - Download processed PDFs

---

### Section 1: Field Detection Options

**Visual**: White card with checkboxes and presets

#### Master Toggle

**Control**: `🚀 Enable All AI Features` checkbox
**Variable**: `enableAllAI`
**Default**: `true`
**Behavior**:
```csharp
@bind="enableAllAI" @bind:after="UpdateAIControls"

private void UpdateAIControls()
{
    if (enableAllAI)
    {
        useSyncfusion = true;
        useGoogle = true;
        useClaudeVision = true;
        useClaudeValidation = true;
        useGroqValidation = true;
        useMultiStageValidation = true;
        useSignatureDetection = true;
    }
}
```

**Effect**: When enabled, all individual service checkboxes are checked and grayed out.

---

#### Individual Service Checkboxes

**1. Syncfusion**
- **Variable**: `useSyncfusion`
- **Default**: `true`
- **Purpose**: Native coordinate detection and ContentControl/FormField detection
- **Description**: "Coordinate detection"
- **Use Case**: Required for basic field detection, fastest option

**2. Google Document AI**
- **Variable**: `useGoogle`
- **Default**: `false`
- **Purpose**: Structured document analysis via Google Cloud API
- **Description**: "Structured analysis"
- **Cost**: ~$1.50 per 1,000 pages
- **Use Case**: High accuracy for structured forms

**3. Claude Vision**
- **Variable**: `useClaudeVision`
- **Default**: `false`
- **Purpose**: Visual field detection with AI labeling
- **Description**: "Visual field labeling"
- **Cost**: ~$0.10-0.30 per page
- **Use Case**: Complex layouts, unlabeled fields

**4. Claude Validation**
- **Variable**: `useClaudeValidation`
- **Default**: `false`
- **Purpose**: Bounding box validation and correction
- **Description**: "Fix bounding boxes"
- **Use Case**: Validate and correct coordinate errors

**5. Groq Label Validation**
- **Variable**: `useGroqValidation`
- **Default**: `false`
- **Status**: **DEPRECATED** - Use Multi-Stage instead
- **Purpose**: Label validation via Groq Llama API
- **Description**: "DEPRECATED - use Multi-Stage"

**6. Multi-Stage Validation** (Highlighted)
- **Variable**: `useMultiStageValidation`
- **Default**: `false`
- **Purpose**: Claude + GPT-5 consensus validation
- **Description**: "Claude + GPT-5 Consensus"
- **Cost**: 2x API costs (both Claude and GPT)
- **Use Case**: Critical forms requiring highest accuracy
- **Visual**: Blue background highlight

**7. Signature Auto-Detection**
- **Variable**: `useSignatureDetection`
- **Default**: `false`
- **Purpose**: Detect signature fields by X markers
- **Description**: "Find X markers"
- **Use Case**: Forms with signature lines marked with X

---

#### Debug Options

**1. Debug Mode**
- **Variable**: `debugMode`
- **Default**: `true`
- **Purpose**: Enable verbose logging and debug output

**2. Show Field IDs**
- **Variable**: `showFieldIds`
- **Default**: `true`
- **Purpose**: Display field IDs in UI for debugging

---

#### Quick Presets

**⚡ Fast (Syncfusion only)**
```csharp
private void SetPresetFast()
{
    enableAllAI = false;
    useSyncfusion = true;
    useGoogle = false;
    useClaudeVision = false;
    useClaudeValidation = false;
    useMultiStageValidation = false;
}
```
**Use Case**: Testing, simple forms, cost-sensitive processing

**🎯 Accurate (Syncfusion + Claude)**
```csharp
private void SetPresetAccurate()
{
    enableAllAI = false;
    useSyncfusion = true;
    useClaudeVision = true;
    useGoogle = false;
    useClaudeValidation = true;
    useMultiStageValidation = false;
}
```
**Use Case**: Production quality for most forms

**🔬 Comprehensive (All services)**
```csharp
private void SetPresetComprehensive()
{
    enableAllAI = true;
    UpdateAIControls();
}
```
**Use Case**: Critical forms, maximum accuracy, cost not a concern

---

### Section 2: Accessibility Services

**Visual**: White card with checkboxes for post-processing options

#### Auto-tagging Services

**Aspose Auto-tag**
- **Variable**: `useAsposeAutotag`
- **Default**: `true`
- **Purpose**: Local PDF/UA compliance processing
- **Description**: "Local processing, no credits"
- **Library**: Aspose.Pdf
- **Use Case**: Add structure tags without cloud API costs

---

#### Font & Optimization

**Aspose Font Embedding**
- **Variable**: `useAsposeFontEmbed`
- **Default**: `true`
- **Purpose**: Embed fonts before PassportPDF conversion
- **Description**: "Recommended for compliance"
- **Library**: Aspose.Pdf
- **Status**: Currently limited by missing Liberation fonts
- **Use Case**: Pre-processing font embedding

---

### Section 3: Drag & Drop Zone

**Visual**: Large white card with border, centered content

#### States

**State 1: Empty (No File Selected)**
```html
<div class="drop-zone-content">
    <div class="drop-icon mb-3">
        <svg><!-- Cloud upload icon --></svg>
    </div>
    <h4 class="mb-3">Drop Your Document Here</h4>
    <p class="text-muted mb-3">Supports Word (.docx) and PDF (.pdf) files</p>
    <p class="text-muted">or</p>
    <button class="btn btn-primary" @onclick="TriggerFileSelect">
        Browse Files
    </button>
    <div class="mt-3">
        <button class="btn btn-success" @onclick="TriggerFileSelectPassport">
            🎆 Browse Files (PassportPDF)
        </button>
        <small class="d-block text-muted mt-1">
            Uses PassportPDF for full PDF/UA compliance
        </small>
    </div>
</div>
```

**Buttons**:
- **Browse Files** - Standard file picker
- **Browse Files (PassportPDF)** - Same as above but labeled for clarity

---

**State 2: File Selected**
```html
<div class="file-selected">
    <div class="file-icon mb-3">
        <svg><!-- Word or PDF icon --></svg>
    </div>
    <h5>form.docx</h5>
    <p class="text-muted">2.4 MB • Word Document</p>
    <div class="mt-3">
        <button class="btn btn-success btn-lg me-2" @onclick="ProcessFile">
            <span class="me-2">🚀</span> Make Accessible
        </button>
        <button class="btn btn-outline-secondary" @onclick="ClearSelection">
            Choose Different File
        </button>
    </div>
</div>
```

**Buttons**:
- **🚀 Make Accessible** - Start processing with current configuration
- **Choose Different File** - Clear selection and return to empty state

---

**State 3: Processing**
```html
<div class="text-center">
    <div class="spinner-border text-primary mb-3" style="width: 3rem; height: 3rem;">
        <span class="visually-hidden">Processing...</span>
    </div>
    <h4>Processing Your Document</h4>
    <p class="text-muted small">AI Mode: true/false</p>
    <button class="btn btn-danger mt-3" @onclick="CancelProcessing">
        <i class="bi bi-x-circle"></i> Cancel Processing
    </button>

    @if (UseAiMode)
    {
        <div class="alert alert-info mt-3">
            <p class="mb-2">🤖 <strong>Anthropic Claude AI Analysis</strong></p>
            <p class="mb-1">Analyzing document structure and identifying form fields...</p>
            <p class="text-muted small">This can take up to 2 minutes for complex documents</p>
            <div class="fs-1 fw-bold text-primary mt-3">
                ⏱️ @processingTime
            </div>
        </div>
    }
</div>
```

**Features**:
- Animated spinner
- AI mode indicator
- Real-time processing timer (updates every second)
- Cancel button with `CancellationTokenSource`

**Timer Logic**:
```csharp
private System.Timers.Timer? processingTimer;
private DateTime processingStartTime;
private int processingSeconds = 0;

private void StartProcessingTimer()
{
    processingStartTime = DateTime.Now;
    processingTimer = new System.Timers.Timer(1000);
    processingTimer.Elapsed += (sender, e) =>
    {
        processingSeconds++;
        processingTime = $"{processingSeconds / 60}:{processingSeconds % 60:D2}";
        InvokeAsync(StateHasChanged);
    };
    processingTimer.Start();
}
```

---

### Section 4: Results Section

**Visual**: Animated card section (slides up when results ready)

#### Left Card: Standard/Original PDF

**Header**: "📄 Standard PDF" (if Word) or "📄 Original PDF" (if PDF)
**Content**:
```html
<div class="card h-100 border-secondary">
    <div class="card-header" style="background-color: #4a90e2; color: white;">
        <h5 class="mb-0">
            <span class="me-2">📄</span> Standard PDF
        </h5>
    </div>
    <div class="card-body">
        <p class="mb-2"><strong>File:</strong> form_normal.pdf</p>
        <p class="mb-3"><strong>Size:</strong> 245.6 KB</p>
        <p class="text-muted small">
            Converted from Word without accessibility features
        </p>
        <button class="btn btn-secondary w-100 mt-3" @onclick="() => DownloadFile(...)">
            <span class="me-2">⬇️</span> Download
        </button>
        <button class="btn btn-info w-100 mt-2" @onclick="() => ViewTagTree(...)">
            <span class="me-2">🔍</span> View Tag Structure
        </button>
    </div>
</div>
```

**Buttons**:
- **⬇️ Download** - Download non-accessible PDF
- **🔍 View Tag Structure** - Open tag structure viewer modal

---

#### Right Card: Accessible PDF

**Header**: "♿ Accessible PDF"
**Content**:
```html
<div class="card h-100 border-success shadow">
    <div class="card-header" style="background-color: #4a90e2; color: white;">
        <h5 class="mb-0">
            <span class="me-2">♿</span> Accessible PDF
        </h5>
    </div>
    <div class="card-body">
        <p class="mb-2"><strong>File:</strong> form_accessible.pdf</p>
        <p class="mb-2"><strong>Size:</strong> 287.3 KB</p>
        <p class="mb-3"><strong>Status:</strong>
            <span class="badge bg-success">PDF/A-2u Compliant</span>
        </p>

        <div class="improvements-list mb-3">
            <small class="text-muted">Improvements Applied:</small>
            <ul class="small mb-0 mt-1">
                <li>✓ 25 form fields enhanced</li>
                <li>✓ 8 accessibility measures</li>
                @if (processingResults.AiEnhanced)
                {
                    <li>✨ <strong>AI-Enhanced Processing</strong> (ClaudeVision)</li>
                    <li>🎯 AI Score: 92/100</li>
                }
                <li>✓ Screen reader optimized</li>
                <li>✓ Keyboard navigation enabled</li>
            </ul>
        </div>

        <button class="btn btn-success w-100" @onclick="() => DownloadFile(...)">
            <span class="me-2">⬇️</span> Download Accessible PDF
        </button>
        <button class="btn btn-info w-100 mt-2" @onclick="() => ViewTagTree(...)">
            <span class="me-2">🔍</span> View Tag Structure
        </button>
        <button class="btn btn-primary w-100 mt-2" @onclick="OpenTagEditor">
            <span class="me-2">✏️</span> Edit Fields & Tags
        </button>
        <button class="btn btn-warning w-100 mt-2" @onclick="() => ViewAiDebugResponses()">
            <span class="me-2">🐛</span> Debug AI Responses
        </button>

        @if (!string.IsNullOrEmpty(lastDebugId))
        {
            <div class="alert alert-info mt-2">
                <small>
                    <strong>Debug ID:</strong> <code>@lastDebugId</code><br/>
                    <a href="/debug-raw.html?id=@lastDebugId" target="_blank">
                        🔗 Raw Debug View
                    </a>
                </small>
            </div>
        }
    </div>
</div>
```

**Buttons**:
1. **⬇️ Download Accessible PDF** - Download PDF/UA compliant PDF
2. **🔍 View Tag Structure** - Open tag structure viewer
3. **✏️ Edit Fields & Tags** - Open field editor modal
4. **🐛 Debug AI Responses** - View cached AI responses
5. **🔗 Raw Debug View** - Link to raw JSON debug data

---

#### AI-Detected Tag Structure Section

**Condition**: Only shown if `processingResults.AiEnhanced && processingResults.TotalTagNodes > 0`

```html
<div class="card mt-4 border-info">
    <div class="card-header" style="background-color: #4a90e2; color: white;">
        <h5 class="mb-0">
            <span class="me-2">🏷️</span> AI-Detected Tag Structure
        </h5>
    </div>
    <div class="card-body">
        <div class="row">
            <div class="col-md-6">
                <h6>Document Structure Statistics</h6>
                <ul class="list-unstyled">
                    <li>📄 <strong>Total Tags:</strong> 127 nodes</li>
                    <li>📑 <strong>Headings:</strong> 5</li>
                    <li>📝 <strong>Paragraphs:</strong> 45</li>
                    <li>📋 <strong>Tables:</strong> 3</li>
                </ul>

                <h6 class="mt-3">Tag Distribution</h6>
                <div class="small">
                    <span class="badge bg-secondary me-1 mb-1">H1: 1</span>
                    <span class="badge bg-secondary me-1 mb-1">H2: 4</span>
                    <span class="badge bg-secondary me-1 mb-1">P: 45</span>
                    <!-- ... more tags ... -->
                </div>
            </div>

            <div class="col-md-6">
                <h6>Accessibility Recommendations</h6>
                <ul class="small">
                    <li>Add alt text to 2 images</li>
                    <li>Verify table header associations</li>
                </ul>
                <!-- OR if perfect: -->
                <div class="alert alert-success">
                    <strong>✅ Excellent!</strong> Document structure meets all accessibility standards.
                </div>
            </div>
        </div>

        <details class="mt-3">
            <summary class="btn btn-sm btn-outline-info">View Full Tag Tree</summary>
            <pre class="mt-2 p-2 bg-light" style="max-height: 300px; overflow-y: auto;">
                Document
                  ├─ Sect
                  │  ├─ H1: Form Title
                  │  ├─ P: Introduction text
                  │  └─ Form
                  │     ├─ FormField: EmployeeName (textbox)
                  │     └─ FormField: AgreementCheckbox (checkbox)
            </pre>
        </details>
    </div>
</div>
```

---

#### Bottom Action Buttons

```html
<div class="text-center mt-4">
    <a href="/api/accessibility-report/latest" target="_blank" class="btn btn-primary me-2">
        <span class="me-2">📊</span> View Detailed Report
    </a>
    <button class="btn btn-primary" @onclick="ProcessAnother">
        <span class="me-2">📁</span> Process Another Document
    </button>
</div>
```

**Buttons**:
1. **📊 View Detailed Report** - Opens HTML accessibility report in new tab
2. **📁 Process Another Document** - Reset to empty state

---

### Section 5: Info Cards (Bottom)

**Visual**: Three cards in a row

**Card 1: Smart Detection / AI-Powered Detection**
```html
<div class="info-card @(UseAiMode ? "ai-mode" : "")">
    <div class="info-icon">🤖</div>
    <h5>@(UseAiMode ? "AI-Powered Detection" : "Smart Detection")</h5>
    <p class="small text-muted">
        @(UseAiMode ?
          "Azure Form Recognizer and Llama AI for advanced field detection" :
          "Automatically detects document type and applies appropriate accessibility enhancements")
    </p>
</div>
```

**Card 2: Full Compliance**
```html
<div class="info-card">
    <div class="info-icon">♿</div>
    <h5>Full Compliance</h5>
    <p class="small text-muted">
        WCAG 2.1 AA and Section 508 compliant with detailed reporting
    </p>
</div>
```

**Card 3: Fast Processing / Intelligent Analysis**
```html
<div class="info-card @(UseAiMode ? "ai-mode" : "")">
    <div class="info-icon">⚡</div>
    <h5>@(UseAiMode ? "Intelligent Analysis" : "Fast Processing")</h5>
    <p class="small text-muted">
        @(UseAiMode ?
          "AI analyzes form structure and suggests optimal accessibility improvements" :
          "Algorithmic retrofitting provides instant accessibility improvements")
    </p>
</div>
```

---

### Tag Tree Viewer Modal

**Trigger**: Click "🔍 View Tag Structure" button
**Purpose**: Display PDF structure tree in hierarchical view

```html
<div class="modal fade" id="tagTreeModal" tabindex="-1">
    <div class="modal-dialog modal-xl modal-dialog-scrollable">
        <div class="modal-content">
            <div class="modal-header bg-primary text-white">
                <h5 class="modal-title">
                    <i class="bi bi-diagram-3"></i> PDF Tag Structure Viewer
                </h5>
                <button type="button" class="btn-close btn-close-white" data-bs-dismiss="modal"></button>
            </div>
            <div class="modal-body" id="tagTreeModalBody">
                <!-- Content loaded dynamically via JavaScript -->
            </div>
            <div class="modal-footer">
                <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Close</button>
                <button type="button" class="btn btn-primary" @onclick="ExportTagTree">
                    <i class="bi bi-download"></i> Export as JSON
                </button>
            </div>
        </div>
    </div>
</div>
```

**JavaScript Integration**:
```javascript
await JSRuntime.InvokeVoidAsync("window.showTagTree", tagTreeJson);
```

---

## Field Editor

**Location**: `Pages/FieldEditor.razor`
**Route**: `/field-editor`
**Purpose**: Standalone page for editing PDF form fields

### Layout

**Two-column layout**:
- **Left (7 cols)**: Field table editor with advanced properties
- **Right (5 cols)**: Visual PDF preview with field overlays

---

### Left Panel: Field Table Editor

#### Top Section: File Upload

```html
<div class="card mb-3">
    <div class="card-header">
        <h5>Load PDF for Editing</h5>
    </div>
    <div class="card-body">
        <div class="input-group">
            <input type="file" class="form-control" accept=".pdf" @ref="fileInput" />
            <button class="btn btn-primary" @onclick="LoadFieldsFromFile">
                Load Fields
            </button>
        </div>
        @if (!string.IsNullOrEmpty(CurrentFileName))
        {
            <small class="text-muted mt-2 d-block">
                Currently editing: @CurrentFileName
            </small>
        }
    </div>
</div>
```

**Button**: **Load Fields** - Calls `/api/extract-pdf-fields`

---

#### Field Table

**Header Buttons**:
```html
<div class="btn-group" role="group">
    <button class="btn btn-sm btn-primary" @onclick="AddField">
        <i class="bi bi-plus"></i> Add Field
    </button>
    <button class="btn btn-sm btn-success" @onclick="SaveChanges" disabled="@(!HasChanges)">
        <i class="bi bi-save"></i> Save Changes
    </button>
    <button class="btn btn-sm btn-info" @onclick="ExportFields">
        <i class="bi bi-download"></i> Export JSON
    </button>
</div>
```

**Table Columns**:
1. **#** - Short ID (field index)
2. **Field Name** - Editable text input
3. **Type** - Dropdown (textbox, checkbox, radio, combobox, signature)
4. **Page** - Number input
5. **X** - Number input (step 0.5)
6. **Y** - Number input (step 0.5)
7. **Width** - Number input (step 0.5)
8. **Height** - Number input (step 0.5)
9. **Actions** - Delete button (🗑️)

**Row Selection**: Click row to select field (highlights in blue)

**Editing Behavior**:
```csharp
// All inputs use @bind with @bind:event="oninput" for instant updates
// @onchange triggers MarkAsChanged(field) to track dirty state

private void MarkAsChanged(FieldDetectionResult field)
{
    field.IsDirty = true;
    HasChanges = true;
    StateHasChanged();
}
```

---

#### Advanced Field Properties Panel

**Condition**: Only shown when a field is selected

```html
<div class="card mt-3">
    <div class="card-header">
        <h6>Advanced Properties: @SelectedField.FieldName</h6>
    </div>
    <div class="card-body">
        <div class="row">
            <div class="col-md-6">
                <label class="form-label">Tooltip/Help Text</label>
                <input type="text" class="form-control form-control-sm"
                       @bind="SelectedField.Tooltip"
                       @bind:after="() => MarkAsChanged(SelectedField)" />
            </div>
            <div class="col-md-6">
                <label class="form-label">Validation Pattern</label>
                <input type="text" class="form-control form-control-sm"
                       @bind="SelectedField.ValidationPattern"
                       placeholder="e.g., ^\d{3}-\d{2}-\d{4}$ for SSN" />
            </div>
        </div>
        <div class="row mt-2">
            <div class="col-md-4">
                <label class="form-label">Source</label>
                <input type="text" class="form-control form-control-sm"
                       @bind="SelectedField.Source" readonly />
            </div>
            <div class="col-md-4">
                <label class="form-label">Confidence</label>
                <input type="number" class="form-control form-control-sm"
                       @bind="SelectedField.Confidence" readonly />
            </div>
            <div class="col-md-4">
                <label class="form-label">Tab Index</label>
                <input type="number" class="form-control form-control-sm"
                       @bind="SelectedField.TabIndex" />
            </div>
        </div>

        @if (SelectedField.FieldType == "radio")
        {
            <div class="row mt-2">
                <div class="col-md-12">
                    <label class="form-label">
                        Button Value
                        <small class="text-muted">
                            (Radio buttons with the same Field Name but different Button Values form a mutually exclusive group)
                        </small>
                    </label>
                    <input type="text" class="form-control form-control-sm"
                           @bind="SelectedField.ButtonValue"
                           placeholder="e.g., Yes, No, Option1, A, B, C" />
                </div>
            </div>
        }
    </div>
</div>
```

**Editable Properties**:
- Tooltip/Help Text
- Validation Pattern (regex)
- Tab Index
- Button Value (radio buttons only)

**Read-Only Properties**:
- Source (detection source: ClaudeVision, Syncfusion, etc.)
- Confidence (0.0 - 1.0)

---

### Right Panel: Visual Preview

**Structure**:
```html
<div class="col-md-5">
    <div class="card">
        <div class="card-header">
            <h5>PDF Preview</h5>
            <div class="btn-group btn-group-sm">
                <button class="btn btn-outline-secondary" @onclick="PreviousPage">
                    <i class="bi bi-chevron-left"></i> Previous
                </button>
                <span class="mx-2">Page @CurrentPage of @TotalPages</span>
                <button class="btn btn-outline-secondary" @onclick="NextPage">
                    Next <i class="bi bi-chevron-right"></i>
                </button>
            </div>
        </div>
        <div class="card-body" style="position: relative; overflow: auto;">
            <div class="pdf-preview-container" style="position: relative;">
                <img src="@($"data:image/png;base64,{CurrentPageImage}")"
                     style="width: 100%; display: block;" />

                <!-- Field overlays -->
                @foreach (var field in CurrentPageFields)
                {
                    <div class="field-overlay @(field.IsSelected ? "selected" : "")"
                         style="@GetFieldOverlayStyle(field)"
                         @onclick="() => SelectField(field)">
                        <div class="field-label">@field.FieldName</div>
                    </div>
                }
            </div>
        </div>
    </div>
</div>
```

**Field Overlay Styling**:
```csharp
private string GetFieldOverlayStyle(FieldDetectionResult field)
{
    // Convert field coordinates to percentage-based CSS
    var percentX = (field.X / pageWidth) * 100;
    var percentY = (field.Y / pageHeight) * 100;
    var percentWidth = (field.Width / pageWidth) * 100;
    var percentHeight = (field.Height / pageHeight) * 100;

    var color = field.IsSelected ? "rgba(255, 0, 0, 0.3)" : "rgba(0, 123, 255, 0.2)";
    var border = field.IsSelected ? "2px solid red" : "1px solid blue";

    return $"position: absolute; " +
           $"left: {percentX}%; " +
           $"top: {percentY}%; " +
           $"width: {percentWidth}%; " +
           $"height: {percentHeight}%; " +
           $"background: {color}; " +
           $"border: {border}; " +
           $"cursor: pointer;";
}
```

**Interaction**: Click field overlay to select it in table

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
                <span class="badge bg-warning ms-2" *ngIf="HasChanges">Unsaved changes</span>
            </div>
            <div>
                <button class="btn btn-secondary" @onclick="Cancel">Cancel</button>
                <button class="btn btn-primary" @onclick="SaveAndClose">Save Changes</button>
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
    <button class="position-btn" @onclick="() => AdjustPosition(field, 'x', -1)">◄</button>
    <input type="number" @bind="field.X" step="0.5" />
    <button class="position-btn" @onclick="() => AdjustPosition(field, 'x', 1)">►</button>

    <button class="position-btn" @onclick="() => AdjustPosition(field, 'y', -1)">▲</button>
    <input type="number" @bind="field.Y" step="0.5" />
    <button class="position-btn" @onclick="() => AdjustPosition(field, 'y', 1)">▼</button>
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
     style="position: fixed; left: 20px; top: 50px; width: 1200px; height: calc(100vh - 100px);">
    <div class="window-container">
        <!-- Header (draggable) -->
        <div class="window-header" id="tagModWindowHeader" style="cursor: move;">
            <h5><i class="fas fa-tags me-2"></i> Tag Structure & Field Editor</h5>
            <button type="button" class="btn-close btn-close-white" @onclick="Cancel"></button>
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
            <div class="field-overlay @GetFieldClass(field.Type) @(SelectedField == field ? "selected" : "")"
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
            EditableFields = EditableFields.OrderBy(f => f.Type).ThenBy(f => f.Name).ToList();
            break;
        case "page":
            EditableFields = EditableFields.OrderBy(f => f.PageNumber).ThenBy(f => f.Y).ToList();
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
     style="position: fixed; right: 20px; top: 50px; width: 550px; height: calc(100vh - 100px);">
    <div class="card" style="height: 100%;">
        <div class="card-header bg-warning text-dark" id="cascadeWindowHeader" style="cursor: move;">
            <h5><i class="bi bi-exclamation-triangle"></i> Field Cascade Correction</h5>
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
               @onkeypress="@(async (e) => { if (e.Key == "Enter") await ExecuteCommand(); })"
               placeholder="Enter command: phantom 2, cascade 3, rename 5 'New Name', auto, preview, reset">
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
    <pre class="bg-dark text-light p-2 rounded font-monospace" style="font-size: 0.85em;">
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

## User Workflows

### Workflow 1: Process Word Document with AI

1. **Open home page** (Index.razor)
2. **Configure AI services**:
   - Enable "🚀 Enable All AI Features" OR
   - Click "🎯 Accurate" preset OR
   - Manually select: Syncfusion + Claude Vision
3. **Upload Word document**:
   - Drag & drop into zone OR
   - Click "Browse Files"
4. **Click "🚀 Make Accessible"**
5. **Wait for processing** (timer shows progress)
6. **View results**:
   - Download accessible PDF
   - View tag structure
   - Edit fields if needed
7. **Optional**: Click "✏️ Edit Fields & Tags" to open field editor

---

### Workflow 2: Edit PDF Fields

1. **Open field editor** (`/field-editor`)
2. **Load PDF**: Click "Load Fields" and select PDF
3. **Edit fields in table**:
   - Change field names
   - Adjust coordinates (X, Y, Width, Height)
   - Change field types
   - Set tooltips
4. **Visual preview**: See changes in right panel
5. **Click "Save Changes"**
6. **Download updated PDF**

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

### Workflow 4: Tag Structure Analysis

1. **Process document** with AI
2. **Click "🔍 View Tag Structure"**
3. **View hierarchical tree**:
   - Document → Sections → Headings → Paragraphs → Forms
4. **Check statistics**:
   - Total tags
   - Headings count
   - Paragraphs count
   - Tables count
5. **Review recommendations** (if any violations)
6. **Export as JSON** for external analysis

---

## Accessibility Features

### Keyboard Navigation

**Index Page**:
- `Tab` - Navigate through checkboxes and buttons
- `Space` - Toggle checkboxes
- `Enter` - Activate focused button

**Field Editor**:
- `Tab` - Navigate through table cells
- `Arrow keys` - Move between fields in table
- `Enter` - Start editing cell
- `Escape` - Cancel editing

**Modals**:
- `Escape` - Close modal
- `Tab` - Navigate modal controls

---

### Screen Reader Support

**ARIA Labels**:
```html
<button aria-label="Download accessible PDF" class="btn btn-success">
    <span class="me-2">⬇️</span> Download Accessible PDF
</button>

<div role="alert" aria-live="polite" *ngIf="processingMessage">
    @processingMessage
</div>

<input type="checkbox"
       aria-labelledby="useSyncfusion-label"
       @bind="useSyncfusion" />
<label id="useSyncfusion-label">Syncfusion</label>
```

**Live Regions**:
- Processing status updates announced to screen readers
- Error messages announced immediately
- Success messages announced on completion

---

## Styling & Theming

### Color Scheme

**Primary Color**: `#4a90e2` (Light Royal Blue)
- Used for all primary buttons
- Card headers
- Active states

**Success Color**: `#28a745` (Green)
- Download buttons
- Success badges
- Checkmarks

**Warning Color**: `#ffc107` (Yellow)
- Cascade correction panel
- Warning badges

**Danger Color**: `#dc3545` (Red)
- Delete buttons
- Error messages

---

### Responsive Design

**Breakpoints**:
```css
/* Mobile (< 768px) */
@media (max-width: 768px) {
    .drop-zone {
        padding: 40px 20px;
        min-height: 300px;
    }

    .info-card {
        margin-bottom: 20px;
    }

    .col-md-7, .col-md-5 {
        width: 100%; /* Stack columns */
    }
}
```

**Mobile Optimizations**:
- Single-column layout on mobile
- Smaller padding and font sizes
- Touch-friendly button sizes (min 44x44px)

---

**End of ARCHITECTURE-GUI.md**

**See also:**
- **ARCHITECTURE.md** - Main processing paths
- **ARCHITECTURE-SERVICES.md** - Service class details
- **ARCHITECTURE-API.md** - API endpoints
