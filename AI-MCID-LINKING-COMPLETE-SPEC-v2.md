
# Phase 6 – COMPLETE MCID Linking & Content Association Specification (v2)

This is the **updated, authoritative spec** for MCID linking.  
It incorporates all clarifications about mutability, page index resolution, leaf-node selection, operator names, APIs, geometry, and testing.

File name: `AI-MCID-LINKING-COMPLETE-SPEC-v2.md`

---

## 0. Goal & Current State

We currently have:

- ✅ Semantic `StructureTree` created from `LogicalDocument`
- ✅ Visual content preserved via iText (`new PdfDocument(reader, writer)`)
- ✅ Aspose watermark issues handled separately
- ❌ MCIDs are not linked:
  - No `/MCID` in BDC/EMC in page content streams
  - No usable `MCR` kids (`PdfMcrNumber`) under `PdfStructElem`
  - Acrobat shows tags but cannot highlight text
  - Screen readers can’t navigate correctly

**Objective of Phase 6:**  
Attach real content to structure elements by:

1. Allocating MCIDs per leaf `StructureNode`
2. Wrapping right content segments with `BDC` / `EMC` and `/MCID n`
3. Attaching `MCR` kids to `PdfStructElem` via iText’s `PdfMcrNumber`
4. Keeping the visual appearance unchanged

This document tells Claude exactly how to do that.

---

## 1. StructureNode & MCID Data Model

### 1.1 StructureNode mutability (MCID references)

`StructureNode` is a `record`. We want:

- The **record structure** to stay logically immutable
- But have a **mutable collection** for MCID references

Do this:

```csharp
public sealed record StructureNode
{
    // existing fields: Role, TextContent, Attributes, Children, Bounds, etc.

    /// <summary>
    /// MCID references assigned to this structure node (per page).
    /// </summary>
    public List<McidReference> McidReferences { get; init; } = new();
}
```

And:

```csharp
public sealed class McidReference
{
    public int PageIndex { get; init; }
    public int Mcid { get; init; }
}
```

This lets you:

```csharp
node.McidReferences.Add(new McidReference { PageIndex = pageIndex, Mcid = mcid });
```

No need for `with` clones. We use `{ get; init; }` on the list, and mutate the list contents.

---

## 2. Page Index Resolution

We need a deterministic way to know **which page** a `StructureNode` belongs to when assigning MCIDs.

### 2.1 Bounds + LayoutPlan are the source of truth

Assumptions:

- `StructureNode.Bounds` is already populated from `LogicalBlock.Bounds`.
- `PageLayoutPlan` (from Phase 3c) has `PagePlan.Instructions`, each with:
  - `Node`
  - `TargetBounds`
  - `PageIndex` is implied by the `PagePlan`.

### 2.2 Recommended approach (use LayoutPlan)

During MCID assignment, use `PageLayoutPlan` as the canonical mapping:

1. Iterate over `PageLayoutPlan.Pages`:
   - `pagePlan.PageIndex` is the page index (0-based, or whatever you used consistently).
2. For each `DrawInstruction` in `pagePlan.Instructions`:
   - You have `instruction.Node` and the `PageIndex` from the `PagePlan`.

For each `StructureNode` you want to assign MCIDs to:

- Find the page where it appears by scanning the `PageLayoutPlan`:
  - First `PagePlan` whose `Instructions` include that `Node`.
- Use that `PageIndex`.

**Guideline:**

```csharp
int ResolvePageIndex(StructureNode node, PageLayoutPlan plan)
{
    foreach (var pagePlan in plan.Pages)
    {
        if (pagePlan.Instructions.Any(i => ReferenceEquals(i.Node, node)))
            return pagePlan.PageIndex;
    }

    // if not found – default to 0 or throw, depending on how strict you want to be
}
```

This is **more reliable** than trying to derive it solely from bounds, because it uses the same logic that placed the node onto pages geometrically.

> You do NOT need to add a `PageIndex` property to `StructureNode` for Phase 6. Use `LayoutPlan` to resolve pages.

---

## 3. Which Nodes Get MCIDs (Leaf Node Detection)

