# Phase 6c – Aspose / Font-Fix Integration Without Losing MCIDs (v1)

This document defines how to keep Phase 6b MCID markers intact while still using Aspose-based font fixing and optimization.

You can hand this entire file to Claude Code and say:

> “Apply everything in `PHASE-6C-ASPOSE-FONT-FIX-INTEGRATION-SPEC-v1.md`.”

---

## 0. Problem Recap

- Phase 6b (MCID content rewrite) **works** and inserts BDC/EMC.
- Guard logic in the iText writer correctly prevents **additional structure rebuilds** from overwriting BDC/EMC.
- Final PDFs still have **no BDC/EMC**, because:
  - A later **Aspose Cloud font-fix/optimization phase** rewrites the PDF and discards the MCID markers.

We must:

1. Preserve **font fixes** (they matter for compliance).
2. Preserve **MCIDs** (they matter for accessibility).
3. Ensure the pipeline **order and rules** make that possible.

---

## 1. Design Principles

1. **Aspose optimization must not run after MCID content rewrite.**
2. Font fixes are important, so we **reposition** them, not just turn them off.
3. If there is a scenario where Aspose can only be used post-structure, that mode must be explicitly **incompatible** with MCID linking, not silently broken.

---

## 2. Target Pipeline Order

### 2.1 Desired order

**Preflight / Pre-Structure Phase:**

1. Original PDF
2. **Font Fix + Aspose optimization (once)**
3. Output: _Preflight PDF_ (same visual layout, fonts corrected)

**Structure & AI Phase:**

4. AI analysis, LogicalDocument, etc.
5. StructureTree + LayoutPlan
6. Tagged PDF finalization:
   - Phase 6: MCID assignment
   - Phase 6b: MCID content stream rewrite

**Post-Structure Phase:**

7. No more full-PDF rewrites that affect content streams.
   - Only metadata-level tweaks allowed (if any).

Key idea:

> Aspose runs **before** any tagging / MCID work, never after.

---

## 3. Concrete Code-Level Changes

### 3.1 Identify current Aspose usage

Claude should:

1. Search for Aspose-related code:

   ```text
   "ASPOSE-CLOUD"
   "AsposePdf"
   "FontEmbeddingServiceAdapter"
   ```

2. Determine:
   - Which service(s) invoke Aspose?
   - Which remediation **phase** they run in?
   - Whether they completely overwrite the working PDF bytes.

Document that in comments or internal notes for clarity.

### 3.2 Move Aspose to a Preflight Phase

Create a **preflight component**:

```csharp
public interface IPdfPreflightService
{
    byte[] RunPreflight(byte[] inputPdf, AccessibilityRemediationSettings settings);
}
```

Initial implementation of `RunPreflight`:

1. Invokes existing Aspose font-fix/optimization:
   - Wrap `FontEmbeddingServiceAdapter` or equivalent.
2. Returns the optimized PDF bytes.

Then, in the top-level orchestrator:

```csharp
var preflightPdf = _pdfPreflightService.RunPreflight(originalPdf, settings);
var finalPdf = _remediationPipeline.Run(preflightPdf, settings);
```

Rules:

- Preflight runs **once per remediation job**, before any structure rebuild.
- Preflight is the **only** place Aspose optimization is allowed.

### 3.3 Remove Aspose calls from later phases

In the current “Font Fixes” or “PDF/A Conversion” phase that runs **after** structure rebuild:

- Remove or disable Aspose optimization calls.
- If that phase has other non-Aspose behaviors (e.g., local CID/ToUnicode fixes), keep those but ensure they do not:
  - Overwrite the entire PDF
  - Rebuild page content streams in a way that deletes BDC/EMC

In other words:

> After Phase 6b, no component should call Aspose to produce a new PDF.

---

## 4. Configuration & Guarding Behavior

### 4.1 Add configuration for Aspose mode

In settings, add something like:

```json
"AccessibilityRemediation": {
  "EnableMcidLinking": true,
  "EnableMcidContentRewrite": true,
  "AsposeOptimizationMode": "PreStructureOnly"
}
```

Supported values:

- `"PreStructureOnly"` (default, recommended)
  - Aspose is used only in preflight (before any tagging / MCID).
- `"Disabled"`
  - Aspose is not used at all.
- `"PostStructureAllowed"` (advanced, **not** recommended)
  - If enabled, this mode must be explicitly handled as incompatible with MCID linking.

### 4.2 Guard to stop post-structure Aspose

Anywhere Aspose would be called outside preflight (especially in later phases):

```csharp
if (context != null && context.McidContentRewriteExecuted)
{
    if (settings.AsposeOptimizationMode == "PreStructureOnly")
    {
        _logger.LogError(
            "Aspose optimization requested after MCID content rewrite, which is not allowed in PreStructureOnly mode. Skipping."
        );
        return existingPdfBytes;
    }

    if (settings.AsposeOptimizationMode == "PostStructureAllowed" &&
        settings.EnableMcidContentRewrite)
    {
        throw new InvalidOperationException(
            "Aspose post-structure optimization is incompatible with MCID content rewrite. " +
            "Disable MCID content rewrite or use AsposeOptimizationMode="PreStructureOnly"."
        );
    }
}
```

This ensures:

- In normal mode, post-structure Aspose is blocked.
- In an explicitly “dangerous” mode, you cannot pretend MCIDs still work.

---

## 5. Verification Strategy

Claude must implement / run these checks.

### 5.1 Preflight vs Final PDF

1. Run remediation with:
   - Aspose in preflight
   - Phase 6 + 6b enabled

2. Extract BDC/EMC counts:

```bash
strings preflight.pdf | grep -c "BDC"
strings preflight.pdf | grep -c "EMC"

strings final_remediated.pdf | grep -c "BDC"
strings final_remediated.pdf | grep -c "EMC"
```

Expected:

- Preflight:
  - `BDC = 0`
  - `EMC = 0`
- Final:
  - `BDC > 0`
  - `EMC > 0`

### 5.2 Real-world test: Alexandria

1. Run full remediation on the Alexandria school request form.
2. Confirm logs show:
   - Aspose only in preflight.
   - One AI Structure Rebuild + Phase 6 + 6b.
3. Open final PDF in:
   - Acrobat → tags highlight correct content.
   - PDFix → tag/content linkage is present.

### 5.3 Regression safety

Add a test (or at least log checks) to ensure:

- If any later phase tries to call Aspose, a clear error or warning is logged.
- The MCID flags (`McidContentRewriteExecuted`) are never reset after Phase 6b.

---

## 6. Summary for Claude

1. **Do not** simply “skip Aspose when MCIDs exist” – font fixes are important.
2. Instead, **move Aspose to a preflight phase** that runs once **before** AI structure rebuild + MCID content rewrite.
3. Remove Aspose calls from any later phase that runs after Phase 6b.
4. Add `AsposeOptimizationMode` configuration and guards:
   - Default = `"PreStructureOnly"`.
   - If someone insists on post-structure Aspose, they must either:
     - Disable MCID content rewrite, or
     - Accept an explicit error.
5. Verify:
   - Preflight PDF has no BDC/EMC.
   - Final PDF has BDC/EMC and passes tag/content checks in Acrobat/PDFix.

---

**Filename:**  
`PHASE-6C-ASPOSE-FONT-FIX-INTEGRATION-SPEC-v1.md`

This is the complete spec for keeping Aspose font fixes and MCIDs working together.
