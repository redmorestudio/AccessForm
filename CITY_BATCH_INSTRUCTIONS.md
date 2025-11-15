# City Batch Processing - Magical Instructions

This system automatically tags, processes, organizes, and reports on city PDF batches for remediation.

## Quick Start

```bash
# Step 1: Tag PDFs (do this BEFORE remediation)
python3 city_batch_processor.py "StateAssets/[State]/[City]" [CODE] tag

# Step 2: Run your remediation service on the tagged PDFs
# (Your existing remediation process)

# Step 3: Organize results (do this AFTER remediation completes)
python3 city_batch_processor.py "StateAssets/[State]/[City]" [CODE] organize
```

## Detailed Workflow

### Prerequisites

1. **Source PDFs** in `StateAssets/[State]/[City]/`
2. **City code** (3 letters, e.g., CEN for Centennial, ALX for Alexandria)
3. **No files over 25MB** (move to `_too_large/` subfolder)

### Step 1: Tag Source PDFs

```bash
python3 city_batch_processor.py "StateAssets/Colorado/Centennial" CEN tag
```

**What this does:**
- Adds `CEN_` prefix to all PDFs in the city folder
- Prepares PDFs for batch remediation
- Skips any files in `_too_large/` folder

**Example:**
- `building-permit.pdf` → `CEN_building-permit.pdf`

### Step 2: Run Remediation

Process the tagged PDFs through your remediation service. The service will:
- Read `CEN_*.pdf` files
- Output to `remediation-best/CEN_*_best_iter*.pdf`
- Generate violation reports for each iteration

### Step 3: Check Status (Optional)

```bash
python3 city_batch_processor.py "StateAssets/Colorado/Centennial" CEN check
```

**Output:**
```
Completion Status:
  Expected: 21 documents
  Completed: 18 documents
  Status: ⏳ In Progress
```

### Step 4: Organize Results

Once all PDFs complete remediation:

```bash
python3 city_batch_processor.py "StateAssets/Colorado/Centennial" CEN organize
```

**What this does:**

1. **Collects all iteration data** (iter0, iter1, iter2, iter3, etc.)
2. **Generates comprehensive report** showing iteration-by-iteration progress
3. **Categorizes documents** into tiers:
   - `100 percent compliant/`
   - `99 percent (nearly complete)/`
   - `97-98 percent (minor work)/`
   - `95-96 percent (moderate work)/`
   - `below 95 percent (significant work)/`
4. **Copies PDFs and violation reports** to tier folders
5. **Strips city code** from filenames (e.g., `CEN_doc.pdf` → `doc.pdf`)

## Report Format

The generated report shows:

```
Document                                            Iter 0     Iter 1     Iter 2     Iter 3      FINAL       %
------------------------------------------------------------------------------------------------------------------
OSTPSchoolYrFO2425EngFillableR3                        159        159        159        159        159    95.3%
KingCallahanRussellFinalPlans                          107        107        107        107        107    96.2%
building-permit-fee-schedule-ada                        45         12          4          1          1    99.1%
...

========================================
TIER: 100 PERCENT COMPLIANT
========================================
[Documents that reached 100% compliance]

========================================
TIER: 99 PERCENT (NEARLY COMPLETE)
========================================
[Documents at 99.1% compliance]
```

## Example: Centennial, Colorado

```bash
# 1. Tag
python3 city_batch_processor.py \
  "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/StateAssets/Colorado/Centennial" \
  CEN \
  tag

# 2. Run remediation service (your existing process)
# ...

# 3. Organize when complete
python3 city_batch_processor.py \
  "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/StateAssets/Colorado/Centennial" \
  CEN \
  organize
```

## City Codes Reference

| City | State | Code |
|------|-------|------|
| Alexandria | Virginia | ALX |
| Centennial | Colorado | CEN |
| Burlingame | California | BUR |
| Pleasant Hill | California | PLH |
| Green Bay | Wisconsin | GRB |

## File Size Limits

**Maximum:** 25MB per PDF

**Handling large files:**
```bash
# Create exclusion folder
mkdir "StateAssets/[State]/[City]/_too_large"

# Move large files
mv large-file.pdf "_too_large/"
```

Files in `_too_large/` are automatically skipped by the processor.

## Output Structure

After organization, your city folder will look like:

```
StateAssets/Colorado/Centennial/
├── _too_large/
│   └── huge-document.pdf
├── 100 percent compliant/
│   ├── doc1_best_iter3_20251103.pdf
│   └── doc1_best_iter3_20251103_violations.txt
├── 99 percent (nearly complete)/
│   ├── doc2_best_iter3_20251103.pdf
│   ├── doc2_best_iter0_20251103_violations.txt
│   ├── doc2_best_iter1_20251103_violations.txt
│   ├── doc2_best_iter2_20251103_violations.txt
│   └── doc2_best_iter3_20251103_violations.txt
├── 97-98 percent (minor work)/
├── 95-96 percent (moderate work)/
└── centennial_remediation_report_20251103_083045.txt
```

## Troubleshooting

### "Expected X documents, completed Y documents"

Some PDFs may still be processing. Wait for all to complete, then run `organize` again.

### "No PDFs found to tag"

Check that:
- PDFs are in the correct city folder
- PDFs don't already have the city code prefix
- PDFs aren't in `_too_large/` folder

### Files not being organized

Ensure remediation output is in `remediation-best/` with pattern:
- PDFs: `[CODE]_[docname]_best_iter[N]_[timestamp].pdf`
- Reports: `[CODE]_[docname]_best_iter[N]_[timestamp]_violations.txt`

## Advanced Usage

### Dry Run Mode

Test without making changes:

```python
from city_batch_processor import CityBatchProcessor

processor = CityBatchProcessor("StateAssets/Colorado/Centennial", "CEN")
processor.tag_source_pdfs(dry_run=True)
processor.organize_files(tiers, dry_run=True)
```

### Custom Base Directory

```python
processor = CityBatchProcessor(
    "StateAssets/Colorado/Centennial",
    "CEN",
    base_dir="/custom/path"
)
```

## What Makes This Magical?

1. **Automatic tagging** - No manual renaming
2. **Iteration tracking** - See progress across all remediation passes
3. **Smart categorization** - Documents auto-sort into complexity tiers
4. **Tag stripping** - Final files have clean names
5. **Comprehensive reporting** - Full visibility from start to finish
6. **Repeatable process** - Same workflow for every city

## Next Steps

1. Tag your Centennial PDFs (done! ✓)
2. Run remediation on `CEN_*.pdf` files
3. Come back and run `organize` command
4. Review the beautiful report and organized folders
5. Repeat for the next city!
