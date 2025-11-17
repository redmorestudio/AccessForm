
# AI Tables & Artifacts Spec  
**Phase 2.5: Artifact vs Real Content + Smarter Tables**  
_File name suggestion: `AI-TABLES-AND-ARTIFACTS-SPEC.md`_

---

## 0. Context & Goal

You now have:

- AI → `LogicalDocument` → `StructureTree`
- `StructureRebuildService`
- `SyncfusionPdfStructureWriter` implementing `IPdfStructureWriter`
- MCID-backed tagged PDFs via Syncfusion
- A Phase 2 layout plan using bounding boxes

From the extracted tag tree of your building permit, we saw:  

- Real text correctly wired to MCIDs and structure elements  
- Tables built as `Table` → `TR` → `TD` → `P`  
- But also:
  - Cells whose content is **only `Path`** (checkbox outlines, table grid lines)
  - Some `TD`s with no child `K` (empty layout cells)
  - A mix of **true content** and **decorative/layout junk**  
  - Some tables that could be semantically clearer (header rows, scopes, etc.)

**This spec focuses on two things:**

1. **Artifacts vs Real Content**  
   - Mark decorative/layout-only items (borders, grid lines, purely visual cells) as `/Artifact`, or drop them from the structure.
   - Ensure only meaningful text (and meaningful figures) appear as true content elements.

2. **Improved Table Semantics**  
   - Consistent `Table` → `TR` → `TH`/`TD` hierarchy  
   - Heuristics to identify header rows and/or header columns  
   - Add `scope="column"` / `scope="row"` attributes  
   - Avoid empty junk cells where possible

We’ll do this at the **`StructureTree` level**, *before* the Syncfusion writer.

---

## 1. Representing Artifacts and Table Semantics in `StructureNode`

### 1.1. Extend `StructureNode` for artifacts and scopes

```csharp
public record StructureNode(
    string Role,
    string? TextContent,
    IReadOnlyDictionary<string, string>? Attributes,
    IReadOnlyList<StructureNode>? Children)
{
    public bool IsArtifact { get; init; }

    public string? TableScope { get; init; }
}
```

---

## 2. Table Semantics in `StructureTreeBuilder`

Refine the logic so:

- Header rows → `TH` with `scope="column"`
- Header columns → `TH` with `scope="row"`
- Decorative/empty cells → `IsArtifact = true`

Heuristics:

- If row index == 0 and multiple columns → column header
- If column index == 0 and multiple rows → row header
- If `TextContent` is null/empty AND source cell contains only non-text shapes → artifact

---

## 3. Artifact Detection Rules

### 3.1. When to mark a node as artifact

A `StructureNode` should be marked:

```csharp
node = node with { IsArtifact = true, Role = "Artifact" };
```

If:

- Underlying block contains only vector paths (checkbox borders, grid lines)
- Underlying text is empty/whitespace
- Underlying region is purely layout (spacers)
- Cell exists only to maintain table grid alignment

### 3.2. Propagation rules

- If a `TD` or `TH` is artifact, mark its `P` child (if any) as artifact.
- Artifact nodes should not produce MCIDs in Syncfusion writer.
- Artifact nodes should not appear as tags in the structure tree.
- Writer should treat them as `/Artifact` content or skip entirely.

---

## 4. Preprocessing Pass: `StructureTreeCleaner`

Create:

`Services/Remediation/Structure/StructureTreeCleaner.cs`

Responsibilities:

1. Traverse tree
2. Identify artifacts using heuristics
3. Rewrite nodes:
   - Mark artifact nodes
   - Remove empty paragraphs
   - Collapse empty `TD`s if safe
4. Identify header cells:
   - Add `scope` attribute
   - Convert `TD` → `TH` when heuristics apply
5. Normalize table structure:
   - Ensure consistent `TR` → `TH`/`TD` hierarchy
   - Ensure every cell has either:
     - Meaningful text
     - Or `IsArtifact = true`

Pseudo-code:

```csharp
public StructureTree Clean(StructureTree tree)
{
    foreach (var table in tree.AllNodes().Where(n => n.Role == "Table"))
        ProcessTable(table);

    foreach (var node in tree.AllNodes())
        DetectArtifact(node);

    return tree;
}
```

---

## 5. Syncfusion Writer Changes

### 5.1. Artifact nodes

Before creating a `PdfStructureElement`:

```csharp
if (node.IsArtifact)
{
    // Draw nothing, produce no structure element
    return null;
}
```

### 5.2. Header cell scopes

```csharp
if (node.Role == "TH" && node.TableScope is not null)
{
    element.Attributes["scope"] = node.TableScope; 
}
```

### 5.3. Empty table cells

If a `TD` or `TH` is non-artifact but has no text:

- Draw nothing, still produce the cell element for proper table structure.

---

## 6. “Do This” Instructions for Claude Code

> 1. Create a new class `StructureTreeCleaner` under `Services/Remediation/Structure/`.  
> 2. Implement table semantics:  
>    - Convert header rows/columns to `TH`  
>    - Add `scope="column"` or `scope="row"`  
> 3. Implement artifact detection:  
>    - If underlying `LogicalBlock` contains only paths or no meaningful text, mark node as `IsArtifact = true`.  
>    - Change role to `"Artifact"`.  
> 4. Ensure `StructureRebuildService` calls `StructureTreeCleaner` after `StructureTreeBuilder` but before `IPdfStructureWriter`.  
> 5. Update `SyncfusionPdfStructureWriter`:  
>    - Skip artifact nodes entirely.  
>    - Apply table header scopes when present.  
> 6. Add tests:  
>    - Table with header row → TH cells with scope="column"  
>    - Empty cell with only paths → IsArtifact = true  
>    - TD with text → preserved as real content  
>    - No artifact nodes appear as real tags in final PDF.  

---

## 7. What This Phase Solves

- Decorative boxes/lines no longer pollute tag structure  
- Header rows/columns become **true headers**  
- Assistive tech reads tables properly  
- Reduces noise in PDF/UA validators  
- Moves us closer to “commercial-grade” remediation output  

---

## 8. Next Steps (Phase 3.0+)

Later specs will cover:

- Form fields (checkboxes, radios) mapped as actual interactive controls  
- Real figure extraction and placement  
- Multi-column geometric flow with resolved overlap cases  
- RoleMap optimizations  
