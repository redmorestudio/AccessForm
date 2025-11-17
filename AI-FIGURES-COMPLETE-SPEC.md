
# Phase 3b – Figures / Images Integration Spec  
_Complete Implementation Instructions (Claude-ready)_  
_File name: `AI-FIGURES-COMPLETE-SPEC.md`_

---

## 0. Overview

This document gives **complete, final, authoritative instructions** for implementing Phase 3b (Images/Figures) of the AI PDF Remediation pipeline.  
It assumes:

- We **reuse the existing image extraction + AI tagging pipeline**.
- We **extend** that pipeline to classify images as decorative or meaningful.
- We integrate image information into:
  - `FigureBlock`
  - `LogicalDocument` enrichment
  - `StructureTreeBuilder`
  - `IPageLayoutEngine` → `PageLayoutPlan`
  - `SyncfusionPdfStructureWriter`

This is the drop‑in spec to implement everything.

---

# 1. Extend Existing Image Tagging Module

Locate the module responsible for:

- Extracting images from PDFs  
- Generating alt text (AI or metadata)  
- Returning image metadata (bounds, page index, etc.)

You will **extend its output type**.

### 1.1 Extend result type

If the type is called something like `ImageTagResult`, extend it with:

```csharp
public sealed class ImageTagResult
{
    public int PageIndex { get; init; }
    public Rect Bounds { get; init; }
    public string? AltText { get; init; }

    // NEW:
    public bool IsLikelyDecorative { get; init; }
}
```

If the name differs, adjust accordingly.  
This type becomes the primary source of truth for all Phase 3b metadata.

---

# 2. Implement Decorative vs Meaningful Classification

Add a new helper:

`ImageDecorativeClassifier.cs`

### 2.1 API

```csharp
public static class ImageDecorativeClassifier
{
    public static bool IsLikelyDecorative(ImageTagResult image, PageDimensions page);
}
```

### 2.2 Inputs used

- `image.Bounds`
- `image.PageIndex`
- `image.AltText`
- Page dimensions (from PDF)

### 2.3 Rules

Implement these heuristics:

#### **Rule 1 – Size-based**
If BOTH:
- `widthRatio < 0.05`
- `heightRatio < 0.05`

→ decorative candidate

#### **Rule 2 – Header/footer**
If image is in:
- Top 15% of page (header)
- OR bottom 15% (footer)

AND the image is narrow/small → decorative candidate

#### **Rule 3 – Alt text indicates decoration**
Lowercase alt text; if it contains:
- "logo"
- "icon"
- "watermark"
- "divider"

→ decorative candidate

#### **Rule 4 – Content indicators override all**
If alt contains:
- "map", "diagram", "chart", "graph", "figure", "photo", "illustration"

→ force **meaningful**

#### **Rule 5 – Tie-breaker**
- If >1 decorative signals → decorative  
- Else → meaningful

### 2.4 Integration

After generating `ImageTagResult` in the existing module, run:

```csharp
result = result with
{
    IsLikelyDecorative = ImageDecorativeClassifier.IsLikelyDecorative(result, pageDimensions)
};
```

Return this extended result.

---

# 3. Create `FigureDetectionEnricher`

This service adapts the image-tagging results into the AI remediation pipeline.

Filename suggestion:
`Services/Enrichment/FigureDetectionEnricher.cs`

### 3.1 Responsibilities

1. Receive:
   - `LogicalDocument`
   - Original PDF bytes (for page dimensions)
2. Call existing image-tagging module.
3. For each returned `ImageTagResult`, create a **FigureBlock**:
   ```csharp
   var figureBlock = new FigureBlock(
       bounds: result.Bounds,
       pageIndex: result.PageIndex,
       altText: result.AltText,
       isLikelyDecorative: result.IsLikelyDecorative,
       sourceImageId: $"p{result.PageIndex}_img{index}" // synthetic stable ID
   );
   ```
4. Add each `FigureBlock` to the appropriate `LogicalPage`.

