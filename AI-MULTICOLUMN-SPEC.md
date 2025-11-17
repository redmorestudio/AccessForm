
# AI Multi-Column Layout Spec  
_Phase 3c: Column-Aware Reading Order & Placement_  
_File name suggestion: `AI-MULTICOLUMN-SPEC.md`_

---

## 0. Context & Goals

You now have:

- Bounding-box-aware `LogicalDocument`.
- Structure enrichment (forms, figures, artifacts).
- `StructureTree`.
- `IPageLayoutEngine` and `PageLayoutPlan` (or at least the spec for them).
- A working `SyncfusionPdfStructureWriter` that can:
  - Use a layout plan to draw text/fields/figures.
  - Associate structure elements and MCIDs correctly.

Goal for Phase 3c:

> Make the reading order and placement **column-aware**, so that multi-column pages read in the expected human order: left column top-to-bottom, then right column, etc., without headers/footers or sidebars interrupting the main flow.

We want this to be **modular** and debuggable:

- Multi-column logic should live almost entirely in the `IPageLayoutEngine` implementation.
- The writer should stay dumb: it just follows the plan.

---

## 1. Page Layout Engine Interfaces (Recap)

If not already created, define:

```csharp
public interface IPageLayoutEngine
{
    PageLayoutPlan BuildLayoutPlan(StructureTree tree);
}

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

    public int ColumnIndex { get; set; }  // new for multicolumn
    public string Region { get; set; } = "body"; // "header", "body", "footer", "sidebar"
}
```

This addition (`ColumnIndex`, `Region`) is what makes multi-column behavior explicit and debuggable.

---

## 2. Separate Implementations for Debuggability

Create **two** implementations of `IPageLayoutEngine`:

1. `SimpleTopDownLayoutEngine`:
   - Existing behavior (Phase 2):
     - Orders nodes purely by page, then Y (top-to-bottom), then X.
     - Ignores multi-column logic.
   - Serves as a debug baseline.

2. `ColumnAwareLayoutEngine`:
   - New behavior (Phase 3c):
     - Detects columns using X positions.
     - Assigns `ColumnIndex`.
     - Classifies header/footer/sidebar regions.
     - Orders nodes by (Region, ColumnIndex, Y).

Use DI or configuration to switch between them:

```csharp
if (settings.UseColumnAwareLayout)
    services.AddSingleton<IPageLayoutEngine, ColumnAwareLayoutEngine>();
else
    services.AddSingleton<IPageLayoutEngine, SimpleTopDownLayoutEngine>();
```

This lets you isolate multi-column behavior by flipping a flag.

---

## 3. Column Detection Algorithm

All of this lives inside `ColumnAwareLayoutEngine`.

### 3.1. Input set

For each page:

1. Gather all `StructureNode`s that:
   - Are not artifacts (i.e., `IsArtifact == false`).
   - Have `Bounds` (directly or via `SourceBlock.Bounds`).
2. Convert their bounds into a common coordinate system (if not already).

### 3.2. Compute column bands

Basic approach:

1. For each node, compute its horizontal center:
   - `centerX = bounds.Left + bounds.Width / 2`.
2. Sort nodes by `centerX`.
3. Walk the sorted list and group nodes into “bands” based on gaps:
   - Start a new column when the gap between adjacent `centerX` values exceeds some threshold.
   - Threshold could be:
     - A fraction of page width (e.g., 10–15%).
     - Or based on clustering/histogram.

Result:

- Each node gets a `ColumnIndex`:
  - `0` for leftmost column
  - `1` for next, etc.

Edge cases:

- Single-column pages will naturally form one band.
- Narrow sidebars may appear as their own band.

---

## 4. Region Classification: Header, Body, Footer, Sidebar

We add a simple “region” dimension:

- `"header"`
- `"body"`
- `"footer"`
- `"sidebar"`

### 4.1. Vertical bands (header/footer)

Compute:

- `headerThreshold = pageHeight * 0.15`
- `footerThreshold = pageHeight * 0.85`

For each node:

- If `bounds.Top < headerThreshold` → region `"header"`.
- Else if `bounds.Top > footerThreshold` → region `"footer"`.
- Else → candidate for `"body"` or `"sidebar"`.

### 4.2. Sidebar detection

Within the `"body"` region, detect narrow columns:

