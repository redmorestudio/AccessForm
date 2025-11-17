
# AI Forms, Figures & Multi‑Column Layout Spec  
_Phase 3: Interactive Forms, Real Images, and Geometric Flow_  
_File name suggestion: `AI-FORMS-FIGURES-MULTICOL-SPEC.md`_

---

## 0. Context & Goals

You now have:

- AI → `LogicalDocument` → `StructureTree`
- `StructureRebuildService`
- `SyncfusionPdfStructureWriter` implementing `IPdfStructureWriter`
- MCID-backed tagged PDFs via Syncfusion
- Phase 2 layout using bounding boxes
- Phase 2.5 artifact + table semantics (`AI-TABLES-AND-ARTIFACTS-SPEC.md`)

You also already have **strong, existing intelligence** in the codebase for:

- Detecting form fields (types, labels, semantics)
- Working with images/figures
- Layout/geometry for Word→PDF

This spec **must reuse what already exists**; don’t reinvent field/figure detection.

**Objectives:**

1. **Interactive Form Fields**  
   - Rebuild real form fields (text, checkbox, radio, combo, etc.) in the new PDF.
   - Ensure they are correctly tagged and associated with labels/help text.
   - Preserve or improve semantics vs original.

2. **Real Figures / Images**  
   - Extract images/figures from original PDF.
   - Reinsert them into the rebuilt document in approximately correct positions.
   - Associate them with `Figure` structure elements and alt text.

3. **Multi‑Column / Geometric Flow**  
   - Use geometry (bounding boxes) to determine reading order on complex pages.
   - Respect multi‑column layouts, sidebars, and header/footer regions.
   - Integrate this with the existing layout engine you already use in Word→PDF.

All of this builds on your current architecture, not parallel to it.

---

## 1. Form Fields: Data Model & Semantics

### 1.1. Reuse existing form intelligence

**Instruction to the implementer (Claude Code):**

1. Locate the existing code in the repo that:
   - Detects Word form fields or PDF form fields
   - Classifies them (text, checkbox, radio, dropdown, etc.)
   - Knows about field names, tooltips, and labels
2. Extract the **core abstractions** (interfaces / DTOs) that represent:
   - Field type
   - Field identifier (name/id)
   - Text label/description
   - Tooltip/help text
   - Options (for radio/combobox)
   - Checked/selected state
   - Geometric bounds
3. Do **not** reimplement field detection from scratch. Reuse these abstractions in the AI remediation path.

If you already have something like `FormFieldInfo`, `FormFieldKind`, `FieldGeometry`, etc. — use those.

### 1.2. Logical form block type

Add a new logical block type that wraps existing field metadata:

```csharp
namespace AccessForm.Logical;

public enum LogicalFormFieldType
{
    Text,
    MultilineText,
    Checkbox,
    Radio,
    ComboBox,
    ListBox,
    Signature,
    Date,
    Numeric,
    Other
}

public record LogicalFormFieldBlock(
    Rect Bounds,
    int PageIndex,
    LogicalFormFieldType FieldType,
    string FieldName,
    string? Tooltip,
    string? LabelText,
    IReadOnlyList<string>? Options,
    string? DefaultValue,
    bool? IsChecked
) : LogicalBlock(Bounds);
```

Notes:

- `FieldName`, `Options`, etc. should be sourced from existing detection logic.
- `LabelText` may be:
  - Nearby text block associated as a label
  - Explicit label metadata from existing logic
- `IsChecked` only relevant for toggles (checkbox/radio).

### 1.3. StructureTree representation

Update `StructureTreeBuilder` to convert `LogicalFormFieldBlock` into structured nodes.

Proposed structure:

- `Form` container (optional per page or per logical group)
  - `FormField` nodes
    - Specific roles per type:
      - `FormTextField`
      - `FormCheckBox`
      - `FormRadioButton`
      - `FormComboBox`
    - Child `Label` nodes where applicable

Example:

```csharp
// Pseudo-code within StructureTreeBuilder

case LogicalFormFieldBlock field:
    var fieldRole = field.FieldType switch
    {
        LogicalFormFieldType.Checkbox   => "FormCheckBox",
        LogicalFormFieldType.Radio      => "FormRadioButton",
        LogicalFormFieldType.ComboBox   => "FormComboBox",
        LogicalFormFieldType.ListBox    => "FormListBox",
        LogicalFormFieldType.Signature  => "FormSignature",
        _                               => "FormTextField"
    };

    var attributes = new Dictionary<string, string>
    {
        ["fieldName"] = field.FieldName
    };

    if (!string.IsNullOrWhiteSpace(field.Tooltip))
        attributes["tooltip"] = field.Tooltip;

    var children = new List<StructureNode>();

    if (!string.IsNullOrWhiteSpace(field.LabelText))
    {
        children.Add(new StructureNode(
            Role: "Label",
            TextContent: field.LabelText,
            Attributes: null,
            Children: null));
    }

    return new StructureNode(
        Role: fieldRole,
        TextContent: null, // value is interactive, not static text
        Attributes: attributes,
        Children: children);
```

You can wrap related fields in higher-level `Form` / `Group` nodes if helpful.

---

## 2. Syncfusion Writer: Form Field Creation

### 2.1. Map StructureNode roles to Syncfusion form fields

Update `SyncfusionPdfStructureWriter` (or an extracted helper) so that when it encounters:

- `FormTextField`
- `FormCheckBox`
- `FormRadioButton`
- `FormComboBox`
- etc.

…it creates Syncfusion form fields instead of just text.

Pseudo-code:

```csharp
private void CreateFormFieldForNode(
    Syncfusion.Pdf.PdfDocument document,
    Syncfusion.Pdf.PdfPage page,
    StructureNode node,
    RectangleF bounds)
{
    var form = document.Form;

    switch (node.Role)
    {
        case "FormTextField":
            var textField = new Syncfusion.Pdf.Interactive.PdfTextBoxField(page, bounds)
            {
                Name = node.Attributes?["fieldName"] ?? Guid.NewGuid().ToString("N"),
            };
            form.Fields.Add(textField);
            AttachStructureElementForField(node, textField);
            break;

        case "FormCheckBox":
            var checkBox = new Syncfusion.Pdf.Interactive.PdfCheckBoxField(page, bounds)
            {
                Name = node.Attributes?["fieldName"] ?? Guid.NewGuid().ToString("N")
            };
            form.Fields.Add(checkBox);
            AttachStructureElementForField(node, checkBox);
            break;

        case "FormRadioButton":
            // Use existing radio-group logic if present
            break;

        case "FormComboBox":
            var combo = new Syncfusion.Pdf.Interactive.PdfComboBoxField(page, bounds)
            {
                Name = node.Attributes?["fieldName"] ?? Guid.NewGuid().ToString("N")
            };
            if (node.Attributes is { } attrs && attrs.TryGetValue("optionsJson", out var json))
            {
                // Parse options and add to combo
            }
            form.Fields.Add(combo);
            AttachStructureElementForField(node, combo);
            break;
    }
}
```

**Important:** Instead of inventing new logic for radio groups, option lists, etc., **reuse** whatever structures you already have in the existing form pipeline.

### 2.2. Attach form fields to structure elements

To keep accessibility intact, form fields should be associated with structure elements and labels.

Patterns:

- The `StructureNode` for a form field corresponds to a `PdfStructureElement` with an appropriate role (e.g., `PdfTagType.Form` or a custom role).
- The visible label should be:
  - A text node structurally adjacent to the field
  - Or content inside a `Label` child of the field node
- Use Syncfusion support for tagged form fields (if available in your version) so screen readers correctly announce label + role.

If Syncfusion lacks explicit tagged form support:

- We still keep:
  - The structure element for the field (role: `FormField` / `FormCheckBox` etc.)
  - The visual form field on the page
  - The label text in a sibling/child `P`/`Label` structure node

That combination is still usable by assistive tech.

---

