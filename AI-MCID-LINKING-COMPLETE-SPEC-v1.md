
# Phase 6 – COMPLETE MCID Linking & Content Association Specification  
_File: `AI-MCID-LINKING-COMPLETE-SPEC-v1.md`_  

This spec assumes:
- Structure trees are built
- Visual content is preserved with iText (reader → writer)
- MCIDs are **not** yet wired to real content

Goal: turn “floating tags” into **fully linked** tagged PDFs, where each structure element points to marked content in page streams via MCIDs.

---

## 0. Current State

From the existing implementation:

- ✅ Semantic `StructureTree` is built from `LogicalDocument`
- ✅ Visual content is preserved with:

  `new PdfDocument(reader, writer);`

- ✅ Aspose watermark issue is fixed via cloud API
- ❌ MCIDs are **not** linked:
  - No BDC/EMC with `/MCID` in page content
  - No `PdfMcrNumber` / `PdfMcrDictionary` kids under `PdfStructElem`
  - Acrobat shows tags, but cannot highlight underlying text
  - Screen readers cannot navigate content correctly

Phase 6 solves:

1. Mark content streams with BDC/EMC + MCID  
2. Create MCR kids (`PdfMcrNumber`) under structure elements  
3. Wire `StructureNode` ↔ MCID mapping

---

## 1. Core Concepts

### 1.1 Marked Content + MCID

In PDF syntax:

`/P <</MCID 12>> BDC`  
`    (Hello World) Tj`  
`EMC`

- `BDC` / `EMC` wrap a marked content sequence.
- `/MCID 12` is a page-scoped integer ID.

### 1.2 MCR (Marked Content Reference)

In the structure tree, a leaf `StructElem` holds a `K` entry pointing to this marked content.

In iText 7 (.NET):

```csharp
var mcr = new PdfMcrNumber(page, structElem);
var mcid = mcr.GetMcid();
```

This:

- Allocates the next MCID for that page
- Adds the MCR as a kid of `structElem`
- Returns the assigned `mcid` for the content stream

---

## 2. High-Level Design

Add one core component:

- `ITextMcidContentMarker` – low-level content-stream rewriter

Extend one existing component:

- `ITextPdfStructureWriter` – orchestrator that:
  1. Builds iText tag tree (`PdfStructElem` hierarchy)
  2. Allocates `PdfMcrNumber` per leaf `StructureNode`
  3. Builds mapping `(pageIndex, mcid) → StructureNode`
  4. Calls `ITextMcidContentMarker` to rewrite page content streams

Flow:

`LogicalDocument`  
→ `StructureTree`  
→ `ITextPdfStructureWriter` (build tags + allocate MCRs)  
→ `ITextMcidContentMarker` (insert BDC/EMC with MCID)  
→ `PdfDocument.Save()`

---

## 3. Data Model Changes

### 3.1 StructureNode – MCID references

Extend `StructureNode`:

```csharp
public sealed class StructureNode
{
    // existing fields...

    /// <summary>
    /// MCID references assigned to this node (per page).
    /// </summary>
    public List<McidReference> McidReferences { get; } = new();
}

public sealed class McidReference
{
    public int PageIndex { get; init; }
    public int Mcid { get; init; }
}
```

Even if we only use one MCID per node now, the list allows future refinement.

### 3.2 Internal mapping for marker

Internally, for the marker:

```csharp
public sealed class McidTarget
{
    public StructureNode Node { get; init; }
    // Optional: Bounds, role, etc.
}

Dictionary<(int PageIndex, int Mcid), McidTarget> mcidTargets;
```

---

## 4. ITextPdfStructureWriter – New Responsibilities

`ITextPdfStructureWriter` currently:

- Creates a `PdfDocument` (reader + writer)
- Builds a `PdfStructElem` tree mirroring our `StructureTree`
- Sets roles, attributes, etc.

Phase 6 adds MCID allocation and marker invocation.

### 4.1 Allocate MCIDs for leaf nodes

For each **leaf** `StructureNode` representing real content (P, H*, Figure, Table cell, form field, etc.):

1. Resolve target page:
   - Use `PageIndex` from node’s bounds or layout info.

2. Get corresponding iText `PdfStructElem` for that node (existing mapping).

3. Allocate MCR + MCID:

```csharp
var page = pdfDoc.GetPage(pageIndex + 1); // iText is 1-based
var mcr = new PdfMcrNumber(page, structElem);
var mcid = mcr.GetMcid();
```

4. Record in `StructureNode`:

```csharp
node.McidReferences.Add(new McidReference {
    PageIndex = pageIndex,
    Mcid = mcid
});
```

5. Build `mcidTargets`:

```csharp
mcidTargets[(pageIndex, mcid)] = new McidTarget {
    Node = node
};
```

Notes:

- `PdfMcrNumber(page, structElem)` automatically updates the `K` entry in `structElem`.
- MCIDs are page-scoped; iText handles uniqueness per page.

### 4.2 Integration point in writer

Inside `ITextPdfStructureWriter`:

