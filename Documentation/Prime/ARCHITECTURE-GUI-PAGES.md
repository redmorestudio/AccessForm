# AccessForm GUI Architecture - Pages Documentation

**Last Updated:** 2025-10-26

## Overview

This document provides detailed documentation for the main page components in AccessForm. The application uses Blazor Server for server-side rendering with real-time SignalR communication.

**Related Documentation:**
- [ARCHITECTURE-GUI-MODALS.md](ARCHITECTURE-GUI-MODALS.md) - Modal components documentation
- [ARCHITECTURE.md](ARCHITECTURE.md) - Main processing paths
- [ARCHITECTURE-SERVICES.md](ARCHITECTURE-SERVICES.md) - Service class details
- [ARCHITECTURE-API.md](ARCHITECTURE-API.md) - API endpoints

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

## Section 1: Field Detection Options

**Visual**: White card with checkboxes and presets
**Purpose**: Configure which AI and detection services to use for field detection

### Master Toggle

**Control**: `🚀 Enable All AI Features` checkbox
**Variable**: `enableAllAI`
**Default**: `true`
**Visual**: Green highlighted box with border

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
        useGroqValidation = false;  // Deprecated
        useMultiStageValidation = true;
        useSignatureDetection = true;
    }
    else
    {
        // Disable all services (user can manually re-enable)
        useSyncfusion = false;
        useGoogle = false;
        useClaudeVision = false;
        useClaudeValidation = false;
        useGroqValidation = false;
        useMultiStageValidation = false;
        useSignatureDetection = false;
    }
}
```

**Effect**: When enabled, all individual service checkboxes are checked and grayed out (disabled).

---

### Individual Service Checkboxes

**1. Syncfusion**
- **Variable**: `useSyncfusion`
- **Default**: `true` (when enableAllAI is on)
- **Purpose**: Native coordinate detection and ContentControl/FormField detection
- **Description**: "Coordinate detection"
- **Use Case**: Required for basic field detection, fastest option
- **Behavior**: Disabled when master toggle is on

**2. Google Document AI**
- **Variable**: `useGoogle`
- **Default**: `false`
- **Status**: Currently not available (grayed out)
- **Purpose**: Structured document analysis via Google Cloud API
- **Description**: "Not Available"
- **Cost**: ~$1.50 per 1,000 pages
- **Use Case**: High accuracy for structured forms (when available)

**3. Claude Vision**
- **Variable**: `useClaudeVision`
- **Default**: `false` (enabled when enableAllAI is on)
- **Purpose**: Visual field detection with AI labeling
- **Description**: "Visual field labeling"
- **Cost**: ~$0.10-0.30 per page
- **Use Case**: Complex layouts, unlabeled fields
- **Behavior**: Disabled when master toggle is on

**4. Claude Validation**
- **Variable**: `useClaudeValidation`
- **Default**: `false` (enabled when enableAllAI is on)
- **Purpose**: Bounding box validation and correction
- **Description**: "Fix bounding boxes"
- **Use Case**: Validate and correct coordinate errors
- **Behavior**: Disabled when master toggle is on

**5. Groq Label Validation**
- **Variable**: `useGroqValidation`
- **Default**: `false`
- **Status**: **DEPRECATED** - Not available
- **Purpose**: Label validation via Groq Llama API
- **Description**: "Not Available"
- **Note**: Replaced by Multi-Stage Validation

**6. Multi-Stage Validation** (Highlighted)
- **Variable**: `useMultiStageValidation`
- **Default**: `false` (enabled when enableAllAI is on)
- **Purpose**: Claude + GPT-5 consensus validation
- **Description**: "Claude + GPT-5 Consensus"
- **Cost**: 2x API costs (both Claude and GPT)
- **Use Case**: Critical forms requiring highest accuracy
- **Visual**: Blue background highlight
- **Behavior**: Disabled when master toggle is on

**7. Signature Auto-Detection**
- **Variable**: `useSignatureDetection`
- **Default**: `false` (enabled when enableAllAI is on)
- **Purpose**: Detect signature fields by X markers
- **Description**: "Find X markers"
- **Use Case**: Forms with signature lines marked with X
- **Behavior**: Disabled when master toggle is on

---

### Debug Options

**Purpose**: Control verbose logging and field ID display

**1. Debug Mode**
- **Variable**: `debugMode`
- **Default**: `true`
- **Purpose**: Enable verbose logging and debug output
- **Effect**: Shows detailed processing information in console and reports

**2. Show Field IDs**
- **Variable**: `showFieldIds`
- **Default**: `true`
- **Purpose**: Display field IDs in UI for debugging
- **Effect**: Adds field identifiers to generated PDFs for tracking

---

### Quick Presets

**Purpose**: One-click configuration for common use cases

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
    useSignatureDetection = false;
}
```
**Use Case**: Testing, simple forms, cost-sensitive processing
**Speed**: Fastest (~10-30 seconds)
**Accuracy**: Basic