## 3. Figures & Images

### 3.1. Reuse existing figure/image handling

You already have logic for:

- Identifying images/maps/logos and their bounding boxes.
- Providing alt text suggestions.

Instruction to implementer:

1. Find the code that:
   - Extracts figures/images in your existing pipelines.
   - Associates alt text and geometry.
2. Ensure `FigureBlock` (in `LogicalDocument`) carries:
   - `Bounds`
   - `PageIndex`
   - A **stable identifier** that can be used to locate the original image in the source PDF, e.g.:
     - XObject name
     - Object number
     - Or a synthetic “figure id” that maps to your existing image detection metadata.

Example update:

```csharp
public record FigureBlock(
    Rect Bounds,
    string? AltTextSuggestion,
    bool IsLikelyDecorative,
    string? SourceImageId // link into existing image metadata
) : LogicalBlock(Bounds);
```

### 3.2. StructureTree for figures

You already build `StructureNode` with `Role = "Figure"`.

Ensure:

- `Attributes["alt"] = AltTextSuggestion` when available.
- `Attributes["decorative"] = "true"` for likely decorative figures.
- `Attributes["sourceImageId"] = SourceImageId` if you need to find the original content.

### 3.3. Syncfusion: placing real images

In `SyncfusionPdfStructureWriter`, when encountering a `Figure` node:

1. Find the page and `TargetBounds` via your layout plan.
2. Resolve the image data:
   - Either from the **original PDF**:
     - Load `PdfLoadedDocument` from `originalPdf` bytes.
     - Locate the image (e.g., scanning XObjects and matching by ID or approximate geometry).
   - Or from pre-extracted images stored in your existing pipelines.

3. Draw the image:

```csharp
var graphics = page.Graphics;
var pdfImage = new Syncfusion.Pdf.Graphics.PdfBitmap(streamOrBytes);
graphics.DrawImage(pdfImage, targetBounds);
```

4. Associate the drawn image with the `Figure` structure element:
   - Use Syncfusion’s tagged content API so the image drawing operation is attached to the `PdfStructureElement` representing the figure.
   - Set `element.AlternateText` from `Attributes["alt"]` if present.
   - If decorative, mark appropriately or omit alt text.

---

## 4. Multi‑Column & Geometric Flow

This builds on the bounding-box layout engine from the previous spec.

### 4.1. Column detection heuristic

For each page:

1. Gather all `StructureNode`s that:
   - Are not artifacts
   - Have valid `Bounds` / `SourceBlock.Bounds`
2. Cluster content into columns based on X position:
   - Sort by `Bounds.Left`
   - Group nodes whose `Bounds.Left` fall into similar bands
   - A simple approach:
     - Compute histogram over X positions
     - Use distance threshold to split into “column bands”

3. Label columns:
   - Column 0: leftmost
   - Column 1: next, etc.

### 4.2. Reading order within columns

For each column:

- Sort nodes by `Bounds.Top` (ascending).
- Reading order: top-to-bottom in column 0, then column 1, etc.

### 4.3. Handling headers, footers, and sidebars

Use simple vertical bands:

- Top band (e.g., top 10–15% of page height) → header region  
- Bottom band (e.g., bottom 10–15%) → footer region  
- Narrow, tall column on far right → possible sidebar

Heuristics:

- Header content:
  - Usually spans most of the page width
  - Appears in top band
  - May contain title, date, logo
- Footer content:
  - Appears in bottom band
- Sidebars:
  - Narrow width relative to page
  - Continuous vertical extent

You do **not** have to handle all edge cases. The goal is to:

- Avoid mixing header/footer/sidebar into the main reading flow.
- Keep main body columns as the primary reading order.

### 4.4. Integration with `IPageLayoutEngine`

In the layout engine implementation:

1. Before generating `PageLayoutPlan`, compute:
   - Column assignments for each node
   - Region (header/body/footer/sidebar) classification
2. Build reading order:
   - Header region (if important) first, then
   - Body columns (multi-column handled by column order), then
   - Sidebar (if present), then
   - Footer

