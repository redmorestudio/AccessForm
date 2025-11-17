
# AI Figures / Images Spec  
_Phase 3b: Real Images, Real Figures, Real Alt Text_  
_File name suggestion: `AI-FIGURES-SPEC.md`_

---

## 0. Context & Goals

You now have:

- AI → `LogicalDocument` → `StructureTree`
- `StructureRebuildService`
- `StructureTreeCleaner` (artifacts + table semantics)
- `SyncfusionPdfStructureWriter` implementing `IPdfStructureWriter`
- Phase 2+ layout using bounding boxes and `IPageLayoutEngine`
- Phase 3a forms: interactive fields wired up and tagged

This spec adds **Phase 3b: Figures / Images**:

- Extract images/figures from the **original PDF**.
- Represent them in `LogicalDocument` and `StructureTree` with:
  - Page, bounds, semantics (alt text, decorative flag).
- Redraw them in the **rebuilt tagged PDF** using Syncfusion.
- Associate them with `Figure` structure elements and alt text so screen readers see them properly.

We will **reuse any existing figure/image logic** in the repo; no wheel-reinventing.

---

## 1. Reuse Existing Image / Figure Infrastructure

### 1.1. Where to look

The implementer should:

- Search the repo for:
  - `FigureBlock`
  - “map”, “logo”, “image extraction”, “XObject”
  - Any PDF services that work with images (Syncfusion/Aspose/iText wrappers).
- Identify:
  - Existing types that describe an image/figure (bounds, page index, source ID).
  - Existing logic that extracts images from a PDF.

**Rule:** if there is already an abstraction for an image in the PDF, use it. Only add new DTOs when necessary to bridge into the AI remediation pipeline.

### 1.2. Existing roles

We already use `StructureNode` with `Role == "Figure"` for figures.

We will standardize:

- Use `Role = "Figure"` for all image/figure nodes that are relevant to reading.
- Use attributes to capture alt text, decorative flag, and a link back to the original image.

---

## 2. Logical Model: FigureBlock

Add or update a `FigureBlock` logical block type to represent figures in the `LogicalDocument`:

- File: `Logical/FigureBlock.cs` or equivalent.

Example:

```csharp
namespace AccessForm.Logical;

public sealed record FigureBlock(
    Rect Bounds,
    int PageIndex,
    string? AltText,
    bool IsLikelyDecorative,
    string? SourceImageId
) : LogicalBlock(Bounds);
```

Notes:

- `Bounds` and `PageIndex` are required for layout.
- `AltText` may come from:
  - Existing heuristics.
  - Claude Vision describing the image.
  - Manual metadata if present.
- `IsLikelyDecorative`:
  - True for logos, borders, purely decorative icons.
  - False for maps, diagrams, content-bearing images.
- `SourceImageId`:
  - Stable identifier to locate the underlying image in the original PDF:
    - Could be XObject name, object number, or a synthetic ID tied to your existing extraction logic.

If the repo already defines a similar type, extend it rather than inventing a new one.

---

## 3. Populating FigureBlocks

### 3.1. Source of truth

Use a **post-AI enrichment step** (similar to forms) that:

- Opens the original PDF.
- Uses existing PDF/image extraction logic to:
  - Enumerate images per page.
  - Get bounding boxes in page coordinates.
  - Get any existing metadata (e.g., alt-like descriptions, or known logos).
- Optionally calls Claude Vision to:
  - Generate candidate alt text for content-bearing images.
  - Label likely decorative images.

### 3.2. Enrichment service

Create a service, for example:

- `FigureDetectionEnricher` (name is flexible, but intent should be clear).

Responsibilities:

1. Inputs:
   - `LogicalDocument` from AI analysis.
   - Original PDF bytes (same as passed into `StructureRebuildService`).
2. Outputs:
   - `LogicalDocument` with additional `FigureBlock`s appended to each `LogicalPage` where figures are found.
   - `FigureBlock`s populated with:
     - `Bounds`
     - `PageIndex`
     - `AltText`
     - `IsLikelyDecorative`
     - `SourceImageId`

Integration:

- `StructureRebuildService`:
  - After AI layout analysis and form enrichment, call `FigureDetectionEnricher` before building the `StructureTree`.

---

## 4. StructureTreeBuilder: turn FigureBlock into Figure nodes

Update `StructureTreeBuilder` so that when it sees a `FigureBlock`:

- It creates a `StructureNode` like:

```csharp
var attributes = new Dictionary<string, string>();

if (!string.IsNullOrWhiteSpace(figureBlock.AltText))
    attributes["alt"] = figureBlock.AltText;

attributes["pageIndex"] = figureBlock.PageIndex.ToString(CultureInfo.InvariantCulture);

if (!string.IsNullOrWhiteSpace(figureBlock.SourceImageId))
    attributes["sourceImageId"] = figureBlock.SourceImageId;

if (figureBlock.IsLikelyDecorative)
    attributes["decorative"] = "true";

var node = new StructureNode(
    Role: "Figure",
    TextContent: null,
    Attributes: attributes,
    Children: null)
{
    // Optional: store back-reference to the block if your StructureNode has that
    // SourceBlock = figureBlock
};
```

Let `StructureTreeCleaner` treat:

- `Figure` nodes with `decorative = "true"` as potential artifacts if you wish (in many cases, decorative logos might be kept as figures with no alt; either approach is fine depending on your accessibility stance).

---

## 5. SyncfusionPdfStructureWriter: Drawing Real Images

Extend `SyncfusionPdfStructureWriter` to handle `Figure` nodes.

### 5.1. Locate the image data

For each `Figure` node:

1. Determine the page and bounds:
   - Use `TargetBounds` from the layout engine (via `PageLayoutPlan` and `DrawInstruction`).
2. Locate the image bytes:
   - Open the original PDF as `PdfLoadedDocument` (or equivalent) using Syncfusion.
   - Use `SourceImageId` (if present) to find the correct image:
     - This could be mapped to:
       - A dictionary from `SourceImageId` → `PdfBitmap` or raw stream.
       - Or a lookup over page images where `Bounds` intersects closely with the figure’s `Bounds`.
   - If `SourceImageId` is not available:
     - Fallback: pick the image whose bounding box best overlaps the figure’s bounds on the same page.

Implementation detail:

- To keep this modular, encapsulate this logic in a helper class, e.g.:
  - `OriginalPdfImageResolver` with:
    - `PdfImage Resolve(FigureBlock figure, PdfLoadedDocument originalDoc)`

### 5.2. Draw the image into the new PDF

Once you have:

- `PdfPage page`
- `RectangleF targetBounds`
- `PdfImage image`

Do:

```csharp
var graphics = page.Graphics;

// If Syncfusion has a dedicated PdfImageElement that can be tied to a struct element,
// prefer that. Otherwise, use DrawImage within a tagged context.
graphics.DrawImage(image, targetBounds);
```

### 5.3. Associate with the Figure structure element

The exact API depends on your Syncfusion version. The high-level pattern:

1. Create a `PdfStructureElement` for the figure (if not already created).
2. Set `AlternateText` from `Attributes["alt"]` if present and `decorative != true`.
3. Ensure the image draw operation is associated with that structure element:
   - Using Syncfusion’s tagged content API:
     - For example, setting a property on a `PdfImageElement` or using a tagged content context that links drawing operations to the current structure element.

If your current writer already handles text with:

- `PdfTextElement.Element = structureElement`

Then the image equivalent should follow the same idea. Use Syncfusion docs / existing code in repo as a template.

---

## 6. Decorative vs Non-Decorative Figures

Policy:

- If `IsLikelyDecorative == true` and `AltText` is null/empty:
  - Either:
    - Draw the image but set no alt text (so it behaves as decorative).
    - Or treat it as layout-only and skip it entirely (depending on your accessibility standards).
- If `IsLikelyDecorative == false`:
  - Prefer to assign meaningful `AltText`.
  - If you don’t have alt yet, consider:
    - A short AI-generated description.
    - Or a placeholder like “Map of service area” if it’s obviously a map.

In all cases, **do not** put debug strings or internal type names into alt text.

---

## 7. Testing & Debugging Figures

1. Create or pick a PDF with:
   - A logo (decorative).
   - A map or diagram (content-bearing).
2. Run through the remediation pipeline.
3. Inspect in Acrobat:
   - Check Tags panel: `Figure` elements exist.
   - Use “Highlight Content” to ensure they match the right images.
   - Check element properties for alt text.
4. Use a screen reader:
   - Confirm that:
     - Logo is either skipped or announced minimally.
     - Map/diagram alt text is read.

For debugging isolation:

- Add a toggle/config flag:
  - `EnableFigureRedraw` (true/false)
- When false:
  - Writer skips image drawing but still creates `Figure` nodes.
- This lets you test structure separately from drawing and vice versa.