**🎯 Accurate (Syncfusion + Claude + Multi-Stage)**
```csharp
private void SetPresetAccurate()
{
    enableAllAI = false;
    useSyncfusion = true;
    useClaudeVision = false;
    useClaudeValidation = true;
    useMultiStageValidation = true;
    useSignatureDetection = true;
}
```
**Use Case**: Production quality for most forms
**Speed**: Medium (~1-3 minutes)
**Accuracy**: High

**🔬 Comprehensive (All services)**
```csharp
private void SetPresetComprehensive()
{
    enableAllAI = true;
    UpdateAIControls();
}
```
**Use Case**: Critical forms, maximum accuracy, cost not a concern
**Speed**: Slowest (~2-5 minutes)
**Accuracy**: Maximum

---

## Section 2: Accessibility Services

**Visual**: White card with checkboxes for post-processing options
**Purpose**: Configure PDF/UA compliance and font optimization

### Auto-tagging Services

**Aspose Auto-tag**
- **Variable**: `useAsposeAutotag`
- **Default**: `true`
- **Purpose**: Local PDF/UA compliance processing
- **Description**: "Local processing, no credits"
- **Library**: Aspose.Pdf
- **Use Case**: Add structure tags without cloud API costs
- **Process**: Runs after field detection, before final output

**Note**: The alert below the checkboxes reads:
> "**Note:** Aspose Auto-tag provides local PDF/UA compliance processing without API costs."

---

### Font & Optimization

**Aspose Font Embedding**
- **Variable**: `useAsposeFontEmbed`
- **Default**: `true`
- **Purpose**: Embed fonts before PassportPDF conversion
- **Description**: "Recommended for compliance"
- **Library**: Aspose.Pdf
- **Use Case**: Pre-processing font embedding for PDF/A compliance
- **Current Status**: Limited by missing Liberation fonts, but still provides value

**Note**: PassportPDF OCR checkbox was removed - OCR is now handled by Marker/markdown conversion

---

## Section 3: Drag & Drop Zone

**Visual**: Large white card with border, centered content
**Minimum Height**: 400px
**CSS Class**: `drop-zone`
**Behavior**: Interactive zone that changes appearance based on state

### States

The drop zone has four distinct states:

---

#### State 1: Empty (No File Selected)

**Condition**: `!isProcessing && selectedFile == null`

```html
<div class="drop-zone-content">
    <div class="drop-icon mb-3">
        <svg width="64" height="64" fill="currentColor" viewBox="0 0 16 16">
            <!-- Cloud upload icon -->
        </svg>
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
    <InputFile id="fileInput" class="d-none" accept=".docx,.pdf" />
</div>
```

**Buttons**:
- **Browse Files** - Standard file picker for AI processing
- **Browse Files (PassportPDF)** - File picker that uses PassportPDF closed-loop remediation
- **Hidden Input** - Blazor InputFile component

**Visual**: Gray cloud icon, subtle appearance

---

#### State 2: File Selected

**Condition**: `!isProcessing && selectedFile != null`