3. `DrawInstruction` emission respects this ordering.

The Syncfusion writer stays mostly the same; it just receives instructions already ordered correctly.

---

## 5. Integration Points

1. **Form Fields:**
   - Ensure AI layout analysis / LogicalDocument building phase creates `LogicalFormFieldBlock` entries using existing form detection logic.
   - `StructureTreeBuilder` converts them into form-related `StructureNode`s.
   - `StructureTreeCleaner` can handle artifact vs real form layout if needed (e.g., decorative text vs actual field).
   - `SyncfusionPdfStructureWriter` creates real interactive form fields and associates them with structure elements.

2. **Figures / Images:**
   - AI analysis and/or existing image logic produces `FigureBlock`s with bounding boxes and alt text.
   - `StructureTreeBuilder` builds `Figure` nodes with `alt` attribute.
   - Layout engine positions them via bounding boxes.
   - Syncfusion writer draws real images and wires them to `Figure` structure elements.

3. **Multi‑Column Flow:**
   - Implemented entirely inside `IPageLayoutEngine` implementation as part of building `PageLayoutPlan`.
   - No change to StructureTree model, only to reading order and placement.

---

## 6. Testing & Validation

### 6.1. Forms

- Use a document with a mixture of:
  - Text fields
  - Checkboxes
  - Radios
  - Combos
- After remediation:
  - Open in Acrobat
  - Ensure fields are interactive and tagged
  - Use screen reader to:
    - Tab through fields
    - Confirm labels are announced correctly

### 6.2. Figures

- Use a document with logos, maps, and decorative graphics.
- After remediation:
  - Check Tags panel for `Figure` elements with correct alt text.
  - Use “Highlight Content” to ensure figures correspond to the right images.
  - Use a screen reader to confirm alt text is announced.

### 6.3. Multi‑Column

- Use a two–three column layout.
- After remediation:
  - Read with NVDA/VoiceOver:
    - Confirm text is read down left column, then right.
    - Confirm headers/footers aren’t interleaved into main content.

---

## 7. “Do This” Checklist for Claude Code

You can hand this section directly to Claude:

> 1. Locate and reuse existing form-field abstractions and image/figure handling logic in the repository; do **not** reimplement field or figure detection.  
> 2. Add a `LogicalFormFieldBlock` logical block type that wraps existing form field metadata (type, name, label, tooltip, options, bounds, etc.).  
> 3. Update `StructureTreeBuilder` to convert `LogicalFormFieldBlock` into appropriate form-related `StructureNode`s (e.g., `FormTextField`, `FormCheckBox`, `FormRadioButton`, `FormComboBox`), including child `Label` nodes where applicable.  
> 4. Extend `SyncfusionPdfStructureWriter` so that when it encounters form-related `StructureNode`s, it creates real Syncfusion form fields (`PdfTextBoxField`, `PdfCheckBoxField`, `PdfRadioButtonListField`, `PdfComboBoxField`, etc.) using the existing form abstractions, and associates them with structure elements.  
> 5. Ensure `FigureBlock` carries enough metadata (bounds, alt text, source image ID) to locate and redraw the original images. Update the writer so `Figure` nodes draw real images and set `AlternateText` correctly.  
> 6. Implement multi‑column reading order in the layout engine by clustering nodes into columns based on X positions, sorting within columns by Y, and ordering header/body/footer regions sensibly.  
> 7. Keep the Syncfusion writer free of complex layout heuristics; all geometric reasoning should be in the `IPageLayoutEngine` implementation.  
> 8. Add targeted tests for:  
>    - Interactive forms (fields are present and tagged).  
>    - Figures (alt text visible in tags, highlight content follows images).  
>    - Multi‑column layouts (screen reader reading order matches visual expectation).  

Once these steps are implemented, the AI remediation pipeline will not only fix structure and MCIDs, but also **rebuild real interactive forms, real images, and robust multi‑column reading order**, all grounded in your existing, battle-tested layout and detection logic.

