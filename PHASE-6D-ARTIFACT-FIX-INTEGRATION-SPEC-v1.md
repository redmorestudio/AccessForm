# Phase 6d – Artifact Fix Integration Without Destroying MCIDs (v1)

This document defines how to integrate `ArtifactTaggedContentFixService` (“ARTIFACT-FIX”) so that it no longer destroys the MCID markers written by Phase 6b.

You can hand this entire file to Claude Code and say:

> “Implement everything in `PHASE-6D-ARTIFACT-FIX-INTEGRATION-SPEC-v1.md`.”

---

## 0. Problem Recap

Recent run behavior:

1. **Preflight (Aspose) runs successfully**:
   - `[PREFLIGHT] ✓ Font optimization complete: 1,624,949 → 1,874,471 bytes`
2. **MCID markers are written**:
   - `[MCID-MARKER-6b] Rewrote content stream with 28 BDC/EMC pairs`
3. **Post-structure Aspose is correctly blocked**:
   - `[FONT-SERVICE] ⚠ BLOCKED: Aspose font optimization cannot run after MCID content rewrite`
4. **New issue**:
   - `[ARTIFACT-FIX] Page 1: Wrapped unmarked XObject (22 times!)`
   - Final remediated PDF:
     - `BDC` count = 0
     - `EMC` count = 0

Conclusion:

> `ArtifactTaggedContentFixService` is rewriting page content streams **after** MCID content rewrite and in the process removes or replaces the BDC/EMC operators we added in Phase 6b.

We need to fix this without giving up artifact tagging.

---

## 1. Design Principles

1. Any component that rewrites page content streams must be **coordinated** with Phase 6b.
2. **Default behavior**: MCID content rewrite (Phase 6b) should be the **last** content-stream rewrite.
3. ARTIFACT-FIX is still valuable (marking decorative/meaningless content), so:
   - Prefer to **run it earlier**, before MCIDs exist.
   - Only allow post-6b execution if it is explicitly **MCID-aware**.
4. We must not accidentally ship PDFs that are “compliant” but have no working MCIDs.

---

## 2. Target Pipeline (Including ARTIFACT-FIX)

### 2.1 Updated high-level order

**Preflight:**

1. Original PDF
2. Aspose Font Fix & Optimization (preflight)
3. Artifact Fix (decorate unmarked XObjects; see below)

**Structure / AI:**

4. AI / logical analysis → LogicalDocument
5. StructureTree + LayoutPlan
6. Tagged PDF finalization:
   - Phase 6 – MCID assignment (structure-side)
   - Phase 6b – MCID content rewrite (BDC/EMC insertion)

**Post-structure:**

7. No further content-stream rewriting (unless MCID-aware and explicitly configured).

Key idea:

> ARTIFACT-FIX should run **before** MCID insert, so it’s not stomping our markers.

---

## 3. Preferred Fix: Run ARTIFACT-FIX Before Phase 6/6b

### 3.1 Move service earlier in the pipeline

Claude should:

1. Locate where `ArtifactTaggedContentFixService` is invoked now:
   - Search for class name and/or `[ARTIFACT-FIX]` log prefix.
   - Identify which remediation phase it lives in (likely “Structure Enhancement” / Phase 2).

2. Move its invocation into a pre-structure phase, after preflight but before AI structure rebuild, e.g.:

   - New step: `Phase 0B – Artifact Pre-tag Fix`
   - Called like:

     ```csharp
     var preflightPdf = _pdfPreflightService.RunPreflight(originalPdf, settings);
     var artifactFixedPdf = _artifactTaggedContentFixService.WrapUnmarkedXObjects(preflightPdf, settings);
     var finalPdf = _remediationPipeline.RunStructureAndMcidPipeline(artifactFixedPdf, settings);
     ```

3. Ensure that:
   - The **output** of ARTIFACT-FIX becomes the **input** to structure rebuild / Phase 6/6b.
   - No *later* phase calls ARTIFACT-FIX again after MCID markers exist.

### 3.2 Update logs and docs

- Update log messages to clarify:

  - `[ARTIFACT-FIX] Running in pre-structure phase`
  - `[ARTIFACT-FIX] Complete: wrapped N unmarked XObjects before MCID linking`

This makes it clear in future debugging that ARTIFACT-FIX happens before tagging.

---

## 4. Fallback: MCID-Aware Mode (If It Must Run Post-6b)

If there is a strong requirement to run ARTIFACT-FIX **after** tagging (e.g., it needs structure context to decide artifacts), then it must become **MCID-aware**.

This is more work, but here’s the outline:

### 4.1 Share the content parsing model