```html
<div class="file-selected">
    <div class="file-icon mb-3">
        @if (IsWordFile(selectedFile.Name))
        {
            <!-- Word icon (blue) -->
        }
        else
        {
            <!-- PDF icon (red) -->
        }
    </div>
    <h5>@selectedFile.Name</h5>
    <p class="text-muted">@GetFileSize(selectedFile.Size) • @GetFileType(selectedFile.Name)</p>
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

**Visual**: File icon changes based on type, shows file name and size

---

#### State 3: Processing (Word Documents)

**Condition**: `isProcessing && selectedFile is Word file`

```html
<div class="processing-container">
    <h4 class="text-center mb-4">Processing Your Document</h4>

    <!-- Processing steps checklist -->
    <div class="processing-steps-list mb-4">
        @foreach (var step in processingSteps)
        {
            <div class="processing-step d-flex align-items-center mb-2
                 @(step.IsActive ? "active-step" : "")">
                <div class="step-checkbox me-3">
                    @if (step.IsComplete)
                    {
                        <i class="bi bi-check-circle-fill text-success fs-5"></i>
                    }
                    else if (step.IsActive)
                    {
                        <div class="spinner-border spinner-border-sm text-primary">
                            <span class="visually-hidden">Processing...</span>
                        </div>
                    }
                    else
                    {
                        <i class="bi bi-circle text-muted fs-5"></i>
                    }
                </div>
                <div class="step-content flex-grow-1">
                    <div class="step-name">@step.Name</div>
                    @if (!string.IsNullOrEmpty(step.Details))
                    {
                        <small class="text-muted">@step.Details</small>
                    }
                </div>
            </div>
        }
    </div>

    <!-- Timer display -->
    <div class="text-center mb-3">
        <div class="fs-2 fw-bold text-primary">
            ⏱️ @processingTime
        </div>
        <p class="text-muted small mb-0">AI Mode: @UseAiMode</p>
    </div>

    <!-- Action buttons -->
    <div class="text-center">
        <button class="btn btn-warning btn-lg mb-2 w-75" @onclick="DownloadCurrentPdf">
            <span class="me-2">⬇️</span> Download Current PDF Now
        </button>
        <br/>
        <button class="btn btn-danger mt-2" @onclick="CancelProcessing">
            <i class="bi bi-x-circle"></i> Cancel Processing
        </button>
    </div>
</div>
```

**Processing Steps for Word Documents**:
1. Converting Word to PDF
2. Detecting form fields
3. Applying accessibility measures
4. Generating accessible PDF
5. Creating report

**Features**:
- Real-time checklist showing current step
- Animated spinner for active step
- Green checkmarks for completed steps
- Real-time processing timer (updates every second)
- Download button to get intermediate PDF
- Cancel button with `CancellationTokenSource`

---

#### State 4: Processing (PDF Documents with PassportPDF)

**Condition**: `isProcessing && selectedFile is PDF file`

**Processing Steps for PDF Documents** (see Section 4: Remediation Modal in ARCHITECTURE-GUI-MODALS.md):
1. Analyzing PDF structure
2. Detecting existing form fields
3. Preserving field calculations
4. Initial PDF/UA validation
5. Whitespace cleanup
6. Content remediation
7. Structure enhancement
8. Form field remediation
9. Table and list fixes
10. Font and PDF/A conversion
11. Alternative text
12. Post-remediation cleanup
13. GPT fallback remediation
14. Metadata finalization
15. Final validation
16. Generating report

**Visual Differences**:
- More detailed step breakdown (16 steps vs 5)
- Shows iteration counts for remediation loops
- Displays compliance percentage improvements
- Real-time violation count updates

---

### Timer Logic

```csharp
private System.Timers.Timer? processingTimer;
private DateTime processingStartTime;
private string processingTime = "0:00";
private int processingSeconds = 0;

private void StartProcessingTimer()
{
    processingStartTime = DateTime.Now;
    processingSeconds = 0;
    processingTimer = new System.Timers.Timer(1000);
    processingTimer.Elapsed += (sender, e) =>
    {
        processingSeconds++;
        processingTime = $"{processingSeconds / 60}:{processingSeconds % 60:D2}";
        InvokeAsync(StateHasChanged);
    };
    processingTimer.Start();
}

