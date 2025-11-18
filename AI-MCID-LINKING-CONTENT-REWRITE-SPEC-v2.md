
# Phase 6b – COMPLETE MCID Content Stream Rewrite Specification (v2)

This is the authoritative, single-file spec for implementing **real MCID → content linking** through **full content-stream reconstruction**.

It supplements and extends `AI-MCID-LINKING-COMPLETE-SPEC-v2.md` (structure-side MCIDs).

You can hand this directly to Claude Code and say:  
> “Implement everything in `AI-MCID-LINKING-CONTENT-REWRITE-SPEC-v2.md`.”

---

## 0. Purpose

Phase 6b implements **correct MCID linking** for accessibility by rewriting page content streams to insert:

/Span <</MCID n>> BDC
   ... original content operators ...
EMC

This is the only method that allows:

- Acrobat to highlight content when clicking a tag  
- PDFix to show tagged content  
- Screen readers to follow tags → MCIDs → content  

Structure-tree MCIDs from Phase 6 (via PdfMcrNumber) remain unchanged. Phase 6b **only** touches page content streams.

The design goals:

1. Keep visual output identical  
2. Keep logic **isolated** in a single module (IContentMcidMarker)  
3. Start with a **safe subset** of content types (text + images), with clear fallbacks  

---

## 1. Architecture Overview

### 1.1 New module

Create:

- Services/Pdf/IContentMcidMarker.cs  
- Services/Pdf/ItextContentMcidMarker.cs  

### 1.2 Interface

public interface IContentMcidMarker
{
    void Apply(
        PdfDocument doc,
        IReadOnlyDictionary<(int pageIndex, int mcid), McidTarget> targets);
}

Where McidTarget is the same as (or a refinement of) what we already have from Phase 6:

public sealed class McidTarget
{
    public StructureNode Node { get; init; }
    public Rect Bounds { get; init; }   // Node’s layout bounds on that page
}

Key points:

- pageIndex is 0-based  
- mcid is the page-scoped MCID assigned via PdfMcrNumber(page, structElem)  
- The **structure tree side** is already set up; we only add the content-side marking here  

### 1.3 Integration point

In ITextPdfStructureWriter (or equivalent):

1. Build structure tree → PdfStructElem hierarchy  
2. Allocate MCIDs and populate McidTargets via PdfMcrNumber  
3. Call content marker:

if (settings.EnableMcidLinking && settings.EnableMcidContentRewrite)
{
    _contentMcidMarker.Apply(pdfDoc, mcidTargets);
}

This must be done **before** pdfDoc.Close().

---

## 2. Content Stream Parsing Strategy

The marker works **page by page**.

### 2.1 High-level approach

For each page:

1. Extract and parse the **full content stream** into a sequence of operators  
2. Derive **content segments** (text blocks, images, inline images)  
3. Compute approximate geometry for each segment  
4. Match segments to MCID targets  
5. Rebuild the stream with BDC/EMC wrappers  

### 2.2 Data structures

Represent each operator as:

public sealed class PdfOp
{
    public string Operator { get; set; }           // e.g., "BT", "ET", "Tj", "TJ", "Do", "cm", "q", "Q", "BI", "EI"
    public List<object> Operands { get; set; }     // boxed numbers, PdfName, PdfString, etc.
    public float? ApproxTop { get; set; }          // for geometry estimation
    public float? ApproxLeft { get; set; }
}

We maintain:

public sealed class PageOps
{
    public int PageIndex { get; set; }
    public List<PdfOp> Ops { get; } = new();
}

### 2.3 Extraction mechanism

Use **iText 7’s canvas parsing infrastructure**, not raw byte hacking.

Implementation options (choose 1):

- Option A: Use PdfCanvasProcessor with a custom listener that:
  - Receives render events (TextRenderInfo, ImageRenderInfo)  
  - Simultaneously records low-level operator stream (tokens) and geometry  

- Option B: Implement a low-level tokenizer using iText’s PdfTokenizer and PdfCanvasParser, combined with manual interpretation of text/image events  

Required behavior:

