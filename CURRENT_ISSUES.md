# AccessForm - Current Issues & TODOs
**Last Updated:** 2025-10-01
**Branch:** feature/cherry-pick-improvements

## 🚨 CRITICAL: Ghost Fields Bug - FIX APPLIED, NEEDS VERIFICATION

### Status: FIX IMPLEMENTED - AWAITING USER TESTING

**Problem:**
When processing documents (specifically vr1307-twc.docx) in **Syncfusion + Claude Vision** mode, ghost address fields appear at the TOP of the page when they should be in the middle/bottom of the page.

**Example Ghost Fields:**
- Home Street Address
- Physical Street Address
- Mailing Street Address
- City, State, Zip

**User's Key Observation:**
> "syncfusion + claude vision. syncfusion alone places them correctly"

This confirms that Syncfusion coordinates are CORRECT, so the bug is NOT in Sequential mode itself.

### Root Cause Discovered

The ghost fields are **NOT from Sequential mode processing** at all! They come from **TAG EXTRACTION** in `Program.cs` around line 3330.

**What's happening:**
1. Legacy fields are embedded in PDF tag structure from previous processing runs
2. These old fields don't have `[PAGE:X]` tooltips (which current system adds)
3. Without tooltips, code was **falling back to page 1** with wrong coordinates
4. This caused fields to appear at top of page

**Evidence from logs:**
```
[TAG-STRUCTURE-TEXT] Processing text field 'Home Street Address' with Y=22.400574
[TAG-STRUCTURE-ERROR] Field at Y=22.400574 has no [PAGE:X] tooltip! Defaulting to page 1
```

### Fix Applied

Modified `Program.cs` to **SKIP fields without [PAGE:X] tooltips** instead of falling back to page 1:

**Changed lines:**
- **3344-3345:** Text fields - changed from fallback → skip with `continue;`
- **3377-3378:** Checkboxes - changed from fallback → skip with `continue;`
- **3407-3409:** Signature fields - changed from fallback → skip with `continue;`
- **3437-3439:** Radio buttons - changed from fallback → skip with `continue;`

**Before:**
```csharp
page = FallbackToPageOne(txtField.Bounds.Y);
logger.LogError($"🚫 [TAG-STRUCTURE-ERROR] Text field '{f.Name}' missing [PAGE:X] tooltip! Using page {page} fallback");
```

**After:**
```csharp
logger.LogError($"🚫 [TAG-STRUCTURE-SKIP] Text field '{f.Name}' missing [PAGE:X] tooltip! Skipping this field (likely from previous processing)");
continue; // Skip this field entirely
```

### Testing Required

**To verify this fix works:**

1. **Delete any cached/processed versions** of vr1307-twc.docx from the system
2. **Process vr1307-twc.docx fresh** using Syncfusion + Claude Vision mode
3. **Check the field positions** - address fields should appear in their correct location (middle/bottom of page)
4. **Look for log entries** - should see `[TAG-STRUCTURE-SKIP]` instead of `[TAG-STRUCTURE-ERROR]` for legacy fields
5. **Verify no ghost fields** appear at top of page

**Expected behavior:**
- Address fields appear in correct locations
- No ghost fields at top of page
- Logs show skipped legacy fields with `[TAG-STRUCTURE-SKIP]`

### User Feedback
> "it still isn't fixed"

**Note:** Fix was applied and server rebuilt, but user hasn't tested with fresh document processing yet. The fix will only work on **newly processed documents** - it won't fix documents that were already processed with the old code.

---

## 📋 TODO List

### High Priority

1. **[ ] VERIFY GHOST FIELD FIX**
   - **Action:** User needs to process vr1307-twc.docx fresh to verify fix
   - **Expected:** Ghost fields should no longer appear at top of page
   - **Log marker:** Look for `🚫 [TAG-STRUCTURE-SKIP]` entries
   - **File:** Program.cs lines 3344-3345, 3377-3378, 3407-3409, 3437-3439