private void StopProcessingTimer()
{
    if (processingTimer != null)
    {
        processingTimer.Stop();
        processingTimer.Dispose();
        processingTimer = null;
    }
}
```

**Features**:
- Updates every second
- Formats as M:SS
- Runs until processing completes or is cancelled

---

### Processing Step Management

```csharp
private class ProcessingStep
{
    public string Name { get; set; }
    public bool IsComplete { get; set; }
    public bool IsActive { get; set; }
    public string Details { get; set; }
}

private List<ProcessingStep> processingSteps = new();

private void UpdateProcessingStep(string stepName, bool isComplete = false,
                                   string details = null)
{
    // Mark all steps as not active first
    foreach (var step in processingSteps)
    {
        step.IsActive = false;
    }

    // Map backend phase names to UI step names
    var stepMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Validating PDF", "Initial PDF/UA validation" },
        { "Whitespace Cleanup", "Whitespace cleanup" },
        { "Content Remediation", "Content remediation" },
        // ... more mappings
    };

    // Find and update the matching step
    var matchingStep = processingSteps.FirstOrDefault(s =>
        s.Name.Equals(uiStepName, StringComparison.OrdinalIgnoreCase) ||
        s.Name.Contains(stepName, StringComparison.OrdinalIgnoreCase) ||
        stepName.Contains(s.Name, StringComparison.OrdinalIgnoreCase));

    if (matchingStep != null)
    {
        if (isComplete)
        {
            matchingStep.IsComplete = true;
            matchingStep.IsActive = false;
        }
        else
        {
            matchingStep.IsActive = true;
        }

        if (!string.IsNullOrEmpty(details))
        {
            matchingStep.Details = details;
        }
    }
}
```

---

## Section 4: Results Section

**Visual**: Animated card section (slides up when results ready)
**Condition**: `processingResults != null`
**Animation**: `slideUp 0.5s ease`

### Results Overview

The results section displays two side-by-side cards:
- **Left**: Standard/Original PDF
- **Right**: Accessible PDF (with validation results)

---

### Left Card: Standard/Original PDF

**Header**: "📄 Standard PDF" (if Word) or "📄 Original PDF" (if PDF)
**Border**: Secondary color
**Purpose**: Provide baseline comparison

```html
<div class="col-md-6 mb-3">
    <div class="card h-100 border-secondary">
        <div class="card-header" style="background-color: #4a90e2; color: white;">
            <h5 class="mb-0">
                <span class="me-2">📄</span>
                @if (processingResults.IsWordSource)
                {
                    <span>Standard PDF</span>
                }
                else
                {
                    <span>Original PDF</span>
                }
            </h5>
        </div>
        <div class="card-body">
            <p class="mb-2"><strong>File:</strong> @processingResults.NormalFileName</p>
            <p class="mb-3"><strong>Size:</strong> @GetFileSize(processingResults.NormalFileSize)</p>
            <p class="text-muted small">
                @(processingResults.IsWordSource ?
                  "Converted from Word without accessibility features" :
                  "Your original PDF without modifications")
            </p>
            <button class="btn btn-secondary w-100 mt-3"
                    @onclick="() => DownloadFile(processingResults.NormalFileData,
                                                 processingResults.NormalFileName)">
                <span class="me-2">⬇️</span> Download
            </button>

            <button class="btn btn-info w-100 mt-2"
                    @onclick="() => ViewTagTree(processingResults.NormalFileData,
                                                processingResults.NormalFileName)">
                <span class="me-2">🔍</span> View Tag Structure
            </button>
        </div>
    </div>