- For each column group:
  - Compute its average width.
  - If a column’s width is:
    - Significantly smaller than others (e.g., < 50% of max column width).
    - And placed at far left or far right.
  - Mark that column’s nodes as `"sidebar"`.

Otherwise:

- Default region for non-header/footer nodes is `"body"`.

---

## 5. Reading Order Construction

Inside `ColumnAwareLayoutEngine.BuildLayoutPlan`:

1. For each page:
   - Classify each node with:
     - `ColumnIndex`
     - `Region`
2. Produce `PagePlan.Instructions` in this order:
   - All `"header"` nodes:
     - Ordered by Y ascending, then X.
   - All `"body"` nodes:
     - Ordered by `ColumnIndex` ascending.
     - Within each column, by Y ascending.
   - All `"sidebar"` nodes:
     - Sorted by `ColumnIndex` and Y.
     - Typically this means right-hand narrow column gets read after main body.
   - All `"footer"` nodes:
     - Ordered by Y ascending, then X.

For each node, compute `TargetBounds`:

- Usually `TargetBounds = Bounds` converted to PDF coordinate system.
- If no bounds exist (rare), fall back to a simple top-down placement at the bottom of the page.

Create a `DrawInstruction`:

```csharp
instructions.Add(new DrawInstruction
{
    Node = node,
    TargetBounds = targetBounds,
    ColumnIndex = columnIndex,
    Region = region
});
```

Now the multi-column logic is fully encoded in `PageLayoutPlan`.

---

## 6. Syncfusion Writer Stays Simple

`SyncfusionPdfStructureWriter` behavior does **not** need to change much:

- It still:
  - Iterates over `PageLayoutPlan.Pages`.
  - For each `PagePlan`, loops over `Instructions` **in order**.
  - For each `DrawInstruction`:
    - Creates/gets the `PdfStructureElement` for `Node`.
    - Draws:
      - Text (for P, H1–H6, TD/TH, labels).
      - Figures (for `Figure`).
      - Fields (for form nodes).
    - Uses `TargetBounds` to position things.
- The writer does **not** need to know about `ColumnIndex` or `Region` beyond potential debug logging.

This is what keeps multi-column logic modular and debuggable.

---

## 7. Debugging & Diagnostics

To make debugging layout behavior easier:

1. Add a debug export in `ColumnAwareLayoutEngine`:
   - Optionally write out a JSON snapshot per page:
     - Node ID, Role, ColumnIndex, Region, Bounds, TargetBounds.
   - Controlled by a config flag (e.g., `LayoutDebug.Enabled`).
2. Provide a small CLI or test helper that:
   - Runs the layout engine on a page.
   - Prints a human-readable sequence:
     - `Region=header: [H1 'Title']`
     - `Region=body, Col=0: [P 'Left column text 1']`
     - etc.

You don’t have to build the CLI immediately, but the engine should be written with this possibility in mind (e.g., by keeping the data model straightforward).

---

## 8. Testing Multi-Column Layout

For testing:

1. Create a small synthetic multi-column PDF:
   - Two columns on the page.
   - Clear numeric/lettered markers (“A1, A2, A3” left; “B1, B2, B3” right).
2. Run remediation and inspect:
   - In the **rebuilt** PDF:
     - Visual layout should roughly match.
     - Screen reader reading order should be:
       - A1, A2, A3, then B1, B2, B3 (or similar).
3. For more complex docs:
   - Include a header, a sidebar, and a footer.
   - Confirm reading order: header → body main columns → sidebar → footer.

Use the config flag to switch between:

- `SimpleTopDownLayoutEngine`
- `ColumnAwareLayoutEngine`

and confirm the behavioral difference.

---

## 9. “Do This” Summary for Claude Code

You can give Claude this summary:

1. Implement `SimpleTopDownLayoutEngine` and `ColumnAwareLayoutEngine` as two `IPageLayoutEngine` implementations.
2. In `ColumnAwareLayoutEngine`, implement:
   - Column detection by X clustering.
   - Region classification (header/body/footer/sidebar).
   - Reading order based on region and column, producing `PageLayoutPlan` with `DrawInstruction`s including `ColumnIndex` and `Region`.
3. Wire layout engine selection through configuration so multi-column behavior can be isolated for debugging.
4. Add focused tests for multi-column reading order (simple synthetic docs, then real-world multi-column PDFs).