2. **[ ] FIX CLICK-TO-SELECT FIELDS**
   - **Problem:** Clicking field boxes in PDF preview doesn't select/scroll to that field in the table
   - **Expected behavior:** Click colored box → highlight and scroll to that field in table
   - **User feedback:** "the 'click a field on the pdf preview' aka 'click the pretty colored box' isn't working"
   - **Files to investigate:**
     - `Pages/Index.razor` - PDF preview component
     - `Pages/TagModificationModal.razor` - Field table component
     - JavaScript interop for click handling

### Medium Priority

3. **[ ] CASCADE CORRECTION - VERIFY FIELD UPDATES**
   - **Note:** Cascade correction field ordering was fixed in previous session (Y descending sort)
   - **Action:** Verify that changes in cascade correction panel propagate to both:
     - The field list table
     - The PDF preview overlays
   - **User mention:** "we will need to make corrections to the list and to the preview"

### Low Priority / Future Enhancements

4. **[ ] IMPROVE SEQUENTIAL MODE LOGGING**
   - Add more detailed logging for field matching in Sequential mode
   - Log why specific matches were made or rejected
   - File: `Services/ConfigurableFieldDetectionService.cs`

5. **[ ] DOCUMENT COORDINATE SYSTEMS**
   - Create visual diagram showing coordinate system conversions
   - PDF (72 DPI, bottom-left) vs Display (150 DPI, top-left)
   - File: `Services/PdfCoordinateConverter.cs`

---

## 🐛 Known Issues (Not Actively Being Fixed)

### Click-to-Select Fields
- **Status:** Reported but not yet investigated
- **Impact:** Medium - UX inconvenience, not a blocker
- **File:** Likely `Pages/Index.razor` or JavaScript interop

### Spurious Syncfusion Ghost Fields
- **Status:** User mentioned "spurious syncfusion ghosts" in earlier conversation
- **Note:** Different from coordinate bug - these are fields Syncfusion detects that shouldn't exist
- **Workaround:** Claude Validate removes them, or manual deletion
- **Impact:** Low - handled by existing tools

---

## 🔍 Debugging Tips for Ghost Fields

If ghost fields appear after the fix:

1. **Check the logs** for field processing:
   ```bash
   grep "TAG-STRUCTURE" Logs/accessform.log | tail -50
   ```

2. **Look for these markers:**
   - `🚨 [TAG-STRUCTURE-TEXT]` - Processing text field from tags
   - `🚫 [TAG-STRUCTURE-SKIP]` - Skipping field without tooltip (GOOD)
   - `🚫 [TAG-STRUCTURE-ERROR]` - Falling back to page 1 (BAD - shouldn't happen after fix)

3. **Check for [PAGE:X] tooltips:**
   - Fields from current system have tooltips like `[PAGE:1]`, `[PAGE:2]`
   - Legacy fields from old processing don't have these tooltips
   - After fix, legacy fields should be skipped entirely

4. **Verify coordinate system:**
   - PDF coordinates: Bottom-left origin (Y=0 at bottom)
   - Display coordinates: Top-left origin (Y=0 at top)
   - Look for coordinate conversion in `PdfCoordinateConverter.cs`

5. **Check which detection mode was used:**
   - Syncfusion alone: Uses PDF field objects (accurate)
   - Claude alone: Uses vision API (may have coordinate issues)
   - Syncfusion + Claude: Uses Syncfusion coords + Claude labels (should be accurate)

---

## 📞 For Next Session

When starting a new conversation, review:

1. **SYSTEM_OVERVIEW.md** - Overall system architecture and current state
2. **This file (CURRENT_ISSUES.md)** - Active bugs and pending tasks
3. **Git status** - Current branch and uncommitted changes
4. **Recent logs** - Check for errors or warnings

### Quick Commands
```bash
# Check git status
git status

# View recent logs
tail -100 Logs/accessform.log

# Search for specific log markers
grep "TAG-STRUCTURE-SKIP" Logs/accessform.log
grep "SEQUENTIAL_MODE" Logs/accessform.log

# Start server
cd "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"
ASPNETCORE_URLS="http://localhost:5001" dotnet run --project AccessFormServer.csproj --nologo --verbosity quiet
```

---

**Last User Feedback:** "it still isn't fixed" - Fix has been applied but needs verification with fresh document processing.
