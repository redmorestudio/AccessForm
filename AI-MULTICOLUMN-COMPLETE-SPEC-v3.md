
# Phase 3c – COMPLETE Multi-Column Layout & Reading Order Specification (v3)
This version incorporates **all clarifying answers**, is fully self-contained, and is formatted for clean copy/paste.

---

# 0. Purpose
Phase 3c introduces a full **multi-column, region-aware reading-order engine** that creates a `PageLayoutPlan` describing the exact order and coordinates for drawing all visual/semantic elements in the remediated PDF.

The writer no longer computes order or geometry—it follows the plan.

This spec replaces all earlier versions.

---

# 1. Updated Pipeline

```
LogicalDocument
   ↓
Enrichers (forms, figures)
   ↓
StructureTreeBuilder
   ↓
StructureRebuildService  ← NEW
   • Calls IPageLayoutEngine
   • Builds PageLayoutPlan
   • Builds StructureRebuildContext
   ↓
IPdfStructureWriter (Syncfusion)
   ↓
Remediated PDF
```

---

# 2. StructureRebuildService (NEW – MUST BE CREATED)

Create:

```
Services/StructureRebuildService.cs
```

Responsibilities:

1. Build `LogicalDocument` (already implemented upstream)
2. Run all enrichers (forms, figures)
3. Build `StructureTree`
4. Call layout engine:
   ```
   var layoutPlan = _layoutEngine.BuildLayoutPlan(tree);
   ```
5. Build `StructureRebuildContext`:
   ```
   new StructureRebuildContext {
       ImageCache = collectedImageCache,
       LayoutPlan = layoutPlan
   }
   ```
6. Call Syncfusion writer:
   ```
   return _writer.Write(tree, context);
   ```

This service is the ONLY place that invokes the layout engine.

---

# 3. DrawInstruction (EXTEND EXISTING CLASS)

Update the **existing** DrawInstruction created in Phase 3b:

```
public sealed class DrawInstruction
{
    public StructureNode Node { get; set; } = default!;
    public RectangleF TargetBounds { get; set; }

    // Phase 3c additions:
    public int ColumnIndex { get; set; }
    public string Region { get; set; } = "body";
}
```

Do not create a new class.

---

# 4. StructureNode.Bounds (NEW FIELD)

Add a `Bounds` property directly to `StructureNode`:

```
public sealed class StructureNode
{
    // existing...

    public Rect? Bounds { get; set; }
}
```

### Bounds Source
`StructureTreeBuilder` must set:

```
node.Bounds = logicalBlock.Bounds;
```

Group nodes may leave Bounds = null or union children.

---

# 5. PageLayoutPlan (UNCHANGED)

```
public sealed class PageLayoutPlan
{
    public List<PagePlan> Pages { get; } = new();
}

public sealed class PagePlan
{
    public int PageIndex { get; set; }
    public List<DrawInstruction> Instructions { get; } = new();
}
```

---

# 6. IPageLayoutEngine Interface

```
public interface IPageLayoutEngine
{
    PageLayoutPlan BuildLayoutPlan(StructureTree tree);
}
```

Two implementations are required:
- `SimpleTopDownLayoutEngine`
- `ColumnAwareLayoutEngine`

---

# 7. ColumnAwareLayoutEngine (FULL RULES)

This is the core of Phase 3c.

## 7.1 Step 1 — Gather Eligible Nodes

For each page:
- Only include nodes where `Bounds != null`.
- Ignore artifacts.

Gather:
- Node
- Bounds
- PageIndex

---

## 7.2 Step 2 — Column Detection

For each node compute:

```
centerX = bounds.Left + bounds.Width / 2;
```

Sort nodes by `centerX`.

Initialize:

```
currentColumn = 0;
previousCenterX = centerX_of_first_node;
```

Start new column when:

```
abs(centerX_current - previousCenterX) > pageWidth * 0.15
```

Assign:

```
node.ColumnIndex = currentColumn;
```

Emits 1, 2, or 3+ columns automatically.

