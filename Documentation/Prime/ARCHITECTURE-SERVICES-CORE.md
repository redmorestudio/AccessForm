# AccessForm Core Service Architecture

**Last Updated:** 2025-10-26

## Overview

This document details the core pipeline, AI detection, and field management services that form the foundation of AccessForm's document processing capabilities.

**Navigation:**
- [ARCHITECTURE-SERVICES-REMEDIATION.md](ARCHITECTURE-SERVICES-REMEDIATION.md) - PDF/UA remediation services
- [ARCHITECTURE-SERVICES-ACCESSIBILITY.md](ARCHITECTURE-SERVICES-ACCESSIBILITY.md) - Accessibility & utility services
- [ARCHITECTURE.md](ARCHITECTURE.md) - Main processing paths
- [ARCHITECTURE-API.md](ARCHITECTURE-API.md) - API endpoints

---

## Core Pipeline Services

These services orchestrate the main processing flows for Word→PDF and PDF→PDF conversion.

### ConfigurableFieldDetectionService

**Location**: `Services/ConfigurableFieldDetectionService.cs`
**Purpose**: Orchestrates AI-powered field detection using multiple services (Syncfusion, Claude, Google, GPT)
**Dependencies**: AnthropicService, ClaudeVisionFieldDetector, GoogleDocumentAiService, MultiStageValidationService, ClaudeBoundingBoxValidator

**Key Methods**:

#### `ConvertWithConfig(byte[] wordBytes, string fileName, FieldDetectionConfig config)`
Converts Word document to PDF with configurable field detection.

**Parameters**:
- `wordBytes` - Word document as byte array
- `fileName` - Original filename
- `config` - Configuration specifying which AI services to use

**Returns**: `(byte[] pdfBytes, List<FieldDetectionResult> fields)`

**Processing Modes**:
```csharp
// Determined by config.Services flags:
- SYNCFUSION_ONLY: Uses only Syncfusion native detection
- CLAUDE_VISION_ONLY: Uses only Claude Vision API
- HYBRID: Combines Syncfusion + Claude Vision + optional Google/GPT validation
```

**Code Flow**:
```csharp
1. Convert Word → PDF using Syncfusion DocIORenderer
2. Render PDF pages to PNG images for AI analysis
3. Run configured detection services:
   - Syncfusion: Detect ContentControls, FormFields, Tables
   - Claude Vision: Visual field detection with labels
   - Google: Structured document analysis (if enabled)
   - Multi-Stage: Claude + GPT-5 consensus (if enabled)
4. Merge results from all services
5. Deduplicate overlapping fields
6. Create form fields in PDF using FormFieldCreationService
7. Return PDF bytes + field list
```

**Example Usage**:
```csharp
var config = new FieldDetectionConfig
{
    Services = new ServiceSelection
    {
        UseSyncfusion = true,
        UseClaudeVision = true,
        UseMultiStageValidation = true
    }
};

var (pdfBytes, fields) = await fieldService.ConvertWithConfig(wordBytes, "form.docx", config);
```

**Performance Notes**:
- Syncfusion only: ~5-10 seconds
- + Claude Vision: +20-40 seconds
- + Multi-Stage: +30-60 seconds additional

---

### PdfPreservationService

**Location**: `Services/PdfPreservationService.cs`
**Purpose**: Preserves existing PDF form fields (including JavaScript) while adding accessibility features
**Dependencies**: AccessibilityService, AccessibilityRetrofitService, PdfAccessibilityEnhancer, PassportPdfService, ArtifactViolationFixService, TaggedWhitespaceFixService

**Key Methods**:

#### `GetExistingFields(byte[] pdfBytes)`
Extracts existing form fields from a PDF.

**Returns**: `List<FieldDetectionResult>`

**Extraction Logic**:
```csharp
using var ms = new MemoryStream(pdfBytes);
using var loadedDoc = new PdfLoadedDocument(ms);

foreach (var field in loadedDoc.Form.Fields)
{
    var result = new FieldDetectionResult
    {
        FieldName = field.Name,
        FieldType = DetermineFieldType(field),
        Bounds = field.Bounds,
        Tooltip = GetTooltipFromField(field),
        PageNumber = GetPageNumberFromField(field),
        // Radio button specific:
        ButtonValue = (field as PdfLoadedRadioButtonListField)?.Value
    };

    fields.Add(result);
}
```

