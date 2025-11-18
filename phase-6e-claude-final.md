# Phase 6E – Final Implementation Instructions for Claude  
### Precise Steps to Complete MCID Flag Wiring in `StructureRebuildService` and `TaggedPdfFinalizer`

This document tells Claude **exactly** what to do inside the repository to finish Phase 6E.  
All steps must be followed verbatim.  
Claude should output **unified diffs** for every file changed.

---

# ✅ Objective

MCID configuration values (`EnableMcidLinking`, `EnableMcidContentRewrite`) are now properly loaded and stored in `RemediationJobContext.Options`.  
The remaining task is to update downstream services so they **use** these flags to control MCID operations.

This requires modifying:

- `StructureRebuildService` (always)
- `TaggedPdfFinalizer` (only if it performs MCID-related work)

---

# 1. Verify `RemediationOptions` Contains Both Flags

Claude must:

1. Search for:

```
class RemediationOptions
```

2. Ensure these two properties exist **with default values set to true**:

```csharp
public bool EnableMcidLinking { get; set; } = true;
public bool EnableMcidContentRewrite { get; set; } = true;
```

3. If anything is missing or defaults are not set → **modify accordingly**.

4. Output a diff if changes were necessary.

---

# 2. Verify `RemediationJobContext` Exposes `Options`

Claude must:

1. Search for:

```
class RemediationJobContext
```

2. Ensure it contains:

```csharp
public RemediationOptions? Options { get; set; }
```

3. If needed, add it.

4. Only output a diff if modified.

---

# 3. Update `StructureRebuildService` to Honor MCID Flags

Claude must:

### A. Locate the class

Search:

```
class StructureRebuildService
```

### B. Ensure constructor receives `RemediationJobContext`

If `_jobContext` is not already a constructor parameter:

Add:

```csharp
private readonly RemediationJobContext _jobContext;
private readonly ILogger<StructureRebuildService> _logger;

public StructureRebuildService(
    RemediationJobContext jobContext,
    ILogger<StructureRebuildService> logger
    /* other dependencies */)
{
    _jobContext = jobContext;
    _logger = logger;
    // assign other deps...
}
```

Show a diff if modified.

### C. Add options-reading to the main method

Find the **entrypoint** method (names vary):

- `RebuildAsync`
- `ExecuteAsync`
- `RunAsync`
- etc.

At the **top** of that method, add:

```csharp
var options = _jobContext.Options ?? new RemediationOptions();

_logger.LogInformation(
    "StructureRebuildService starting. MCID linking={McidLinking}, MCID content rewrite={McidRewrite}",
    options.EnableMcidLinking,
    options.EnableMcidContentRewrite);
```

### D. Gate MCID CONTENT REWRITE

Claude must:

1. Search inside the file for MCID-related code using these keywords:

```
MCID
Mcid
BDC
EMC
Rewrite
Normalize
```

2. Identify the method or inline block performing **MCID content rewrite**.

3. Wrap the **call site** in:

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

If the logic is inline instead of in a method, wrap the entire block with the same condition.

### E. Gate MCID LINKING

Same pattern.

Search for linking operations:

```
LinkMcid
BuildMcidLinks
FixMcidLinks
AssociateMcid
```

Wrap the call site:

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

### F. Output the complete diff for `StructureRebuildService`.

---

# 4. Update `TaggedPdfFinalizer` (Only If It Touches MCIDs)

Claude must:

1. Search for:

```
class TaggedPdfFinalizer
```

2. Look for MCID-related code using:

```
MCID
Mcid
BDC
EMC
```

### If NO MCID logic is found  
→ Output: **“No MCID logic found in TaggedPdfFinalizer; no changes required.”**

### If MCID logic DOES exist

Claude must:

#### A. Inject `RemediationJobContext` if not present

Add fields:

```csharp
private readonly RemediationJobContext _jobContext;
private readonly ILogger<TaggedPdfFinalizer> _logger;
```

Add constructor parameters:

```csharp
public TaggedPdfFinalizer(
    RemediationJobContext jobContext,
    ILogger<TaggedPdfFinalizer> logger
    /* other deps */)
{
    _jobContext = jobContext;
    _logger = logger;
}
```

#### B. In the main method (likely `FinalizeAsync`), add options-read:

```csharp
var options = _jobContext.Options ?? new RemediationOptions();
```

#### C. Wrap MCID rewrite or linking logic using the same gating pattern from Section 3.

#### D. Output a unified diff of all changes.

---

# 5. Tests (Optional but Preferred)

If tests exist for `StructureRebuildService`, Claude should add:

- Test proving content rewrite runs when enabled
- Test proving it is skipped when disabled

If tests do not exist, Claude should note that explicitly.

---

# 6. Final Summary (Claude Must Output)

After completing all code changes, Claude must provide a summary including:

- Which methods perform MCID content rewrite
- Which methods perform MCID linking
- Where gating logic was added
- Which files now read `RemediationJobContext.Options`
- Confirmation that:
  - With both flags `true`, behavior is unchanged
  - Disabling either flag skips its corresponding MCID operations

---

# End of Instructions