We only want MCIDs on nodes that correspond to **actual rendered content**. Not structural containers.

**Rules:**

A `StructureNode` should get MCIDs if:

1. It has **no children**, OR its children are purely structural/grouping, AND  
2. It represents one of the following roles:
   - `"P"` (paragraph)
   - `"H"`, `"H1"`–`"H6"` (headings)
   - `"Figure"`
   - `"Tbl"` (table)
   - `"TR"`, `"TH"`, `"TD"` (rows/cells)
   - Any node that corresponds to a **form field widget** (e.g., text field, checkbox)

Concrete implementation:

```csharp
bool IsLeafContentNode(StructureNode node)
{
    if (node.Children != null && node.Children.Count > 0)
        return false; // for now, MCIDs on leaf-like nodes only

    var role = node.Role; // or however role is stored
    switch (role)
    {
        case "P":
        case "H":
        case "H1":
        case "H2":
        case "H3":
        case "H4":
        case "H5":
        case "H6":
        case "Figure":
        case "Tbl":
        case "TR":
        case "TH":
        case "TD":
            return true;
        default:
            return false;
    }
}
```

If you already track “is artifact” or “is purely structural,” exclude those nodes.

We’re **not** using `TextContent != null` as the sole test, because figures, tables, and some form nodes may not have plain text.

---

## 4. BDC Operator Name (`/P`, `/Span`, etc.)

In page content streams, BDC uses a **marked-content tag name** (like `/P`), which is distinct from the **structure type**.

For Phase 6, we can keep this simple and consistent:

**Decision:**

- Use `/Span` for **all** BDCs in this phase, regardless of role.
- Let the **structure tree** express roles (P, H1, Figure, etc.), not the content stream.

So the inserted block will be:

```pdf
/Span <</MCID n>> BDC
    ...content operators...
EMC
```

Reasons:

- Simpler to implement.
- Structure tree already carries full semantics, and MCID binding is what matters.
- We can later refine and map role → tag name if needed.

---

## 5. Content Stream Manipulation API (iText7)

Best tool for this job in iText7 is **PdfCanvasProcessor**, not raw byte hacking.

**Approach:**

- Use `PdfCanvasProcessor` with a custom `IEventListener` to:
  - Walk operators and operands
  - Segment text/image content
  - Capture enough info to compute `ApproxTop` and `ApproxLeft`
- But, instead of letting `PdfCanvasProcessor` draw, you then:
  - Rebuild the content stream manually using the operator sequence you collected
  - Inject BDC/EMC around selected ranges.

So:

- Do **not** perform direct byte-level manipulation of the `PdfStream`.
- Do **not** only prepend/append – we need to insert inside.

Recommended pattern:

1. Use `PdfCanvasProcessor` to build a list like:

   ```csharp
   class PdfOp
   {
       public string Operator { get; set; }
       public List<object> Operands { get; set; }
       public float? ApproxTop { get; set; }
       public float? ApproxLeft { get; set; }
   }
   ```

2. Group these into `ContentSegment`s.
3. Decide which segments get which MCIDs.
4. Write a new sequence of `PdfOp`, injecting BDC/EMC ops.
5. Convert this op list back into a `PdfStream` for the page.

You can use lower-level token writing on the `PdfStream` for the **final** stream creation, but the **logic** should be driven by structured ops you got from `PdfCanvasProcessor`.

---

## 6. Geometry Approximation (How Fancy?)

For Phase 6, we do **not** need full-blown graphics state tracking. We just need a consistent ordering.

**Guidance:**

- Use **simple heuristics**:
  - For text:
    - Use the text matrix from events like `EventType.RENDER_TEXT` (iText exposes this through `TextRenderInfo`).
    - Extract baseline or ascent coordinates; use Y as `ApproxTop`, X as `ApproxLeft`.
  - For images:
    - Use image render events (e.g., `ImageRenderInfo`) to get the image’s CTM.
    - Take the transformed top-left coordinate as `ApproxLeft`/`ApproxTop`.

You do **not** need to manually multiply CTMs if iText’s `TextRenderInfo` and `ImageRenderInfo` already expose user-space coordinates.

