# PassportPDF Accessibility Violation Fix

## The Problem

PassportPDF, while converting PDFs to PDF/UA format, introduces specific patterns that create untagged text violations detectable by PAC (PDF Accessibility Checker). These violations consistently appear as:
- 90% appear immediately after colons (:)
- Some appear after parentheses )
- Occasional random characters

## Root Cause Analysis

PassportPDF uses `\r` (carriage return, ASCII 13) as line separators instead of spaces or newlines. This creates patterns that our standard fixes don't detect. Specifically:

1. **ETX Control Characters**: PassportPDF adds `<0003>` (End of Text) characters throughout the document
2. **Transparency Mode Text**: Uses `3 Tr` (transparency rendering mode) for invisible text
3. **Untagged Whitespace**: Adds spaces and colons outside proper tagging structure

## The Solution: fix_passportpdf_violations.py

### Pattern 1: ETX Character Removal
```python
etx_pattern = r'<0003>\s*Tj'
```
**What it does**: Finds hexadecimal ETX characters followed by the text operator
**Why**: ETX (End of Text) is a control character that shouldn't appear in PDF text
**Example**: `<0003> Tj` becomes nothing (removed entirely)

### Pattern 2: Clean BT Blocks (CRITICAL!)
```python
bt_pattern = r'(BT.*?)ET'

def clean_bt_block(match):
    block = match.group(0)

    # Remove spaces after colons
    block = re.sub(r'\(:?\s*\)\s*Tj', '', block)

    # Remove empty text operations
    block = re.sub(r'\(\s*\)\s*Tj', '', block)

    # Remove single spaces (octal notation)
    block = re.sub(r'\(\\40\)\s*Tj', '', block)
```

**CRITICAL INSIGHT**: We MUST preserve BT/ET pairs! Initially, we removed entire blocks, which caused "Operator 'ET' not allowed" errors. The fix now surgically removes only the problematic text operations while keeping the block structure intact.

#### Regex Breakdown:

1. **`r'\(:?\s*\)\s*Tj'`** - Spaces after colons
   - `\(` - Literal opening parenthesis
   - `:?` - Optional colon character
   - `\s*` - Zero or more whitespace characters (including `\r`)
   - `\)` - Literal closing parenthesis
   - `\s*Tj` - Text operator with optional whitespace
   - **Example**: `(: )Tj` or `( )Tj` after a colon

2. **`r'\(\s*\)\s*Tj'`** - Empty text operations
   - Matches empty parentheses with just whitespace
   - **Example**: `( )Tj` or `()Tj`

3. **`r'\(\\40\)\s*Tj'`** - Octal space character
   - `\\40` is octal notation for space (ASCII 32)
   - **Example**: `(\40)Tj`

### Pattern 3: Empty Artifact Cleanup
```python
artifact_pattern = r'/Artifact\s+BMC[^E]*?EMC'
```
Removes artifact blocks that contain only whitespace or problematic characters.

## Why This Works

1. **Preserves Structure**: By keeping BT/ET pairs intact, we avoid syntax errors
2. **Targeted Removal**: Only removes the specific patterns causing violations
3. **PassportPDF-Specific**: Handles the unique `\r` line separator patterns

## Integration

The fix is integrated as Step 5 in the ArtifactViolationFixService pipeline:
1. Control character removal
2. Aggressive whitespace cleanup
3. Tagged whitespace cleanup
4. EMC/Artifact fix
5. **PassportPDF-specific fix** (NEW)

## Testing Results

- **Before**: 13 persistent violations after all other fixes
- **After**: 0 violations
- **No syntax errors** in PAC 2024

## Key Learnings

1. **PassportPDF uses `\r` not spaces**: This is why our space-based patterns missed them
2. **BT/ET pairs are sacred**: Breaking these causes PDF syntax errors
3. **Surgical precision required**: Remove operations, not structures
4. **Colons are the key**: Most violations appear as `(: )Tj` after actual colons in text

## Files Modified

- `fix_passportpdf_violations.py` - The fix script
- `Services/ArtifactViolationFixService.cs` - Added Step 5 to pipeline
- `bin/Debug/net8.0/fix_passportpdf_violations.py` - Deployed version

## Commit Hash
bd5d2ed - "Fix PassportPDF-induced accessibility violations"