---

## 7.3 Step 3 — Region Detection

Let H = page height.

### Header:
```
bounds.Top < 0.15 * H
```

### Footer:
```
bounds.Top > 0.85 * H
```

### Body / Sidebar Candidates:
Nodes not in header/footer.

#### Sidebar Detection (Clarified)
A column is a sidebar if:
1. It is leftmost or rightmost column
2. Column width is < 50% of max column width

Column width =

```
columnLeft = min(node.Bounds.Left)
columnRight = max(node.Bounds.Right)
columnWidth = columnRight - columnLeft
```

---

## 7.4 Step 4 — Reading Order Synthesis

### Final order per page:

1. **Header**
   - sort by Y asc, X asc

2. **Body**
   For each column in ascending ColumnIndex:
   - sort by Y asc, X asc

3. **Sidebar**
   - sort X asc, Y asc

4. **Footer**
   - sort Y asc, X asc

This becomes the canonical sequence.

---

## 7.5 Step 5 — Build DrawInstructions

```
new DrawInstruction {
    Node = node,
    TargetBounds = convert(node.Bounds),
    ColumnIndex = node.ColumnIndex,
    Region = node.Region
};
```

Append to `PagePlan.Instructions`.

---

# 8. StructureRebuildContext (EXTEND)

```
public sealed class StructureRebuildContext
{
    public IReadOnlyDictionary<string, ExtractedImageData> ImageCache { get; init; }
    public PageLayoutPlan LayoutPlan { get; init; }
}
```

---

# 9. SyncfusionPdfStructureWriter Integration

### New behavior:
If `context.LayoutPlan != null`:
- **Ignore recursive traversal**
- Use `LayoutPlan.Pages[pageIndex].Instructions`

### Legacy fallback:
If `LayoutPlan == null`:
- Use old recursive behavior.

### Drawing:
Use existing drawing machinery:
- Text
- Forms
- Tables
- Figures (Phase 3b)
But always use:

```
instruction.TargetBounds
```

---

# 10. Dependency Injection (CONFIGURABLE)

Add appsettings:

```
"AccessibilityRemediation": {
  "LayoutEngine": "ColumnAware"
}
```

In Startup:

```
if (settings.LayoutEngine == "ColumnAware")
    services.AddSingleton<IPageLayoutEngine, ColumnAwareLayoutEngine>();
else
    services.AddSingleton<IPageLayoutEngine, SimpleTopDownLayoutEngine>();
```

---

# 11. Testing Requirements

## 11.1 Unit Tests (MANDATORY)

Test:
- ColumnIndex assignment
- Region classification
- Reading order output sequence

Do this with **in-memory synthetic StructureNodes**.

## 11.2 Synthetic PDFs (RECOMMENDED)
Commit 1–2 PDFs into `TestAssets/Pdfs`:

- Header text
- Two columns A1/A2/A3 vs B1/B2/B3
- Footer text

Expected order:

```
HEADER
A1
A2
A3
B1
B2
B3
FOOTER
```

## 11.3 Full Pipeline Test (OPTIONAL)

Run full remediation and manually verify reading order in a screen reader.

---

# 12. Phase 3c Implementation Checklist (Claude)

1. Add `Bounds` to `StructureNode`
2. Update `StructureTreeBuilder` to set `node.Bounds`
3. Extend `DrawInstruction` with ColumnIndex + Region
4. Create `StructureRebuildService`
5. Implement `PageLayoutPlan`, `PagePlan`
6. Implement `SimpleTopDownLayoutEngine`
7. Implement `ColumnAwareLayoutEngine`
8. Add:
   - Column detection
   - Region detection
   - Reading-order synthesis
9. Build ordered DrawInstructions
10. Fill `LayoutPlan` in `StructureRebuildContext`
11. Modify Syncfusion writer:
    - If LayoutPlan present: use it exclusively
    - Else: fallback
12. Add unit tests
13. Add synthetic PDF tests

---

This is the **complete, authoritative Phase 3c spec with all answers integrated**.
