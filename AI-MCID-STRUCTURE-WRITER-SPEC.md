
# AccessForm MCID & Syncfusion Structure Writer Spec  
**Single-source spec for binding StructureTree → real tagged content with MCIDs**  
_File name suggestion: `AI-MCID-STRUCTURE-WRITER-SPEC.md`_

---

## 0. Context and Goal

This spec assumes you have already implemented the pieces from:

- `AI-STRUCTURE-REBUILD-SPEC.md`  
  - `LogicalDocument`, `LogicalPage`, `LogicalBlock`, `HeadingBlock`, `ParagraphBlock`, `TableBlock`, `FigureBlock`, etc.
  - `StructureNode`, `StructureTree`, `StructureTreeBuilder`
  - `IPdfStructureWriter` + stub
  - `StructureRebuildService` in the remediation pipeline

**Now the goal is:**
> Implement a real `IPdfStructureWriter` using **Syncfusion PDF** that creates a valid tagged PDF where **structure elements are actually linked to content via MCIDs** (through Syncfusion’s tagged PDF APIs).

We will:
- Use Syncfusion’s **tagged PDF** support (which handles MCIDs internally).
- Map `StructureTree` nodes to `PdfStructureElement`s and text/image content.
- Ensure screen readers get correct reading order and semantics, not just a pretty `/StructTreeRoot`.

We will **not** manipulate low-level `/MCID` tokens ourselves; instead we rely on Syncfusion’s API to generate them correctly when we bind content elements to structure elements.

---

## 1. Quick PDF/MCID Primer (Conceptual)

You don’t need to implement `/MCID` manually if you use Syncfusion’s tagging API, but it helps to understand what it’s doing.

### 1.1. What is an MCID?

- MCID = **Marked Content ID**.  
- It is an integer that associates a piece of content in a page’s content stream (text, image, path) with a **structure element** in the structure tree.

At the raw PDF level, you see things like:

```pdf
/Span << /MCID 12 >> BDC
  (Hello world) Tj
EMC
```

In the structure tree, an element might reference that MCID:

```pdf
<<
  /Type /StructElem
  /S /P
  /Pg 4 0 R
  /K 12
>>
```

Screen readers follow `/StructTreeRoot` → `/K` → `/StructElem` → `/K` → `/MCID` → page content.

### 1.2. How Syncfusion helps

Syncfusion’s tagged PDF API essentially does this for you when you:

1. Turn on tagged PDF (`document.TaggedPdf = true;`).
2. Obtain the tagged content root (`PdfTaggedContent tagged = document.TaggedContent;`).
3. Create `PdfStructureElement` instances (e.g., headings, paragraphs, tables, figures).
4. Add content elements (e.g., `PdfTextElement`) that are associated with those structure elements.

Syncfusion internally creates the marked content sequences with MCIDs and links them up to the structure elements.

**Therefore:** our job is to correctly map `StructureTree` → `PdfStructureElement` hierarchy and route text/image drawing through those elements.

---

## 2. Strategy for `IPdfStructureWriter` with Syncfusion

### 2.1. Overall approach

We will implement `IPdfStructureWriter` as **a rebuild writer**:

- Input: original PDF bytes + `StructureTree`
- Output: **new PDF bytes** with:
  - Tagged PDF enabled
  - Structure elements created from `StructureTree`
  - Text drawn in the correct order and associated to structure elements
  - Figures (if available) associated to figure structure elements

Initial constraints/assumptions for Phase 1:

- We prioritize **accessibility and reading order** over visual pixel-perfect fidelity.
- It is acceptable if the rebuilt PDF is a simplified visual layout, as long as:
  - All text is present
  - Structure roles/headings/tables/figures are correct
  - Screen readers read content in the intended order.

Later phases can add better layout fidelity (using the `Rect` bounds on LogicalBlocks).

### 2.2. High-level steps inside `Rewrite`

1. Load original PDF into Syncfusion (optional, for reference only).  
2. Create a **new** `PdfDocument` instance and enable tagging (`TaggedPdf = true`).  
3. For each page in the logical model / structure tree:
   - Create a new `PdfPage` in the new document.
   - Use `PdfGraphics` to draw the text.
   - Use `PdfTaggedContent` / `PdfStructureElement` to semantically tag the content.
4. Map `StructureTree` roles to Syncfusion’s tag types (e.g., H1 → `PdfTagType.H1`, P → `PdfTagType.Paragraph`, Table → `PdfTagType.Table`, etc.).  
5. Save the new document to a byte array and return it.

MCIDs are handled internally by Syncfusion when we associate content to structure elements.

---

## 3. Mapping Roles → Syncfusion Tags

