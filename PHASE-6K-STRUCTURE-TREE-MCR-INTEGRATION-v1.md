# Phase 6K – Structure Tree + MCID Integration (v1)

**Goal**  
Finish the MCID story end‑to‑end by ensuring that every BDC/EMC + MCID marker inserted by the external rewriter is **actually connected to a structure element** in the PDF’s tag tree.

Right now (after 6H/6I/6J):

- Python microservice:
  - ✅ Parses content streams
  - ✅ Computes geometry for text + images
  - ✅ Inserts `/Span <</MCID n>> BDC … EMC` markers around the right segments
- But:
  - ❌ No corresponding /StructTreeRoot → /StructElem → /K entries pointing at those MCIDs
  - ➜ Markers are **orphaned** and don’t improve accessibility

Phase 6K defines how to:

1. Generate a **McidAssignmentPlan** in .NET during structure rebuild.
2. Write a **proper structure tree** using iText7 that references those MCIDs via MCR objects.
3. Ensure the MCID numbers in the structure tree and the MCID numbers the Python service uses are **identical**.

You can give this file to Claude and say:

> “Implement everything in `PHASE-6K-STRUCTURE-TREE-MCR-INTEGRATION-v1.md`.”

---

## 0. Architecture Overview

### 0.1 Two sides of MCID

In a fully linked tagged PDF, MCID appears in two places:

1. **Content side** (page content stream):

   /Span <</MCID 12>> BDC  
   ... text / image operators ...  
   EMC

2. **Structure side** (tag tree):

   /StructTreeRoot  
     /K [  
       << /S /P  
          /Pg 1 0 R  
          /K << /Type /MCR  
                 /Pg 1 0 R  
                 /MCID 12  
             >>  
       >>  
     ]

These must be consistent:

- The MCR’s `/MCID 12` must match the `/MCID 12` in the BDC block in the page content stream.
- The `/Pg` pointers must match the actual page object.

Phase 6H–6J handle (1).  
Phase 6K makes sure (2) exists and is consistent.

### 0.2 Division of responsibilities

- **.NET + iText7:**
  - Owns the **structure tree** and MCR objects.
  - Decides which logical nodes exist (Headings, Paragraphs, Figures, Form fields, etc.).
  - Assigns MCIDs to those nodes and records mapping.

- **Python microservice:**
  - Treats PDF as “tag tree already built”.
  - Only injects BDC/EMC in the right places using MCIDs from .NET.
  - Does **not** modify the structure tree.

---

## 1. Data Model on the .NET Side

### 1.1 StructureNode + MCID

Extend your logical/structural model so that each leaf node that should link to content has an explicit MCID.

In C# (conceptually):

```csharp
public sealed record StructureNode
{
    public string Role { get; init; }           // "P", "H1", "TD", "TH", "Figure", "Form", etc.
    public Rect? Bounds { get; init; }         // Logical bounds on page (from layout)
    public int? PageIndex { get; init; }       // 1-based page index
    public int? Mcid { get; init; }            // NEW: MCID assigned to this node
    public List<StructureNode> Children { get; init; } = new();
    public StructureNode? Parent { get; init; }
    // ... existing properties
}
```

Notes:

- Only nodes that represent actual content get `Mcid`:
  - Paragraphs, headings, table cells, figures, form fields, etc.
- Container nodes (Sections, Art, Div, Table containers, etc.) may **not** have `Mcid` themselves, but their children do.

### 1.2 McidAssignmentPlan

This is the **bridge** between .NET and the Python microservice. It is the only place where MCIDs are defined and must be used consistently by both sides.

Model:

```csharp
public sealed class McidSegment
{
    public int PageIndex { get; init; }      // 1-based
    public int Mcid { get; init; }           // MCID value
    public string Role { get; init; } = "";  // e.g. "P", "H1", "Figure"
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public int SequenceIndex { get; init; }  // reading order index
}

public sealed class McidRewritePlan
{
    public string DocumentId { get; init; } = "";
    public string Version { get; init; } = "6K-1";
    public List<McidSegment> Segments { get; init; } = new();
}
```

**Key rule:**

> `StructureNode.Mcid` and `McidSegment.Mcid` refer to the same MCID values.  
> The plan is derived from the structure tree, not invented independently.

---

## 2. Assigning MCIDs During Structure Rebuild

### 2.1 Where to do this

In the existing structure rebuild pipeline (ITextPdfStructureWriter / StructureTreeBuilder), add a step **after** logical tree layout but **before** building the iText structure tree:

1. You already have:
   - `LogicalDocument` / `StructureTree` with Role + Bounds + PageIndex.
   - `LayoutPlan` with reading order.

2. Add a service: `McidAssignmentService`:

```csharp
public interface IMcidAssignmentService
{
    void AssignMcids(StructureNode root);
    McidRewritePlan BuildRewritePlan(StructureNode root, string documentId);
}
```

3. Register and call it from the structure rebuild orchestrator **once per job**.

### 2.2 MCID assignment algorithm

Simple deterministic algorithm:

1. Walk all `StructureNode`s in **reading order**, limited to nodes that should have MCIDs:

   - `Role` in { "P", "H1", "H2", "H3", "H4", "H5", "H6", "TD", "TH", "Figure", "Form", "Lbl", "Span" } etc.
   - `PageIndex` not null.

2. Keep a running counter per page:

   ```csharp
   var mcidByPage = new Dictionary<int, int>(); // PageIndex -> next MCID
   ```

3. For each eligible node:

   - Let `page = node.PageIndex.Value`.
   - If `mcidByPage` does not contain `page`, initialize `mcidByPage[page] = 0`.
   - `var mcid = mcidByPage[page]++;`
   - Set `node.Mcid = mcid`.

This ensures:

- MCIDs are assigned sequentially **per page** starting at 0.
- The mapping is entirely under your control.

### 2.3 Building the McidRewritePlan

After MCIDs are assigned:

```csharp
public McidRewritePlan BuildRewritePlan(StructureNode root, string documentId)
{
    var segments = new List<McidSegment>();
    int sequence = 0;

    foreach (var node in EnumerateInReadingOrder(root))
    {
        if (node.Mcid is null || node.PageIndex is null || node.Bounds is null)
            continue;

        var b = node.Bounds.Value;
        segments.Add(new McidSegment
        {
            PageIndex = node.PageIndex.Value,
            Mcid = node.Mcid.Value,
            Role = node.Role,
            X = b.X,
            Y = b.Y,
            Width = b.Width,
            Height = b.Height,
            SequenceIndex = sequence++
        });
    }

    return new McidRewritePlan
    {
        DocumentId = documentId,
        Version = "6K-1",
        Segments = segments
    };
}
```

This is the object you serialize to JSON and send along with the PDF to the Python service.

---

## 3. Building the Structure Tree with MCRs (iText7)

### 3.1 Principle

For each `StructureNode` with an `Mcid`, we must:

- Create a corresponding `PdfStructElem` in the iText tag tree.
- Add a **MCR (Marked Content Reference)** as a kid, pointing to:
  - The correct page.
  - The corresponding MCID value.

iText7 has helpers like `PdfMcrNumber` or `PdfMcrDictionary` to do this.

### 3.2 Where to implement

In your current `ITextPdfStructureWriter` (or equivalent), you likely already have code that:

- Creates a `PdfDocument` with tagging enabled.
- Creates `PdfStructElem`s based on `StructureNode`s.
- Writes roles like `/H1`, `/P`, `/Table` etc.

Phase 6K requires updating this to also:

- Attach MCR kids with the **assigned MCID** from `node.Mcid` and **page reference**.

### 3.3 Pseudocode for structure building

Conceptual C# pseudocode (adapt to your actual code):

```csharp
void BuildStructureTree(PdfDocument pdfDoc, StructureNode root, McidRewritePlan plan)
{
    var tagStructRoot = pdfDoc.GetStructTreeRoot();

    // Map StructureNode -> PdfStructElem
    var elemByNode = new Dictionary<StructureNode, PdfStructElem>();

    void BuildRecursive(StructureNode node, PdfStructElem parentElem)
    {
        PdfStructElem currentElem;

        // Create a new struct element for this node
        currentElem = new PdfStructElem(tagStructRoot, node.Role);
        parentElem.AddKid(currentElem);
        elemByNode[node] = currentElem;

        // If this node has an MCID, add MCR kid
        if (node.Mcid is not null && node.PageIndex is not null)
        {
            var page = pdfDoc.GetPage(node.PageIndex.Value);
            int mcid = node.Mcid.Value;

            // Create an MCR object pointing to (page, mcid).
            // Example pattern (exact API may differ per itext version):
            var mcr = new PdfMcrNumber(page, mcid);

            currentElem.AddKid(mcr);
        }

        // Recurse into children
        foreach (var child in node.Children)
        {
            BuildRecursive(child, currentElem);
        }
    }

    // Root element - e.g., /Document or /Div container
    var rootElem = new PdfStructElem(tagStructRoot, "Document");
    tagStructRoot.AddKid(rootElem);

    foreach (var child in root.Children)
    {
        BuildRecursive(child, rootElem);
    }
}
```

