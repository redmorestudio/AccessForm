
# Phase 6b – MCID Content Stream Rewrite – COMPLETE SPEC (v3)

This is the authoritative, single-file spec for implementing **real MCID → content linking** via **full content-stream reconstruction**.

It **extends** `AI-MCID-LINKING-COMPLETE-SPEC-v2.md` (structure-side MCIDs) and **supersedes** any earlier “6b” drafts.

You can hand this directly to Claude Code and say:  
> “Implement everything in `AI-MCID-LINKING-CONTENT-REWRITE-SPEC-v3.md`.”

All clarifying questions have been resolved and baked in.

---

## 0. Purpose

Phase 6b makes MCIDs actually work by rewriting page content streams to insert:

`/Span <</MCID n>> BDC`  
`   ... original content operators ...`  
`EMC`

This is required so that:

- Acrobat highlights the real content when clicking a tag
- PDFix shows tag ↔ content links
- Screen readers can navigate structure → MCID → content reliably

Phase 6b **only** touches page content streams.  
Structure tree + `PdfMcrNumber` MCID assignment from Phase 6 stay as-is.

Design goals:

1. Preserve visual output exactly (no layout changes)
2. Keep all MCID logic isolated in a single service
3. Start with a safe, limited subset (text + images) and fail gracefully

---

## 1. Architecture Overview

### 1.1 New module

Create:

- `Services/Pdf/IContentMcidMarker.cs`
- `Services/Pdf/ItextContentMcidMarker.cs`

### 1.2 Interface

```csharp
public interface IContentMcidMarker
{
    void Apply(
        PdfDocument doc,
        IReadOnlyDictionary<(int pageIndex, int mcid), McidTarget> targets);
}
```

Where:

```csharp
public sealed class McidTarget
{
    public StructureNode Node { get; init; }
    public Rect Bounds { get; init; }   // Node’s layout bounds on that page
}
```

Assumptions:

- `pageIndex` is 0-based
- `mcid` is the page-scoped MCID created earlier via `new PdfMcrNumber(page, structElem)`
- Targets are already restricted to nodes that should have MCIDs

### 1.3 Integration point

In `ITextPdfStructureWriter` (or equivalent):

1. Build tag tree (`PdfStructElem` hierarchy)
2. Allocate MCIDs with `PdfMcrNumber(page, structElem)` and build `targets`
3. Then:

```csharp
if (settings.EnableMcidLinking && settings.EnableMcidContentRewrite)
{
    _contentMcidMarker.Apply(pdfDoc, mcidTargets);
}
```

Must be called **before** `pdfDoc.Close()`.

---

## 2. Content Stream Parsing Strategy

### 2.1 REQUIRED approach (Option A)

Use **`PdfCanvasProcessor` with a custom listener**.

We explicitly choose **Option A** (PdfCanvasProcessor) because:

- It uses iText’s parsing & graphics state machinery
- It gives us `TextRenderInfo` / `ImageRenderInfo` (geometry) for free
- It’s more maintainable than raw tokenization

**Do not** implement a separate raw tokenizer for v3.

### 2.2 Data structures

Represent each low-level operator as:

```csharp
public sealed class PdfOp
{
    public string Operator { get; set; }           // e.g. "BT", "ET", "Tj", "TJ", "Do", "cm", "q", "Q", "BI", "EI"
    public List<object> Operands { get; set; }     // boxed numbers, PdfName, PdfString, etc.
    public float? ApproxTop { get; set; }          // for geometry
    public float? ApproxLeft { get; set; }
}
```

Per page:

```csharp
public sealed class PageOps
{
    public int PageIndex { get; set; }
    public List<PdfOp> Ops { get; } = new();
}
```

### 2.3 How to populate `PdfOp` with PdfCanvasProcessor

Implement a custom `IEventListener` that:

- Receives `EventType.RENDER_TEXT` and `EventType.RENDER_IMAGE`
- Maintains an in-memory list of `PdfOp` representing the **exact operator sequence**

