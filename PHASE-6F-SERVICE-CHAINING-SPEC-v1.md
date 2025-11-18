# Phase 6F – Service Chaining & PDF Propagation Fix (v1)

**Goal:**  
MCID markers are now correctly created (e.g., “Rewrote content stream with 7 BDC/EMC pairs”), and MCID settings are wired end-to-end (Phase 6E).  
However, the **final output PDF still has 0 BDC/EMC** because later services, when their guards trigger, are returning the **original input PDF bytes** instead of passing through the **current PDF with markers**.

This spec defines how to fix **service chaining** so that:

- The first service that writes MCID markers becomes the new “current PDF”
- All subsequent services either:
  - Transform that current PDF, or
  - No-op and **return it unchanged**, not revert to the original
- The orchestrator always ends up with the version that still contains MCIDs

You can hand this file to Claude and say:

> “Implement everything in `PHASE-6F-SERVICE-CHAINING-SPEC-v1.md`.”

---

## 0. Problem Recap

From your summary:

- ✅ MCID content rewrite **does** execute:
  - Logs: `Rewrote content stream with 7 BDC/EMC pairs`
- ✅ Orchestrator and configuration wiring are correct:
  - Settings flow: `appsettings.json → RemediationOptions → RemediationJobContext → all services`
  - Orchestrator correctly **prefers PDFs with MCID markers** during best-iteration selection.
- ❌ Final output PDF has **0 BDC/EMC**:
  - Root cause: **Downstream services with guard clauses** return the **original input PDF** (pre-MCID), instead of the “current” MCID-enriched PDF.

In other words:

> The MCID work is correct but is **not preserved** because the pipeline chain “snaps back” to earlier bytes whenever a guarded service skips.

---

## 1. Design Principles for Service Chaining

We need to formalize how each remediation service behaves in the chain.

### 1.1 Pure function principle

Every remediation service should behave like:

```csharp
byte[] Process(byte[] inputPdf, RemediationJobContext jobContext, AccessibilityRemediationSettings settings);
```

**Rule:**

- If the service **decides to skip** (due to a guard / feature disabled / context flag):
  - It must return **`inputPdf`**, *not* the “original job PDF” or a separately cached base.
- The **input to the service is always the canonical “current PDF”**.

### 1.2 No “reset to original” behavior

No service should ever do:

```csharp
return originalPdfBytesFromJob;
```

as a “skip” behavior once the pipeline has advanced.

Instead:

```csharp
// Skip: return whatever came in
return inputPdf;
```

The only place that should ever start from the original file is the **very first step** in the pipeline or preflight.

### 1.3 Final PDF must be the last “current” value

The orchestrator’s view of the PDF should be:

```csharp
var currentPdf = preflightPdf;

foreach (var service in pipelineServices)
{
    currentPdf = service.Process(currentPdf, jobContext, settings);
}

return currentPdf;
```

MCID-bearing versions must flow forward, not be overwritten by an earlier snapshot.

---

## 2. Identify the Offending Pattern

Claude should search for the anti-pattern where:

- A service logs a “skipped” or “guarded” message and then returns original bytes.

### 2.1 Search patterns

In the repo, search for things like:

- `"Returning original PDF bytes"`
- `"return originalPdf"`
- `"return _jobContext.OriginalPdfBytes"`
- `"return context.OriginalPdfBytes"`
- Any similar wording around:

  - MCID guards
  - “Already executed”
  - “Skipped due to flag”

Also search for guard branches:

```csharp
if (context.McidContentRewriteExecuted)
{
    // ...
    return something;
}
```

and inspect what `something` is.

### 2.2 Typical incorrect pattern

It might look like:

```csharp
if (context.McidContentRewriteExecuted)
{
    _logger.LogWarning("... would overwrite BDC markers. Returning original PDF bytes.");
    return originalPdfBytes; // ❌ WRONG
}
```

We want:

```csharp
if (context.McidContentRewriteExecuted)
{
    _logger.LogWarning("... would overwrite BDC markers. Passing through input PDF unchanged.");
    return inputPdf; // ✅
}
```

---

## 3. Correct Service Signature & Behavior

### 3.1 Standardized method signature

Wherever possible, normalize service signatures to this pattern:

```csharp
public interface IRemediationStep
{
    byte[] Process(
        byte[] inputPdf,
        RemediationJobContext jobContext,
        AccessibilityRemediationSettings settings);
}
```

Implementation example:

```csharp
public byte[] Process(byte[] inputPdf, RemediationJobContext jobContext, AccessibilityRemediationSettings settings)
{
    var context = jobContext.StructureContext;

    if (context.McidContentRewriteExecuted)
    {
        _logger.LogWarning(
            "SERVICE-X: Skipping PDF rewrite because MCID content rewrite has already executed. " +
            "Passing through input PDF unchanged.");

        return inputPdf; // ✅ never original
    }

    // Normal behavior when not skipped:
    var outputPdf = DoWork(inputPdf, context, settings);
    return outputPdf;
}
```

### 3.2 Use job context for flags, not for bytes

`RemediationJobContext` (or `StructureRebuildContext`) should track:

- Flags like `StructureRebuildExecuted`
- `McidContentRewriteExecuted`
- Possibly some metadata (page count, segment counts, etc.)

