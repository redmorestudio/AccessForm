# Pikepdf Modular Structure System

Complete replacement for iText7 using pikepdf (open source, MPL 2.0).

## Architecture

Highly modular design for easy debugging and testing.

### Core Modules (`Core/`)

**pdf_utils.py** - Basic PDF I/O operations
- Open/save PDFs
- Validate structure tree
- Get PDF info

**content_parser.py** - Content stream parsing
- Parse content streams into operators
- Find specific operators
- Extract operator ranges

### Structure Tree Modules (`Structure/`)

**tree_cleaner.py** - Remove old structure
- Clean old StructTreeRoot
- Remove MarkInfo
- Clean page StructParents

**element_builder.py** - Build elements
- Create structure elements from model
- Build StructTreeRoot
- Add RoleMap

**hierarchy_builder.py** - Connect relationships
- Build parent-child hierarchy
- Validate structure tree
- Count elements

### MCID Modules (`Mcid/`)

**segment_detector.py** - Detect content segments
- Find BT/ET text blocks
- Find Do image operators
- Filter by segment type

**allocator.py** - Allocate MCID numbers
- Assign sequential MCIDs
- Create MCID-to-segment mapping
- Maintain reading order

**marker_inserter.py** - Insert BDC/EMC markers
- Insert markers into content streams
- Rebuild marked content
- Verify marker pairs

**mcr_builder.py** - Create MCR kids
- Add MCR dictionaries to structure elements
- Link structure to content
- Verify MCR kids

### Orchestrator

**orchestrator.py** - Main coordinator
- Coordinates all modules
- Manages workflow
- CLI entry point

## Usage

### From Command Line

```bash
python3 orchestrator.py input.pdf output.pdf structure.json
```

### From Python

```python
from orchestrator import PikepdfOrchestrator

orchestrator = PikepdfOrchestrator(enable_mcid=True)
result = orchestrator.rebuild_structure(
    'input.pdf',
    'output.pdf',
    structure_json_string
)

print(f"Success: {result['success']}")
print(f"Elements: {result['elements_created']}")
print(f"MCR kids: {result['mcr_kids_created']}")
```

### From C#

```csharp
// TODO: Create C# wrapper service
```

## Testing

Test module imports:
```bash
python3 test_modules.py
```

Individual module tests:
```bash
python3 Core/pdf_utils.py
python3 Mcid/segment_detector.py
# etc.
```

## Module Testing

Each module has standalone test capability:
- Import test at bottom of file
- Can run individually: `python3 <module>.py`
- No dependencies on other modules

## Benefits vs iText7

✅ **Free** - No licensing costs (MPL 2.0)
✅ **Modular** - Easy to debug individual pieces
✅ **Reliable** - Direct PDF manipulation, no validation interference
✅ **Transparent** - See exactly what's written
✅ **Testable** - Each module tests independently

## Next Steps

1. Create C# wrapper service
2. Test with real PDFs
3. Replace ITextPdfStructureWriter
4. Remove iText7 dependency

## Structure JSON Format

Expected StructureTree JSON format:

```json
{
  "nodes": [
    {
      "id": "/0",
      "role": "H1",
      "alt": null,
      "actual_text": null,
      "lang": null,
      "children": [
        {
          "id": "/0/0",
          "role": "P",
          "children": []
        }
      ]
    }
  ]
}
```

## Logging

All modules use Python logging:
- `[MODULE-NAME]` prefix for filtering
- `INFO` level for major operations
- `DEBUG` level for detailed traces
- `WARNING` for non-fatal issues
- `ERROR` for failures

Filter logs by module:
```bash
python3 orchestrator.py ... 2>&1 | grep "\[MCID-"
```