- You must end up with **the full ordered operator list** for the page, including:
  - Graphics-state ops: q, Q, cm  
  - Text ops: BT, ET, Tj, TJ, Tm, Td, Tf, Tr, Ts, Tw, Tc, T*, etc.  
  - Image ops: /Im# Do  
  - Inline image ops: BI, ID, EI  

- For each text/image operator, you must also capture ApproxTop/ApproxLeft for segment sorting (see Section 4).

### 2.4 Handling complex streams (XObjects, forms, etc.)

For v1:

- **Do not descend into Form XObjects** (form content streams). Treat each Do that draws a form as:
  - A single opaque segment, OR  
  - Ignored for MCID marking  

Guideline:

- If you can easily detect that the XObject is an image → treat it as an image segment.  
- Otherwise:
  - Log a warning: “Skipped MCID marking for form XObject on page {p}”  
  - Do not alter that portion of the stream.  

Later phases can handle full recursion.

---

## 3. Segment Detection Logic

Segments are contiguous ranges of PdfOp representing “one piece of content” to attach an MCID to.

### 3.1 Segment model

public sealed class ContentSegment
{
    public int StartIndex { get; set; }     // index into PageOps.Ops
    public int EndIndex { get; set; }       // inclusive
    public float ApproxTop { get; set; }    // derived from ops inside segment
    public float ApproxLeft { get; set; }
    public int? AssignedMcid { get; set; }  // filled in later
}

### 3.2 Text segments

For v1, we use **BT/ET blocks as segments**:

- Find indices where Operator == "BT" and Operator == "ET".  
- For each matching pair (btIndex, etIndex) where:
  - btIndex < etIndex  
  - No nested BT/ET pairing that cannot be resolved  

Create a ContentSegment:

- StartIndex = btIndex  
- EndIndex = etIndex  

We do **not** currently attempt to split BT/ET blocks into smaller segments by individual Tj/TJ calls for v1.

### 3.3 Image segments (Do)

For image XObjects:

- Recognize pattern:

  - Typically: q, cm, /Im# Do, Q  

- For each PdfOp where Operator == "Do" and the operand is an image XObject:
  - StartIndex = index of preceding "cm" or "q" if present; if not, the "Do" index  
  - EndIndex = index of that same "Do" (or "Q" if you want to include the restore)  

For v1, it is acceptable to:

- Set StartIndex = doIndex  
- Set EndIndex = doIndex  

…if the surrounding state is stable.

### 3.4 Inline images (BI/ID/EI)

For inline images (BI … ID … EI):

- Detect the BI operator index  
- Continue until the matching EI operator index  
- Treat BI … EI as a single segment:

  - StartIndex = biIndex  
  - EndIndex = eiIndex  

### 3.5 Text-state operators (Tf, Tm, Td, etc.)

Text-state operators are included **inside** segments. They are not separate segments.

- For text segments, we include any Tf, Tm, Td, Tw, Tc, T*, Tr, Ts that appear between BT and ET.
- We do not segment based on these operators in v1.

---

## 4. Geometry Approximation

We need approximate (ApproxTop, ApproxLeft) for:

- Sorting segments by visual reading order  
- Matching them to McidTarget.Bounds  

### 4.1 Text geometry

Use iText’s TextRenderInfo data from PdfCanvasProcessor:

- For each text render event:
  - Get baseline start (or ascent) in user-space coordinates, for example:
    - baselineStart = textRenderInfo.GetBaseline().GetStartPoint()
    - or ascentStart = textRenderInfo.GetAscentLine().GetStartPoint()

- Record:
  - ApproxTop = baselineStart.Get(Vector.I2)  (Y coordinate)  
  - ApproxLeft = baselineStart.Get(Vector.I1) (X coordinate)  

Attach these values to the corresponding PdfOp entries for text operators (Tj, TJ, etc.).

### 4.2 Image geometry

Use ImageRenderInfo:

- Get a point representing the image position:
  - p = imageRenderInfo.GetStartPoint() (or equivalent)  

- Assign ApproxTop / ApproxLeft to the PdfOp that represents the Do operator for that image.

### 4.3 Segment-level geometry

For each ContentSegment:

- ApproxTop = minimum ApproxTop of all PdfOp inside that segment with non-null ApproxTop  
- ApproxLeft = minimum ApproxLeft of all PdfOp inside that segment with non-null ApproxLeft  

If a segment has no geometry:

- Set ApproxTop = float.MaxValue  
- Set ApproxLeft = float.MaxValue  

This ensures such segments sort last and will likely not be assigned an MCID.

---

## 5. MCID Assignment Algorithm

We now have:

- A list of ContentSegment for each page (with ApproxTop, ApproxLeft)  
- A list of McidTarget for each page (with Bounds from layout)  

### 5.1 Per-page preparation

For page index p:

1. Extract all segments for page p.  
2. Extract all (mcid, McidTarget) entries where pageIndex == p.  

### 5.2 Sort segments by reading order

Sort segments ascending by:

1. ApproxTop (top-to-bottom)  
2. ApproxLeft (left-to-right)  

### 5.3 Sort MCID targets by reading order

For each McidTarget:

- top = target.Bounds.Top  
- left = target.Bounds.Left  

Sort ascending by:

1. top  
2. left  

### 5.4 Matching: sequential zip

For indices i = 0 .. min(segments.Count, targets.Count) - 1:

- segment = segments[i]  
- target = targets[i]  

- segment.AssignedMcid =
  target.Node.McidReferences.First(r => r.PageIndex == p).Mcid

This is a **sequential approximation**: we assume the segments and targets are already roughly in visual reading order because both use consistent coordinate systems.

### 5.5 Handling mismatches

#### More segments than MCIDs

- For any i >= targets.Count:
  - segments[i].AssignedMcid = null  

These segments will not be BDC/EMC-wrapped.

#### More MCIDs than segments

- Targets with no corresponding segment are not used.  
- Log a warning:
  - “MCID content marker: Not enough content segments on page {p} for all MCIDs. Unmatched targets: {count}”  

We prefer having **unused MCIDs** to incorrect assignments.

---

## 6. Stream Reconstruction

With segments and their AssignedMcid values, we now rebuild the content stream.

### 6.1 Serialization primitives

We need a method to serialize a List<PdfOp> back into a byte[].

Each PdfOp serializes as:

- Operands (space-separated, using appropriate PDF syntax)  
- Space  
- Operator name  
- Newline  

Example:

- 1 0 0 1 50 700 Tm
- (Hello) Tj
- /Im1 Do

We can leverage iText’s low-level writer APIs (PdfOutputStream, etc.) to correctly encode numbers, strings, and names.

### 6.2 BDC/EMC insertion

We must build a new sequence of tokens that includes the original ops plus BDC/EMC wrappers.

Approach:

- Build maps for quick lookup:

  - Dictionary<int, ContentSegment> segmentsByStartIndex  
  - Dictionary<int, ContentSegment> segmentsByEndIndex  

Then:

For i from 0 to ops.Count - 1:

1. If there is a segment s with:
   - s.StartIndex == i
   - s.AssignedMcid has a value
   then before serializing ops[i], emit:

   - /Span <</MCID {s.AssignedMcid}>> BDC

2. Serialize ops[i] normally.

3. If there is a segment s with:
   - s.EndIndex == i
   - s.AssignedMcid has a value
   then after serializing ops[i], emit:

   - EMC

At the end, each matched segment is wrapped as:

/Span <</MCID n>> BDC
   ... original operators from StartIndex..EndIndex ...
EMC

### 6.3 Preservation of graphics state and structure

We must ensure:

- BT/ET pairs remain balanced:
  - For text segments, StartIndex should be at BT and EndIndex at ET.  
  - So BDC is emitted immediately before BT, and EMC immediately after ET.  

- Graphics state ops (q, Q, cm, etc.) remain in the same order relative to content.  

We are not reordering ops; we are only **inserting** new ones.

### 6.4 Resources

No changes to the resource dictionary are needed:

- MCID attribute is local to the marked content.  
- No new fonts, XObjects, or color spaces introduced.  

---

## 7. Error Handling & Recovery

### 7.1 Parsing failures

If content stream parsing fails, or if PdfCanvasProcessor throws:

- Log an error:
  - “MCID content marker: Failed to parse content stream on page {p}: {reason}”  

- Do not modify that page’s content stream.  
- Continue with other pages.  

### 7.2 Malformed text segments (BT/ET)

If you detect:

- BT without matching ET  
- ET without BT  
- Nested BT/ET that cannot be resolved into linear segments  

Then:

- Log a warning:
  - “MCID content marker: Malformed BT/ET structure on page {p}; skipping MCID marking for this page.”  

- Do not modify that page.

### 7.3 Severe mismatch of segments vs targets

Examples:

- segments.Count == 0 and targets.Count > 0  
- targets.Count == 0 and segments.Count > 0  

Behavior:

- Log a warning with counts.  
- Do not throw; do not modify the page.  

If you implement a “strict mode”, you may throw and fail the remediation job, but **default** behavior should be graceful.

### 7.4 Logging requirements

At minimum, log:

- For each page:
  - Page index  
  - Segment count  
  - MCID target count  
  - Match count  

- For warnings / errors:
  - Reason, including:
    - parse failures  
    - malformed BT/ET  
    - severe mismatch  

---

## 8. Feature Flags & Modes

Add to configuration:

{
  "AccessibilityRemediation": {
    "EnableMcidLinking": true,
    "EnableMcidContentRewrite": true,
    "McidRewriteMode": "Experimental"
  }
}

Where:

- EnableMcidLinking:
  - Enables Phase 6 (structure tree MCIDs)  

- EnableMcidContentRewrite:
  - Enables Phase 6b (content stream rewriting)  

- McidRewriteMode:
  - "Experimental": never throw, skip pages on error, log everything  
  - "Strict": throw when critical errors occur (optional; implement only if you want)  

If EnableMcidContentRewrite is false:

- Do not call IContentMcidMarker.Apply.  
- Result: structure tree has MCIDs, but page streams don’t → “floating tags”.

---

## 9. Testing Strategy

### 9.1 Minimal programmatic PDF (Claude should create)

Claude should create a helper that generates a small PDF:

- Page 1:
  - P1 at top: “Hello world”
  - P2 below: “Second paragraph”
  - One small image at the bottom  

Then:

- Run full remediation (structure rebuild + Phase 6 + Phase 6b) on this PDF.  

Check:

1. Page content stream includes:

   - /Span <</MCID 0>> BDC … EMC  
   - /Span <</MCID 1>> BDC … EMC  
   - /Span <</MCID 2>> BDC … EMC (for image, if MCID allocated)  

2. Tagged structure:

   - P tags and Figure tag have MCR kids pointing to correct MCIDs.  

3. Acrobat:

   - Clicking each P or Figure tag highlights the appropriate content.  

4. PDFix Desktop:

   - Tags appear with linked content.  

### 9.2 Real-world PDFs

Use at least one of your real target PDFs and:

- Confirm document still opens perfectly.  
- Confirm tags → highlight works better than pre-Phase-6b.  

### 9.3 Regression

Ensure that:

- When EnableMcidContentRewrite = false:
  - No content streams are modified  
  - Existing behavior remains.  

---

## 10. Implementation Checklist (Claude)

1. Implement IContentMcidMarker and ItextContentMcidMarker.  
2. Implement operator extraction into PdfOp list for each page.  
3. Implement ContentSegment construction:
   - BT/ET-based text segments  
   - Do-based image segments  
   - BI/EI-based inline image segments  

4. Implement geometry extraction from TextRenderInfo and ImageRenderInfo.  
5. Implement per-page matching:
   - Sort segments and McidTargets by visual order  
   - Sequential zip to assign MCIDs  

6. Implement stream reconstruction with BDC/EMC injection around matched segments.  
7. Integrate the marker into ITextPdfStructureWriter, before pdfDoc.Close().  
8. Add config flags and mode handling.  
9. Create a simple programmatic test PDF and verify behavior in Acrobat and PDFix.  

---

File name for this spec:

AI-MCID-LINKING-CONTENT-REWRITE-SPEC-v2.md

This document is self-contained and is the **single source of truth** for Phase 6b content-stream rewriting for MCIDs.
