# Phase 6e – MCID Settings Wiring & Execution Control (v1)

**Goal:**  
`EnableMcidContentRewrite` is `true` in `appsettings.json`, but MCID content rewrite is not executing.  
Phase 6b/6d are structurally correct; the remaining issue is **settings/config wiring** so that the finalizer and StructureRebuild services actually *see* and *honor* the MCID flags.

This spec defines how to:

1. Trace MCID-related settings from configuration → runtime options → pipeline → finalizer.
2. Ensure MCID content rewrite is enabled/disabled **only** via those settings.
3. Add logging so this class of bug is easy to spot next time.

You can hand this file to Claude and say:

> “Implement everything in `PHASE-6E-MCID-SETTINGS-WIRING-SPEC-v1.md`.”

---

## 0. Problem Recap

Observations:

- `appsettings.json` (or equivalent) has:

  ```json
  "AccessibilityRemediation": {
    "EnableMcidLinking": true,
    "EnableMcidContentRewrite": true
  }
  ```

- Preflight and ARTIFACT-FIX now run exactly as desired and **do not** destroy MCIDs.
- However:
  - MCID content rewrite **does not execute at all** in the current run.
  - BDC/EMC marker count in final PDFs is still `0`.
  - There are no `[MCID-MARKER-6b]` logs for that remediation, or they indicate “disabled/skipped”.
- Root cause (per Claude’s analysis):
  - `StructureRebuildService` / finalizer **is not reading** the MCID flags from the same remediation configuration used by the pipeline.

Conclusion:

> We now have a configuration/DI wiring issue: the MCID settings are set in config but not actually used where they matter.

---

## 1. Canonical Source of MCID Options

We need to establish **one canonical type** that owns MCID toggles, e.g.:

```csharp
public sealed class AccessibilityRemediationSettings
{
    public bool EnableMcidLinking { get; set; } = true;
    public bool EnableMcidContentRewrite { get; set; } = true;

    // Existing properties:
    // public AsposeOptimizationMode AsposeOptimizationMode { get; set; } = AsposeOptimizationMode.PreStructureOnly;
    // public ArtifactFixMode ArtifactFixMode { get; set; } = ArtifactFixMode.PreStructureOnly;
    // ...
}
```

Or reuse an existing type (e.g. `RemediationOptions`) if that’s already present.

**Key decision:**

- Whatever type is currently used as the “remediation options/options snapshot” flowing through the pipeline should include these MCID flags and be the **single source of truth**.

---

## 2. Config Binding from appsettings.json

Ensure that `appsettings.json` → `AccessibilityRemediationSettings` binding is **correct**:

1. In `Program.cs` / Startup, you should have something like:

   ```csharp
   services.Configure<AccessibilityRemediationSettings>(
       configuration.GetSection("AccessibilityRemediation"));
   ```

2. Or, if you prefer strongly-typed options:

   ```csharp
   services.AddSingleton(resolver =>
       configuration.GetSection("AccessibilityRemediation").Get<AccessibilityRemediationSettings>()
       ?? new AccessibilityRemediationSettings());
   ```

3. Confirm that the **same config section name** is used consistently in:
   - The JSON
   - The options binding
   - Any DI registration

If you have both `RemediationOptions` and `AccessibilityRemediationSettings`, either:

- Merge them, or
- Ensure one is constructed from the other so there is no divergence.

---

## 3. Flow of Settings Through the Pipeline

We need to trace:

`appsettings.json` → `AccessibilityRemediationSettings` → Remediation request/command → Remediation orchestrator → `ITaggedPdfFinalizer` / `StructureRebuildService`.

### 3.1 Orchestrator: per-job settings snapshot

Pattern:

```csharp
public class RemediationOrchestrator
{
    private readonly IOptions<AccessibilityRemediationSettings> _defaultSettings;

    public async Task<RemediationResult> RunAsync(RemediationJobRequest request)
    {
        var jobSettings = BuildSettingsForJob(request, _defaultSettings.Value);

        _logger.LogInformation(
            "REMEDIATION JOB: MCID linking={Link}, MCID content rewrite={Rewrite}",
            jobSettings.EnableMcidLinking,
            jobSettings.EnableMcidContentRewrite);

        return await _remediationPipeline.RunAsync(request.InputPdf, jobSettings);
    }
}
```

- `BuildSettingsForJob`:
  - Start with `_defaultSettings.Value`
  - Apply any per-request overrides if your API supports them.

### 3.2 Pipeline must carry settings explicitly

Avoid re-resolving `IOptions<AccessibilityRemediationSettings>` at lower levels.

Instead, the pipeline should pass a **single `settings` instance** throughout:

```csharp
public interface IRemediationPipeline
{
    Task<byte[]> RunAsync(byte[] inputPdf, AccessibilityRemediationSettings settings);
}
```

Then:

- All calls to `StructureRebuildService`
- `ITaggedPdfFinalizer`
- And any MCID/Phase 6-related services

receive that same `settings` instance.

---

## 4. Finalizer & Structure Rebuild – Honor MCID Flags

### 4.1 In `ITaggedPdfFinalizer`

Explicitly gate MCID behaviors on settings:

```csharp
public byte[] FinalizeTaggedPdf(
    byte[] originalPdf,
    LogicalDocument logical,
    StructureTree structure,
    PageLayoutPlan layoutPlan,
    AccessibilityRemediationSettings settings,
    StructureRebuildContext context)
{
    var enableMcidLinking = settings.EnableMcidLinking;
    var enableMcidRewrite = settings.EnableMcidContentRewrite;

    _logger.LogInformation(
        "FINALIZER: MCID linking={Linking}, MCID content rewrite={Rewrite}",
        enableMcidLinking,
        enableMcidRewrite);

    if (!enableMcidLinking && !enableMcidRewrite)
    {
        _logger.LogInformation("FINALIZER: MCID features disabled. Skipping MCID assignment and content rewrite.");
        return WriteTaggedPdfWithoutMcid(originalPdf, logical, structure, layoutPlan, context);
    }

    return WriteTaggedPdfWithMcid(originalPdf, logical, structure, layoutPlan, settings, context);
}
```

### 4.2 In MCID content marker / Phase 6b host

Within the implementation that actually injects BDC/EMC:

```csharp
public byte[] RewriteContentWithMcids(
    PdfDocument pdfDoc,
    StructureTree structure,
    PageLayoutPlan layoutPlan,
    AccessibilityRemediationSettings settings,
    StructureRebuildContext context)
{
    if (!settings.EnableMcidContentRewrite)
    {
        _logger.LogInformation(
            "[MCID-MARKER-6b] MCID content rewrite disabled via settings. Skipping Phase 6b.");
        return pdfDoc.GetBytes(); // or equivalent
    }

    _logger.LogInformation(
        "[MCID-MARKER-6b] Starting content rewrite (enabled={Enabled})",
        settings.EnableMcidContentRewrite);

    // ... existing 6b logic ...

    _logger.LogInformation(
        "[MCID-MARKER-6b] Completed content rewrite: {PageCount} pages, {SegmentCount} segments, {McidCount} MCIDs.",
        pageCount,
        segmentCount,
        mcidCount);

    return pdfDoc.GetBytes();
}
```

The critical part: **do not** hardcode or shadow MCID flags in local config; always read from the passed-in `settings`.

---

## 5. Diagnostics: Make Wiring Bugs Obvious

Add logging at three levels:

### 5.1 Orchestrator (job start)

```csharp
_logger.LogInformation(
    "REMEDIATION JOB {JobId}: MCID linking={Link}, MCID content rewrite={Rewrite}",
    jobId,
    jobSettings.EnableMcidLinking,
    jobSettings.EnableMcidContentRewrite);
```

### 5.2 Finalizer

```csharp
_logger.LogInformation(
    "FINALIZER: Using MCID settings: linking={Link}, rewrite={Rewrite}",
    settings.EnableMcidLinking,
    settings.EnableMcidContentRewrite);
```

### 5.3 MCID marker (Phase 6b)

- When **skipping** due to settings:

  ```csharp
  _logger.LogInformation(
      "[MCID-MARKER-6b] Skipped: EnableMcidContentRewrite=false.");
  ```

- When **executing**:

  ```csharp
  _logger.LogInformation(
      "[MCID-MARKER-6b] Executing content rewrite for {PageCount} pages.",
      pageCount);
  ```

This makes it impossible to silently have “true in appsettings, false in practice” without seeing it in logs.

---

## 6. Guarding Against Misconfiguration

Optionally, add a simple consistency check:

- At application startup (or orchestrator construction):

  ```csharp
  if (defaultSettings.EnableMcidContentRewrite && !defaultSettings.EnableMcidLinking)
  {
      _logger.LogWarning(
          "Configuration: MCID content rewrite is enabled but MCID linking is disabled. " +
          "This is unusual and may indicate misconfiguration.");
  }
  ```

- In debug environments, you can even throw if:
  - `EnableMcidContentRewrite == true`
  - But the **MCID content marker** is never invoked (e.g., tracked via a one-time flag per job).

---

## 7. Testing Checklist

Claude should:

1. Turn **on** MCID rewrite in `appsettings.json` (already done).
2. Add the logging described above.
3. Run remediation on Alexandria and confirm logs show:

   - `REMEDIATION JOB: MCID linking=true, MCID content rewrite=true`
   - `FINALIZER: Using MCID settings: linking=true, rewrite=true`
   - `[MCID-MARKER-6b] Executing content rewrite...`
   - `[MCID-MARKER-6b] Completed content rewrite: ...`

4. Confirm the final PDF has BDC/EMC:

   ```bash
   strings alexandria_school_remediated.pdf | grep -c "BDC"
   strings alexandria_school_remediated.pdf | grep -c "EMC"
   ```

5. If BDC/EMC are still zero:
   - Verify that the MCID marker is actually being called for that job.
   - Check whether any later service is still overwriting content streams (we’ve addressed Aspose + ARTIFACT-FIX; there may be others).

---

## 8. Summary for Claude

> - Make sure MCID flags (`EnableMcidLinking`, `EnableMcidContentRewrite`) live in the canonical remediation settings type.
> - Bind them correctly from `appsettings.json`.
> - Pass a job-specific `settings` instance through the entire remediation pipeline.
> - Have the finalizer and MCID marker explicitly honor those flags, with clear logs.
> - Re-run Alexandria to confirm Phase 6b executes when enabled and that BDC/EMC markers appear in the final PDF.

---

**Filename:**  
`PHASE-6E-MCID-SETTINGS-WIRING-SPEC-v1.md`
