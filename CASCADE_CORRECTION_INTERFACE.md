# Interactive Cascade Correction Interface Design

## Core Problem
- Syncfusion detects accurate field positions but has poor naming (e.g., "text332aba")
- Claude Vision provides good names but may miss fields or create phantoms
- When phantom fields exist, all subsequent field names get shifted ("off by one" error)
- Multiple cascade points can exist in a single document

## Text-Based Interface Solution

### Display Format
```
═══════════════════════════════════════════════════════════════════════════════
                     FIELD CASCADE CORRECTION - Page 1
═══════════════════════════════════════════════════════════════════════════════

#   Y,X        Current Name                Type    Status      Near Text
--- ---------- ---------------------------- ------- ----------- ----------------
1   120,50     Customer's name:             text    ✓ OK        "Customer's name:"
2   120,250    text332aba                   text    ⚠ PHANTOM   [no label]
3   180,50     Customer's name:             text    ⚡ CASCADE  "Case ID number:"
4   240,50     Case ID number:              text    ⚡ CASCADE  "Services to be"
5   300,50     Services to be delivered:    text    ⚡ CASCADE  "Under the prov"
6   420,100    Contract number:             text    ✓ OK        "Contract number"
7   420,350    $                           text    ✓ OK        "amount of $"
8   480,50     Hospital rep name:          text    ⚠ DUP NAME  "Hospital rep"
9   540,50     Hospital rep name:          text    ⚠ DUP NAME  "Date:"
10  540,250    Hospital rep signature:     text    ⚡ CASCADE  "Hospital rep"

Legend: ✓ OK | ⚠ ISSUE | ⚡ CASCADE AFFECTED | 🚫 PHANTOM
```

### Commands

#### Basic Operations
- `phantom <#>` - Mark field as phantom (will be removed)
- `cascade <#>` - Start cascade from this field
- `cascade <#-#>` - Apply cascade to range
- `swap <#> <#>` - Swap two field names
- `rename <#> "new name"` - Manually rename a field

#### Bulk Operations
- `auto` - Auto-detect phantom fields and cascades
- `preview` - Show before/after comparison
- `apply` - Execute all corrections
- `undo` - Revert last change
- `reset` - Start over

#### Analysis
- `duplicates` - Highlight duplicate names
- `phantoms` - Suggest likely phantom fields
- `validate` - Check for common issues
- `compare` - Show Claude vs Syncfusion comparison

### Smart Features

#### Pattern Detection
```
DETECTED PATTERNS:
- Phantom field at position 2 (no nearby label text)
- Off-by-one cascade starting at field 3
- Duplicate names at fields 8-9
- Field 10 name doesn't match nearby text
```

#### Cascade Preview
```
CASCADE CORRECTION PREVIEW:
Remove phantom at #2, cascade from #3:

Before                          → After
--------------------------------  --------------------------------
3: Customer's name:             → Case ID number:
4: Case ID number:              → Services to be delivered:
5: Services to be delivered:    → Contract number:
9: Hospital rep name:           → Date:
10: Hospital rep signature:     → Hospital rep signature: (X)
```

### Implementation Strategy

#### Phase 1: Core Interface
- Text table display with spatial sorting
- Basic commands (phantom, cascade, preview, apply)
- Session storage for undo/redo

#### Phase 2: Smart Detection
- Auto-detect Syncfusion phantom fields (text###abc patterns)
- Identify duplicate names
- Suggest cascade points based on text proximity analysis

#### Phase 3: Learning System
- Save correction patterns per form type
- Apply learned patterns to similar forms
- Export/import correction templates

### API Design

```csharp
// GET /api/cascade-correction/{sessionId}
// Returns current field table

// POST /api/cascade-correction/{sessionId}/command
{
    "command": "phantom",
    "args": ["2"]
}

// POST /api/cascade-correction/{sessionId}/apply
// Applies all corrections and returns corrected fields

// WebSocket option for real-time updates
// ws://localhost:5002/ws/cascade-correction/{sessionId}
```

### User Workflow

1. Upload form → Fields detected
2. System shows field table with issues highlighted
3. User identifies phantoms: `phantom 2`
4. System suggests cascade: "This will cascade fields 3-10"
5. User previews: `preview`
6. User applies: `apply`
7. System returns corrected fields

### Advanced Features

#### Multi-Cascade Support
```
CASCADE ZONES DETECTED:
Zone 1: Fields 3-5 (triggered by phantom at 2)
Zone 2: Fields 9-12 (triggered by phantom at 8)

Command: cascade-zones auto
```

#### Visual Feedback in PDF
- Add semi-transparent overlay showing:
  - Red X on phantom fields
  - Yellow arrows showing cascade flow
  - Green checkmarks on correct fields

#### Confidence Scoring
```
#   Field Name              Confidence  Suggestion
3   Customer's name:        LOW (23%)   → Case ID number: (87%)
4   Case ID number:         LOW (31%)   → Services: (92%)
```

### Benefits of Text-Based Approach

1. **Speed**: Keyboard-driven, no mouse needed
2. **Clarity**: See all fields at once in logical order
3. **Precision**: Exact control over corrections
4. **Scriptable**: Can automate common patterns
5. **Accessibility**: Works with screen readers
6. **Version Control**: Text diffs show exactly what changed
7. **Remote-Friendly**: Works over SSH/terminal

### Future Enhancements

1. **Machine Learning**: Learn cascade patterns per form type
2. **Batch Processing**: Apply saved patterns to multiple forms
3. **Integration**: Direct integration with existing GUI
4. **Export**: Generate correction reports for audit trail
5. **Templates**: Save/load correction templates
6. **Collaboration**: Multiple users can review corrections