</div>
```

**Buttons**:
- **⬇️ Download** - Download non-accessible PDF
- **🔍 View Tag Structure** - Open tag structure viewer modal (see ARCHITECTURE-GUI-MODALS.md)

---

### Right Card: Accessible PDF

**Header**: "♿ Accessible PDF"
**Border**: Success color with shadow
**Purpose**: Showcase enhanced accessible document

```html
<div class="col-md-6 mb-3">
    <div class="card h-100 border-success shadow">
        <div class="card-header" style="background-color: #4a90e2; color: white;">
            <h5 class="mb-0">
                <span class="me-2">♿</span> Accessible PDF
            </h5>
        </div>
        <div class="card-body">
            <p class="mb-2"><strong>File:</strong> @processingResults.AccessibleFileName</p>
            <p class="mb-2"><strong>Size:</strong> @GetFileSize(processingResults.AccessibleFileSize)</p>
            <p class="mb-3">
                <strong>Status:</strong>
                <span class="badge bg-success">@processingResults.ComplianceLevel</span>
            </p>

            <div class="improvements-list mb-3">
                <small class="text-muted">Improvements Applied:</small>
                <ul class="small mb-0 mt-1">
                    <li>✓ @processingResults.FieldsProcessed form fields enhanced</li>
                    <li>✓ @processingResults.MeasuresApplied accessibility measures</li>
                    @if (processingResults.AiEnhanced)
                    {
                        <li>✨ <strong>AI-Enhanced Processing</strong>
                            (@processingResults.AiProvider)</li>
                        <li>🎯 AI Score: @processingResults.AccessibilityScore/100</li>
                    }
                    <li>✓ Screen reader optimized</li>
                    <li>✓ Keyboard navigation enabled</li>
                </ul>
            </div>

            <!-- Validation Results (if enabled) -->
            @if (processingResults.Validation != null && processingResults.Validation.Enabled)
            {
                <!-- See Validation Results section below -->
            }

            <!-- Action Buttons -->
            <button class="btn btn-success w-100"
                    @onclick="() => DownloadFile(processingResults.AccessibleFileData,
                                                 processingResults.AccessibleFileName)">
                <span class="me-2">⬇️</span> Download Accessible PDF
            </button>

            <button class="btn btn-info w-100 mt-2"
                    @onclick="() => ViewTagTree(processingResults.AccessibleFileData,
                                                processingResults.AccessibleFileName)">
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
                        <a href="/debug-raw.html?id=@lastDebugId" target="_blank"
                           class="text-decoration-none">
                            🔗 Raw Debug View
                        </a>
                    </small>
                </div>
            }
        </div>
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

### Validation Results Display

**Condition**: `processingResults.Validation != null && processingResults.Validation.Enabled`
**Purpose**: Show closed-loop remediation results from PassportPDF processing

```html
<div class="validation-results mb-3">
    <div class="alert @(processingResults.PdfUACompliant ? "alert-success" : "alert-warning") mb-2">
        <strong>PDF/UA Validation:</strong>
        @if (processingResults.PdfUACompliant)
        {
            <span class="badge bg-success">✓ Compliant</span>
        }
        else
        {
            <span class="badge bg-warning text-dark">
                ⚠ @processingResults.ViolationsRemaining violations remaining
            </span>
        }
    </div>

    <small class="text-muted">Remediation Summary:</small>
    <ul class="small mb-0 mt-1">
        <li>🔄 Iterations: @processingResults.RemediationIterations
            (@processingResults.Validation.Duration)</li>
        <li>📊 Starting Violations: @processingResults.Validation.InitialViolations</li>
        <li>✓ Violations Fixed: @processingResults.ViolationsFixed</li>
        <li>⚠️ Violations Remaining: @processingResults.ViolationsRemaining</li>
        @if (processingResults.Validation.ComplianceImprovement > 0)
        {
            <li>📈 Compliance Improved:
                +@processingResults.Validation.ComplianceImprovement.ToString("F1")%</li>
        }
        <li>🏁 Exit Reason: @processingResults.ExitReason</li>
    </ul>

    <!-- Expandable sections -->
    @if (processingResults.Validation.Recommendations.Any())
    {
        <details class="mt-2">
            <summary class="text-muted small" style="cursor: pointer;">
                View Recommendations (@processingResults.Validation.Recommendations.Count)
            </summary>
            <!-- Recommendations list -->
        </details>
    }

    @if (processingResults.Validation.IterationHistory.Any())
    {
        <details class="mt-2">
            <summary class="text-muted small" style="cursor: pointer;">
                View Iteration History
                (@processingResults.Validation.IterationHistory.Count iterations)
            </summary>
            <!-- Iteration history table -->
        </details>
    }

    @if (processingResults.Validation.InitialViolationsDetailed.Any())
    {
        <details class="mt-2">
            <summary class="text-muted small" style="cursor: pointer;">
                View Initial Violations - Before Remediation
                (@processingResults.Validation.InitialViolationsDetailed.Count)
            </summary>
            <!-- Violations list -->
        </details>
    }

    @if (processingResults.Validation.RemainingViolations.Any())
    {
        <details class="mt-2">
            <summary class="text-muted small" style="cursor: pointer;">
                View Remaining Violations - After Remediation
                (@processingResults.Validation.RemainingViolations.Count)
            </summary>
            <!-- Violations list -->
        </details>
    }
</div>
```

