# Phase 6K Integration Clarification

## Question: "Where are the remediation steps?"

Good question! The test I ran (`TestPhase6KFullPipeline`) only tests **StructureRebuildService in isolation**, not the full remediation pipeline.

---

## Architecture Clarification

There are **TWO** StructureRebuildService classes:

### 1. `Services/Remediation/StructureRebuildService.cs`
- **What I tested** ✅
- Newer Phase 6K implementation
- Uses `ITextPdfStructureWriter` with MCID support
- **NOT** integrated into RemediationOrchestrator yet

### 2. `Services/StructureRebuildService.cs`
- **Actually used** in production
- Wrapped by `StructureRebuildServiceAdapter`
- **IS** integrated into RemediationOrchestrator
- May not have Phase 6K MCID support yet

---

## Full Remediation Pipeline

When you call `RemediationOrchestrator.RemediateAsync()`:

```
1. Preflight Phase
   └─ Aspose font fixes
   └─ Artifact tagging

2. Initial VeraPDF Validation

3. Remediation Loop (max 3-5 iterations):
   ├─ Violation Analysis
   ├─ Strategy Selection
   └─ Phase Execution:
      ├─ Phase 0: Structure Rebuild ← Phase 6K should integrate here
      │   └─ StructureRebuildServiceAdapter
      │       └─ Services/StructureRebuildService  ← Check if this has MCID
      ├─ Phase 1: Metadata Fixes
      ├─ Phase 2: Form Fixes
      ├─ Phase 3: Content Fixes
      ├─ Phase 4: Font Fixes
      └─ Phase 5: Cleanup Fixes

4. Post-Remediation Cleanup

5. Final VeraPDF Validation
```

---

## Status Check Required

To answer "where are the remediation steps?", I need to verify:

**Question 1**: Does `Services/StructureRebuildService.cs` have Phase 6K MCID support?
- If **YES**: Full remediation already includes Phase 6K ✅
- If **NO**: Need to add Phase 6K to that service

**Question 2**: Is `Services/Remediation/StructureRebuildService.cs` used anywhere?
- This is the newer implementation with confirmed Phase 6K support
- May be a duplicate that should replace the old one

---

## Next Steps

1. **Check which StructureRebuildService has MCID support**
2. **If both exist, consolidate them**
3. **Run full remediation via RemediationOrchestrator**
4. **Verify all fix services run + MCID markers persist**

---

## Why This Matters

Phase 6K spec says:
> "Reattach the completed MCID pipeline into the **full remediation pipeline** without disturbing existing remediation logic."

So far, I've only tested MCID in isolation. To truly verify Phase 6K, I need to:
- Run full remediation with all fix services
- Verify MCID markers survive all remediation phases
- Confirm guard clauses prevent overwrites

---

**Conclusion**: Phase 6K may be complete, but I need to verify it's integrated into the **production** StructureRebuildService that RemediationOrchestrator actually uses.
