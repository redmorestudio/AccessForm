# Phase 6b Pipeline Issue - Context Not Shared

## Problem Summary

Phase 6b successfully inserts BDC/EMC markers during the initial structure rebuild, BUT subsequent services in the remediation pipeline overwrite these markers because:

1. Each remediation service gets its own fresh `StructureRebuildContext` instance
2. Context flags (`StructureRebuildExecuted`, `McidContentRewriteExecuted`) are not shared across services
3. Guards in `ITextPdfStructureWriter` can't prevent secondary rebuilds because context flags are always false

## Evidence from Logs

```
[FINALIZER] Final tagged PDF generation complete. Context flags set: StructureRebuildExecuted=true, McidContentRewriteExecuted=True
[MCID-MARKER-6b] Rewrote content stream with 28 BDC/EMC pairs ✅

... later ...

[ITEXT-STRUCTURE] Starting PDF structure tree rebuild ❌ (no guard triggered)
[ITEXT-STRUCTURE] Starting PDF structure tree rebuild ❌ (4 more times!)
```

## Root Cause

`Services/Remediation/StructureRebuildService.cs:108` calls `_writer.Rewrite(pdfBytes, structure, context)` with a fresh context that doesn't have the flags from the initial rebuild.

Other remediation services likely do the same thing.

## Proper Solution

**Make StructureRebuildContext a session-scoped object that flows through the entire remediation pipeline:**

1. `RemediationOrchestrator` creates a single `StructureRebuildContext` at the start of remediation
2. This context is passed to ALL services that might trigger structure rebuilds
3. Services update the context flags as they run
4. Guards in `ITextPdfStructureWriter` check the shared context

### Implementation Steps:

1. Add `StructureRebuildContext` to `RemediationSession`
2. Update `IRemediationService.RemediateAsync()` signature to accept context
3. Update all service implementations to receive and pass context
4. Update orchestrator to pass shared context to all services

## Quick Workaround (Current Limitation)

The guards work correctly when called from `Services/StructureRebuildService` (via finalizer) because it creates and manages its own context.

But services in the remediation pipeline bypass this by creating their own contexts.

A temporary workaround is to disable redundant structure rebuild services, but this doesn't scale.

## Files Affected

- `/Services/Remediation/StructureRebuildService.cs` - Creates own context
- `/Services/Remediation/RemediationOrchestrator.cs` - Needs to create and pass shared context
- `/Services/Remediation/IRemediationService.cs` - Interface needs context parameter
- All remediation service implementations

## Testing Required

After implementing shared context:

1. Run Alexandria PDF through remediation
2. Verify only ONE "[MCID-MARKER-6b] Rewrote content stream with N BDC/EMC pairs" message
3. Verify all subsequent "[ITEXT-STRUCTURE] Rebuild skipped" messages
4. Verify final PDF has BDC markers: `strings output.pdf | grep -c "BDC"` should be > 0

## Status

- ✅ ITaggedPdfFinalizer implemented and working
- ✅ Guards in ITextPdfStructureWriter implemented
- ✅ Phase 6b MCID marker insertion working
- ❌ Context not shared across remediation services
- ❌ BDC markers still being overwritten by secondary rebuilds

This is a **pipeline architecture issue**, not a Phase 6b implementation issue.