**Field Types Detected**:
- `PdfLoadedTextBoxField` → TextBox
- `PdfLoadedCheckBoxField` → CheckBox
- `PdfLoadedRadioButtonListField` → RadioButton (with ButtonValue for grouping)
- `PdfLoadedComboBoxField` → ComboBox
- `PdfLoadedListBoxField` → ListBox

**Special Handling**:
- **Radio Button Grouping**: Extracts `ButtonValue` property to preserve radio button groups
- **Tooltip Extraction**: Parses `[PAGE:X]` pattern or uses widget detection
- **JavaScript Preservation**: Does NOT remove JavaScript actions

#### `ProcessExistingPdfAsync(byte[] pdfBytes, List<FieldDetectionResult>? additionalFields = null)`
Processes existing PDF by adding accessibility without destroying fields.

**Processing Steps**:

**Step 1: Fix Artifact Violations**
```csharp
var artifactFixResult = await _artifactViolationFixService.FixArtifactViolationsAsync(pdfBytes);
if (artifactFixResult.Success && artifactFixResult.ViolationsFixed > 0)
{
    pdfBytes = artifactFixResult.FixedPdf;
}
```

**Step 2: Fix Tagged Whitespace**
```csharp
var whitespaceFixResult = await _taggedWhitespaceFixService.FixTaggedWhitespaceAsync(pdfBytes);
if (whitespaceFixResult.Success && whitespaceFixResult.ViolationsFixed > 0)
{
    pdfBytes = whitespaceFixResult.FixedPdf;
}
```

**Step 3: Apply Accessibility**
```csharp
// Load with Syncfusion
using var loadedDoc = new PdfLoadedDocument(new MemoryStream(pdfBytes));

// Set metadata
_accessibilityService.MakeAccessible(loadedDoc, fileName);

// Apply algorithmic retrofitting
_retrofitService.RetrofitAccessibility(loadedDoc);

// Final enhancements
_enhancer.EnhanceAccessibility(loadedDoc, fileName);

// Save
using var outputStream = new MemoryStream();
loadedDoc.Save(outputStream);
return outputStream.ToArray();
```

**What It Preserves**:
- ✅ Form fields (including JavaScript actions)
- ✅ Field names and tooltips
- ✅ Calculated fields (SUM, formulas)
- ✅ Radio button grouping

**What It Adds**:
- ✅ PDF/UA metadata
- ✅ Accessibility tags
- ✅ Field tooltips (if missing)
- ✅ Tab order
- ✅ Normalized checkbox sizes

---

### PassportPdfService

**Location**: `Services/PassportPdfService.cs`
**Purpose**: PDF/A-2u conversion and comprehensive font embedding via PassportPDF cloud API
**Dependencies**: None (uses PassportPDF SDK)

**Key Methods**:

#### `ConvertToPdfAAsync(byte[] pdfBytes, string fileName, bool preserveJavaScript = false)`
Converts PDF to PDF/A-2u format with full font embedding.

**Processing Steps**:

**1. Upload to PassportPDF**
```csharp
var documentApi = new DocumentApi();
var loadParams = new LoadDocumentFromByteArrayParameters(pdfBytes);
loadParams.FileName = fileName;
var loadResponse = await documentApi.DocumentLoadAsync(loadParams);
string fileId = loadResponse.FileId;
```

**2. Reduce/Clean PDF**
```csharp
var reduceParams = new PdfReduceParameters(fileId);
reduceParams.RemoveFormFields = false;      // Keep form fields!
reduceParams.RemoveAnnotations = false;     // Keep annotations
reduceParams.RemoveJavaScript = !preserveJavaScript;  // ⚠️ CRITICAL SETTING
reduceParams.RemoveHyperlinks = true;
reduceParams.EnableCharRepair = true;
reduceParams.PackFonts = false;             // Don't subset fonts
```

**3. Repair Document**
```csharp
var repairParams = new PdfRepairDocumentParameters(fileId);
await pdfApi.RepairDocumentAsync(repairParams);
```