The basic pattern:

- Wrap the page’s content with a `PdfCanvasProcessor` where:
  - `EventOccurred` gets the high-level info (text/image geometry)
  - At the same time, you also intercept operators & operands (using a combination of:
    - `PdfCanvasProcessor`’s internal event order, plus
    - A custom extension that records the low-level ops)

There are two ways you can do this in practice:

1. Use iText’s internal `PdfCanvasParser` to walk the page content and build `PdfOp` tokens while `PdfCanvasProcessor` handles geometry (you can combine both in one pass).
2. Or use `PdfCanvasProcessor`’s own callbacks and parallel a token-recording mechanism.

**Key requirement**: at the end of the pass, you must have:

- `PageOps.Ops`: complete operator list in correct order
- For ops that correspond to text / images, `ApproxTop` & `ApproxLeft` set from the associated render info.

### 2.4 Handling XObjects and forms

For **v3**:

- We **do not** descend into form XObjects (`/Subtype /Form`)
- We **do** treat image XObjects (`/Subtype /Image`) as images

When you see a `Do` operator:

- Look up the XObject in the page’s resources
- Inspect its dictionary:

```csharp
var xObject = resources.GetAsStream(PdfName.XObject).GetAsStream(name);
var subtype = xObject.GetAsName(PdfName.Subtype);
```

- If `subtype == PdfName.Image` → treat as **image segment**
- If `subtype == PdfName.Form` (or anything else) → for v3:
  - Log: `"MCID marker: skipping form XObject /{name} on page {p}"`
  - Do **not** mark it with MCID

Later phases can recursively process form XObjects.

---

## 3. Segment Detection Logic

Segments are contiguous ranges of `PdfOp` that together represent one logical “chunk of content” for MCID.

### 3.1 Segment model

```csharp
public sealed class ContentSegment
{
    public int StartIndex { get; set; }      // index into PageOps.Ops
    public int EndIndex { get; set; }        // inclusive
    public float ApproxTop { get; set; }
    public float ApproxLeft { get; set; }
    public int? AssignedMcid { get; set; }
}
```

### 3.2 Text segments (BT/ET)

For v3, we keep the simple model:

- A **text segment** = one `BT` .. `ET` block.

Algorithm:

1. Scan `PageOps.Ops` for indices where `Operator == "BT"` and `"ET"`.
2. Maintain a stack or single “currently open BT” index.
3. On `BT`:
   - If no BT is open, record `btIndex = currentIndex`.
   - If BT is already open, treat as malformed; we can decide to:
     - Close previous at `currentIndex - 1`, open new at `currentIndex`, or
     - Mark whole page as malformed and skip (safer).
4. On `ET`:
   - If BT is open:
     - Create a `ContentSegment`:
       - `StartIndex = btIndex`
       - `EndIndex = currentIndex`
   - If no BT is open:
     - Malformed → log and skip page.

The **simplest safe behavior**:

- If BT/ET nesting is inconsistent → log and skip MCID marking for that page.

### 3.3 Image segments (Do) – with bounded lookback

For each `PdfOp` where:

- `Operator == "Do"`
- The XObject subtype is `/Image` (from Section 2.4)

We create an image segment using a **bounded backward search**:

1. Let `doIndex` be the index of the `Do` operator.
2. Look backward up to **max 5 operators** (configurable constant, e.g. `MAX_LOOKBACK = 5`):
   - Starting from `i = doIndex - 1` down to `max(0, doIndex - MAX_LOOKBACK)`
   - If you encounter a `cm` or `q` operator that likely sets up the image transform:
     - Set `StartIndex = i`
     - `EndIndex = doIndex`
     - Stop the search.
3. If no `cm` or `q` is found within the lookback window:
   - Set `StartIndex = doIndex`
   - `EndIndex = doIndex`

We **do not** look arbitrarily far back (no unbounded scan), to avoid capturing unrelated state.

### 3.4 Inline images (BI/ID/EI)

