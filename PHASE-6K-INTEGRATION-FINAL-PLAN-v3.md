# Phase 6K Integration – Final Plan (v3, Claude-Ready)

**Audience:** Claude Code, working against the `redmorestudio/accessform` repo.  
**Goal:** Finish MCID integration *without* rebuilding or destabilizing the remediation pipeline.

This document supersedes any earlier informal notes. Treat this as the **source of truth** for Phase 6K integration.

---

## 0. Ground Truth / Current State

From code inspection and logs:

1. There are **two** `StructureRebuildService` classes:
   - `Services/StructureRebuildService.cs`  
   - `Services/Remediation/StructureRebuildService.cs`

2. The **production remediation pipeline** uses:
   - `Services/StructureRebuildService.cs`  
   - via `Services/Remediation/Adapters/StructureRebuildServiceAdapter.cs`  
   - which is invoked by the remediation strategy / orchestrator.

3. The **“remediation” variant** (`Services/Remediation/StructureRebuildService.cs`) is effectively a *test harness / experimental path*, not the one wired into the full remediation orchestrator.

4. On the .NET side, **Phase 6K is already implemented**:
   - MCID allocation logic lives in `ITextPdfStructureWriter.AllocateMcids(...)`.
   - MCR kids are created when building the iText structure tree.
   - Guard flags such as `McidContentRewriteExecuted` are already present and used.
   - The pipeline order (preflight → structure rebuild → post-structure phases) is already correct.
   - `StructureRebuildContext` and `RemediationJobContext.StructureContext` carry the right flags.

5. The remaining **hard problem** is:
   - The **Python MCID rewrite microservice** (pikepdf-based) sometimes:
     - Produces PDFs without BDC/EMC markers surviving.
     - Produces PDFs Acrobat cannot open.
     - Wraps content incorrectly (markers not aligned to visual segments).

**Conclusion:**  
The .NET remediation and MCID architecture is essentially correct. Phase 6K on the C# side is done. The remaining work is:

- Verifying that the *production* `StructureRebuildService` path uses the Phase 6K logic.
- Fixing the Python content-stream rewrite so markers persist and PDFs remain valid.

---

## 1. High-Level Rules for This Phase

Claude MUST follow these rules:

1. **Do NOT redesign or duplicate MCID assignment.**
   - No new `IMcidAssignmentService` unless it simply wraps existing logic.
   - No second MCID system.
   - The single source of truth for MCID assignment is whatever is currently in `ITextPdfStructureWriter`.

2. **Do NOT alter overall remediation pipeline architecture.**
   - Do not reorder major remediation phases.
   - Do not remove or re-home preflight, structure enhancement, or cleanup stages.

3. **Do NOT introduce new content-stream-mutating stages after MCID rewrite.**
   - Any existing or future services that rewrite content streams MUST respect the guard flag.

4. **Focus work on:**
   - Ensuring the production `StructureRebuildService` path uses Phase 6K logic.
   - Fixing Python MCID rewrite correctness.

---

## 2. Clarifying the Two StructureRebuildService Classes

### 2.1 Files

- **Production path:**
  - `Services/StructureRebuildService.cs`
  - `Services/Remediation/Adapters/StructureRebuildServiceAdapter.cs`
  - Used by remediation orchestrator through adapter-based strategy selection.

- **Experimental/test path:**
  - `Services/Remediation/StructureRebuildService.cs`
  - Used by older tests like `TestPhase6KFullPipeline`, but **not** by the main orchestrator.

### 2.2 Required Action

1. **Do NOT delete either class yet.**
2. **Treat `Services/StructureRebuildService.cs` as canonical for production.**
3. Ensure:
   - The production `StructureRebuildService` (the one used by the adapter) is the one that:
     - Builds the structure tree.
     - Cleans the tree.
     - Calls `ITextPdfStructureWriter` with a fully prepared structure + context.
   - Any additional Phase 6K logic (MCID, MCR, external rewriter) that currently only exists in the “remediation” version is either:
     - Confirmed to already exist in the production version, OR
     - Ported over carefully.

4. If there is functionality divergence:
   - Prefer consolidating onto the **production** `Services/StructureRebuildService` implementation.
   - Optionally leave a comment in the experimental one marking it as *test-only*.

---

## 3. What NOT to Change

Claude MUST NOT:

- Create a brand-new standalone MCID pipeline.
- Move MCID allocation into a totally separate service that bypasses `ITextPdfStructureWriter`.
- Reorder preflight, structure rebuild, and post-structure phases.
- Remove or bypass `StructureRebuildServiceAdapter`, which is how the orchestrator invokes structure rebuild.
- Rewrite tag-tree-building logic in C# from scratch.

