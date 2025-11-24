# Phase 6I: Content Stream Segmentation by Bounding Box

**Status**: Specification
**Created**: 2025-11-18
**Problem**: Phase 6H successfully inserts BDC/EMC markers but destroys PDF content by wrapping everything incorrectly

## Problem Statement

### What Went Wrong in Phase 6H

The current implementation in `McidRewriterMicroservice/main.py` uses this pattern:

```python
# CURRENT (BROKEN) - Lines 159-180
new_instructions = []

# Add ALL BDC markers at the start
for segment in sorted_segments:
    bdc_instr = ContentStreamInstruction([Name.Span, Name.MCID, segment.mcid], Operator("BDC"))
    new_instructions.append(bdc_instr)

# Add ALL original content in the middle
new_instructions.extend(instructions)

# Add ALL EMC markers at the end
for _ in sorted_segments:
    emc_instr = ContentStreamInstruction([], Operator("EMC"))
    new_instructions.append(emc_instr)
```

**Result**: Content stream becomes:
```
BDC MCID=0
BDC MCID=1
<ENTIRE PAGE CONTENT - 1027 instructions>
EMC
EMC
```

**Impact**: PDF renders as blank/corrupted because all content is nested incorrectly

### What We Need

```
<instruction 0-500>        ← Untagged content
BDC MCID=0                 ← Start region 0
<instruction 501-600>      ← Content at (72, 720, 468, 12)
EMC                        ← End region 0
<instruction 601-800>      ← Untagged content
BDC MCID=1                 ← Start region 1
<instruction 801-900>      ← Content at (72, 700, 468, 12)
EMC                        ← End region 1
<instruction 901-1027>     ← Untagged content
```

## Technical Approach

### Algorithm: Content Stream Segmentation

**Goal**: Determine which content stream instructions fall within each McidSegment's bounding box

**Inputs**:
- `instructions`: List of ContentStreamInstructions (parsed from PDF)
- `segments`: List of McidSegments with bounding boxes

**Output**:
- New instruction list with BDC/EMC markers wrapping the correct content

See full specification at: PHASE-6I-CONTENT-SEGMENTATION-BY-BOUNDING-BOX-v1.md
