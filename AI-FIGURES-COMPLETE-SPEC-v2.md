
# Phase 3b – COMPLETE Figures / Images Remediation Specification  
_File: `AI-FIGURES-COMPLETE-SPEC-v2.md`_  
_This document is fully self‑contained and ready for Claude Code with zero prior context._

---

# 0. Purpose

This specification defines the entire implementation of **Phase 3b – Figures / Images** in the PDF AI Remediation System.  
It provides a complete design for:

- Image extraction (via existing module)
- Alt-text generation (via existing module)
- Decorative vs meaningful image classification (new)
- Integration into:
  - LogicalDocument
  - FigureBlock
  - StructureTree
  - PageLayoutPlan (minimal version)
  - SyncfusionPdfStructureWriter
- Image caching and rendering
- Debug toggles
- Testing approach

This file is the **authoritative spec** for implementing Phase 3b.

---

# 1. Architectural Overview

The PDF remediation pipeline currently looks like this:

```
Original PDF
   ↓
AI Layout Analysis  → LogicalDocument
   ↓
FormFieldEnrichmentService
   ↓
FigureDetectionEnricher   ← (THIS SPEC)
   ↓
StructureTreeBuilder
   ↓
PageLayoutPlan (minimal Phase 3b version)
   ↓
SyncfusionPdfStructureWriter
   ↓
Rebuilt, fully tagged PDF (with alt text and figure placement)
```

Phase 3b adds:

- A FigureDetection step
- Accurate geometric placement of images
- Alt text based on existing AI module
- Decorative image detection
- Structure tree integration
- Image drawing in Syncfusion writer

---

# 2. Existing Image Tagging Module

## 2.1 Locate the module

There **already exists** a module that:

- Extracts images
- Sends images to AI for captioning
- Receives metadata (bounds, page index)

Claude should find it by searching for:

- `"alt"`, `"alt text"`, `"caption"`, `"image"`, `"vision"`, `"bitmap"`, `"extract image"`, `"tag image"`

This module is now referred to as the:

**Image Tagging Module**

## 2.2 Required output type

Normalize module output into:

```csharp
public sealed class ImageTagResult
{
    public int PageIndex { get; init; }
    public Rect Bounds { get; init; }
    public string? AltText { get; init; }

    // NEW for Phase 3b
    public bool IsLikelyDecorative { get; init; }
}
```

## 2.3 Alt text generation responsibilities

The module must:

1. Use any alt text that already exists in the PDF.
2. Otherwise call Claude Vision (or configured vision model).
3. Produce brief, readable, meaningful alt text.
4. Provide the extracted image's bounds.

Phase 3b uses these results directly.

---

# 3. Decorative vs Meaningful Classification

A new classifier must be added to the Image Tagging Module.

## 3.1 New class

`Services/Images/ImageDecorativeClassifier.cs`

```csharp
public static class ImageDecorativeClassifier
{
    public static bool IsLikelyDecorative(ImageTagResult image, PageDimensions page);
}
```

## 3.2 Heuristics

### Rule 1 — Size
If both width and height < 5% of page size → decorative candidate.

### Rule 2 — Header/footer
If Y position is in top 15% or bottom 15% of page **and small** → decorative.

### Rule 3 — Decorative alt text cues
If alt text contains:

- "logo"
- "icon"
- "watermark"
- "divider"
- "bullet"

→ decorative candidate.

### Rule 4 — Content cues override all
If alt text contains:

- "map"
- "chart"
- "diagram"
- "graph"
- "figure"
- "photo"
- "illustration"

→ **force meaningful**.

### Rule 5 — Tie-breaking
Two or more decorative signals → decorative  
Otherwise → meaningful

## 3.3 Integrating classification

After generating alt text:

```csharp
result = result with
{
    IsLikelyDecorative = ImageDecorativeClassifier.IsLikelyDecorative(result, pageDimensions)
};
```

---

# 4. Update FigureBlock

Modify existing type:

```
Models/Logical/FigureBlock.cs
```

Include:

```csharp
public sealed record FigureBlock(
    Rect Bounds,
    int PageIndex,
    string? AltTextSuggestion,
    bool IsLikelyDecorative,
    string? SourceImageId
) : LogicalBlock(Bounds);
```

Where:

- `PageIndex` — from tagging module  
- `SourceImageId` — synthetic ID (e.g. `p1_img3`)  
- `AltTextSuggestion` — from tagging module  
- `IsLikelyDecorative` — from classifier  
- `Bounds` — unified coordinate system  

---

# 5. Create FigureDetectionEnricher

