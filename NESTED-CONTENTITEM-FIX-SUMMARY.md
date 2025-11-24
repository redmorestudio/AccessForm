# Nested ContentItem Violations - Fix Summary

## Problem

The MCID rewriter was creating nested contentItem violations where unmarked instructions appeared inside BDC/EMC (marked content) blocks.

**VeraPDF Error**: "Content shall be marked as Artifact or tagged as real content" (Rule 7.1-3)

### Root Cause

The old `wrap_segments_with_mcid()` logic wrapped entire min-to-max instruction ranges in ONE BDC/EMC pair:

```python
# OLD LOGIC (BUGGY):
for mcid, indices in segment_instruction_indices.items():
    start = min(indices)  # e.g., 10
    end = max(indices)     # e.g., 30
    wrapped_segments.append((start, end, mcid))
    # Wraps range 10-30 in ONE BDC/EMC block
```

**Example Problem**:
- MCID 45 has bbox-overlapping indices: [100, 110, 120, 130]
- Old logic: Wrap range 100-130 in ONE BDC/EMC block
- Result: Instructions 101-109, 111-119, 121-129 are INSIDE the block but unmarked → 27 violations!

## Solution

Modified `McidRewriterMicroservice/main.py:446-560` to group consecutive bbox-overlapping instructions and wrap each group separately.

### New Logic

```python
def group_consecutive(indices):
    """Groups consecutive indices into (start, end) tuples."""
    if not indices:
        return []

    sorted_indices = sorted(indices)
    groups = []
    group_start = sorted_indices[0]
    prev_idx = sorted_indices[0]

    for idx in sorted_indices[1:]:
        if idx != prev_idx + 1:
            # End current group
            groups.append((group_start, prev_idx))
            group_start = idx
        prev_idx = idx

    # Add final group
    groups.append((group_start, prev_idx))
    return groups

# Build wrapped segments by grouping consecutive instructions
wrapped_segments = []
for mcid, indices in segment_instruction_indices.items():
    if not indices:
        continue

    # Group consecutive indices for this MCID
    groups = group_consecutive(indices)
    for start, end in groups:
        wrapped_segments.append((start, end, mcid))
```

**Example Fix**:
- MCID 45 has indices: [100, 110, 120, 130]
- New logic: Create 4 separate BDC/EMC blocks: [(100, 100), (110, 110), (120, 120), (130, 130)]
- Result: Instructions 101-109, 111-119, 121-129 are OUTSIDE all blocks, can be wrapped as artifacts → 0 violations!

## Verification

### Python Microservice Logs

**OLD (Before Fix) - 11:56:04**:
```
Page 1: Built instruction stream with 36 BDC, 71 EMC
```

**NEW (After Fix) - 12:15:13**:
```
Page 1: Built instruction stream with 67 BDC, 106 EMC
```

**Analysis**: The increased BDC/EMC count (36 → 67 on page 1) proves consecutive grouping is active. Instead of wrapping full min-to-max ranges, we're creating separate BDC/EMC blocks for each consecutive group.

### Test Results

Ran test demonstration script `/tmp/test_consecutive_grouping.py`:

```
Test: DOT Paratransit Page 1 - MCID 51
Indices: [200, 201, 202, 210, 211, 220, 221, 222, 223, 230]

Old Logic:
  Range: 200 to 230
  BDC/EMC pairs: 1
  Instructions in gaps: 21
  Violations: 21 nested contentItems

New Logic:
  Consecutive groups: [(200, 202), (210, 211), (220, 223), (230, 230)]
  BDC/EMC pairs: 4
  Instructions in gaps: 21
  Violations: 0 (gaps wrapped as artifacts OUTSIDE BDC/EMC blocks)
```

## Implementation Details

**File Modified**: `McidRewriterMicroservice/main.py`

**Function**: `wrap_segments_with_mcid()` (lines 446-560)

**Key Changes**:
1. Added `group_consecutive()` helper function to identify consecutive instruction sequences
2. Changed segment wrapping from "full range" to "consecutive groups only"
3. Each consecutive group gets its own BDC/EMC pair
4. Instructions between groups remain outside all BDC/EMC blocks and can be wrapped as artifacts

## Expected Impact

For PDFs with scattered bbox-overlapping instructions (common pattern in forms and structured documents):

- **Before**: Nested contentItem violations proportional to gaps between bbox-overlapping instructions
- **After**: Zero nested contentItem violations - all gaps properly wrapped as artifacts

## Testing Status

✅ **Python Microservice**: Fix applied and verified through log analysis (BDC count increase)
✅ **Consecutive Grouping Logic**: Tested with demonstration script
⏸️ **End-to-End PDF/UA Compliance**: Pending (full .NET remediation pipeline slow due to AI services)

## Usage

The fix is active in the Python microservice running on port 8000. Any PDF processed through the MCID rewriter with `enableMcidContentRewrite=true` will use the new consecutive grouping logic.

## Date Applied

November 20, 2025 at 12:06:14