**4. Convert to PDF/A-2u**
```csharp
var convertParams = new PdfConvertToPDFAParameters(fileId);
convertParams.Conformance = PdfAConformance.PDFA2u;  // Unicode support
var convertResponse = await pdfApi.ConvertToPDFAAsync(convertParams);
```

**What This Does**:
- ✅ Embeds ALL fonts (including base-14 like Times-Roman, Helvetica)
- ✅ Replaces ZapfDingbats with Unicode checkbox symbols
- ✅ Validates PDF/A-2u conformance
- ✅ Repairs font encoding issues
- ⚠️ Removes JavaScript (unless `preserveJavaScript = true`)

**5. Download Result**
```csharp
var saveParams = new PdfSaveDocumentParameters(fileId);
var saveResponse = await pdfApi.SaveDocumentAsync(saveParams);
return saveResponse.Data;
```

#### `ConvertToPdfAPreservingFieldsAsync(byte[] pdfBytes, string fileName)`
Converts to PDF/A-2u while preserving field names and metadata.

**Why This Exists**: PassportPDF's PDF/A conversion sometimes renames form fields. This method:

1. **Extracts field metadata before conversion**:
```csharp
using var pdfDoc = new PdfLoadedDocument(new MemoryStream(pdfBytes));
foreach (var field in pdfDoc.Form.Fields)
{
    fieldMetadata.Add(new FieldMetadata
    {
        Name = field.Name,
        Tooltip = field.ToolTip,
        Bounds = field.Bounds,
        // ... other properties
    });
}
```

2. **Converts to PDF/A-2u** (regular conversion)

3. **Re-injects field metadata**:
```csharp
using var convertedDoc = new PdfLoadedDocument(new MemoryStream(convertedBytes));
foreach (var field in convertedDoc.Form.Fields)
{
    var originalMeta = fieldMetadata.Find(m => MatchesField(m, field));
    if (originalMeta != null)
    {
        field.Name = originalMeta.Name;           // Restore original name
        field.ToolTip = originalMeta.Tooltip;     // Restore tooltip
    }
}
```

**Use Case**: Ensures machine-readable field names (e.g., "EmployeeName") are preserved through PDF/A conversion.

---

### PdfCompleteRebuildService

**Location**: `Services/PdfCompleteRebuildService.cs`
**Purpose**: Complete PDF rebuild with field injection (used for complex scenarios)
**Dependencies**: PassportPdfService, AsposePdfService

**Key Methods**:

#### `CompletelyRebuildPdfAsync(byte[] pdfBytes, List<FieldUpdate> fieldUpdates, ServiceOptions options)`
Rebuilds entire PDF from scratch with new field data.

**Parameters**:
- `pdfBytes` - Source PDF
- `fieldUpdates` - List of field changes to apply
- `options` - Which services to use (Aspose, PassportPDF, etc.)

**ServiceOptions**:
```csharp
public class ServiceOptions
{
    public bool UseAsposeAutotag { get; set; }
    public bool UseAsposeFontEmbed { get; set; }
    public bool UsePassportPdf { get; set; }
}
```

**Processing Flow**:
```csharp
1. Load PDF with Syncfusion
2. Apply field updates (rename, change type, update tooltips)
3. If UseAsposeFontEmbed:
   → Run AsposePdfService.OptimizePdfAsync() for font embedding
4. If UsePassportPdf:
   → Run PassportPdfService.ConvertToPdfAAsync() for full compliance
5. Save and return
```

**Use Cases**:
- Bulk field renaming
- Field type changes (e.g., TextBox → CheckBox)
- Pre-processing before PassportPDF conversion

#### `RemoveZapfDingbatsFromCheckboxes(byte[] pdfBytes)`
Uses iText7 to remove ZapfDingbats appearance from checkboxes.

**Why This Exists**: ZapfDingbats font for checkbox symbols (✓) is not PDF/UA compliant.