Define a small helper that maps our `StructureNode.Role` strings to Syncfusion’s `PdfTagType` / structural constructs.

Example (pseudo-C#):

```csharp
private static PdfTagType MapRoleToTagType(string role)
{
    return role switch
    {
        "Document" => PdfTagType.Document,
        "H1"       => PdfTagType.H1,
        "H2"       => PdfTagType.H2,
        "H3"       => PdfTagType.H3,
        "P"        => PdfTagType.Paragraph,
        "Table"    => PdfTagType.Table,
        "TR"       => PdfTagType.TableRow,
        "TH"       => PdfTagType.TableHeaderCell,
        "TD"       => PdfTagType.TableDataCell,
        "Figure"   => PdfTagType.Figure,
        _          => PdfTagType.Span
    };
}
```

Note: adjust to actual Syncfusion enum names (e.g., `PdfTagType.Paragraph`, `PdfTagType.Figure`, etc.). If Syncfusion uses different naming, adapt accordingly.

---

## 4. Implementing `SyncfusionPdfStructureWriter`

> **Task:** Replace the stub `IPdfStructureWriter` implementation with a real one backed by Syncfusion.

### 4.1. New class file

Create a new file, e.g.:

- `Services/Pdf/SyncfusionPdfStructureWriter.cs`

```csharp
using System.IO;
using AccessForm.Services.Pdf;
using AccessForm.Services.Remediation.Structure;
// using Syncfusion.Pdf;
// using Syncfusion.Pdf.Graphics;
// using Syncfusion.Pdf.Parsing;
// using Syncfusion.Pdf.Interactive;

namespace AccessForm.Services.Pdf;

/// <summary>
/// IPdfStructureWriter implementation backed by Syncfusion tagged PDF support.
/// Rebuilds a new tagged PDF from a StructureTree.
/// </summary>
public sealed class SyncfusionPdfStructureWriter : IPdfStructureWriter
{
    public byte[] Rewrite(byte[] originalPdf, StructureTree tree)
    {
        // NOTE: this implementation focuses on structure + content mapping;
        // layout is simplified but reading order and semantics are correct.

        using var outputStream = new MemoryStream();

        // 1. Create a new tagged PDF document
        using (var document = new Syncfusion.Pdf.PdfDocument())
        {
            document.TaggedPdf = true;

            var taggedContent = document.TaggedContent;
            var rootElement = taggedContent.StructureTreeRoot;

            // 2. For now, assume a single "Document" node at the root of StructureTree
            foreach (var node in tree.Nodes)
            {
                var rootChild = CreateStructureElementRecursive(
                    document,
                    taggedContent,
                    rootElement,
                    node);
            }

            document.Save(outputStream);
        }

        return outputStream.ToArray();
    }

    private Syncfusion.Pdf.PdfStructureElement CreateStructureElementRecursive(
        Syncfusion.Pdf.PdfDocument document,
        Syncfusion.Pdf.PdfTaggedContent taggedContent,
        Syncfusion.Pdf.PdfStructureElement parent,
        StructureNode node)
    {
        var tagType = MapRoleToTagType(node.Role);
        var element = new Syncfusion.Pdf.PdfStructureElement(tagType);
        parent.Children.Add(element);

        // Handle textual roles directly
        if (!string.IsNullOrEmpty(node.TextContent))
        {
            // For now, draw content on the first page; later we can use actual page mapping.
            var page = document.Pages.Count == 0
                ? document.Pages.Add()
                : document.Pages[document.Pages.Count - 1];

            var font = new Syncfusion.Pdf.Graphics.PdfStandardFont(
                Syncfusion.Pdf.Graphics.PdfFontFamily.Helvetica,
                10);

            var textElement = new Syncfusion.Pdf.PdfTextElement(node.TextContent, font);
            textElement.Element = element; // Link content to structure element

            // Simple layout: append vertically using a running Y coordinate
            // In a more advanced version, we can use node.Bounds (via LogicalDocument) to position.
            var layoutResult = textElement.Draw(page, 20, GetNextY(page));
        }

        // Apply alt text and other attributes for figures
        if (string.Equals(node.Role, "Figure", StringComparison.OrdinalIgnoreCase)
            && node.Attributes != null)
        {
            if (node.Attributes.TryGetValue("alt", out var altText))
            {
                element.AlternateText = altText;
            }
        }

        // Recurse into children
        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                CreateStructureElementRecursive(document, taggedContent, element, child);
            }
        }

        return element;
    }

    private double GetNextY(Syncfusion.Pdf.PdfPage page)
    {
        // TODO: Implement a simple running Y tracker per page
        // For a first version, you can store a Dictionary<PdfPage, double> and bump by line height.
        // Here we just use a fixed position for demo purposes.
        return 20;
    }

    private Syncfusion.Pdf.PdfTagType MapRoleToTagType(string role)
    {
        return role switch
        {
            "Document" => Syncfusion.Pdf.PdfTagType.Document,
            "H1"       => Syncfusion.Pdf.PdfTagType.H1,
            "H2"       => Syncfusion.Pdf.PdfTagType.H2,
            "H3"       => Syncfusion.Pdf.PdfTagType.H3,
            "P"        => Syncfusion.Pdf.PdfTagType.Paragraph,
            "Table"    => Syncfusion.Pdf.PdfTagType.Table,
            "TR"       => Syncfusion.Pdf.PdfTagType.TableRow,
            "TH"       => Syncfusion.Pdf.PdfTagType.TableHeaderCell,
            "TD"       => Syncfusion.Pdf.PdfTagType.TableDataCell,
            "Figure"   => Syncfusion.Pdf.PdfTagType.Figure,
            _          => Syncfusion.Pdf.PdfTagType.Span
        };
    }
}
```

> **IMPORTANT:** The Syncfusion namespaces, class names, and tag types above are representative. Adjust to match the exact Syncfusion version and API you are using in AccessForm.

### 4.2. Page layout & Y tracking

The stub above cheats with `GetNextY` always returning `20`. You’ll want to implement something like:

```csharp
private readonly Dictionary<Syncfusion.Pdf.PdfPage, double> _pageYPositions
    = new();

private double GetNextY(Syncfusion.Pdf.PdfPage page)
{
    if (!_pageYPositions.TryGetValue(page, out var y))
    {
        y = 40; // top margin
    }

    var next = y;
    _pageYPositions[page] = y + 16; // line spacing

    return next;
}
```

In a more advanced implementation, you can:

- Use `Rect` from the underlying `LogicalBlock` (via a mapping between `StructureNode` and `LogicalBlock`) to place content closer to the original layout.
- Create new pages when you exceed page height.

For **Phase 1**, a simple top-down layout is acceptable as long as content and structure are correct.

---

## 5. Handling Tables and Figures More Explicitly

The recursive `CreateStructureElementRecursive` method can be improved to special-case tables and figures:

```csharp
private Syncfusion.Pdf.PdfStructureElement CreateStructureElementRecursive(
    Syncfusion.Pdf.PdfDocument document,
    Syncfusion.Pdf.PdfTaggedContent taggedContent,
    Syncfusion.Pdf.PdfStructureElement parent,
    StructureNode node)
{
    var tagType = MapRoleToTagType(node.Role);
    var element = new Syncfusion.Pdf.PdfStructureElement(tagType);
    parent.Children.Add(element);

    var page = document.Pages.Count == 0
        ? document.Pages.Add()
        : document.Pages[document.Pages.Count - 1];

    if (node.Role == "Table" && node.Children != null)
    {
        // Node.Children: TR elements
        foreach (var rowNode in node.Children)
        {
            var rowElement = new Syncfusion.Pdf.PdfStructureElement(Syncfusion.Pdf.PdfTagType.TableRow);
            element.Children.Add(rowElement);

            if (rowNode.Children == null) continue;

            foreach (var cellNode in rowNode.Children)
            {
                var cellTagType = MapRoleToTagType(cellNode.Role);
                var cellElement = new Syncfusion.Pdf.PdfStructureElement(cellTagType);
                rowElement.Children.Add(cellElement);

                if (!string.IsNullOrEmpty(cellNode.TextContent))
                {
                    var font = new Syncfusion.Pdf.Graphics.PdfStandardFont(
                        Syncfusion.Pdf.Graphics.PdfFontFamily.Helvetica,
                        10);

                    var textElement = new Syncfusion.Pdf.PdfTextElement(cellNode.TextContent, font);
                    textElement.Element = cellElement;
                    textElement.Draw(page, 20, GetNextY(page));
                }
            }
        }
    }
    else if (!string.IsNullOrEmpty(node.TextContent))
    {
        var font = new Syncfusion.Pdf.Graphics.PdfStandardFont(
            Syncfusion.Pdf.Graphics.PdfFontFamily.Helvetica,
            10);

        var textElement = new Syncfusion.Pdf.PdfTextElement(node.TextContent, font);
        textElement.Element = element;
        textElement.Draw(page, 20, GetNextY(page));
    }

    if (node.Role == "Figure" && node.Attributes != null)
    {
        if (node.Attributes.TryGetValue("alt", out var altText))
        {
            element.AlternateText = altText;
        }
    }

    // Recurse for non-table structural children
    if (node.Role != "Table" && node.Children != null)
    {
        foreach (var child in node.Children)
        {
            CreateStructureElementRecursive(document, taggedContent, element, child);
        }
    }

    return element;
}
```

Again, **adjust Syncfusion API calls to match your version**.

---

## 6. Wiring the New Writer into DI

Replace the stub registration with the Syncfusion implementation.

In your DI configuration (e.g., `Program.cs` or `ServiceCollectionExtensions`):

```csharp
using AccessForm.Services.Pdf;

// Old:
//// services.AddSingleton<IPdfStructureWriter, StubPdfStructureWriter>();

// New:
services.AddSingleton<IPdfStructureWriter, SyncfusionPdfStructureWriter>();
```

Leave `StructureRebuildService` as-is; it now benefits from a real writer that creates tagged content with MCIDs under the hood.

---

## 7. Validating MCID Behavior

You don’t see MCIDs directly via Syncfusion, but you can validate that structure is really bound to content via:

1. **Acrobat Pro / Acrobat Reader**:
   - Use the Tags pane and “Highlight Content” to see if content highlights when you select a tag.  
   - If highlighting follows your tag tree and reading order, MCIDs are present and correct.

2. **Screen readers (NVDA, JAWS, VoiceOver)**:
   - Navigate by headings and read through the document.
   - Confirm that reading order matches your `StructureTree`.

3. **VeraPDF / PDF/UA validation**:
   - Run the output through VeraPDF.
   - Look specifically for errors related to:
     - “Structure element associated with no page content”
     - “Content not referenced from structure tree”

If those errors are gone and screen readers behave as expected, your MCID binding via Syncfusion is working.

---

## 8. Phased Implementation Plan

### Phase 1 — Minimal viable MCID writer

- Implement `SyncfusionPdfStructureWriter` as above with:
  - Simple top-down layout
  - Mapping `StructureTree` nodes to tags
  - Text-only content (no advanced figures or images yet)
- Validate on a sample brochure/schedule PDF:
  - NVDA reading order is correct
  - Headings navigable
  - Tables read as tables

### Phase 2 — Layout refinement

- Use the `Rect` positions from the underlying `LogicalBlock` (via a mapping between `StructureNode` and `LogicalBlock`) to:
  - Position text closer to original layout
  - Create new pages when needed
- Improve table rendering visually while preserving structure.

### Phase 3 — Images / Figures

- Extract map/figure images from the original PDF (using Syncfusion’s parsing API).
- Reinsert them into the new document as `PdfBitmap`/`PdfImage` drawn at reasonable positions.
- Associate those images to `Figure` structure elements, with `AlternateText` set from `node.Attributes["alt"]`.

---

## 9. “Do This” Summary for Claude Code

You can hand this section directly to Claude Code:

> 1. Create a new class `SyncfusionPdfStructureWriter` implementing `IPdfStructureWriter` in `Services/Pdf/`.  
> 2. In `Rewrite`, create a new `Syncfusion.Pdf.PdfDocument`, enable `TaggedPdf = true`, and obtain `document.TaggedContent.StructureTreeRoot`.  
> 3. Walk the incoming `StructureTree` and map nodes into Syncfusion `PdfStructureElement`s using a helper `MapRoleToTagType`.  
> 4. For nodes with `TextContent`, create `PdfTextElement` instances, attach them to the corresponding `PdfStructureElement` (e.g., `textElement.Element = element`), and draw them onto pages using a simple top-down layout.  
> 5. For tables, create `Table` → `TR` → `TH`/`TD` structure elements and draw cell text in order.  
> 6. For figures, set `element.AlternateText` from the node’s `Attributes["alt"]` when present.  
> 7. Remove or stop using `StubPdfStructureWriter`; register `SyncfusionPdfStructureWriter` as the `IPdfStructureWriter` implementation in DI.  
> 8. Run the remediation pipeline on a sample PDF and verify, using Acrobat + a screen reader, that tags are linked to actual content (highlighting in the Tags pane works, reading order follows the structure).  
> 9. Keep the implementation clean and modular so that we can later improve layout fidelity and add image support.

If Claude follows this spec and wires it into the existing `StructureRebuildService`, you will have a **MCID-backed, Syncfusion-based structural writer** that converts your AI-derived `StructureTree` into a real, usable tagged PDF that screen readers can navigate correctly.

---

## 10. Note for Seth

You can drop this file into your repo next to the first spec and tell Claude:

> “Implement the MCID/Syncfusion writer described in `AI-MCID-STRUCTURE-WRITER-SPEC.md`, and wire it into the existing structure rebuild pipeline.”

From there, you can iterate on layout fidelity and edge cases, but this gets you over the hump from “pretty but hollow tag tree” to **real, navigable, screen-reader-friendly structure**.  