Create:

`Services/Enrichment/FigureDetectionEnricher.cs`

## Responsibilities

1. Call the Image Tagging Module.
2. Receive list of `ImageTagResult`.
3. Create `FigureBlock`s and attach to the corresponding `LogicalPage`.
4. Populate ImageCache (dictionary mapping ID → image bytes).

## ImageCache entry type

```
public sealed class ExtractedImageData
{
    public byte[] Bytes { get; init; } = Array.Empty<byte>();
    public int? Width { get; init; }
    public int? Height { get; init; }
}
```

## SourceImageId

Generate:

```
p{pageIndex}_img{nthImageOnPage}
```

Attach to each `FigureBlock` and ImageCache.

---

# 6. StructureRebuildContext

Create new type:

`Models/Remediation/StructureRebuildContext.cs`

```csharp
public sealed class StructureRebuildContext
{
    public IReadOnlyDictionary<string, ExtractedImageData> ImageCache { get; init; }
    public PageLayoutPlan LayoutPlan { get; init; }
}
```

This context is passed into:

```csharp
_pdfStructureWriter.Write(structureTree, context);
```

---

# 7. StructureTreeBuilder – Adding Figure Nodes

When processing a `FigureBlock`, emit:

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
    Children: null
);
```

---

# 8. PageLayoutPlan (Minimal Phase 3b Version)

Create:

- `Models/Layout/PageLayoutPlan.cs`
- `Models/Layout/PagePlan.cs`
- `Models/Layout/DrawInstruction.cs`

### Types

```csharp
public sealed class PageLayoutPlan
{
    public List<PagePlan> Pages { get; } = new();
}

public sealed class PagePlan
{
    public int PageIndex { get; set; }
    public List<DrawInstruction> Instructions { get; } = new();
}

public sealed class DrawInstruction
{
    public StructureNode Node { get; set; } = default!;
    public RectangleF TargetBounds { get; set; }
}
```

### Bounds logic

For figures:

```
TargetBounds = FigureBlock.Bounds
```

For text:

Use the existing simple flow until Phase 3c replaces it.

---

# 9. SyncfusionPdfStructureWriter – Rendering Images

Modify writer so that for each Figure node:

## 9.1 Get image bytes

```csharp
var id = node.Attributes["sourceImageId"];
var data = context.ImageCache[id];
```

## 9.2 Create Syncfusion image

```csharp
using var ms = new MemoryStream(data.Bytes);
var bitmap = new PdfBitmap(ms);
```

## 9.3 Draw at correct bounds

```csharp
page.Graphics.DrawImage(bitmap, targetBounds);
```

## 9.4 Associate with structure element

Use the same pattern already used for tagging text elements.

## 9.5 Decorative logic

If decorative:

- No alt attribute
- Do not force reading order  
- Optionally mark as artifact in future phases

---

# 10. Debug Toggle

In config:

```json
"AccessibilityRemediation": {
  "EnableFigureRedraw": true
}
```

### Behavior:

- **TRUE** → draw all images  
- **FALSE** → create structure nodes **but skip drawing**  

This isolates layout and tagging logic.

---

# 11. Coordinate System

Ensure FigureBlock.Bounds uses the **same coordinate system** as other LogicalBlocks.

If PDF gives values in native coordinates (bottom-left origin):

- Convert to system your LogicalDocument uses (likely top-left origin, Y-down).

Do NOT mix coordinate systems.

---

# 12. Testing Requirements

## 12.1 Create a simple PDF containing:

- A header logo (decorative)
- A small icon (decorative)
- A meaningful image (diagram, photo, chart)

## 12.2 Verify:

### Tagging Module
- Alt text exists
- Decorative flags correct  
- Bounds correct  

### Structure Tree
- `Figure` nodes present  
- `decorative="true"` correct  
- Meaningful images have alt text  

### Rebuilt PDF
- Images drawn in correct locations  
- Decorative figures skipped by screen reader  
- Alt text available for meaningful images  

---

# 13. Phase 3b Completion Summary

Claude must implement:

1. Locate & unify image tagging module.  
2. Normalize output → `ImageTagResult`.  
3. Add decorative classifier.  
4. Add `FigureDetectionEnricher`.  
5. Update `FigureBlock`.  
6. Add image cache.  
7. Add `StructureRebuildContext`.  
8. Emit figure nodes in structure tree.  
9. Implement minimal layout plan.  
10. Draw figures in writer.  
11. Add debug toggle.  
12. Test with synthetic PDF.

This completes the full implementation of **Phase 3b**.