**How It Works**:
```csharp
using var reader = new PdfReader(new MemoryStream(pdfBytes));
using var writer = new PdfWriter(outputStream);
using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader, writer);

var form = iText.Forms.PdfAcroForm.GetAcroForm(pdfDoc, false);
foreach (var field in form.GetAllFormFields())
{
    if (field.Value is PdfFormField formField && formField.IsCheckBox())
    {
        // Remove appearance streams that use ZapfDingbats
        var ap = formField.GetAppearance();
        if (ap != null)
        {
            ap.Remove(PdfName.N);  // Remove normal appearance
            ap.Remove(PdfName.D);  // Remove down appearance
        }

        // Set Unicode checkbox value instead
        formField.SetValue("☑");  // U+2611 BALLOT BOX WITH CHECK
    }
}
```

**Result**: Checkboxes use Unicode symbols instead of ZapfDingbats font.

---

## AI Detection Services

These services use AI APIs to detect and analyze form fields in documents.

### ClaudeVisionFieldDetector

**Location**: `Services/ClaudeVisionFieldDetector.cs`
**Purpose**: Visual field detection using Claude Vision API
**Dependencies**: AnthropicService

**Key Methods**:

#### `DetectFieldsFromPdf(byte[] pdfBytes, string fileName, FieldDetectionConfig config)`
Analyzes PDF images with Claude Vision to detect form fields.

**Processing Steps**:

**1. Convert PDF Pages to Images**
```csharp
using var pdfDoc = new PdfLoadedDocument(new MemoryStream(pdfBytes));
var pageImages = new List<byte[]>();

foreach (var page in pdfDoc.Pages)
{
    using var imageStream = page.ExportAsImage(0, 0, 0, 300); // 300 DPI
    pageImages.Add(imageStream.ToArray());
}
```

**2. Send to Claude Vision**
```csharp
var prompt = @"Analyze this form and identify all form fields.
For each field, provide:
- Field type (textbox, checkbox, radio, dropdown, signature)
- Bounding box coordinates (x, y, width, height in points)
- Field label/name
- Page number

Return as JSON array.";

var response = await _anthropicService.CallClaudeVisionAsync(
    pageImages,
    prompt,
    maxTokens: 4000
);
```

**3. Parse Response**
```csharp
var fields = JsonSerializer.Deserialize<List<ClaudeFieldDetection>>(response);

// Convert to FieldDetectionResult
foreach (var field in fields)
{
    results.Add(new FieldDetectionResult
    {
        FieldName = field.Label,
        FieldType = MapClaudeTypeToFieldType(field.Type),
        Bounds = new RectangleF(field.X, field.Y, field.Width, field.Height),
        PageNumber = field.PageNumber,
        Source = "ClaudeVision"
    });
}
```

**Strengths**:
- ✅ Detects visually subtle fields (light borders, no borders)
- ✅ Provides field labels automatically
- ✅ Handles complex layouts (tables, multi-column forms)
- ✅ Detects signature fields

**Limitations**:
- ⚠️ Costs ~$0.10-0.30 per page
- ⚠️ Can take 20-40 seconds per document
- ⚠️ Bounding boxes may need validation/correction

---

### GoogleDocumentAiService

**Location**: `Services/GoogleDocumentAiService.cs`
**Purpose**: Structured document analysis using Google Document AI
**Dependencies**: Google.Cloud.DocumentAI NuGet package

**Key Methods**:

#### `DetectFieldsAsync(byte[] pdfBytes, string fileName)`
Uses Google Document AI to detect form fields with high accuracy.

**Processing**:
```csharp
// Initialize client
var client = DocumentProcessorServiceClient.Create();
var processorName = ProcessorName.FromProjectLocationProcessor(
    projectId, location, processorId
);

// Send document
var request = new ProcessRequest
{
    Name = processorName.ToString(),
    RawDocument = new RawDocument
    {
        Content = Google.Protobuf.ByteString.CopyFrom(pdfBytes),
        MimeType = "application/pdf"
    }
};

var response = await client.ProcessDocumentAsync(request);

// Extract form fields
foreach (var page in response.Document.Pages)
{
    foreach (var formField in page.FormFields)
    {
        results.Add(new FieldDetectionResult
        {
            FieldName = formField.FieldName.TextAnchor.Content,
            FieldType = DetermineFieldType(formField),
            Bounds = ConvertGoogleBounds(formField.FieldValue.BoundingPoly),
            PageNumber = page.PageNumber,
            Confidence = formField.FieldValue.Confidence,
            Source = "GoogleDocumentAI"
        });
    }
}
```