For inline images:

1. Locate `BI` operators.
2. Scan forward until the matching `EI`.
3. Create a `ContentSegment` with:

   - `StartIndex = biIndex`
   - `EndIndex = eiIndex`

Internal `ID` and data remain inside the segment as-is.

### 3.5 Text-state operators

Operators like `Tf`, `Tm`, `Td`, `Tw`, `Tc`, `T*`, `Tr`, `Ts`:

- If they occur between a `BT` and `ET`, they are simply part of that text segment.
- We do **not** create separate segments for them.
- We do **not** split segments based on them in v3.

---

## 4. Geometry Approximation for Sorting

We want approximate `(ApproxTop, ApproxLeft)` to sort segments and MCID targets consistently.

### 4.1 Text geometry (PdfCanvasProcessor)

For each text render event (`TextRenderInfo`):

- Get a point for the text baseline or ascent:

```csharp
var baseline = textInfo.GetBaseline().GetStartPoint();
// or:
var ascent = textInfo.GetAscentLine().GetStartPoint();
```

- In PDF user space:
  - `x = point.Get(Vector.I1)`
  - `y = point.Get(Vector.I2)`

Attach:

- `ApproxLeft = x`
- `ApproxTop = y`

to the relevant `PdfOp` entries (those representing the actual `Tj` / `TJ` painting operation).

### 4.2 Image geometry (ImageRenderInfo)

For `ImageRenderInfo`:

- Get a reference point, e.g.:

```csharp
var p = imageInfo.GetStartPoint();
var x = p.Get(Vector.I1);
var y = p.Get(Vector.I2);
```

Attach:

- `ApproxLeft = x`
- `ApproxTop = y`

to the `PdfOp` that corresponds to `Do` (image draw).

### 4.3 Segment-level geometry

For each `ContentSegment`:

- `ApproxTop = min(ApproxTop of ops in segment that have non-null ApproxTop)`
- `ApproxLeft = min(ApproxLeft of ops in segment that have non-null ApproxLeft)`

If no ops have geometry:

- `ApproxTop = float.MaxValue`
- `ApproxLeft = float.MaxValue`

### 4.4 Coordinate system & sort direction (important)

We assume:

- PDF user-space coordinates: origin bottom-left
- `y` increases upwards
- A **visually higher** object has a **larger y** value.

We define:

- For targets: `top = Bounds.Top` **in PDF coordinates**
- For segments: `ApproxTop` from Text/ImageRenderInfo (PDF coordinates)

To sort in **visual reading order (top-to-bottom, then left-to-right)**:

- Sort by `top` **descending** (larger y first), then `left` ascending
- Sort by `ApproxTop` **descending**, then `ApproxLeft` ascending

So:

```csharp
segments = segments
    .OrderByDescending(s => s.ApproxTop)
    .ThenBy(s => s.ApproxLeft)
    .ToList();

targets = targets
    .OrderByDescending(t => t.Bounds.Top)
    .ThenBy(t => t.Bounds.Left)
    .ToList();
```

This fixes the ambiguity from earlier drafts.

---

## 5. MCID Assignment Algorithm

Per page `p`:

### 5.1 Prepare lists

- `segments` = all `ContentSegment` for page `p`, sorted as above
- `targets` = all `McidTarget` with `pageIndex == p`, sorted as above

### 5.2 Sequential matching

For `i = 0 .. min(segments.Count, targets.Count) - 1`:

```csharp
var segment = segments[i];
var target  = targets[i];

var refForPage = target.Node.McidReferences
    .First(r => r.PageIndex == p);

segment.AssignedMcid = refForPage.Mcid;
```

### 5.3 Handling mismatches

- **segments > targets**:
  - Extra segments: `AssignedMcid = null`
  - They will not be wrapped.

- **targets > segments**:
  - Extra targets are unused.
  - Log: `"MCID marker: Not enough segments on page {p}. targets={targets.Count}, segments={segments.Count}"`

