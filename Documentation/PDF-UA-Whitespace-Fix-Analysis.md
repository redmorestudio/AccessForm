# PDF/UA Whitespace Fix Analysis

## Problem Summary

We're encountering **33 "text object not tagged" errors** in PDF/UA validation when processing Word documents to accessible PDFs. These errors persist despite multiple fix attempts.

## Root Cause

The errors are caused by **untagged whitespace characters** in the PDF content stream. Specifically:

1. **Octal-encoded spaces**: `(\40) Tj` - where `\40` is octal for space (ASCII 32)
2. **Literal whitespace**: `( ) Tj` - literal space characters in parentheses
3. **Other whitespace**: `(\n)`, `(\r)`, `(\t)` - newlines, carriage returns, tabs

These whitespace text operations exist **outside** of any tagged content blocks (BDC...EMC), making them invisible to accessibility tools but flagged as errors by PDF/UA validators.

## What We've Been Doing

### The Fix Strategy

We're using a Python script (`fix_artifact_violations.py`) with PyMuPDF to:

1. **Find artifact violations** - Remove /Artifact BMC...EMC wrappers from tagged content
2. **Cleanup untagged whitespace** - Delete ALL untagged whitespace text operations

### The Regex Pattern (The Problem)

The cleanup pass uses a regex pattern to find whitespace-only text operations:

```python
# Pattern to match whitespace-only Tj operations
whitespace_pattern = r'\((\\(40|11|12|15|n|r|t)|\s)*\)\s*Tj'
```

**What this pattern matches:**
- `(` - Opening parenthesis (start of PDF text string)
- `(\\(40|11|12|15|n|r|t)|\s)*` - Zero or more of:
  - `\\40` - Backslash followed by `40` (octal space)
  - `\\11` - Backslash followed by `11` (octal tab)
  - `\\12` - Backslash followed by `12` (octal newline)
  - `\\15` - Backslash followed by `15` (octal carriage return)
  - `\\n` - Backslash followed by `n` (escaped newline)
  - `\\r` - Backslash followed by `r` (escaped carriage return)
  - `\\t` - Backslash followed by `t` (escaped tab)
  - `\s` - Any whitespace character (space, tab, newline, etc.)
- `)` - Closing parenthesis (end of PDF text string)
- `\s*` - Optional whitespace
- `Tj` - PDF text-showing operator

**In raw Python strings (`r'...'`):**
- `\\` matches ONE literal backslash character
- `\s` matches whitespace (special regex character)

### The Detection Logic

After finding matches, we check if they're inside tagged blocks:

```python
for match in reversed(matches):  # Reverse to preserve indices
    match_pos = match.start()

    # Check if this is inside a tagged block (BDC/BMC...EMC)
    before = content_str[:match_pos]
    bdc_count = before.count('BDC')
    bmc_count = before.count('BMC')
    emc_count = before.count('EMC')

    # If total opens equals closes, we're NOT inside any marked content block
    if (bdc_count + bmc_count) == emc_count:
        # This is untagged whitespace - delete it
        content_str = content_str[:match.start()] + content_str[match.end():]
        modified = True
        total_fixed += 1
```

**Logic:**
- Count all `BDC` (begin marked content with dictionary) occurrences before the match
- Count all `BMC` (begin marked content) occurrences before the match
- Count all `EMC` (end marked content) occurrences before the match
- If `(BDC + BMC) == EMC`, we're outside all tagged blocks → **delete it**
- If `(BDC + BMC) > EMC`, we're inside a tagged block → **keep it**

## PDF Content Stream Examples

### Example 1: Untagged Whitespace (Should be deleted)

```
BT
/F1 12 Tf
100 700 Td
(Hello) Tj
(\40) Tj          ← UNTAGGED SPACE - DELETE THIS
(World) Tj
ET
```

### Example 2: Tagged Whitespace (Should be kept)

```
BT
/F1 12 Tf
100 700 Td
/P <</MCID 5>> BDC
(Hello) Tj
(\40) Tj          ← TAGGED SPACE - KEEP THIS
(World) Tj
EMC
ET
```

### Example 3: Mixed Content