**Key Metrics**:
- **Iterations**: Number of remediation loops performed
- **Duration**: Total time spent in remediation
- **Starting Violations**: Count before remediation
- **Violations Fixed**: Successfully remediated violations
- **Violations Remaining**: Still present after max iterations
- **Compliance Improvement**: Percentage increase
- **Exit Reason**: Why remediation stopped (e.g., "Compliant", "MaxIterations", "NoProgress")

---

### AI-Detected Tag Structure Section

**Condition**: `processingResults.AiEnhanced && processingResults.TotalTagNodes > 0`
**Purpose**: Show detailed tag structure analysis from AI processing

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
                    <li>📄 <strong>Total Tags:</strong> @processingResults.TotalTagNodes nodes</li>
                    <li>📑 <strong>Headings:</strong> @processingResults.TotalHeadings</li>
                    <li>📝 <strong>Paragraphs:</strong> @processingResults.TotalParagraphs</li>
                    <li>📋 <strong>Tables:</strong> @processingResults.TotalTables</li>
                </ul>

                @if (processingResults.TagCounts.Any())
                {
                    <h6 class="mt-3">Tag Distribution</h6>
                    <div class="small">
                        @foreach (var tag in processingResults.TagCounts.OrderBy(t => t.Key))
                        {
                            <span class="badge bg-secondary me-1 mb-1">
                                @tag.Key: @tag.Value
                            </span>
                        }
                    </div>
                }
            </div>

            <div class="col-md-6">
                @if (processingResults.TagRecommendations.Any())
                {
                    <h6>Accessibility Recommendations</h6>
                    <ul class="small">
                        @foreach (var rec in processingResults.TagRecommendations)
                        {
                            <li>@rec</li>
                        }
                    </ul>
                }
                else
                {
                    <div class="alert alert-success">
                        <strong>✅ Excellent!</strong> Document structure meets all
                        accessibility standards.
                    </div>
                }
            </div>
        </div>

        @if (!string.IsNullOrEmpty(processingResults.TagTree))
        {
            <details class="mt-3">
                <summary class="btn btn-sm btn-outline-info">View Full Tag Tree</summary>
                <pre class="mt-2 p-2 bg-light"
                     style="max-height: 300px; overflow-y: auto; font-size: 0.85em;">
                    @processingResults.TagTree
                </pre>
            </details>
        }
    </div>
</div>
```

**Example Tag Tree Output**:
```
Document
  ├─ Sect
  │  ├─ H1: Form Title
  │  ├─ P: Introduction text
  │  └─ Form
  │     ├─ FormField: EmployeeName (textbox)
  │     └─ FormField: AgreementCheckbox (checkbox)
```

---

### Bottom Action Buttons

```html
<div class="text-center mt-4">
    <a href="/api/accessibility-report/latest" target="_blank"
       class="btn btn-primary me-2">
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

## Section 5: Info Cards (Bottom)

**Visual**: Three cards in a row
**Purpose**: Highlight key features of the system

### Card 1: Smart Detection / AI-Powered Detection

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

**Dynamic Content**: Changes based on whether AI mode is enabled