Refactor ARTIFACT-FIX so that it:

1. Uses the **same `PdfOp` / `PageOps` representation** as Phase 6b’s `IContentMcidMarker`:
   - Reuse the operator extraction logic (PdfCanvasProcessor + tokenization).
2. When modifying streams:
   - It must re-emit **all existing operators**, including:
     - BDC
     - EMC
     - Any nested artifact markers

### 4.2 Nest artifact markers inside MCID spans where appropriate

Rules:

- If an XObject is already inside a `Span` /MCID:

  ```pdf
  /Span <</MCID n>> BDC
    ... XObject draw sequence ...
  EMC
  ```

  and we want to mark a decorative component within that:

  ```pdf
  /Span <</MCID n>> BDC
    /Artifact BMC
      ... XObject draw sequence ...
    EMC
  EMC
  ```

- If an XObject is outside any MCID scopes:
  - Wrap with `BMC/EMC` as usual, *without* touching BDC/EMC.

### 4.3 Configuration and guards

Add:

```json
"ArtifactFixMode": "PreStructureOnly" | "PostStructureMcidAware"
```

- Default: `"PreStructureOnly"` (ARTIFACT-FIX moved earlier).
- `"PostStructureMcidAware"`:
  - Allowed only after refactor to MCID-aware behavior.

Guard for post-6b:

```csharp
if (context.McidContentRewriteExecuted && settings.ArtifactFixMode != "PostStructureMcidAware")
{
    throw new InvalidOperationException(
        "ArtifactTaggedContentFixService cannot run after MCID content rewrite " +
        "unless in PostStructureMcidAware mode."
    );
}
```

This prevents accidentally running the old, non-MCID-aware ARTIFACT-FIX on a tagged PDF.

---

## 5. Global Guard Rules (Content-Stream Mutators)

We now have multiple content-stream mutators:

- Preflight Aspose
- ARTIFACT-FIX
- Phase 6b MCID content rewrite
- (Potentially others later)

Global rule in code:

> Once `context.McidContentRewriteExecuted == true`, **no service** may replace or reconstruct page content streams unless it is explicitly MCID-aware and re-emits all BDC/EMC markers.

Claude should:

1. Ensure ARTIFACT-FIX consults the shared context:
   - E.g., `RemediationJobContext.StructureContext.McidContentRewriteExecuted`
2. Apply the guard above to enforce correct mode.

---

## 6. Testing Checklist

Claude must validate:

### 6.1 Default mode: PreStructureOnly

1. Configure:
   - `ArtifactFixMode = "PreStructureOnly"`
   - MCID linking + content rewrite enabled.

2. Run remediation on the Alexandria school request form.

3. Verify logs show:

   - `[PREFLIGHT]` (Aspose) early.
   - `[ARTIFACT-FIX]` **before** any AI structure rebuild / Phase 6/6b.
   - One AI Structure Rebuild + MCID content rewrite.
   - **No** ARTIFACT-FIX after Phase 6b.
   - No warnings about ARTIFACT-FIX being blocked post-6b.

4. Check final PDF:

   ```bash
   strings final.pdf | grep -c "BDC"
   strings final.pdf | grep -c "EMC"
   ```

   Expected: both > 0.

5. Check behavior in Acrobat / PDFix:
   - Tags highlight actual content.
   - Decorative images are correctly marked as artifacts.

### 6.2 Guard behavior

1. Temporarily (for testing) wire ARTIFACT-FIX after Phase 6b with `ArtifactFixMode = "PreStructureOnly"`.
2. Confirm:
   - It is blocked with a clear error or warning.
   - MCID markers remain in place (BDC/EMC still present).

---

## 7. Summary for Claude

1. ARTIFACT-FIX is currently running **after** MCID content rewrite and destroying BDC/EMC.
2. Preferred fix:
   - Move ARTIFACT-FIX into a **pre-structure/pre-tag phase** (after preflight Aspose, before AI structure rebuild + Phase 6/6b).
3. If it must run after 6b:
   - It must be refactored into an **MCID-aware mode** that shares the same parsing/serialization model and preserves all BDC/EMC operators.
4. Add `ArtifactFixMode` configuration and guards:
   - Default: `"PreStructureOnly"`.
   - `"PostStructureMcidAware"` only after proper refactor.
5. Validate with Alexandria PDF that:
   - MCIDs persist to the final output.
   - Artifact tagging still works.

---

**Filename:**  
`PHASE-6D-ARTIFACT-FIX-INTEGRATION-SPEC-v1.md`

This is the complete spec for integrating `ArtifactTaggedContentFixService` without destroying MCID markers.