**Strengths**:
- ✅ Very high accuracy for structured forms
- ✅ Detects field relationships (label → value)
- ✅ Provides confidence scores
- ✅ Handles tables well

**Limitations**:
- ⚠️ Requires Google Cloud account and API key
- ⚠️ Costs ~$1.50 per 1,000 pages
- ⚠️ May not detect signature fields

---

### MultiStageValidationService

**Location**: `Services/MultiStageValidationService.cs`
**Purpose**: Multi-model consensus validation using Claude + GPT-5
**Dependencies**: AnthropicService, OpenAIService

**Key Methods**:

#### `ValidateFieldsAsync(List<FieldDetectionResult> fields, byte[] pdfImages)`
Validates field detections using multiple AI models for consensus.

**Processing**:

**Stage 1: Individual Model Validation**
```csharp
// Send to Claude
var claudeValidation = await _anthropicService.ValidateFieldsAsync(fields, pdfImages);

// Send to GPT-5
var gptValidation = await _openAiService.ValidateFieldsAsync(fields, pdfImages);
```

**Stage 2: Consensus Analysis**
```csharp
var consensusFields = new List<FieldDetectionResult>();

foreach (var field in fields)
{
    var claudeConfidence = claudeValidation.GetConfidence(field);
    var gptConfidence = gptValidation.GetConfidence(field);

    // Require both models to agree (>70% confidence each)
    if (claudeConfidence > 0.7 && gptConfidence > 0.7)
    {
        field.Confidence = (claudeConfidence + gptConfidence) / 2;
        consensusFields.Add(field);
    }
    else if (Math.Abs(claudeConfidence - gptConfidence) > 0.3)
    {
        // Significant disagreement - flag for review
        field.NeedsReview = true;
        consensusFields.Add(field);
    }
}
```

**Stage 3: Bounding Box Correction**
```csharp
// Use the more confident model's bounding box
foreach (var field in consensusFields)
{
    if (claudeConfidence > gptConfidence)
        field.Bounds = claudeValidation.GetBounds(field);
    else
        field.Bounds = gptValidation.GetBounds(field);
}
```

**Strengths**:
- ✅ Highest accuracy (95%+ for most forms)
- ✅ Catches errors that single models miss
- ✅ Provides confidence scores

**Limitations**:
- ⚠️ Most expensive option (2x API costs)
- ⚠️ Slowest (30-60 seconds additional)

---

### ClaudeBoundingBoxValidator

**Location**: `Services/ClaudeBoundingBoxValidator.cs`
**Purpose**: Validates and corrects field bounding boxes using Claude
**Dependencies**: AnthropicService

**Key Methods**:

#### `ValidateAndCorrectBounds(List<FieldDetectionResult> fields, byte[] pdfImage)`
Validates bounding boxes and corrects obvious errors.

**Common Issues Detected**:
1. **Overlapping fields** - Same position for multiple fields
2. **Zero-size fields** - Width or height = 0
3. **Off-page fields** - Coordinates outside page bounds
4. **Incorrect field boundaries** - Box doesn't match visual field

**Correction Logic**:
```csharp
// Send marked-up image to Claude
var prompt = $@"Review these {fields.Count} form fields.
The red boxes show detected bounding boxes.
Identify any errors:
- Overlapping boxes
- Boxes that don't align with visual fields
- Missing fields

For each error, provide corrected coordinates.";

var response = await _anthropicService.CallClaudeVisionAsync(
    new[] { markedUpImage },
    prompt
);

// Apply corrections
var corrections = ParseCorrections(response);
foreach (var correction in corrections)
{
    var field = fields.Find(f => f.FieldName == correction.FieldName);
    if (field != null)
    {
        field.Bounds = correction.CorrectedBounds;
        field.ValidationStatus = "Corrected";
    }
}
```

**Use Case**: Run after initial field detection to catch coordinate errors before field creation.

---

## Field Management Services