Then:

- Sort segments by `ApproxTop` (ascending = visually top first), then `ApproxLeft`.
- Sort `McidTarget`s by their node bounds in the same way.
- Pair them sequentially.

This is “good enough” for Phase 6 as long as the layout is reasonably regular.

---

## 7. Testing PDF

You don’t need me to provide a PDF; for Claude, the simplest path is:

1. **Create one programmatically** in tests (or a tiny console utility):
   - New blank document
   - Write two paragraphs (`P1`, `P2`) and maybe one image
   - Save as `TestAssets/Pdfs/McidTwoParagraphs.pdf` (for example)

2. Use this PDF in integration tests for Phase 6:
   - Run full remediation (including MCID linking).
   - Inspect resulting PDF:
     - Confirm `/Span <</MCID n>> BDC`…`EMC` blocks exist.
     - Confirm structure `P` nodes have MCRs that point to those MCIDs.

3. Optionally also have a **hand-made** small PDF (e.g., from Word) for manual Acrobat + screen reader validation.

So: **Claude should create the minimal test PDF as part of the work**, not wait for an external one.

---

## 8. Updated High-Level Flow (with Clarifications Applied)

1. **Structure tree** is built from `LogicalDocument`.
2. **PageLayoutPlan** (Phase 3c) already exists, mapping nodes → pages.
3. **MCID allocation in `ITextPdfStructureWriter`:**
   - Iterate over `StructureTree` nodes.
   - For each node where `IsLeafContentNode(node)` is `true`:
     - Resolve `pageIndex` via `PageLayoutPlan`.
     - Get `PdfPage` for `pageIndex`.
     - Get corresponding `PdfStructElem`.
     - Create `PdfMcrNumber(page, structElem)` and get `mcid`.
     - Add `McidReference` to `node.McidReferences`.
     - Populate `mcidTargets[(pageIndex, mcid)]`.
4. **Content marking via `ITextMcidContentMarker`:**
   - For each page, gather text/image ops using `PdfCanvasProcessor`.
   - Build `ContentSegment`s with `ApproxTop`/`ApproxLeft`.
   - Match segments to MCIDs for that page in sorted order.
   - Rewrite page stream with `/Span <</MCID n>> BDC` … `EMC`.
5. Save PDF. Acrobat and screen readers now see structure elems linked to real content.

---

## 9. Implementation Checklist (v2 – With All Clarifications)

Claude should:

1. Update `StructureNode` to include `List<McidReference> McidReferences { get; init; } = new();`.
2. Implement `McidReference` class.
3. Implement `IsLeafContentNode(StructureNode)` using role + children rules above.
4. Use `PageLayoutPlan` to resolve `pageIndex` for each node.
5. In `ITextPdfStructureWriter`:
   - After building `PdfStructElem` tree:
     - For each leaf content node:
       - Resolve page index.
       - Get `PdfPage page = pdfDoc.GetPage(pageIndex + 1)`.
       - Get `PdfStructElem structElem`.
       - Allocate `PdfMcrNumber(page, structElem)` and `mcid`.
       - Add `McidReference` and populate `mcidTargets`.
6. Implement `ITextMcidContentMarker` using `PdfCanvasProcessor`:
   - Build `PdfOp` / `ContentSegment` lists for each page.
   - Approximate `ApproxTop`/`ApproxLeft` from `TextRenderInfo` / `ImageRenderInfo`.
   - Sort segments and targets, then assign MCIDs sequentially.
   - Rewrite streams with `/Span <</MCID n>> BDC` … `EMC`.
7. Add config flag `EnableMcidLinking` to optionally skip Phase 6.
8. Create a small programmatic test PDF (2 paragraphs + optional image).
9. Add tests that:
   - Confirm MCIDs are present in page content.
   - Confirm `P`/`Figure` tags have MCRs referencing those MCIDs.
   - (Optional) Confirm Acrobat tag highlight works correctly.

---

This document (`AI-MCID-LINKING-COMPLETE-SPEC-v2.md`) is the **single source of truth** for Phase 6 MCID linking and includes every clarification you asked about.
