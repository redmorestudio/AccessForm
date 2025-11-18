# Phase 6E – MCID Flag Wiring Instructions  
### Instructions for Claude (repo-aware assistant)

You are working in a C#/.NET solution that contains the PDF remediation pipeline. Configuration has already been wired into `RemediationOptions` and into `RemediationJobContext.Options`. Your task is to finish **Phase 6E** by making `StructureRebuildService` (and, if needed, `TaggedPdfFinalizer`) actually honor the MCID flags from `RemediationJobContext.Options`.

Please follow these steps **exactly** and produce **unified diffs** for each file you change.

---

## 1. Verify `RemediationOptions` Has MCID Flags

1. Search for: `class RemediationOptions`.
2. Ensure it contains:

```csharp
public bool EnableMcidLinking { get; set; } = true;
public bool EnableMcidContentRewrite { get; set; } = true;
```

3. If defaults aren’t set, set both to `true`.

Show the diff for this file if changes are needed.

---

## 2. Verify `RemediationJobContext` Exposes Options

1. Search for: `class RemediationJobContext`.
2. Ensure it includes:

```csharp
public RemediationOptions? Options { get; set; }
```

Show a diff only if you changed anything.

---

## 3. Update `StructureRebuildService` to Honor the Flags

### A. Locate the class

Search: `class StructureRebuildService`.

Confirm the constructor receives `RemediationJobContext`. If not, update the constructor:

```csharp
private readonly RemediationJobContext _jobContext;
private readonly ILogger<StructureRebuildService> _logger;

public StructureRebuildService(
    RemediationJobContext jobContext,
    ILogger<StructureRebuildService> logger
    /* existing deps */)
{
    _jobContext = jobContext;
    _logger = logger;
    // assign other deps...
}
```

### B. At the entrypoint method

Identify the main orchestration method (likely `RebuildAsync`, `ExecuteAsync`, or similar).

At the top of that method, add:

```csharp
var options = _jobContext.Options ?? new RemediationOptions();

_logger.LogInformation(
    "StructureRebuildService starting. MCID linking={McidLinking}, MCID content rewrite={McidRewrite}",
    options.EnableMcidLinking,
    options.EnableMcidContentRewrite);
```

### C. Gate MCID Content Rewrite

Search inside `StructureRebuildService` for MCID-related logic:

- `MCID`
- `Mcid`
- `BDC`
- `EMC`
- `Rewrite`
- `Link`

Find the method or code block that performs **MCID content rewrite**.  
Wrap the **call site** (not the implementation) in:

```csharp
if (options.EnableMcidContentRewrite)
{
    _logger.LogInformation("Running MCID content rewrite for this job.");
    await RewriteMcidContentAsync(/* existing args */);
}
else
{
    _logger.LogInformation("MCID content rewrite disabled; skipping.");
}
```

If inlined, wrap the whole MCID rewrite block in the same conditional.

### D. Gate MCID Linking

Find the method or block that handles **MCID linking**.

Wrap the call site in:

```csharp
if (options.EnableMcidLinking)
{
    _logger.LogInformation("Running MCID linking for this job.");
    await LinkMcidsAsync(/* existing args */);
}
else
{
    _logger.LogInformation("MCID linking disabled; skipping.");
}
```

### E. Confirm Behavior

When both flags are `true`, behavior should be identical to the current implementation.

Show the complete diff for `StructureRebuildService`.

---

## 4. Update `TaggedPdfFinalizer` (Only If It Touches MCIDs)

1. Search for: `class TaggedPdfFinalizer`.
2. Search inside the file for `MCID`, `Mcid`, `BDC`, `EMC`.

### If no MCID logic exists  
→ State explicitly: **“No MCID logic found; no changes required.”**

### If MCID logic exists  
1. Ensure the constructor receives `RemediationJobContext`:

```csharp
private readonly RemediationJobContext _jobContext;
private readonly ILogger<TaggedPdfFinalizer> _logger;
```

```csharp
public TaggedPdfFinalizer(
    RemediationJobContext jobContext,
    ILogger<TaggedPdfFinalizer> logger
    /* existing deps */)
{
    _jobContext = jobContext;
    _logger = logger;
}
```

2. In the main method (`FinalizeAsync` or similar), add:

```csharp
var options = _jobContext.Options ?? new RemediationOptions();
```

3. Wrap MCID content rewrite and/or linking logic with the same checks used in `StructureRebuildService`.

Show the diff for this file.

---

## 5. Add/Update Tests

Locate tests for `StructureRebuildService`:

- Search: `StructureRebuildServiceTests`

If tests exist, add tests verifying:

### A. With Content Rewrite Enabled

```csharp
[Fact]
public async Task Rebuild_RunsMcidRewrite_WhenEnabled()
{
    var ctx = new RemediationJobContext
    {
        Options = new RemediationOptions
        {
            EnableMcidContentRewrite = true,
            EnableMcidLinking = true
        }
    };

    var spy = new McidRewriteSpy();
    var svc = CreateStructureRebuildService(ctx, spy);

    await svc.RebuildAsync();

    Assert.True(spy.ContentRewriteCalled);
}
```

### B. With Content Rewrite Disabled

```csharp
[Fact]
public async Task Rebuild_SkipsMcidRewrite_WhenDisabled()
{
    var ctx = new RemediationJobContext
    {
        Options = new RemediationOptions
        {
            EnableMcidContentRewrite = false
        }
    };

    var spy = new McidRewriteSpy();
    var svc = CreateStructureRebuildService(ctx, spy);

    await svc.RebuildAsync();

    Assert.False(spy.ContentRewriteCalled);
}
```

If no tests exist, create a minimal test file verifying these two behaviors.

Show diffs for test updates.

---

## 6. Final Output Summary

After applying all changes, provide:

- A list of methods that perform MCID content rewrite.
- A list of methods that perform MCID linking.
- Exact locations where gating logic was applied.
- All files that now read `RemediationJobContext.Options`.
- Confirmation that behavior is unchanged when both flags are `true`.
- Confirmation that disabling flags cleanly skips their respective logic.

---

**End of Instructions**
