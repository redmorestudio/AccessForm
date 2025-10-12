# Signal Path Isolation Fix

## Problem
The user reported that different detection modes are contaminating each other:
- When using "Syncfusion Only" mode, Claude Vision coordinates are somehow bleeding through
- When using "Claude Only" mode, Syncfusion processing is still happening
- The signal paths are not properly isolated

## Solution

We need to ensure THREE completely isolated signal paths:

### 1. SYNCFUSION-ONLY Mode
- **Config**: `UseSyncfusion=true`, `UseClaudeVision=false`
- **Expected**: ONLY Syncfusion detection runs
- **Issue**: Claude Vision coordinates are somehow appearing

### 2. CLAUDE-ONLY Mode
- **Config**: `UseSyncfusion=false`, `UseClaudeVision=true`
- **Expected**: ONLY Claude Vision detection runs
- **Issue**: Syncfusion might still be running

### 3. SYNCFUSION+CLAUDE Mode
- **Config**: `UseSyncfusion=true`, `UseClaudeVision=true`
- **Expected**: Both run, but coordinates properly converted
- **Primary use case**: This is what the user uses most

## Key Changes Needed

1. **Add explicit mode detection** at the start of ConvertWithConfig:
```csharp
enum DetectionMode {
    SyncfusionOnly,
    ClaudeOnly,
    SyncfusionPlusClaude
}
```

2. **Use UnifiedCoordinateService** for ALL coordinate conversions

3. **Add comprehensive logging** showing exactly which path is taken

4. **Ensure NO cross-contamination** between modes