It should **not** be used as the source of “the real PDF bytes to return” once the pipeline is underway.

---

## 4. Update All Guard Clauses

Claude must:

1. Enumerate all remediation services that:
   - Interact with structure/MCID,
   - OR log about being “skipped” or “blocked” after MCID rewrite.
2. For each guard, ensure the branch:

   - Returns **`inputPdf`** passed into the method, not any earlier stored bytes.

Example conversions:

### 4.1 MCID-aware structure writer guard

**Before (wrong):**

```csharp
if (context.McidContentRewriteExecuted)
{
    _logger.LogWarning(
        "[ITEXT-STRUCTURE] Rebuild skipped — MCID content rewrite already executed. " +
        "Returning original PDF bytes.");
    return originalPdfBytes; // ❌
}
```

**After (correct):**

```csharp
if (context.McidContentRewriteExecuted)
{
    _logger.LogWarning(
        "[ITEXT-STRUCTURE] Rebuild skipped — MCID content rewrite already executed. " +
        "Passing through input PDF unchanged.");
    return inputPdf; // ✅
}
```

### 4.2 Font service / Aspose guard

If there is any guard that now blocks post-structure Aspose:

```csharp
if (context.McidContentRewriteExecuted)
{
    _logger.LogWarning(
        "[FONT-SERVICE] BLOCKED: Aspose optimization cannot run after MCID content rewrite. " +
        "Passing through input PDF unchanged.");
    return inputPdf; // ✅
}
```

### 4.3 ARTIFACT-FIX (defensive guard)

Even though ARTIFACT-FIX is now in preflight, add defensive behavior if it ever sees `McidContentRewriteExecuted`:

```csharp
if (context.McidContentRewriteExecuted)
{
    _logger.LogError(
        "[ARTIFACT-FIX] Unexpected call after MCID content rewrite. " +
        "Skipping and passing through input PDF unchanged.");
    return inputPdf; // ✅
}
```

---

## 5. Orchestrator: Ensure Proper PDF Flow

In your orchestrator or pipeline runner, the flow should look like:

```csharp
byte[] currentPdf = preflightPdf;

foreach (var step in _remediationSteps)
{
    currentPdf = step.Process(currentPdf, jobContext, settings);
}

return currentPdf;
```

Things to check:

1. There is **no** place where `currentPdf` is reset to the original job input.
2. Per-iteration loops (if any) also treat the PDF as “current” and never reset unless explicitly intended.

If you have iteration-based remediation (e.g., multiple passes for violations), ensure:

- MCID content rewrite is intended to run only once (e.g., on the best or last iteration).
- Once MCID rewrites, **subsequent iterations** shouldn’t reset back to pre-MCID bytes.

---

## 6. Logging Enhancements for Chaining

To make debugging easier next time, add a few log points:

### 6.1 At each step entry

```csharp
_logger.LogDebug(
    "[STEP {Name}] Starting. Input PDF size={Size} bytes. McidContentRewriteExecuted={McidExecuted}.",
    stepName,
    inputPdf.Length,
    jobContext.StructureContext.McidContentRewriteExecuted);
```

### 6.2 At each skip

```csharp
_logger.LogInformation(
    "[STEP {Name}] Skipped due to guard (McidContentRewriteExecuted={McidExecuted}). " +
    "Passing through input PDF unchanged.",
    stepName,
    jobContext.StructureContext.McidContentRewriteExecuted);
```

### 6.3 At pipeline end

```csharp
_logger.LogInformation(
    "PIPELINE COMPLETE: Final PDF size={Size} bytes. McidContentRewriteExecuted={McidExecuted}.",
    currentPdf.Length,
    jobContext.StructureContext.McidContentRewriteExecuted);
```

This will make it very clear that:

- The MCID rewrite occurred.
- Later services did not overwrite it.
- The final bytes are the ones from after 6b.

---

## 7. Testing Checklist

Claude should:

1. Fix all guards to return `inputPdf` on skip.
2. Run Alexandria again and capture logs:
   - Confirm Phase 6b still logs `Rewrote content stream with X BDC/EMC pairs`.
   - Confirm that guarded services after 6b log “passing through input PDF unchanged.”
3. Inspect final result:

   ```bash
   strings alexandria_school_remediated.pdf | grep -c "BDC"
   strings alexandria_school_remediated.pdf | grep -c "EMC"
   ```

   Expected: counts > 0.

4. Confirm orchestrator’s “best iteration” logic still prefers:
   - PDFs that are compliant, **and**
   - PDFs that have MCIDs.

5. Open the final PDF in Acrobat/PDFix:
   - Tags highlight content.
   - MCID-linked navigation works.

---

## 8. Summary for Claude

> - Normalize all remediation services to operate on a passed-in `inputPdf` and **never** revert to original job bytes on skips.
> - When a guard (like `McidContentRewriteExecuted`) triggers, the service must **pass through** `inputPdf` unchanged.
> - Confirm the orchestrator maintains a single `currentPdf` that flows through all steps.
> - After these changes, rerun the Alexandria test and verify BDC/EMC are present in the final output.

---

**Filename:**  
`PHASE-6F-SERVICE-CHAINING-SPEC-v1.md`
