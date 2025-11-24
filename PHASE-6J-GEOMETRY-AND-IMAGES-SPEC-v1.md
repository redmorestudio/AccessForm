# Phase 6J – Improved Geometry & Image Support for External MCID Rewriter (v1)

**Goal**  
Extend the Phase 6H/6I external MCID rewriter so that:

1. Text geometry is more accurate (better width/position estimates).
2. Images (and other XObject-based figures) are detected, given bounding boxes, and wrapped with BDC/EMC + MCID when segments target figures.
3. The segmentation logic can handle both text and image content for a given `McidSegment`.

You can give this file to Claude and say:

> “Implement everything in `PHASE-6J-GEOMETRY-AND-IMAGES-SPEC-v1.md`.”

This spec assumes 6H and 6I are already implemented and builds on them.

---

## 0. Existing Baseline (from 6H / 6I)

We already have:

- A Python microservice using `pikepdf` that:
  - Accepts `pdfBase64` and a `McidRewritePlan` (segments with MCID + bounding boxes).
  - Parses per-page content streams into `ContentStreamInstruction` objects.
  - Computes *approximate* geometry for text (`Tj`, `TJ`) using a `TextState` and basic Tm / Td / TD / Tf handling.
  - Groups instructions per page, maps them to segments using bounding box overlap, and wraps those instruction ranges with BDC/EMC.

- Data model:
  - `McidSegment` (JSON):
    - `PageIndex`, `Mcid`, `Role`, `X`, `Y`, `Width`, `Height`, `SequenceIndex`.
  - `ContentStreamInstruction`:
    - `operands`, `operator`
    - geometry fields: `x`, `y`, `bbox` (approx).

Phase 6J improves the geometry (especially width) and adds support for images.

---

## 1. Improved Text Geometry

### 1.1 Requirements

We want better bounding boxes for text instructions to reduce overlap mistakes and make segment assignment more robust.

Specifically:

- Use **font metrics** where possible (from the font dictionaries in the PDF).
- Handle:
  - Single-string `Tj`
  - Array-based `TJ` (multiple strings + spacing).
- Track per-font information (average width, width table) to compute text width in user space.

### 1.2 Font info extraction

Add a **FontInfoCache** that, per page, can look up font metrics from the page’s `/Resources` dictionary:

- For each font resource (e.g. `/F1`, `/F2` in `/Resources/Font`):
  - Get:
    - `Subtype` (Type1, TrueType, Type0, etc.)
    - `Widths` array, if present.
    - `FirstChar`, `LastChar`, if present.
    - `ToUnicode` map (optional; can be used to interpret character codes, but may be overkill for v1).

For v1, a simplified approach is fine:

- Derive an **average glyph width** per font:

  - If `/Widths` exists:
    - Compute `avg_width = sum(Widths) / len(Widths)` in glyph units.
  - Otherwise:
    - Use a default (e.g. 500 glyph units).

- Store a `FontInfo`:

  ```python
  class FontInfo:
      def __init__(self, name, avg_width_glyph_units=500, units_per_em=1000):
          self.name = name
          self.avg_width = avg_width_glyph_units
          self.units_per_em = units_per_em  # assume 1000 if unknown
  ```

- Maintain a mapping per page:

  ```python
  font_cache = {
      "/F1": FontInfo("/F1", avg_width_glyph_units=520),
      "/F2": FontInfo("/F2", avg_width_glyph_units=480),
      ...
  }
  ```

### 1.3 TextState with font name

Extend `TextState` to track the current font resource name and font size:

```python
class TextState:
    def __init__(self):
        self.in_text = False
        self.x = 0.0
        self.y = 0.0
        self.font_size = 12.0
        self.font_name = None  # like "/F1"
```

When parsing:

- On `Tf` (font selection):

  - Operands: `/FontName fontSize`.
  - Set:
    - `text_state.font_name = font_resource_name` (string)
    - `text_state.font_size = fontSize`.

### 1.4 Width estimation for Tj and TJ

For each text-drawing instruction:

- `Tj`:

  - Operand: a single string, e.g. `"Hello"`.
  - Steps:
    - Compute approximate glyph count: `len(text_string)`.
    - Lookup `FontInfo` from `font_cache` using `text_state.font_name`.
    - Compute width in user units:

      ```python
      glyphs = len(text_string)
      avg_glyph_width = font_info.avg_width  # glyph units
      # Convert glyph units (e.g. 1000 units per em) to user units:
      text_width = (glyphs * avg_glyph_width / font_info.units_per_em) * text_state.font_size
      ```