### FormFieldCreationService

**Location**: `Services/FormFieldCreationService.cs`
**Purpose**: Creates form fields in PDFs from field detection results
**Dependencies**: None (uses Syncfusion)

**Key Methods**:

#### `CreateFields(PdfLoadedDocument document, List<FieldDetectionResult> fields)`
Creates actual form fields in PDF from detection results.

**Field Creation by Type**:

**TextBox**:
```csharp
var textBox = new PdfTextBoxField(page, field.FieldName);
textBox.Bounds = new RectangleF(field.Bounds.X, field.Bounds.Y,
                                 field.Bounds.Width, field.Bounds.Height);
textBox.ToolTip = field.Tooltip ?? field.FieldName;
textBox.BorderColor = new PdfColor(0, 0, 0);
textBox.BorderWidth = 1;
textBox.BackColor = new PdfColor(255, 255, 255);
textBox.Font = new PdfStandardFont(PdfFontFamily.Helvetica, 10);
document.Form.Fields.Add(textBox);
```

**CheckBox**:
```csharp
var checkBox = new PdfCheckBoxField(page, field.FieldName);
checkBox.Bounds = new RectangleF(field.Bounds.X, field.Bounds.Y, 13.8f, 13.8f);
checkBox.ToolTip = field.Tooltip ?? field.FieldName;
checkBox.BorderColor = new PdfColor(0, 0, 0);
checkBox.BorderWidth = 1;
checkBox.Checked = false;
document.Form.Fields.Add(checkBox);
```

**RadioButton Group**:
```csharp
// Radio buttons with same FieldName but different ButtonValue form a group
var existingGroup = document.Form.Fields
    .OfType<PdfRadioButtonListField>()
    .FirstOrDefault(f => f.Name == field.FieldName);

if (existingGroup == null)
{
    // Create new radio button group
    existingGroup = new PdfRadioButtonListField(page, field.FieldName);
    existingGroup.ToolTip = field.Tooltip ?? field.FieldName;
    document.Form.Fields.Add(existingGroup);
}

// Add radio button item to group
var item = new PdfRadioButtonListItem(field.ButtonValue ?? $"Option{index}");
item.Bounds = new RectangleF(field.Bounds.X, field.Bounds.Y, 13.8f, 13.8f);
existingGroup.Items.Add(item);
```

**ComboBox (Dropdown)**:
```csharp
var comboBox = new PdfComboBoxField(page, field.FieldName);
comboBox.Bounds = new RectangleF(field.Bounds.X, field.Bounds.Y,
                                   field.Bounds.Width, field.Bounds.Height);
comboBox.ToolTip = field.Tooltip ?? field.FieldName;

// Add items if provided in field.Options
if (field.Options != null)
{
    foreach (var option in field.Options)
    {
        comboBox.Items.Add(new PdfListFieldItem(option, option));
    }
}

document.Form.Fields.Add(comboBox);
```

**Signature Field**:
```csharp
var signature = new PdfSignatureField(page, field.FieldName);
signature.Bounds = new RectangleF(field.Bounds.X, field.Bounds.Y,
                                    field.Bounds.Width, field.Bounds.Height);
signature.ToolTip = field.Tooltip ?? "Sign here";
document.Form.Fields.Add(signature);
```

---

### FieldAnalysisService

**Location**: `Services/FieldAnalysisService.cs`
**Purpose**: Analyzes field layouts and relationships for intelligent field detection
**Dependencies**: None

**Key Methods**:

#### `AnalyzeFieldLayout(List<FieldDetectionResult> fields)`
Analyzes spatial relationships between fields.

**Analysis Operations**:

**1. Detect Field Alignment**
```csharp
// Find fields aligned horizontally (same Y coordinate ±2 points)
var horizontalGroups = fields
    .GroupBy(f => Math.Round(f.Bounds.Y / 2) * 2)  // Group by Y (±2 point tolerance)
    .Where(g => g.Count() > 1)
    .ToList();

// Find fields aligned vertically (same X coordinate ±2 points)
var verticalGroups = fields
    .GroupBy(f => Math.Round(f.Bounds.X / 2) * 2)
    .Where(g => g.Count() > 1)
    .ToList();
```