Important:

- Use the **same `mcid`** value you assigned in `McidAssignmentService`.
- Use the **correct page** from the PdfDocument.

Now you have:

- Struct tree with elements.
- Each leaf struct element has a kid MCR referencing (page, mcid).

The Python rewriter then ensures the content stream has matching BDC/EMC for those MCIDs.

---

## 4. Full Flow: How Everything Fits Together

### 4.1 AI structure rebuild path

When `EnableMcidContentRewrite` is true and you’re doing AI structure rebuild:

1. **Preflight**:
   - Aspose font fixes, ARTIFACT-FIX etc. (Phase 6D).
2. **Structure rebuild (iText)**:
   - Build `StructureNode` tree for entire document.
   - Run `McidAssignmentService.AssignMcids(root)`.
   - Build `McidRewritePlan = BuildRewritePlan(root, documentId)`.
   - Build iText `PdfDocument` with tagging turned on.
   - Use `BuildStructureTree(pdfDoc, root, plan)` to create StructTreeRoot, elements, and MCRs.
   - Close PdfDocument ⇒ you now have:
     - Tags
     - Structure elements
     - MCR references (but no BDC/EMC yet).
3. **External MCID rewriter (Python)**:
   - Send:
     - The tagged PDF bytes from step 2.
     - The `McidRewritePlan`.
   - Python:
     - Parses page content streams.
     - Uses 6I/6J geometry (text + images) to find which instructions fall into each segment bbox.
     - Inserts `/Span <</MCID n>> BDC` … `EMC` around those instructions.
   - Returns new PDF bytes with:
     - StructTreeRoot + MCRs.
     - Matching BDC/EMC with MCIDs.
4. **Post-structure phases**:
   - MUST NOT rewrite or replace page content streams.
   - They may:
     - Add document-level metadata.
     - Update logical structure metadata that doesn’t touch content streams.

### 4.2 Retagging an already-tagged PDF (optional)

If you want to use this system for PDFs that already have structure trees (like the BAR example):

- Option A (simple, recommended for now):
  - Run full AI structure rebuild on them:
    - Treat them like untagged PDFs.
    - Build a new structure tree + MCIDs from scratch.
    - Use 6H–6K flow.
- Option B (advanced, later phase):
  - Read existing StructTreeRoot and MCRs.
  - Derive a `McidRewritePlan` from them.
  - Run Python rewriter only to fix broken/missing BDC/EMC segments.

Phase 6K focuses on Option A.

---

## 5. Testing Strategy

### 5.1 Synthetic AI-rebuilt PDF

1. Use a simple 1–2 page synthetic PDF as input to full remediation.
2. Verify output:

   - `strings output.pdf | grep -c "BDC"` > 0
   - `strings output.pdf | grep -c "EMC"` > 0

3. Open in PDFix / Acrobat:

   - Tags panel shows a meaningful structure tree.
   - Clicking a tag highlights the correct region on the page.
   - Screen reader (NVDA, JAWS) can navigate by headings/paragraphs in the correct order.

4. Dump the PDF structure (using PDFix, Acrobat Preflight, or a debug script) to verify:

   - MCR objects have `/MCID n` and `/Pg` pointing to the right pages.
   - Those MCIDs match the BDC/EMC markers.

### 5.2 Regression with BAR file

Later, once the pipeline is integrated:

- Run BAR PDF through full remediation (not just the Python microservice alone).
- Confirm:
  - No corruption.
  - Structure tree present.
  - MCIDs and markers linked as above.

---

## 6. Summary for Claude

> - Add an MCID assignment layer in .NET that:
>   - Walks the logical structure tree in reading order.
>   - Assigns per-page MCIDs to each leaf StructureNode.
>   - Produces a `McidRewritePlan` that will be sent to the Python service.
> - Update the iText-based `ITextPdfStructureWriter` to:
>   - Build a complete StructTreeRoot/StructElem hierarchy.
>   - For each StructureNode with an MCID, add an MCR kid referencing the correct page and MCID.
> - Ensure:
>   - The MCIDs in the structure tree match the MCIDs in `McidRewritePlan`.
>   - No downstream service rewrites content streams after the Python rewriter has run.
> - Test end-to-end with synthetic PDFs and then with real-world documents.

---

**Filename:**  
`PHASE-6K-STRUCTURE-TREE-MCR-INTEGRATION-v1.md`