- `TJ`:

  - Operand: an array of mixed strings and numeric spacing adjustments.
  - For v1:
    - Sum character widths for strings as above.
    - Ignore numeric spacing terms or treat them as small adjustments.
    - Optionally add/subtract a simple factor to width based on these numbers.

Use:

```python
inst.x = text_state.x
inst.y = text_state.y
inst.bbox = (inst.x, inst.y, text_width, text_state.font_size)
```

If `font_info` is missing, fall back to a constant width like:

```python
text_width = max(50.0, len(text_string) * text_state.font_size * 0.5)
```

This is an improvement over the previous “pure guess” and should better distinguish adjacent columns/blocks.

---

## 2. Image Geometry & Figure Support

### 2.1 Requirements

We need to:

- Detect **image drawing instructions** in the content stream.
- Compute approximate bounding boxes in page user coordinates.
- Associate those instructions with `McidSegment`s for figures (e.g. `Role == "Figure"` or segments corresponding to images).
- Wrap them with BDC/EMC + MCID.

### 2.2 Graphics state & CTM tracking

Introduce a **GraphicsState** to track the current transformation matrix and manage the `q`/`Q` stack:

```python
import numpy as np

class GraphicsState:
    def __init__(self):
        # 3x3 matrix; start as identity
        self.ctm = np.identity(3)

class GraphicsStateStack:
    def __init__(self):
        self.stack = [GraphicsState()]

    @property
    def current(self):
        return self.stack[-1]

    def push(self):
        # clone top
        new_state = GraphicsState()
        new_state.ctm = self.current.ctm.copy()
        self.stack.append(new_state)

    def pop(self):
        if len(self.stack) > 1:
            self.stack.pop()
```

When parsing instructions:

- On `q`:
  - `gfx_stack.push()`.
- On `Q`:
  - `gfx_stack.pop()`.
- On `cm`:
  - Operands: `a b c d e f`.
  - Multiply the current CTM by this matrix:

    ```python
    cm_matrix = np.array([[a, b, 0],
                          [c, d, 0],
                          [e, f, 1]])
    gfx_stack.current.ctm = gfx_stack.current.ctm @ cm_matrix
    ```

Attach the current CTM (or its copy) to relevant instructions:

```python
inst.ctm = gfx_stack.current.ctm.copy()
```

### 2.3 Detecting image XObjects (Do)

Image drawing uses:

- `Do /XObjectName`

In parsing:

- When you see operator `Do`:
  - The last operand is the XObject name: e.g. `/Im0`.
  - Resolve it via the page’s `/Resources /XObject` dictionary.
  - Inspect the XObject:

    - If `/Subtype /Image` → **image**.
    - If `/Subtype /Form` → form XObject (could contain nested content; for now treat separately or ignore).

### 2.4 Computing image bounding boxes

For image XObjects:

1. Get intrinsic width/height in image space:

   - From the image dictionary (via pikepdf):
     - `/Width` and `/Height` (pixel units).

2. Build a rectangle in *image local coordinates*:

   - E.g., four corner points: `(0, 0)`, `(width, 0)`, `(width, height)`, `(0, height)`.

3. Transform those points into page user space using `inst.ctm`:

   - For each `(x, y)`:

     ```python
     v = np.array([x, y, 1])
     transformed = inst.ctm @ v
     tx, ty = transformed[0], transformed[1]
     ```

4. Compute bounding box:

   - `ix = min(tx for all corners)`
   - `iy = min(ty for all corners)`
   - `iw = max(tx) - ix`
   - `ih = max(ty) - iy`

5. Set:

   ```python
   inst.x = ix
   inst.y = iy
   inst.bbox = (ix, iy, iw, ih)
   inst.is_image = True
   inst.xobject_name = "/Im0"  # for debugging
   ```

### 2.5 Mapping image instructions to segments

Once images have bounding boxes:

- Reuse the **same overlap logic** as text (`bbox_overlaps`).
- Segments that correspond to figures:

  - Heuristics:
    - If `McidSegment.Role == "Figure"` → segment targets an image.
    - Or, if segment bounding box overlaps an image bbox strongly (e.g., high IoU), we treat that image as belonging to that segment.