```
BT
/F1 12 Tf
100 700 Td
/P <</MCID 5>> BDC
(Hello) Tj
EMC
(\40) Tj          ← UNTAGGED - DELETE
/P <</MCID 6>> BDC
(World) Tj
EMC
ET
```

## Testing Results

### Detection Test on Processed PDF

Running detection on `twc1020-twc_pdfua1037 copy.pdf`:

```bash
python3 detect_untagged_whitespace.py "twc1020-twc_pdfua1037 copy.pdf"
```

**Results:**
- Untagged Tj operations: **13**
- Untagged TJ operations: **1**
- Total untagged: **14**

**But PAC reports: 33 errors** ❌

### Pattern Matching Tests

**Test 1: Our pattern**
```python
pattern = r'\((\\(40|11|12|15|n|r|t)|\s)*\)\s*Tj'
matches = re.findall(pattern, content_str)
# Result: UNKNOWN (needs testing)
```

**Test 2: Literal `\40` pattern**
```python
pattern = r'\(\\40\)\s*Tj'
matches = re.findall(pattern, content_str)
# Result: 128 matches on original processed PDF
```

## The Discrepancy

We're finding 14 untagged operations, but PAC reports 33 errors. Possible reasons:

1. **PAC counts individual glyphs/characters**: Each space might be counted separately
2. **Our nesting logic is wrong**: We might be miscounting BDC/BMC/EMC
3. **Pattern not matching all variations**: There might be other whitespace patterns we're missing
4. **TJ array elements**: The `TJ` operator uses arrays like `[(Hello) -50 (World)] TJ` where `-50` is kerning, but whitespace might be in there

## Current Status

### What's Fixed
✅ Regex pattern uses correct escaping (`\\` for one backslash in raw strings)
✅ Nesting level tracking implemented
✅ Detection and cleanup passes in place
✅ Server restarted with latest code

### What's Not Working
❌ Still seeing 33 errors after processing
❌ Our detection finds 14 untagged operations vs PAC's 33
❌ Fix not reducing error count

## Next Steps to Investigate

1. **Verify pattern is matching**: Add debug logging to see exactly what the pattern matches
2. **Examine TJ arrays**: Check if whitespace exists in TJ array operations
3. **Manual PDF inspection**: Open processed PDF in text editor and search for `\40` manually
4. **Compare before/after**: Run detection on PDF before AND after processing to see if we're creating new errors
5. **Test pattern in isolation**: Write a simple test script that applies the pattern to a known string

## Technical Notes

### PDF Text Operators
- `Tj` - Show text string: `(Hello) Tj`
- `TJ` - Show text array: `[(Hello) -50 (World)] TJ`
- `'` - Move to next line and show text
- `"` - Set word/char spacing and show text

### PDF Marked Content
- `BDC` - Begin marked content with properties: `/P <</MCID 5>> BDC`
- `BMC` - Begin marked content without properties: `/Artifact BMC`
- `EMC` - End marked content

### Octal Encoding in PDF
- `\40` = Space (ASCII 32, octal 040)
- `\11` = Tab (ASCII 9, octal 011)
- `\12` = Newline (ASCII 10, octal 012)
- `\15` = Carriage return (ASCII 13, octal 015)

### Character Encoding
In the PDF content stream (read with `latin-1` encoding):
- `\40` appears as literal characters: backslash + `4` + `0`
- Not as an escape sequence
- Python regex `\\` matches ONE literal backslash character

## File Locations

- **Main fix script**: `fix_artifact_violations.py` (line 185 contains the whitespace pattern)
- **Detection script**: `detect_untagged_whitespace.py`
- **C# service**: `Services/TaggedWhitespaceFixService.cs`
- **Processing service**: `Services/PdfPreservationService.cs`
- **Test PDF**: `twc1020-twc_pdfua1037 copy.pdf`
- **Source PDF**: `TWC Forms/eBily/twc1020-twc.pdf`

## User-Reported Error Location

User identified specific error: "one is the space to the right of the 2 at the bottom right of the page"

This refers to:
- Page 2 of the document
- Page number "2" at bottom right
- The space after the "2" is untagged
- Page numbers come from the source Word document (confirmed: "these page numbers are there to start with")