```csharp
// 1. Build PdfStructElem tag tree from StructureTree

// 2. Allocate MCIDs and populate StructureNode.McidReferences + mcidTargets

// 3. Invoke marker BEFORE closing document
_mcidContentMarker.MarkContent(pdfDocument, mcidTargets);

// 4. Close document / return bytes
```

---

## 5. ITextMcidContentMarker – Design

Create:

`Services/Pdf/ITextMcidContentMarker.cs`

### 5.1 Purpose

For each page:

- Read existing content stream(s)
- Identify text + image segments
- Assign MCIDs to segments (using mcidTargets)
- Rewrite content stream to wrap those segments:

`/P <</MCID n>> BDC ... EMC`

while keeping visual output identical.

### 5.2 Per-page workflow

For each page `p`:

1. Extract all **content tokens**:
   - Operators (e.g., `BT`, `ET`, `Tj`, `TJ`, `Do`)
   - Operands (numbers, names, strings)

2. Segment the stream into logical “content segments”:
   - Text segment: from `BT` up to `ET` (or smaller groups, if needed)
   - Image segment: the `Do` operator plus its operands

3. Build:

```csharp
class ContentSegment {
    public int StartIndex;   // index in token list
    public int EndIndex;     // inclusive
    public float ApproxTop;  // approximate top Y
    public float ApproxLeft; // approximate left X
    public int? AssignedMcid;
}
```

4. Match segments to MCIDs for that page:
   - Build a sorted list of `McidTarget` for that page:
     - Sort by `Node.Bounds.Top` ascending (top to bottom)
     - Then `Bounds.Left` ascending (left to right)
   - Sort `ContentSegment`s similarly by `ApproxTop`, then `ApproxLeft`.
   - Sequentially assign:

```csharp
for i in 0..min(targets.Count, segments.Count)-1:
    segments[i].AssignedMcid = targets[i].Node.McidReferences.First().Mcid;
```

5. Rewrite tokens:
   - Copy tokens into a new list.
   - When hitting `segment.StartIndex`:
     - Insert tokens for:
       - `/P`
       - `<< /MCID n >>`
       - `BDC`
   - Copy segment tokens.
   - After `segment.EndIndex`:
     - Insert `EMC`.

6. Replace original page content stream with the rewritten stream.

### 5.3 Approximating segment geometry

We do **not** need pixel-perfect bounding boxes for Phase 6.

To compute `ApproxTop` / `ApproxLeft`:

- Track the current text matrix (`Tm`) within `BT`…`ET`.
- For text:
  - Use the current text position (`Tm` values) as approximation.
- For images:
  - Use the CTM or the arguments that set the image location (usually via `cm`).

This only needs to be consistent enough that:

- The order of segments roughly matches visual reading order.
- Sequential zipping with node bounds produces plausible matches.

---

## 6. Backward Compatibility

### 6.1 Config flag

Add:

```json
"AccessibilityRemediation": {
  "EnableMcidLinking": true
}
```

In your composition root:

- If `EnableMcidLinking = false`:
  - Skip MCID allocation and `_mcidContentMarker.MarkContent(...)`.
  - Keep the previous behavior (floating tags).

- If `true`:
  - Run full Phase 6 behavior.

---

## 7. Testing

### 7.1 Minimal 2-paragraph PDF

Create a tiny PDF:

- Paragraph 1: “Hello world”
- Paragraph 2: “Goodbye universe”

Run through Phase 6.

Verify:

1. Page content stream has:

   - `/P <</MCID 0>> BDC` … `EMC`
   - `/P <</MCID 1>> BDC` … `EMC`

2. In the structure tree:
   - Two `P` elements with `K` entries as MCRs referencing MCID 0 and 1 on that page.

3. Acrobat:
   - Clicking each tag highlights the correct paragraph.

4. Screen reader:
   - Reads the paragraphs in correct order.

### 7.2 PDFs with images

Create a PDF with:

- Paragraph
- Image
- Paragraph

Check:

- Image content is wrapped in `BDC/EMC` with an MCID.
- Image’s `Figure` node has an MCR kid referencing that MCID.

---

## 8. Implementation Checklist (Claude)

1. Add `McidReference` type and `List<McidReference> McidReferences` to `StructureNode`.
2. Extend `ITextPdfStructureWriter`:
   - After building tag tree, allocate `PdfMcrNumber` per leaf node.
   - Populate `StructureNode.McidReferences`.
   - Build `mcidTargets` dictionary.
3. Implement `ITextMcidContentMarker`:
   - Per-page token extraction.
   - Segment detection (text & image).
   - Approximate geometry calculation for segments.
   - Sequential matching of segments to MCIDs per page.
   - Content-stream rewriting with `/P <</MCID n>> BDC` … `EMC`.
4. Wire `ITextMcidContentMarker` into `ITextPdfStructureWriter`.
5. Add `EnableMcidLinking` config and conditional behavior.
6. Create minimal test PDFs and verify MCID linking via Acrobat and screen readers.

---

This file is the **authoritative spec** for implementing MCID linking in the iText-based remediation pipeline.
