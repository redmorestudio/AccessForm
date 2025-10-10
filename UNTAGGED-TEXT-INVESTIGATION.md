# Untagged Text Object Investigation & Fixes

## Problem Summary

TWC government forms were generating 33 "Text object not tagged" PDF/UA violations. These violations occurred when text-showing operators (Tj, TJ, etc.) appeared in the PDF content stream without being wrapped in marked content tags (BMC/BDC...EMC).

## Root Causes Identified

### 1. Orphaned Whitespace (Fixed: 13 violations)
**Issue**: Individual text operators like `(\40) Tj` (space character in octal) were appearing outside any marked content blocks.

**Location**: Throughout the PDF, near checkboxes, after periods, near page numbers.

**Fix**: Enhanced `fix_artifact_violations.py` with a cleanup pass (lines 170-207) that:
- Detects whitespace-only text operators using pattern: `\((\\(40|11|12|15|n|r|t)|\s)*\)\s*Tj`
- Checks if they're outside all marked content (BMC/BDC/EMC depth = 0)
- Removes them entirely from the content stream

**Result**: Reduced errors from 33 → 20

### 2. Service Logic Bug (Critical Fix)
**Issue**: `ArtifactViolationFixService.cs` was checking `violationsFound` instead of `violationsFixed` to determine whether to use the fixed PDF.

**Problem**: The Python script reports:
- `violations_found`: Count of tagged content inside artifacts
- `violations_fixed`: Total fixes including whitespace cleanup

Since whitespace cleanup doesn't count as "violations found", the service was returning the original PDF even when fixes were made.

**Fix**: Changed line 125 in `ArtifactViolationFixService.cs`:
```csharp
// Before:
if (violationsFound == 0)

// After:
if (violationsFixed == 0)
```

**Result**: Step 3 (post-PassportPDF cleanup) now correctly applies fixes.

### 3. Untagged BT...ET Text Objects (Remaining: ~20 violations)
**Issue**: Entire text object blocks (BT...ET) are created without any surrounding marked content tags.

**Location**: Page 2, approximately 65 untagged BT...ET blocks detected.

**Example**:
```pdf
BT
/C2_0 1 Tf 12 0 0 12 419.88 744.12 Tm <0003>Tj
ET
```

**Why This Happens**: Word/Aspose creates these text objects without proper tagging in the source document structure.

**Attempted Fix**: Tried wrapping untagged BT...ET blocks in `/Artifact BMC...EMC` tags.

**Problem**: Too aggressive - wrapped real content that should be properly tagged, creating 40 new "tagged content within artifacts" violations and 13 new "text not tagged" violations (53 total).

**Current Status**: Reverted BT...ET wrapping code. This requires smarter detection to distinguish decorative content from real content.

## Files Modified

### 1. `fix_artifact_violations.py`
**Changes**:
- Lines 170-207: Added whitespace cleanup pass
- Lines 177-186: Enhanced regex pattern to match various whitespace encodings (octal, escape sequences)
- Lines 191-207: Logic to check marked content depth and remove untagged whitespace

### 2. `Services/ArtifactViolationFixService.cs`
**Changes**:
- Line 125: Changed early-return condition from `violationsFound == 0` to `violationsFixed == 0`
- Line 127: Updated log message
- Line 133: Now returns `violationsFound` even when no fixes needed

### 3. `Services/PdfPreservationService.cs`
**Previous Session**: Added Step 3 (lines 366-392) to run artifact cleanup after PassportPDF.

## Diagnostic Tools Created

### 1. `diagnose_untagged.py`
Purpose: Find all text-showing operators and check if they're inside marked content.

Usage:
```bash
python3 diagnose_untagged.py <pdf_file>
```

Output: Lists all untagged text objects with context.

### 2. Manual Testing Commands
```bash
# Count whitespace instances
grep -o '(\\40) Tj' file.pdf | wc -l

# Test fix script directly
python3 fix_artifact_violations.py input.pdf output.pdf

# Check untagged BT...ET blocks
python3 -c "
import fitz, re
doc = fitz.open('file.pdf')
page = doc[1]
# [check BT...ET depth logic]
"
```

## Results

### Before All Fixes
- **33 "text object not tagged" errors**

### After Whitespace Cleanup + Service Fix
- **20 "text object not tagged" errors**
- **Progress**: 13 violations fixed (40% reduction)

### Attempted BT...ET Fix (Reverted)
- **53 total errors** (40 "tagged content within artifacts" + 13 "text not tagged")
- **Conclusion**: Approach too aggressive, needs refinement

## Remaining Issues

### Problem: 20 Untagged BT...ET Text Objects
These are entire text object blocks created by Word/Aspose without proper tagging structure.

### Potential Solutions

#### Option 1: Smart Content Detection
Enhance BT...ET wrapping to only tag decorative content:
- Check if text contains only hex characters like `<0003>` (decorative glyphs)
- Check if text contains only whitespace
- Preserve blocks with readable text content

#### Option 2: Source Document Fix
Fix the Word document structure before conversion:
- Ensure all text runs have proper accessibility tags
- Review Word document's heading structure
- Check if Aspose.Words has settings to improve tagging

#### Option 3: Structure Tree Manipulation
Instead of content stream manipulation, fix the PDF structure tree:
- Use iText7 to add structure elements for untagged text objects
- Map BT...ET blocks to proper parent structure elements
- More complex but potentially safer

## Recommended Next Steps

1. **Analyze the 20 remaining violations** to categorize them:
   - Decorative content (safe to mark as artifacts)
   - Real content (needs proper tagging, not artifacts)

2. **If mostly decorative**: Implement smart BT...ET wrapping with content detection

3. **If mostly real content**: Investigate Word document source and Aspose settings

4. **Fallback**: Document the 20 remaining violations as known issues requiring source document fixes

## Testing Notes

- Always test with PAC after fixes
- Download fresh PDF from server after each rebuild
- Check both "text object not tagged" AND "tagged content within artifacts" error counts
- Manual script testing doesn't always match service results due to timing/intermediate PDFs

## Server Logs Key Indicators

Look for these in logs:
```
[PDF-PRESERVATION] Step 3: Final artifact cleanup after PassportPDF...
✅ Fixed X violations after PassportPDF
```

If X > 15, likely too aggressive.
If X = 0, likely service bug (check violationsFixed vs violationsFound).