**2. Detect Field Grids (Tables)**
```csharp
// Detect if fields form a grid pattern
var gridFields = new List<List<FieldDetectionResult>>();

for (int row = 0; row < horizontalGroups.Count; row++)
{
    var rowFields = horizontalGroups[row]
        .OrderBy(f => f.Bounds.X)
        .ToList();

    if (row > 0 && IsAlignedWithPreviousRow(rowFields, gridFields[row - 1]))
    {
        gridFields.Add(rowFields);
    }
}

// If we found a grid, mark fields as table cells
if (gridFields.Count >= 2)
{
    foreach (var row in gridFields)
    {
        foreach (var field in row)
        {
            field.IsTableCell = true;
            field.TableRow = gridFields.IndexOf(row);
            field.TableColumn = row.IndexOf(field);
        }
    }
}
```

**3. Detect Label-Field Pairs**
```csharp
// For each field, look for nearby text that could be a label
foreach (var field in fields)
{
    // Look for text to the left (within 200 points)
    var leftText = FindTextInRegion(
        new RectangleF(
            field.Bounds.X - 200,
            field.Bounds.Y - 5,
            200,
            field.Bounds.Height + 10
        )
    );

    if (!string.IsNullOrEmpty(leftText))
    {
        field.DetectedLabel = leftText.Trim();
    }
}
```

---

### PdfFieldTagEditorService

**Location**: `Services/PdfFieldTagEditorService.cs`
**Purpose**: Edit form fields and PDF tags (used by field editor UI)
**Dependencies**: None (uses Syncfusion)

**Key Methods**:

#### `UpdateFieldProperties(byte[] pdfBytes, string fieldName, FieldPropertyUpdate update)`
Updates properties of a specific field.

**Updatable Properties**:
```csharp
public class FieldPropertyUpdate
{
    public string? NewName { get; set; }
    public string? NewTooltip { get; set; }
    public RectangleF? NewBounds { get; set; }
    public bool? IsReadOnly { get; set; }
    public bool? IsRequired { get; set; }
    public string? NewDefaultValue { get; set; }
    public string? NewFieldType { get; set; }  // Convert field type
}
```

**Update Logic**:
```csharp
using var doc = new PdfLoadedDocument(new MemoryStream(pdfBytes));

var field = doc.Form.Fields.Cast<PdfLoadedField>()
    .FirstOrDefault(f => f.Name == fieldName);

if (field == null)
    throw new Exception($"Field '{fieldName}' not found");

// Apply updates
if (update.NewName != null)
    field.Name = update.NewName;

if (update.NewTooltip != null)
    field.ToolTip = update.NewTooltip;

if (update.NewBounds.HasValue)
    field.Bounds = update.NewBounds.Value;

if (update.IsReadOnly.HasValue)
    field.ReadOnly = update.IsReadOnly.Value;

if (update.IsRequired.HasValue && field is PdfLoadedTextBoxField textBox)
    textBox.IsRequired = update.IsRequired.Value;

// Field type conversion requires rebuild
if (update.NewFieldType != null)
    return ConvertFieldType(doc, field, update.NewFieldType);

using var outputStream = new MemoryStream();
doc.Save(outputStream);
return outputStream.ToArray();
```

#### `DeleteField(byte[] pdfBytes, string fieldName)`
Removes a field from the PDF.

```csharp
using var doc = new PdfLoadedDocument(new MemoryStream(pdfBytes));

var field = doc.Form.Fields.Cast<PdfLoadedField>()
    .FirstOrDefault(f => f.Name == fieldName);

if (field != null)
{
    doc.Form.Fields.Remove(field);
}

using var outputStream = new MemoryStream();
doc.Save(outputStream);
return outputStream.ToArray();
```

---

**End of ARCHITECTURE-SERVICES-CORE.md**

**See also:**
- **ARCHITECTURE-SERVICES-REMEDIATION.md** - PDF/UA remediation system
- **ARCHITECTURE-SERVICES-ACCESSIBILITY.md** - Accessibility & utility services
- **ARCHITECTURE.md** - Main processing paths
- **ARCHITECTURE-API.md** - API endpoints