For v1:

- For each image instruction (with `inst.is_image == True` and `inst.bbox` not None):

  - For each segment on the same page:
    - If `McidSegment.Role == "Figure"` **and** `bbox_overlaps(seg_bbox, inst.bbox)`:
      - Assign instruction index to that segment’s `segment_instruction_indices`.

If you want to also allow images inside non-figure segments (e.g., decorative images in a paragraph), you can relax the `Role == "Figure"` condition, but starting strict is fine.

---

## 3. Combined Segment Assignment (Text + Images)

After improving geometry and adding image bboxes:

- The step that builds `segment_instruction_indices` should consider **both**:

  - Text instructions (`Tj`, `TJ` with `inst.bbox`).
  - Image instructions (`Do` with `inst.bbox`).

Result:

```python
segment_instruction_indices = {
    mcid_value: sorted(list_of_instruction_indices_for_text_and_images)
}
```

From here, **Phase 6I’s wrapping logic** remains valid:

- For each segment:
  - `start = min(indices)`, `end = max(indices)`.
  - Insert BDC before `start`, EMC after `end`.

That means:

- A segment can contain both text and images; the MCID block will cover them both.

---

## 4. Robustness & Edge Cases

### 4.1 No bbox

If `inst.bbox` is `None`:

- Do not assign it to any segment.
- This keeps the system conservative: only content with known geometry gets MCID-wrapped.

### 4.2 Multiple segments overlapping the same content

If a single instruction overlaps multiple segments:

- Prefer the segment with:
  - The **smallest** bbox area that still covers the instruction (most specific), or
  - The earliest `SequenceIndex` on the page.

For v1:

- Implement: pick the segment with earliest `SequenceIndex`.

### 4.3 Segments with only image content

Some `McidSegment`s (e.g., a pure figure) may have no text instructions assigned, only image instructions. That is OK:

- `segment_instruction_indices[mcid]` will contain the indices of the `Do` instructions.
- We still wrap those instructions.

---

## 5. Integration Checklist

Claude should:

1. Add/extend:
   - `FontInfo` + `FontInfoCache` (per page).
   - `TextState` with `font_name`.
   - `GraphicsStateStack` with CTM tracking, `q`/`Q` support.
2. Update the instruction parsing loop to:
   - Maintain `TextState` and `GraphicsStateStack`.
   - Compute `inst.bbox` for `Tj`, `TJ` based on font metrics.
   - Compute `inst.bbox` for `Do` when drawing image XObjects, using CTM and image dimensions.
3. Update segment assignment to:
   - Use improved bbox-based overlap.
   - Include both text and image instructions.
4. Keep the Phase 6I wrapping logic (instruction range → BDC + EMC) as the final step on a per-page basis.

---

## 6. Testing

### 6.1 Synthetic geometry test

Create a 1-page PDF with:

- Two text lines (with different fonts/sizes).
- One image positioned near one of the segments.

Create a `McidRewritePlan` with:

- Segment 0: covering first line and the image.
- Segment 1: covering second line only.

Expected:

- BDC/EMC MCID=0 wraps the first line’s text instructions + the image Do instruction.
- BDC/EMC MCID=1 wraps only the second line’s text instructions.
- Visual layout unchanged.
- PDF opens cleanly and has non-zero BDC/EMC counts.

### 6.2 Alexandria regression

Re-run Alexandria with the same plan as before and confirm:

- Layout looks correct.
- BDC/EMC markers present.
- Figures/images (if any segments target them) are included in the right MCID range.
- PDFix/Acrobat show tags linked to appropriate content.

---

## 7. Summary for Claude

> - Upgrade the external MCID rewriter to:
>   - Use font metrics to estimate text width more accurately for Tj/TJ.
>   - Track graphics state (CTM) and detect image XObjects via Do operators.
>   - Compute bounding boxes for images using intrinsic size + CTM.
>   - Assign both text and image instructions to `McidSegment`s via bbox overlap.
>   - Wrap those instructions with BDC/EMC per segment, preserving order.
> - Keep the existing 6H/6I architecture, only enhancing geometry and adding image support.
> - Validate with synthetic tests and the Alexandria document.

---

**Filename:**  
`PHASE-6J-GEOMETRY-AND-IMAGES-SPEC-v1.md`