### Card 2: Full Compliance

```html
<div class="info-card">
    <div class="info-icon">♿</div>
    <h5>Full Compliance</h5>
    <p class="small text-muted">
        WCAG 2.1 AA and Section 508 compliant with detailed reporting
    </p>
</div>
```

**Static Content**: Always displays compliance standards

### Card 3: Fast Processing / Intelligent Analysis

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

**Dynamic Content**: Changes based on whether AI mode is enabled

---

## Field Editor Page

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
                            (Radio buttons with the same Field Name but different
                            Button Values form a mutually exclusive group)
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
5. **Wait for processing** (timer shows progress, checklist shows steps)
6. **View results**:
   - Download accessible PDF
   - View tag structure
   - Review improvements and AI scores
7. **Optional**: Click "✏️ Edit Fields & Tags" to open field editor

---

### Workflow 2: Process PDF with Closed-Loop Remediation

1. **Open home page** (Index.razor)
2. **Click "🎆 Browse Files (PassportPDF)"**
3. **Select PDF file**
4. **Watch real-time remediation progress**:
   - 16-step checklist shows current phase
   - Timer tracks processing time
   - Can download intermediate PDF at any point
5. **View detailed validation results**:
   - Compliance status
   - Iterations performed
   - Violations fixed vs remaining
   - Iteration history with metrics
6. **Review violation details**:
   - Initial violations (before remediation)
   - Remaining violations (after remediation)
   - Each with hints and context

---

### Workflow 3: Edit PDF Fields

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
- Consistent across all buttons (secondary, info, primary, success use same color)

**Success Color**: `#28a745` (Green)
- Used only for badges
- Success alerts
- Checkmarks

**Warning Color**: `#ffc107` (Yellow)
- Warning badges
- Validation alerts

**Danger Color**: `#dc3545` (Red)
- Delete buttons
- Error messages
- Cancel buttons

---

### Button Styling

**All buttons use consistent royal blue color**:
```css
.btn-secondary, .btn-info, .btn-primary, .btn-success,
.btn-outline-secondary, .btn-outline-info {
    background-color: #4a90e2 !important;
    border-color: #4a90e2 !important;
    color: white !important;
}

.btn-secondary:hover, .btn-info:hover, .btn-primary:hover,
.btn-success:hover, .btn-outline-secondary:hover, .btn-outline-info:hover {
    background-color: #357abd !important;
    border-color: #357abd !important;
    color: white !important;
}

.btn {
    border-radius: 8px;
    padding: 10px 20px;
    font-weight: 500;
    transition: all 0.3s ease;
}

.btn:hover {
    transform: translateY(-2px);
    box-shadow: 0 4px 12px rgba(0,0,0,0.15);
}
```

---

### Card Styling

```css
.card {
    border-radius: 15px;
    overflow: hidden;
    transition: transform 0.3s ease, box-shadow 0.3s ease;
    box-shadow: 0 4px 15px rgba(0,0,0,0.2);
    background: white;
}

.card:hover {
    transform: translateY(-3px);
    box-shadow: 0 8px 25px rgba(0,0,0,0.3);
}
```

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
- Stacked results cards

---

### Animations

```css
@keyframes fadeIn {
    from { opacity: 0; }
    to { opacity: 1; }
}

@keyframes slideUp {
    from {
        opacity: 0;
        transform: translateY(20px);
    }
    to {
        opacity: 1;
        transform: translateY(0);
    }
}

.animate-in {
    animation: slideUp 0.5s ease;
}
```

---

**End of ARCHITECTURE-GUI-PAGES.md**

**See also:**
- **[ARCHITECTURE-GUI-MODALS.md](ARCHITECTURE-GUI-MODALS.md)** - Modal components documentation
- **[ARCHITECTURE.md](ARCHITECTURE.md)** - Main processing paths
- **[ARCHITECTURE-SERVICES.md](ARCHITECTURE-SERVICES.md)** - Service class details
- **[ARCHITECTURE-API.md](ARCHITECTURE-API.md)** - API endpoints