We prefer **omitted bindings** over incorrect bindings.

---

## 6. Stream Reconstruction

Once `AssignedMcid` is set, rebuild each page’s content stream.

### 6.1 Serialization approach – use iText helpers

For v3, do **NOT** hand-build strings; use iText’s low-level output classes.

Recommended pattern:

1. Create a `MemoryStream`.
2. Wrap it in a `PdfOutputStream`.
3. For each `PdfOp` (plus any injected BDC/EMC pseudo-ops), call methods on `PdfOutputStream` to write:
   - Numbers
   - Names (PdfName)
   - Strings (PdfString)
   - Operators

The exact calls will depend on the iText 7 .NET API, but the idea is:

```csharp
void WriteOp(PdfOutputStream out, PdfOp op)
{
    foreach (var operand in op.Operands)
    {
        // use correct PdfOutputStream methods for numbers, names, strings, etc.
    }
    // then write operator name + newline
}
```

This avoids manual formatting pitfalls with PDF syntax.

### 6.2 BDC/EMC injection logic

Create maps:

```csharp
var segmentsByStart = segments
    .Where(s => s.AssignedMcid.HasValue)
    .ToDictionary(s => s.StartIndex);

var segmentsByEnd = segments
    .Where(s => s.AssignedMcid.HasValue)
    .ToDictionary(s => s.EndIndex);
```

Then:

```csharp
for (int i = 0; i < ops.Count; i++)
{
    if (segmentsByStart.TryGetValue(i, out var startSeg))
    {
        // write BDC
        out.WriteName("Span");
        out.WriteSpace();
        out.WriteBytes(Encoding.ASCII.GetBytes("<< /MCID "));
        out.WriteInt(startSeg.AssignedMcid.Value);
        out.WriteBytes(Encoding.ASCII.GetBytes(" >>"));
        out.WriteSpace();
        out.WriteKeyword("BDC");
        out.WriteNewLine();
    }

    WriteOp(out, ops[i]);

    if (segmentsByEnd.TryGetValue(i, out var endSeg))
    {
        out.WriteKeyword("EMC");
        out.WriteNewLine();
    }
}
```

(Exact helper calls may differ; Claude should adapt to actual iText API.)

### 6.3 Graphics state safety

For text segments:

- By construction, `StartIndex` = index of `BT`, `EndIndex` = index of `ET`.
- BDC is written **before** `BT`.
- EMC is written **after** `ET`.

So:

- BT/ET pairs remain intact.
- BDC/EMC pairs wrap entire text block safely.

For images:

- `StartIndex` is at `Do` or just before, `EndIndex` at `Do` (or `Q` if you choose).
- BDC is written just before `Do`.
- EMC just after `Do` (or after `Q` if you wrap `q/cm/Do/Q` group).

No operator is reordered, only wrapped.

### 6.4 Assigning new content stream

After writing ops+wrappers:

```csharp
var newBytes = ms.ToArray();
page.SetContent(newBytes);  // or equivalent iText call to replace content stream
```

Claude should use the idiomatic iText 7 .NET way to replace a page’s content stream.

---

## 7. Error Handling & Logging

### 7.1 Parsing errors

If operator extraction or PdfCanvasProcessor processing fails for a page:

- Log error:

  `"MCID marker: Failed to parse content stream on page {p}: {reason}"`

- Do not modify the page.
- Continue to next page.

### 7.2 Malformed BT/ET

If:

- Unbalanced BT/ET
- Nested BT/ET that can’t be resolved

Then:

- Log warning:

  `"MCID marker: Malformed BT/ET structure on page {p}; skipping MCID marking for this page."`

- Do not modify the page.

### 7.3 Severe mismatch or missing geometry

If:

- `segments.Count == 0 && targets.Count > 0`, or
- `targets.Count == 0 && segments.Count > 0`

Then:

- Log warning with counts.
- Leave page unmodified.

### 7.4 Logging requirements

For each processed page, log at least:

- Page index
- Segment count
- Target count
- Match count
- Whether page was modified or skipped

Wire logs into whatever `ILogger<IContentMcidMarker>`-style infrastructure you already use.

---

## 8. Feature Flags & Modes

Config:

```json
"AccessibilityRemediation": {
  "EnableMcidLinking": true,
  'EnableMcidContentRewrite": true,
  "McidRewriteMode": "Experimental"
}
```

Behavior:

- `EnableMcidLinking = false`:
  - Skip MCID allocation + content rewrite.
- `EnableMcidLinking = true`, `EnableMcidContentRewrite = false`:
  - Only structure tree gets MCIDs (floating tags).
- `EnableMcidLinking = true`, `EnableMcidContentRewrite = true`:
  - Run full Phase 6b.

`McidRewriteMode`:

- `"Experimental"` (default): never throw on page-level errors, just log and skip pages.
- `"Strict"` (optional): if implemented, can throw when parsing fails or when mismatches exceed thresholds.

---

## 9. Reuse of Existing ITextMcidContentMarker

If `ITextMcidContentMarker.cs` already exists:

- You may **reuse internal geometry extraction helpers** (anything that:
  - Converts `TextRenderInfo`/`ImageRenderInfo` to approximate coordinates
  - Helps with operator mapping)

But:

- The **overall algorithm** in this v3 spec (segmentation, matching, BDC/EMC insertion) **replaces** any previous logic.
- It is fine to:
  - Keep file name and class name
  - Gut/replace method bodies as long as they obey this spec

Do **not** keep any “before/after only” BDC hacks; they are explicitly superseded.

---

## 10. Testing Strategy

### 10.1 Programmatically generated test PDF

Claude should create a helper (e.g. in a test project) that generates a PDF:

- Page 1:
  - Paragraph 1 near top: “Hello world”
  - Paragraph 2 below it: “Second paragraph”
  - One small image at bottom

Run full pipeline (including structure rebuild, Phase 6, Phase 6b) and then verify:

1. Page content stream has:

   - `/Span <</MCID 0>> BDC` … `EMC`
   - `/Span <</MCID 1>> BDC` … `EMC`
   - `/Span <</MCID 2>> BDC` … `EMC` (if image has MCID)

2. Tag tree:
   - P and Figure nodes each have MCR kids referencing MCIDs 0, 1, 2 (or similar).

3. Acrobat:
   - Clicking each tag highlights the correct paragraph/image.

4. PDFix Desktop:
   - Tag selection shows linked content.

### 10.2 Real PDFs

Run one or more real-world PDFs:

- Confirm there are no visual regressions.
- Confirm tag→content navigation is improved where MCIDs are successfully assigned.

---

## 11. Implementation Checklist (Claude)

1. Implement `IContentMcidMarker` and `ItextContentMcidMarker`.
2. Use `PdfCanvasProcessor` (Option A) to produce `PageOps` with full operator list and per-op ApproxTop/ApproxLeft.
3. Build `ContentSegment`s:
   - BT/ET-based text segments
   - Bounded lookback Do-based image segments
   - BI/EI inline image segments
4. Compute segment-level geometry.
5. Per page:
   - Sort segments and McidTargets by visual order (y desc, x asc).
   - Sequentially assign MCIDs.
6. Rebuild content streams:
   - Use iText low-level output (PdfOutputStream) to serialize ops.
   - Inject `/Span <</MCID n>> BDC` and `EMC` at segment boundaries.
7. Replace page content streams with rewritten streams.
8. Add config flags and respect them.
9. Integrate call in `ITextPdfStructureWriter` before `pdfDoc.Close()`.
10. Build and run tests:
    - Programmatic small PDF
    - At least one real PDF
11. Remove or refactor any old MCID marker logic that conflicts with this design.

---

**File name:**  
`AI-MCID-LINKING-CONTENT-REWRITE-SPEC-v3.md`

This is the single source of truth for Phase 6b content-stream rewriting for MCIDs.
