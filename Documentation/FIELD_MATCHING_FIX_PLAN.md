# Field Matching Fix Plan

## Problem Summary
- Syncfusion detects phantom/spurious fields (false positives)
- These phantom fields cause "off by one" naming errors where all subsequent fields get wrong names
- Example: Contract ID gets "Contract Amount" name, Contract Amount gets "Representatives Of" name, etc.
- Current index-based matching breaks when there are phantom fields

## Solution Implementation Plan

### Phase 0: COMMIT CURRENT STATE
```bash
git add -A && git commit -m "Checkpoint before field matching fixes"
```

### Phase 1: Improve Phantom Field Detection
**File: ConfigurableFieldDetectionService.cs - IsLikelyPhantomField method**

1. **Better detection of gibberish Syncfusion names:**
   - Check for 12-character hex-like strings (e.g., "a1bb168e5744")
   - These are Syncfusion's auto-generated field IDs
   - Example detection code:
   ```csharp
   bool hasGibberishName = field.FieldName?.Length == 12 &&
                          field.FieldName.All(c => "0123456789abcdef".Contains(char.ToLower(c)));
   ```

2. **Skip phantom fields during matching:**
   - Don't assign Claude labels to phantom fields
   - This prevents the "off by one" cascade

3. **COMMIT:**
```bash
git add -A && git commit -m "Improve phantom field detection to skip 12-char hex IDs"
```

### Phase 2: Convert Claude Coordinates Properly (CAREFUL!)
**File: ConfigurableFieldDetectionService.cs - EnhanceFieldsWithClaudeLabels**

1. **Convert Claude's percentage coordinates to actual PDF coordinates:**
   ```csharp
   // Claude gives percentages with TOP-LEFT origin
   float x = (vField.Bounds.XPercent / 100f) * pageWidth;
   float yFromTop = (vField.Bounds.YPercent / 100f) * pageHeight;

   // CRITICAL: Convert Y from top-left to bottom-left origin
   float height = (vField.Bounds.HeightPercent / 100f) * pageHeight;
   float y = pageHeight - yFromTop - height;  // NOT just pageHeight - yFromTop!

   float width = (vField.Bounds.WidthPercent / 100f) * pageWidth;
   ```

2. **Use proximity matching when we have valid coordinates**
3. **Fall back to index matching only when coordinates are 0,0**

4. **COMMIT:**
```bash
git add -A && git commit -m "Fix Claude coordinate conversion - properly convert from top-left to bottom-left origin"
```

### Phase 3: Enhance Claude Verification
**File: ClaudeBoundingBoxValidator.cs**

Expand the verification to detect:
1. **Off-by-one naming errors** - "Is 'Contract Amount' actually at the Contract ID position?"
2. **Phantom fields** - "Which fields are false positives?"
3. **Name corrections** - "What should each field actually be named?"

Add this to the prompt:
```
Also verify the field NAMES:
1. Does each field's name match the text/label near it?
2. Are there "off by one" naming errors where all names are shifted?
3. Which fields appear to be false positives (phantoms)?

Return additional verification:
{
  "naming_verifications": [{
    "id": "field_id",
    "current_name": "Contract Amount",
    "is_correctly_named": false,
    "suggested_name": "Contract ID",
    "nearby_text": "Contract number ___",
    "is_phantom": false
  }],
  "off_by_one_detected": true,
  "phantom_field_ids": ["id1", "id2"]
}
```

4. **COMMIT:**
```bash
git add -A && git commit -m "Enhance Claude verification to detect naming errors and phantoms"
```

### Phase 4: Add Field Deletion Support
**File: FieldEditor.razor**

1. Add delete button to field rows
2. Track deleted field IDs
3. When processing final PDF, skip deleted fields
4. Store deletions in session/config for reprocessing

5. **COMMIT:**
```bash
git add -A && git commit -m "Add field deletion support in UI"
```

## Implementation Order with Safety
1. **Commit checkpoint** (so we can revert everything if needed)
2. Fix phantom detection → **Commit**
3. Carefully fix coordinate conversion → **Commit**
4. Enhance Claude verification → **Commit**
5. Add deletion UI → **Commit**

## Critical Coordinate Notes
- PDF: Bottom-left origin, Y increases UPWARD
- Claude/Display: Top-left origin, Y increases DOWNWARD
- Conversion: `pdfY = pageHeight - displayY - fieldHeight`
- NEVER just use `pageHeight - displayY` alone!

## Reprocessing Strategy (from earlier discussion)

Instead of trying to edit PDFs in place, use a two-phase approach:

### Phase 1 - Detection & Preview
Show all fields (including phantoms) with best-guess names

### Phase 2 - Correction & Finalization
User reviews, deletes phantoms, fixes names, then we regenerate:

```csharp
public class FieldCorrectionOverlay
{
    public HashSet<string> DeletedFieldIds { get; set; }
    public Dictionary<string, string> RenamedFields { get; set; }
    public List<string> FieldProcessingOrder { get; set; }
}

public async Task<byte[]> RegeneratePdfWithCorrections(
    byte[] originalPdf,
    FieldCorrectionOverlay corrections)
{
    // Re-detect or use cached detection results
    var syncFields = GetCachedOrRedetect(originalPdf, "Syncfusion");
    var claudeFields = GetCachedOrRedetect(originalPdf, "Claude");

    // Apply corrections
    syncFields = syncFields.Where(f => !corrections.DeletedFieldIds.Contains(f.ShortId));

    // Re-match with corrections applied
    var matchedFields = EnhanceFieldsWithClaudeLabels(syncFields, claudeFields);

    // Apply manual renames
    foreach (var field in matchedFields)
    {
        if (corrections.RenamedFields.TryGetValue(field.ShortId, out var newName))
        {
            field.FieldName = newName;
        }
    }

    // Generate final PDF
    return GeneratePdfWithFields(originalPdf, matchedFields);
}
```

## Key Insight
Don't try to edit an already-generated PDF in place. Instead, treat the first pass as a preview and regenerate with corrections.

Each commit gives us a safe rollback point if something breaks.