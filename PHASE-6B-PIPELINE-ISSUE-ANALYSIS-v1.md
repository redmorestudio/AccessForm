
# Phase 6b – Pipeline Issue Analysis & Required Fix (v1)

This document analyzes the issue shown in `PHASE-6B-PIPELINE-ISSUE-FOUND.md` and provides the **correct architectural fix**.  
This is a complete and copy/paste‑safe specification for Claude Code.

---

# 0. Summary of the Issue

The problem observed:

- Phase 6b successfully inserts BDC/EMC markers during **first** structure rebuild
- Later remediation services trigger **additional** structure rebuilds
- Those services each create **their own** `StructureRebuildContext`
- Guard logic checks a **different context instance**, so:
  - `StructureRebuildExecuted == false`
  - `McidContentRewriteExecuted == false`
  - ⇒ Writer believes it’s always the “first” rebuild  
  - ⇒ **Later rebuilds overwrite BDC/EMC markers**

Therefore:

> The bug is not inside Phase 6b.  
> The bug is in the **pipeline architecture**, due to **non‑shared context instances**.

This spec describes the required fix.

---

# 1. Root Cause (Fully Explained)

### ❌ Current behavior (bug)
Each pipeline service (WhitespaceFixer, TableFixer, etc.) creates something like:

```csharp
var rebuildContext = new StructureRebuildContext(...);
```

or indirectly triggers a rebuild that calls something similar.

Result:

- Each service has its **own private context**, not the shared one.
- Guard logic inside `ITextPdfStructureWriter` examines *that* instance.
- Flags (`StructureRebuildExecuted`, `McidContentRewriteExecuted`) are always default values.
- Guard logic never fires.
- Final PDF loses BDC markers.

### ✔ Desired behavior
There must be **one single, shared** context for the entire remediation job.  
Every service should access exactly the same instance.

---

# 2. Required Architectural Fix

## 2.1 Introduce a job-wide shared context

Create:

```
RemediationJobContext
    -> StructureContext : StructureRebuildContext
    -> (any other shared job-level state)
```

Example:

```csharp
public sealed class RemediationJobContext
{
    public StructureRebuildContext StructureContext { get; } = new();
}
```

### Registered in DI as:
- **Scoped** for each remediation job (NOT transient)
- All remediation services and orchestrator receive the same instance

---

## 2.2 Forbid `new StructureRebuildContext()` outside the orchestrator

Any code that does:

```csharp
var ctx = new StructureRebuildContext();
```

is now invalid.

The only place allowed to instantiate the rebuild context:

- The **orchestrator**
- Or the **RemediationJobContext** constructor

---

## 2.3 Pass the shared context everywhere

Every remediation service must receive the same `RemediationJobContext`, e.g.:

```csharp
public class WhitespaceFixer
{
    private readonly RemediationJobContext _jobContext;

    public WhitespaceFixer(RemediationJobContext jobContext)
    {
        _jobContext = jobContext;
    }
}
```

Thus:

- All fixers (WhitespaceFixer, TableFixer, ListFixer)
- All builders (StructureTreeBuilder)
- All analyzers
- All finalizers

share the **same exact** `StructureRebuildContext`.

---

# 3. Integration with ITaggedPdfFinalizer

### 3.1 Final rebuild uses shared context

Inside `ITaggedPdfFinalizer.FinalizeTaggedPdf`:

```csharp
var ctx = jobContext.StructureContext;

// This MUST be the same instance used everywhere.
```

### 3.2 Guard logic works correctly

Now `ctx.McidContentRewriteExecuted` will be:

- `false` only on first rebuild
- `true` after Phase 6b

Thus:

```csharp
if (ctx.McidContentRewriteExecuted)
{
    _logger.LogWarning("Skipping rebuild — BDC markers already written.");
    return existingPdfBytes;
}
```

**This guard now correctly prevents overwrites.**

---

# 4. Required Pipeline Changes

The following steps MUST be implemented:

---

## 4.1 Remove all per-service context creation

Search for:

- `new StructureRebuildContext`
- `new XxxContext` tied to structure rebuild
- Any helper/factory that spawns a new context

All of these must be replaced with use of the shared instance.

---

## 4.2 Consolidate rebuild call into orchestrator

Only **one** place calls:

```csharp
_finalizer.FinalizeTaggedPdf(...)
```

Everything else:

- Mutates semantic models only
- Sets: `jobContext.StructureContext.RequiresStructureRebuild = true`
- Does **not** rebuild the PDF

---

## 4.3 Make StructureRebuildContext required

If any service tries to call rebuild without the shared context, throw:

```csharp
throw new InvalidOperationException(
    "StructureRebuildContext must be provided by RemediationJobContext"
);
```

---

# 5. LayoutPlan Enforcement (Confirming)

Phase 6b uses geometry, so:

### The final rebuild MUST have:
- A valid `LayoutPlan`
- A fully built `StructureTree`
- Final LogicalDocument

Throw early if missing:

```csharp
if (settings.EnableMcidContentRewrite && layoutPlan == null)
    throw new InvalidOperationException("LayoutPlan required for MCID content rewrite.");
```

This belongs in:
- `ITaggedPdfFinalizer`
- AND inside writer (secondary guard)

---

# 6. Final Correct Pipeline (after fix)

### INITIAL STAGE  
(One shared job context created)

1. Build LogicalDocument  
2. Apply all fixers (mutating LogicalDocument & StructureTree only)  
3. Build final StructureTree  
4. Compute LayoutPlan (Phase 3c)  
5. **Final orchestrator step:**  
   - Call `ITaggedPdfFinalizer`  
   - That:
     - Rebuilds visual PDF  
     - Applies MCIDs (Phase 6)  
     - Rewrites content streams (Phase 6b)  
     - Sets flags:  
       - `StructureRebuildExecuted = true`  
       - `McidContentRewriteExecuted = true`

### AFTER FINAL REBUILD  
Any service attempting rebuild will trigger the guard:

```
WARNING: Skipping rebuild — BDC markers already written.
```

Leaving the PDF intact.

---

# 7. Testing Requirements

Claude must verify:

1. **Multiple services DO NOT create their own context**  
   All access the same instance.

2. **Exactly one rebuild occurs**  
   Logs confirm a single “FINAL STRUCTURE REBUILD” event.

3. **BDC markers persist**  
   Final content streams contain MCIDs.

4. **Later rebuilds attempt & are skipped**  
   Guard logs show the skip.

5. **Alexandria PDF test** succeeds:
   - Acrobat tag highlight → real content
   - PDFix → connected tag/content

---

# 8. Summary for Claude

> **Fix the pipeline by ensuring a single, shared `StructureRebuildContext` instance per remediation job. Eliminate all per-service context creation. Move rebuild into a single orchestrator-finalizer step. Add guard logic to skip later rebuilds. Require LayoutPlan for final rebuild. Verify with Alexandria PDF.**

---

**Filename:**  
`PHASE-6B-PIPELINE-ISSUE-ANALYSIS-v1.md`

This is the complete spec for analyzing and fixing the Phase 6b pipeline issue.
