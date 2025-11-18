# Phase 6b – Pipeline Integration & Rebuild-Control Specification (v1)

This document defines the architecture changes required so that **MCID content-stream rewriting (Phase 6b)** runs exactly **once** and is never overwritten by accidental secondary structure rebuilds.

This is the authoritative specification for pipeline integration of Phase 6 and 6b.  
Give this file directly to Claude Code.

---

## 0. Problem Summary

During testing:

- Phase 6b **successfully inserted** BDC/EMC markers on the first run  
- But **later services re-ran structure rebuild** without a LayoutPlan  
- Those later rebuilds **overwrote all BDC/EMC markers**

Conclusion:

> MCID implementation works; the **pipeline** does not.

This spec fixes the pipeline.

---

## 1. Goals

1. **Exactly one** PDF structure rebuild per remediation run  
2. That rebuild happens **at the very end**  
3. Earlier pipeline steps modify only:
   - LogicalDocument  
   - StructureTree  
   - LayoutPlan  
4. No service except the orchestrator is allowed to rebuild PDF bytes  
5. Second rebuild attempts are **skipped + logged**  
6. LayoutPlan must be available for the final rebuild  
7. MCID content rewrite (Phase 6b) is **write-protected** against overwrites

---

## 2. Required Architecture Changes

### 2.1 Introduce a finalizer component

Create:

```
ITaggedPdfFinalizer
```

```csharp
public interface ITaggedPdfFinalizer
{
    byte[] FinalizeTaggedPdf(
        byte[] originalPdf,
        LogicalDocument logical,
        StructureTree structure,
        PageLayoutPlan layoutPlan,
        AccessibilityRemediationSettings settings);
}
```

The finalizer is responsible for:

- Rebuilding the visual PDF (once)
- Applying MCIDs (Phase 6)
- Applying content rewrite (Phase 6b)
- Returning the fully tagged output PDF

### 2.2 All other services must stop calling structure rebuild

Claude must identify all callers of:

- ITextPdfStructureWriter  
- StructureRebuildService  
- BuildTaggedPdf  
- Any direct PDF-writing method  

and refactor them so that:

> These services **only** mutate `LogicalDocument`, `StructureTree`, or `LayoutPlan`.  
> They **must not** modify PDF bytes directly.

Instead, they should flag:

```csharp
context.RequiresStructureRebuild = true;
```

---

## 3. State Flags to Prevent Double Rebuild

Add to the remediation context:

```csharp
public bool StructureRebuildExecuted { get; set; }
public bool McidContentRewriteExecuted { get; set; }
```

### Guard inside rebuild code:

```csharp
if (context.McidContentRewriteExecuted)
{
    _logger.LogWarning("ITEXT-STRUCTURE: Rebuild skipped — BDC markers would be overwritten.");
    return existingPdfBytes;
}
```

This makes Phase 6b **write-protect** the PDF after it runs.

---

## 4. Required Ordering of Pipeline Steps

### Final remediation pipeline order:

1. Build LogicalDocument  
2. Apply all fix services (WhitespaceFixer, TableFixer, HeadingNormalizer, etc.)  
3. Build StructureTree  
4. Compute LayoutPlan (Phase 3c)  
5. Perform **one** structure rebuild  
6. Apply MCID assignment (Phase 6)  
7. Apply content-stream rewrite (Phase 6b) — **last step**

Nothing after Step 7 touches the PDF.

---

## 5. LayoutPlan Enforcement

Phase 6b requires geometric ordering.

Therefore:

```csharp
if (settings.EnableMcidContentRewrite && layoutPlan == null)
{
    throw new InvalidOperationException(
        "MCID content rewrite requires LayoutPlan. Final rebuild must include LayoutPlan."
    );
}
```

This prevents silent skipping of Phase 6b.

---

## 6. Preventing Secondary Rebuilds

### 6.1 After the final rebuild

Set:

```csharp
context.StructureRebuildExecuted = true;
context.McidContentRewriteExecuted = true;
```

### 6.2 Any later call from other services MUST be a no-op

```csharp
if (context.StructureRebuildExecuted)
{
    _logger.LogWarning("Structure rebuild attempted after final pass. Skipping.");
    return existingPdfBytes;
}
```

---

## 7. Logging Requirements

Every rebuild attempt must log:

- Whether LayoutPlan was available  
- Segment/target counts (Phase 6b)  
- Whether MCIDs were applied  
- Whether content rewrite was applied  
- Whether the operation was skipped due to flags

This is critical for debugging.

---

## 8. Testing Checklist

Claude must confirm the following:

### 8.1 Before final rebuild
- LogicalDocument is mutated by fixers  
- StructureTree is mutated  
- LayoutPlan is present  
- **No PDF bytes modified yet**

### 8.2 During final rebuild
- Only the orchestrator runs rebuild  
- MCIDs assigned  
- 6b content rewrite inserts BDC/EMC  
- Flags set: `StructureRebuildExecuted = true`, `McidContentRewriteExecuted = true`

### 8.3 After final rebuild
- Any additional rebuild attempt:
  - Logs a warning  
  - Does **not** modify PDF bytes  

### 8.4 PDFix + Acrobat
- Clicking a tag highlights real content  
- All BDC/EMC markers present  
- Content navigation works  

---

## 9. Summary for Claude

Claude must:

1. Identify all rebuild callers  
2. Move rebuild responsibility to a single orchestrator  
3. Add context flags:
   - `StructureRebuildExecuted`
   - `McidContentRewriteExecuted`
4. Require LayoutPlan for final rebuild  
5. Skip rebuilds after Phase 6b  
6. Log all attempts  
7. Test via synthetic + real PDFs  

---

**Filename:**  
`PHASE-6B-PIPELINE-INTEGRATION-SPEC-v1.md`

This is the complete authoritative spec for preventing Phase 6b from being overwritten.