MCID-related architecture (on C# side) is correct in concept; we only ensure it is used consistently.

---

## 4. What TO Do on the .NET Side

### 4.1 Verify Production Integration

1. **Locate production StructureRebuildService:**

   - Confirm: `Services/StructureRebuildService.cs` has a method like `RemediateAsync(...)` or similar entrypoint.
   - Confirm it receives:
     - `RemediationJobContext` or equivalent.
     - Input PDF bytes.
     - Services for AI layout analysis, tree building, writer, etc.

2. **Confirm call chain from orchestrator:**

   - `RemediationOrchestrator` (or equivalent) →  
     `StructureRebuildServiceAdapter` →  
     `StructureRebuildService`.

3. **Inside production StructureRebuildService:**

   Confirm this sequence (conceptually):

   ```csharp
   // 1. AI layout analysis (Claude/OpenAI)
   var logical = _layoutAnalysis.Analyze(pdfBytes, context, ...);

   // 2. Build structure tree
   var structure = _structureTreeBuilder.Build(logical);

   // 3. Clean structure tree
   structure = _structureTreeCleaner.Clean(structure);

   // 4. Call writer, which will:
   //    - allocate MCIDs
   //    - create MCR kids
   //    - call external Python rewriter (if enabled)
   var updatedPdfBytes = _pdfStructureWriter.Rewrite(pdfBytes, structure, context);
   ```

4. **If the production StructureRebuildService does NOT call the MCID-aware writer path:**
   - Port the integration pattern from the `Services/Remediation/StructureRebuildService.cs` version that was used by earlier tests.
   - Do *not* duplicate logic; instead, make sure both classes (if both are retained) share the same underlying method or helper for MCID-aware structural rewriting.

### 4.2 Guard Flag Usage

Confirm the following:

- `StructureRebuildContext.McidContentRewriteExecuted` (or equivalent property in job context) is:
  - Set to `true` only after the external Python rewrite returns successfully.
  - Read by any later services that might mutate content streams.

- In any **post-structure** service that rewrites content streams (e.g., artifact fix, optimization, cleanup):

  ```csharp
  if (context.StructureContext?.McidContentRewriteExecuted == true)
  {
      _logger.LogInformation("[{Service}] Skipped – MCID content rewrite already executed, preserving BDC/EMC markers.", nameof(ThisService));
      return pdfBytes;
  }
  ```

- Preflight services **before** structure rebuild may still modify content streams; that is correct.

### 4.3 No New Public Flags

Do **not** introduce additional booleans or feature flags beyond those already defined in `RemediationOptions` and `StructureRebuildContext` unless strictly necessary. Reuse `EnableMcidContentRewrite` and existing context flags.

---

## 5. Python Microservice: The Real Work

### 5.1 Scope

All further changes for Phase 6K completion should be **in Python**, in the external MCID rewrite microservice:

- Content stream parsing
- Text/image segmentation
- Marker insertion
- Stream reserialization
- Validation

### 5.2 Requirements

The Python side must:

1. **Parse the content stream** into a robust operator model:
   - Track:
     - Text placement (`BT`, `ET`, `Tm`, `Td`, `TD`, `Tj`, `'`, `"`)  
     - Image invocation (`Do` with associated XObject)  
     - Graphics state changes (`q`, `Q`, `cm`, `gs`, etc.).

2. **Map structure segments → operator spans**:
   - Input plan (from C#) will give:
     - Page index
     - MCID
     - Geometry / sequence info
   - Segmenter decides which operators fall within each logical segment.

3. **Insert BDC/EMC pairs correctly:**
   - For each segment:
     - Insert `BDC` with `/Span << /MCID n >>` immediately before the first operator in that segment.
     - Insert matching `EMC` immediately after the last operator in that segment.
   - Ensure nesting is valid and balanced:
     - No overlapping BDC/EMC from different segments.
     - Avoid double-wrapping the same range.

4. **Preserve all original operators and ordering.**
   - Do not reorder content.
   - Do not drop any operators.
   - Do not collapse whitespace / numeric precision in ways that break rendering.

5. **Write back using pikepdf** in a spec-compliant way:
   - Replace the stream data for the target page’s contents with the reserialized stream.
   - Do not alter unrelated objects, cross-reference tables, etc.

6. **Validate output:**
   - `qpdf --check` (or equivalent) should pass.
   - Acrobat should open the file without error.
   - PDFix / Acrobat’s tag tree should show:
     - Structure elements with MCR kids referencing MCIDs.
     - Content highlighted when clicking tags.

---

## 6. Flowchart (Accurate to Production Architecture)

```mermaid
flowchart TD
    A[Input PDF] --> B[Preflight<br/>Aspose + ArtifactFix]
    B --> C[RemediationOrchestrator]

    C --> D[StructureRebuildServiceAdapter]
    D --> E[StructureRebuildService<br/>(Production)]

    E --> F[AI Layout + LogicalDoc]
    F --> G[StructureTreeBuilder]
    G --> H[StructureTreeCleaner]
    H --> I[ITextPdfStructureWriter<br/>MCID Allocation + MCR Kids]

    I --> J[External Python MCID Rewriter<br/>(pikepdf)]
    J --> K{Rewrite Success?}

    K -- Yes --> L[Post-Structure Cleanup<br/>(Metadata Only)]
    K -- No --> M[Fallback PDF<br/>(No MCID Rewrite)]

    L --> N[Compliance Scoring + Final Output]
    M --> N
```

---

## 7. Concrete Checklist for Claude

When you (Claude) implement this:

1. **DO**:
   - [ ] Confirm production `StructureRebuildService` uses the MCID-aware writer path.
   - [ ] Ensure `StructureRebuildServiceAdapter` points to the right service.
   - [ ] Verify guard flags are in place and working.
   - [ ] Fix Python rewrite segmentation and BDC/EMC placement.
   - [ ] Add validation helpers/tests for Python output.

2. **DO NOT**:
   - [ ] Add a second MCID assignment path.
   - [ ] Move MCID logic out of `ITextPdfStructureWriter`.
   - [ ] Change orchestrator phase ordering.
   - [ ] Introduce new content-mutating phases after MCID rewrite.
   - [ ] Redesign the structure tree or its models.

---

## 8. How to Use This Doc

Seth can say to Claude:

> “Use `PHASE-6K-INTEGRATION-FINAL-PLAN-v3.md` as the source of truth for Phase 6K.  
> Do not redesign the pipeline; fix the Python MCID rewriter and make sure the production StructureRebuildService path uses the existing 6K logic correctly.”

That’s it. Follow this plan, and we’ll get from **working remediation but no persistent MCIDs** to **fully tagged, MCID-linked, screen-reader-friendly PDFs** without breaking everything else.

---

**Filename:** `PHASE-6K-INTEGRATION-FINAL-PLAN-v3.md`
