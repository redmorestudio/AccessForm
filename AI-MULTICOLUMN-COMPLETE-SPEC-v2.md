
# Phase 3c – COMPLETE Multi-Column Layout & Reading Order Specification  
_File: `AI-MULTICOLUMN-COMPLETE-SPEC-v2.md`_  
_This document is fully self-contained and ready for Claude Code._

---

# 0. Purpose

This specification defines **Phase 3c – Multi-Column Layout & Reading Order**, extending the PDF AI Remediation System with a complete geometric reading-order engine.

Phase 3c introduces:

- Column detection
- Region detection (header, body, footer, sidebar)
- Column-aware reading order
- `IPageLayoutEngine`
- `ColumnAwareLayoutEngine`
- Updated `PageLayoutPlan` for ordering + coordinate mapping
- Integration into `StructureRebuildContext`
- Full pipeline wiring

This spec is fully standalone.

---

# 1. Pipeline Placement

```
Original PDF
   ↓
AI Layout → LogicalDocument
   ↓
Enrichment (Forms, Figures)
   ↓
StructureTreeBuilder
   ↓
IPageLayoutEngine.BuildLayoutPlan()   ← Phase 3c
   ↓
StructureRebuildContext
   ↓
SyncfusionPdfStructureWriter
   ↓
Final Accessibility-Compliant PDF
```

---

# 2. Layout Types

## 2.1 PageLayoutPlan

```csharp
public sealed class PageLayoutPlan
{
    public List<PagePlan> Pages { get; } = new();
}
```

## 2.2 PagePlan

```csharp
public sealed class PagePlan
{
    public int PageIndex { get; set; }
    public List<DrawInstruction> Instructions { get; } = new();
}
```

## 2.3 DrawInstruction

```csharp
public sealed class DrawInstruction
{
    public StructureNode Node { get; set; } = default!;
    public RectangleF TargetBounds { get; set; }

    public int ColumnIndex { get; set; }
    public string Region { get; set; } = "body";
}
```

---

# 3. IPageLayoutEngine

```csharp
public interface IPageLayoutEngine
{
    PageLayoutPlan BuildLayoutPlan(StructureTree tree);
}
```

---

# 4. Two Engines: Simple + ColumnAware

## 4.1 SimpleTopDownLayoutEngine

- Sort by Y ascending, then X.
- `ColumnIndex = 0`
- `Region = "body"`

Used for debugging.

## 4.2 ColumnAwareLayoutEngine

Responsible for:

1. Grouping nodes per page  
2. Column detection  
3. Region classification  
4. Reading order synthesis  
5. Creating ordered DrawInstructions  

---

# 5. ColumnAwareLayoutEngine: Detailed Behavior

## 5.1 Gather nodes

Per page:

- Collect nodes with valid bounds.
- Ignore artifacts.

## 5.2 Column detection

Compute:

```csharp
centerX = bounds.Left + bounds.Width/2;
```

Sort by `centerX`.

New column when:

```
abs(centerX_current - centerX_previous) > pageWidth * 0.15
```

Assign sequential `ColumnIndex`.

Works for 1, 2, 3+ columns.

## 5.3 Region classification

Regions:

- header
- body
- sidebar
- footer

Using page height H:

```
top < 0.15H → header
top > 0.85H → footer
else → body/sidebar candidate
```

### Sidebar detection:

- Compute average width per column
- Determine max width
- A column is sidebar if:

```
columnWidth < 0.5 * maxWidth
```

and it is leftmost or rightmost.

## 5.4 Reading order synthesis

Order:

1. Header (sort: Y asc, then X)
2. Body:
   - Columns in ascending ColumnIndex
   - Inside each column: sort Y asc, X asc
3. Sidebar (sort X asc, Y asc)
4. Footer (sort Y asc, X asc)

## 5.5 Build DrawInstructions

Each:

```csharp
new DrawInstruction {
    Node = node,
    TargetBounds = bounds,
    ColumnIndex = col,
    Region = region
};
```

Append to `PagePlan.Instructions`.

---

# 6. Integration Into StructureRebuildContext

Extend context:

```csharp
public sealed class StructureRebuildContext
{
    public IReadOnlyDictionary<string, ExtractedImageData> ImageCache { get; init; }
    public PageLayoutPlan LayoutPlan { get; init; }
}
```

`StructureRebuildService`:

1. Build StructureTree  
2. Call `IPageLayoutEngine.BuildLayoutPlan`  
3. Set into context  
4. Pass to writer  

---

# 7. Configuration

In app settings:

```json
"AccessibilityRemediation": {
  "LayoutEngine": "ColumnAware"
}
```

DI:

```csharp
if (settings.LayoutEngine == "ColumnAware")
    services.AddSingleton<IPageLayoutEngine, ColumnAwareLayoutEngine>();
else
    services.AddSingleton<IPageLayoutEngine, SimpleTopDownLayoutEngine>();
```

---

# 8. Writer Integration

Writer now:

- Ignores its own ordering
- Follows `context.LayoutPlan`

For each `DrawInstruction`:

- Use `TargetBounds`
- Draw text, tables, figures, form fields

Writer does NOT use `ColumnIndex` or `Region`.

---

# 9. Testing

Create synthetic PDF:

- Header: “HEADER”
- Columns:
  - Left: A1, A2, A3
  - Right: B1, B2, B3
- Footer: “FOOTER”
- Optional sidebar

Expected reading order:

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

Verify:

- LayoutPlan
- Tag tree order
- Screen reader output

---

# 10. Implementation Checklist (Claude)

1. Add/extend layout types.  
2. Implement `IPageLayoutEngine`.  
3. Implement `SimpleTopDownLayoutEngine`.  
4. Implement `ColumnAwareLayoutEngine`.  
5. Add column detection.  
6. Add region detection.  
7. Add reading-order synthesis.  
8. Build DrawInstructions.  
9. Add LayoutPlan to StructureRebuildContext.  
10. Update Syncfusion writer to follow LayoutPlan.  
11. Test with synthetic multi-column PDFs.  

---

This spec completely defines Phase 3c.
