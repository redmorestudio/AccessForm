# Phase 6d – Preflight & Artifact Fix Q&A Clarifications (v1)

This document answers the follow-up questions about preflight structure, ArtifactTaggedContentFixService placement, and configuration.

You can hand this whole file to Claude and say:

> “Apply the decisions in `PHASE-6D-PREFLIGHT-AND-ARTIFACT-QA-v1.md`.”

---

## 1. Preflight Service Structure

### Question

> The current `IPdfPreflightService` only handles Aspose. Should I:
> - Expand it to also run ARTIFACT-FIX (making it a true "preflight pipeline")?
> - OR create a separate call in the orchestrator (simpler, clearer separation)?

### Decision

**Expand `IPdfPreflightService` into a true preflight pipeline**, but keep the underlying steps modular.

Concretely:

1. `IPdfPreflightService` becomes the **single entrypoint** for all pre-structure PDF mutations:

   ```csharp
   public interface IPdfPreflightService
   {
       byte[] RunPreflight(byte[] inputPdf, AccessibilityRemediationSettings settings);
   }
   ```

2. Its implementation will:
   - Step 1: Run Aspose font fix / optimization (if enabled).
   - Step 2: Run `ArtifactTaggedContentFixService` (if `ArtifactFixMode == PreStructureOnly`).
   - Step 3: Return the resulting PDF bytes.

   For example:

   ```csharp
   public byte[] RunPreflight(byte[] inputPdf, AccessibilityRemediationSettings settings)
   {
       var pdf = inputPdf;

       if (settings.AsposeOptimizationMode == AsposeOptimizationMode.PreStructureOnly)
       {
           pdf = _asposeFontService.OptimizeFonts(pdf, settings);
       }

       if (settings.ArtifactFixMode == ArtifactFixMode.PreStructureOnly)
       {
           pdf = _artifactTaggedContentFixService.WrapUnmarkedXObjects(pdf, settings);
       }

       return pdf;
   }
   ```

3. The **orchestrator** only needs to call:

   ```csharp
   var preflightPdf = _preflightService.RunPreflight(originalPdf, settings);
   var finalPdf = _remediationPipeline.RunStructureAndMcidPipeline(preflightPdf, settings);
   ```

This keeps:

- A **single place** where all pre-content-stream mutations happen.
- The responsibilities clear and testable.
- The orchestrator simple.

---

## 2. ArtifactTaggedContentFixService Location

### Question

> ARTIFACT-FIX appears to be called in multiple places:
> - Once during "AI Structure Rebuild" phase
> - Again during "Structure Enhancement" iterations
> - Again in "Post-Remediation Structural Cleanup"
>
> Should I:
> - Remove it from ALL those phases and only run it once in preflight?
> - Or keep it in some phases but add guards to skip if MCID markers exist?

### Decision

For now: **Run ARTIFACT-FIX only once in preflight and remove it from later phases.**

Details:

1. **Remove** calls to `ArtifactTaggedContentFixService` from:
   - AI Structure Rebuild phase
   - Structure Enhancement iterations
   - Post-Remediation Structural Cleanup

2. The **only** place `ArtifactTaggedContentFixService` should be invoked in the default configuration is **inside `IPdfPreflightService.RunPreflight`**, before:

   - AI analysis
   - StructureTree building
   - MCID content rewrite

3. Future extension (optional, not now):

   - If later we truly need a **post-structure MCID-aware artifact pass**, that should be:
     - Controlled via `ArtifactFixMode = PostStructureMcidAware`
     - Implemented with shared parsing/serialization and BDC/EMC preservation
     - Guarded so it cannot accidentally run in non-MCID-aware mode

But for this phase:

> **Implementation scope:** Remove all post-structure usages and run ARTIFACT-FIX only in preflight.

---

## 3. Configuration Priority & Placement

### Question

> The spec says default is "PreStructureOnly". Should this be:
> - A new property on `RemediationOptions`?
> - Part of the existing `AsposeOptimizationMode` system?
> - A separate settings category altogether?

### Decision

- Define a **new property** on your existing remediation options/config type, separate from `AsposeOptimizationMode`.
- Do **not** overload the Aspose setting; they are related but distinct concerns.

Concretely:

1. In your main options class (e.g. `AccessibilityRemediationSettings` or `RemediationOptions`), add:

   ```csharp
   public enum ArtifactFixMode
   {
       PreStructureOnly = 0,
       PostStructureMcidAware = 1   // reserved for future MCID-aware implementation
   }

   public ArtifactFixMode ArtifactFixMode { get; set; } = ArtifactFixMode.PreStructureOnly;
   ```

2. This lives alongside:

   ```csharp
   public AsposeOptimizationMode AsposeOptimizationMode { get; set; } = AsposeOptimizationMode.PreStructureOnly;
   ```

3. Configuration shape (example):

   ```json
   "AccessibilityRemediation": {
     "EnableMcidLinking": true,
     "EnableMcidContentRewrite": true,
     "AsposeOptimizationMode": "PreStructureOnly",
     "ArtifactFixMode": "PreStructureOnly"
   }
   ```

4. Default behavior:

   - `ArtifactFixMode = PreStructureOnly`
     - ARTIFACT-FIX runs in preflight only.
     - Any attempt to run it after MCID rewrite should be blocked or logged as an error.
   - `ArtifactFixMode = PostStructureMcidAware`
     - Only allowed once ARTIFACT-FIX is refactored to preserve MCIDs.

This keeps:

- Aspose behavior and Artifact behavior **configurable independently**.
- A clear signal in logs and code about which modes are active.

---

## 4. Short Summary for Claude

You can paste this to Claude:

> - Update `IPdfPreflightService` to be a **multi-step preflight pipeline** that runs Aspose optimization and then `ArtifactTaggedContentFixService` (when enabled).
> - Remove all post-structure uses of `ArtifactTaggedContentFixService` and only run it from preflight in the default configuration.
> - Add `ArtifactFixMode` as a **new property** on the existing remediation options, with default `PreStructureOnly`. Do **not** bundle it into `AsposeOptimizationMode`.
> - Ensure ARTIFACT-FIX never runs after MCID content rewrite in `PreStructureOnly` mode; if invoked accidentally, it should be blocked or logged as an error.

---

**Filename:**  
`PHASE-6D-PREFLIGHT-AND-ARTIFACT-QA-v1.md`