### 3.2 Caching for writer

Create a dictionary inside this service:

```csharp
Dictionary<string, ExtractedImageData> ImageCache { get; }
```

Populate it:

```
SourceImageId → raw bytes / or PDF image object
```

Attach this cache to the `StructureRebuildContext` so the writer can access it.

---

# 4. Update `FigureBlock` Class

Modify existing `Models/Logical/FigureBlock.cs`:

Add:

```csharp
public int PageIndex { get; }
public string? SourceImageId { get; }
```

Keep:

- `Bounds`
- `AltTextSuggestion`
- `IsLikelyDecorative`

---

# 5. Update `StructureTreeBuilder` for Figures

When encountering a `FigureBlock`, generate:

```csharp
var attributes = new Dictionary<string, string>();

if (!string.IsNullOrWhiteSpace(block.AltTextSuggestion))
    attributes["alt"] = block.AltTextSuggestion;

if (block.IsLikelyDecorative)
    attributes["decorative"] = "true";

attributes["pageIndex"] = block.PageIndex.ToString();
attributes["sourceImageId"] = block.SourceImageId;

var node = new StructureNode(
    Role: "Figure",
    TextContent: null,
    Attributes: attributes,
    Children: null);
```

---

# 6. Page Layout: Use Bounds

Before Phase 3c is complete, simply feed image bounds into:

- `PageLayoutPlan`
- `DrawInstruction.TargetBounds`

This ensures accurate positioning.

Do **not** use the old top-down layout for images.

---

# 7. Update `SyncfusionPdfStructureWriter`

### 7.1 Add support for `Figure` nodes

Steps:

1. Resolve image bytes using:
   ```csharp
   var data = context.ImageCache[node.Attributes["sourceImageId"]];
   ```
2. Convert to `PdfBitmap` or `PdfImage`.
3. Retrieve `TargetBounds` from the draw instruction.
4. Draw image:
   ```csharp
   page.Graphics.DrawImage(pdfBitmap, targetBounds);
   ```
5. Associate structure element:
   - Use same pattern as text: `Element` property or tagged context.
6. If decorative:
   - Do **not** set alt text
   - Optionally set artifact flag later

---

# 8. Debug Flag: `EnableFigureRedraw`

Add config:

```json
"AccessibilityRemediation": {
    "EnableFigureRedraw": true
}
```

Behavior:

- If `false`:  
  - Build tags  
  - Skip drawing images
- If `true`:  
  - Draw images normally

This is essential for debugging.

---

# 9. Testing Plan

Use a PDF with:

- Header logo → should be decorative
- Tiny icon → decorative
- A meaningful image (map/diagram/photo) → not decorative

Test:

- `IsLikelyDecorative` assigned correctly
- Structure tree has alt text only for meaningful images
- Rebuilt PDF shows proper tagging and reading order
- Debug flag works

---

# 10. Implementation Summary (Copy Into Claude)

1. Extend the existing image-tagging result type with `IsLikelyDecorative`.  
2. Implement `ImageDecorativeClassifier` using size, position, alt-text heuristics.  
3. Apply classification inside the existing tagging module **after** alt text generation.  
4. Build `FigureDetectionEnricher`:
   - Consume image-tagging results  
   - Generate `FigureBlock`s  
   - Build `SourceImageId`s  
   - Cache image bytes by ID  
5. Update `FigureBlock` with `PageIndex` and `SourceImageId`.  
6. Update `StructureTreeBuilder` to output `Figure` nodes with attributes.  
7. Feed figure bounds into layout (even before Phase 3c).  
8. Update `SyncfusionPdfStructureWriter`:
   - Resolve images from cache  
   - Draw them at `TargetBounds`  
   - Associate with `PdfStructureElement`  
9. Add `EnableFigureRedraw` flag to toggle drawing on/off.  
10. Test using a PDF with decorative and meaningful images.

---

This spec completely implements Phase 3b using your actual architecture.  
It is fully aligned with what your system already does, and requires no reinvention.

