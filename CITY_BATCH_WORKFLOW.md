# City Batch Remediation Workflow

Complete step-by-step guide for processing PDF batches for any city.

---

## Prerequisites

Before starting any city batch:

### 1. Server Running
```bash
# Ensure remediation server is running on port 5008
ASPNETCORE_URLS="http://localhost:5008" dotnet run --project AccessFormServer.csproj --nologo --verbosity quiet
```

### 2. Clean Source PDFs
- PDFs located in `StateAssets/[State]/[City]/`
- Remove duplicate files
- Move files >25MB to `_too_large/` subfolder

### 3. Choose City Code
3-letter code that's unique and meaningful:
- **CEN** = Centennial, CO
- **ALX** = Alexandria, VA
- **BUR** = Burlingame, CA
- **PLH** = Pleasant Hill, CA
- **GRB** = Green Bay, WI

---

## Complete Workflow (3 Steps)

### STEP 1: Tag Source PDFs

**Purpose:** Prefix all city PDFs with city code for batch tracking

```bash
python3 city_batch_processor.py "StateAssets/[State]/[City]" [CODE] tag
```

**Example:**
```bash
python3 city_batch_processor.py "StateAssets/Colorado/Centennial" CEN tag
```

**What it does:**
- Scans city folder for all `.pdf` files
- Renames each: `document.pdf` → `CEN_document.pdf`
- Skips files already tagged or in `_too_large/`

**Output:**
```
======================================================================
STEP 1: Tagging 21 PDFs with prefix 'CEN_'
======================================================================

  ✓ Tagged: CEN_building-permit.pdf
  ✓ Tagged: CEN_zoning-application.pdf
  ...

✓ Step 1 complete. PDFs tagged and ready for remediation.
```

---

### STEP 2: Run Batch Remediation

**Purpose:** Submit all tagged PDFs to remediation service

#### Option A: Use Provided Script

**For Centennial:**
```bash
./process_centennial_batch.sh
```

**For Any Other City:** Create city-specific script:

```bash
#!/bin/bash

set -e

CITY_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/StateAssets/[State]/[City]"
OUTPUT_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/remediation-best"
SERVER_PORT=5008
API_URL="http://localhost:${SERVER_PORT}/api/v2/remediation/start"

echo "========================================="
echo "[City Name] PDF Remediation - Batch Run"
echo "Started: $(date)"
echo "========================================="
echo ""

# Check if server is running
if ! curl -s "http://localhost:${SERVER_PORT}" > /dev/null 2>&1; then
    echo "❌ Server not running on port ${SERVER_PORT}"
    exit 1
fi

echo "✓ Server is running"
echo ""

# Process each PDF
cd "$CITY_DIR"
PDF_FILES=([CODE]_*.pdf)
TOTAL=${#PDF_FILES[@]}

echo "Found ${TOTAL} PDFs to process"
echo ""

SUCCESS_COUNT=0
FAIL_COUNT=0

for ((i=0; i<$TOTAL; i++)); do
    PDF="${PDF_FILES[$i]}"
    NUM=$((i+1))

    echo "========================================="
    echo "[$NUM/$TOTAL] Processing: $PDF"
    echo "========================================="

    PDF_PATH="${CITY_DIR}/${PDF}"

    TEMP_FILE=$(mktemp)
    HTTP_CODE=$(curl -s -w "%{http_code}" -o "$TEMP_FILE" -X POST \
        -F "file=@${PDF_PATH}" \
        "${API_URL}" 2>&1 | tail -n1)

    BODY=$(cat "$TEMP_FILE")
    rm -f "$TEMP_FILE"

    if [ "$HTTP_CODE" = "200" ]; then
        echo "✓ Success: $PDF"
        SUCCESS_COUNT=$((SUCCESS_COUNT + 1))
    else
        echo "❌ Failed: $PDF (HTTP $HTTP_CODE)"
        FAIL_COUNT=$((FAIL_COUNT + 1))
    fi

    echo ""
    sleep 2
done

echo ""
echo "========================================="
echo "BATCH COMPLETE"
echo "========================================="
echo "Results:"
echo "  Processed: ${SUCCESS_COUNT}/${TOTAL}"
echo "  Failed: ${FAIL_COUNT}"
echo ""
echo "Next: python3 city_batch_processor.py \"${CITY_DIR}\" [CODE] organize"
```

Save as `process_[city]_batch.sh`, make executable:
```bash
chmod +x process_[city]_batch.sh
```

#### Option B: Manual API Calls

For each PDF, send POST request:
```bash
curl -X POST \
  -F "file=@StateAssets/State/City/CEN_document.pdf" \
  http://localhost:5008/api/v2/remediation/start
```

**What happens during remediation:**
- Service receives PDF
- Validates with VeraPDF (iter0)
- Applies remediation fixes
- Re-validates (iter1, iter2, iter3...)
- Continues until compliant or max iterations
- Outputs to `remediation-best/`:
  - `CEN_document_best_iter0_*.pdf` (initial)
  - `CEN_document_best_iter0_*_violations.txt`
  - `CEN_document_best_iter3_*.pdf` (final)
  - `CEN_document_best_iter3_*_violations.txt`

**Typical processing time:**
- Simple forms: 10-30 seconds per PDF
- Complex documents: 1-3 minutes per PDF
- 20 PDFs: 5-15 minutes total

---

### STEP 3: Check Status (Optional)

**While processing, check completion:**

```bash
python3 city_batch_processor.py "StateAssets/[State]/[City]" [CODE] check
```

**Example:**
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

Wait until all show as complete:
```
Completion Status:
  Expected: 21 documents
  Completed: 21 documents
  Status: ✓ COMPLETE
```

---

### STEP 4: Organize Results

**Purpose:** Generate reports and organize into tier folders

```bash
python3 city_batch_processor.py "StateAssets/[State]/[City]" [CODE] organize
```

**Example:**
```bash
python3 city_batch_processor.py "StateAssets/Colorado/Centennial" CEN organize
```

**What it does:**

1. **Collects iteration data** from all violation reports
2. **Generates comprehensive report** with iteration-by-iteration progress
3. **Categorizes documents** into compliance tiers
4. **Creates tier folders** in city directory
5. **Copies PDFs and reports** to appropriate tier folders
6. **Strips city codes** from filenames

**Output Structure:**

```
StateAssets/Colorado/Centennial/
├── _too_large/
│   └── huge-appendix.pdf (429MB)
│
├── 100 percent compliant/
│   ├── building-permit_best_iter3.pdf
│   ├── building-permit_best_iter0_violations.txt
│   ├── building-permit_best_iter3_violations.txt
│   └── ... (16 documents × 2-3 files each)
│
├── 99 percent (nearly complete)/
│   ├── zoning-form_best_iter3.pdf
│   └── ... (violation reports for each iteration)
│
├── 97-98 percent (minor work)/
│   └── ...
│
├── 95-96 percent (moderate work)/
│   └── ...
│
├── below 95 percent (significant work)/
│   └── ...
│
└── centennial_remediation_report_20251103_113520.txt
```

**Report Format:**

The report now contains **TWO SEPARATE TABLES** with different tier classifications:

```
============================================================================
CENTENNIAL, COLORADO - PDF REMEDIATION REPORT
============================================================================
Generated: 2025-11-03 11:35:20
City Code: CEN
Total Documents: 21

OVERALL SUMMARY
----------------------------------------------------------------------------
  Initial Violations (Iteration 0): 1,847
  Final Violations: 245
  Violations Fixed: 1,602 (86.7% reduction)

IMPORTANT: TWO DIFFERENT METRICS
----------------------------------------------------------------------------
This report contains TWO tables with different tier classifications:

TABLE 1: RULES-BASED COMPLIANCE (VeraPDF Metric)
  - Shows what % of PDF/UA rules passed (NOT violation counts)
  - A document with 2 failed rules but 100 violations shows ~98% compliant
  - This is VeraPDF's standard compliance score

TABLE 2: VIOLATIONS-BASED REMEDIATION PROGRESS (Actual Work Done)
  - Shows actual violations fixed: (Initial - Final) / Initial × 100
  - Example: 311 violations → 100 remaining = 67.8% fixed
  - This reflects the real remediation work accomplished

KEY INSIGHT: A document can be "99% compliant" by rules but have fixed
only 5% of violations. Use Table 2 to understand actual remediation progress.


============================================================================
TABLE 1: RULES-BASED COMPLIANCE (VeraPDF Rule Pass/Fail Ratio)
============================================================================
Note: This shows what % of PDF/UA rules passed, NOT violation counts.

TIER: 100 PERCENT COMPLIANT
----------------------------------------------------------------------------
Document                                   Iter 0     Iter 1     Iter 2     Iter 3    Rule %
------------------------------------------------------------------------------------------------------------
building-permit-fee-schedule-ada           92.5%      95.3%      98.1%     100.0%    100.0%
adopt-a-street-application                 94.3%      96.2%      99.1%     100.0%    100.0%
cdoc-general-notes-2022                    93.4%      97.2%     100.0%     100.0%    100.0%
------------------------------------------------------------------------------------------------------------
TIER TOTAL: 3 documents

TIER: 99 PERCENT (NEARLY COMPLETE)
----------------------------------------------------------------------------
Document                                   Iter 0     Iter 1     Iter 2     Iter 3    Rule %
------------------------------------------------------------------------------------------------------------
residential-patio-covers-ada               91.5%      93.4%      95.3%      99.1%     99.1%
str-inspection-checklist-ada               92.5%      94.3%      96.2%      99.1%     99.1%
...


============================================================================
TABLE 2: VIOLATIONS-BASED REMEDIATION PROGRESS (Actual Violation Counts)
============================================================================
Note: This shows actual violations fixed: (Initial - Final) / Initial × 100

TIER: 100% VIOLATIONS FIXED (FULLY REMEDIATED)
----------------------------------------------------------------------------
Document                                   Iter 0     Iter 1     Iter 2     Iter 3    FINAL    Fixed %
------------------------------------------------------------------------------------------------------------
building-permit-fee-schedule-ada              158         45         12          1        0     100.0%
adopt-a-street-application                     87         34          8          0        0     100.0%
cdoc-general-notes-2022                        56         12          0          0        0     100.0%
------------------------------------------------------------------------------------------------------------
TIER TOTAL: 3 documents                       301         91         20          1        0

TIER: 90-99% VIOLATIONS FIXED (NEARLY COMPLETE)
----------------------------------------------------------------------------
Document                                   Iter 0     Iter 1     Iter 2     Iter 3    FINAL    Fixed %
------------------------------------------------------------------------------------------------------------
some-document                                 150        120         80         15       15      90.0%
...

TIER: 70-89% VIOLATIONS FIXED (MAJOR PROGRESS)
----------------------------------------------------------------------------

TIER: 50-69% VIOLATIONS FIXED (MODERATE PROGRESS)
----------------------------------------------------------------------------
Document                                   Iter 0     Iter 1     Iter 2     Iter 3    FINAL    Fixed %
------------------------------------------------------------------------------------------------------------
residential-patio-covers-ada                  234        180        145        100      100      57.3%
...

TIER: 25-49% VIOLATIONS FIXED (MINOR PROGRESS)
----------------------------------------------------------------------------

TIER: BELOW 25% VIOLATIONS FIXED (MINIMAL PROGRESS)
----------------------------------------------------------------------------
Document                                   Iter 0     Iter 1     Iter 2     Iter 3    FINAL    Fixed %
------------------------------------------------------------------------------------------------------------
complex-document                              512          -          -          -      512       0.0%
...


============================================================================
COST SUMMARY
============================================================================

  Total Cost (All Documents): $45.8762
  Average Cost per Document: $2.1846
  Number of Documents Processed: 21

  Cost Breakdown by Service:
    - Anthropic Claude Sonnet 4.5: $38.4523 (83.8%)
    - OpenAI GPT-5: $5.2189 (11.4%)
    - OpenAI GPT-4o: $2.2050 (4.8%)

  Most Expensive Document:
    complex-document: $8.2341

  Least Expensive Document:
    simple-form: $0.4512

  Per-Document Costs:
    building-permit-fee-schedule-ada: $2.4523
    adopt-a-street-application: $1.8762
    cdoc-general-notes-2022: $1.2341
    residential-patio-covers-ada: $3.5678
    str-inspection-checklist-ada: $2.9874
    some-document: $1.7621
    complex-document: $8.2341
    simple-form: $0.4512
    ... (remaining documents)
```

**Key Report Features:**

- **TWO INDEPENDENT TABLES** - Rules-based and violations-based metrics
- **Different tier classifications** for each table
- **Iteration-by-iteration progress** showing how violations decreased
- **FINAL column** aligned across all documents for easy scanning
- **Fixed percentage** shows actual remediation progress
- **Tier grouping** by both VeraPDF compliance AND actual work done
- **Summary statistics** for each tier and overall
- **COST BREAKDOWN** - Total AI costs, per-document costs, service breakdown

**Understanding the Two Metrics:**

1. **TABLE 1 (Rules-Based)**: Shows VeraPDF's standard compliance metric
   - Based on how many PDF/UA rules passed
   - A single failed rule can have many violations
   - Documents grouped by: 100%, 99%, 97-98%, 95-96%, below 95%

2. **TABLE 2 (Violations-Based)**: Shows actual remediation work
   - Based on percentage of violations fixed
   - More accurately reflects remediation effort
   - Documents grouped by: 100%, 90-99%, 70-89%, 50-69%, 25-49%, below 25%

**Why This Matters:**

A document showing "99% compliant" in Table 1 might only have fixed 5% of its
violations. This happens because a small number of rule failures can generate
many violations. Use Table 2 to understand which documents need more work.

**Understanding Cost Tracking:**

The cost summary tracks all AI service usage during the batch remediation:

- **Total Cost**: Sum of all AI API costs across all documents
- **Average Cost**: Total cost divided by number of documents processed
- **Service Breakdown**: Shows which AI service (Claude, GPT-5, GPT-4o) was used and how much
- **Per-Document Costs**: Individual cost for each document (useful for identifying expensive forms)
- **Most/Least Expensive**: Highlights cost outliers

**Cost Data Sources:**

Costs are calculated from actual token usage reported by AI APIs:
- **Anthropic Claude Sonnet 4.5**: $3/M input, $15/M output tokens
- **OpenAI GPT-5**: $1.25/M input, $10/M output tokens
- **OpenAI GPT-4o**: $2.50/M input, $10/M output tokens

Each document's cost is embedded in its `_violations.txt` report and aggregated
during the organize step. This allows you to track remediation costs and identify
which document types are most expensive to process.

---

## Complete Example: Processing a New City

### Scenario: Processing Burlingame, CA

```bash
# 0. Prerequisites
# - Server running on port 5008
# - PDFs in StateAssets/California/Burlingame/
# - Large files moved to _too_large/

# 1. Tag PDFs
python3 city_batch_processor.py \
  "StateAssets/California/Burlingame" \
  BUR \
  tag

# Output: ✓ Tagged 15 PDFs with prefix 'BUR_'

# 2. Create batch script (one-time setup)
# Edit process_centennial_batch.sh:
# - Change CITY_DIR to Burlingame path
# - Change city name in echo statements
# - Change PDF_FILES pattern to BUR_*.pdf
# Save as process_burlingame_batch.sh
chmod +x process_burlingame_batch.sh

# 3. Run batch remediation
./process_burlingame_batch.sh

# Output: ✓ Processed: 15/15, Failed: 0

# 4. Check status (optional, while processing)
python3 city_batch_processor.py \
  "StateAssets/California/Burlingame" \
  BUR \
  check

# Output: ✓ COMPLETE (15/15 documents)

# 5. Organize results
python3 city_batch_processor.py \
  "StateAssets/California/Burlingame" \
  BUR \
  organize

# Output:
# - Report generated: burlingame_remediation_report_*.txt
# - Files organized into 5 tier folders
# - City codes stripped from filenames
```

**Time estimate:** 15-20 minutes total

---

## Troubleshooting

### "No PDFs found to tag"

**Cause:** No untagged PDFs in city folder

**Solution:**
- Check you're in correct directory
- Verify PDFs exist: `ls StateAssets/[State]/[City]/*.pdf`
- Check if already tagged: look for `[CODE]_` prefix

### "Expected X, completed Y documents"

**Cause:** Remediation still processing

**Solution:**
- Wait for all iterations to complete
- Check server logs: `tail -100 Logs/accessform.log`
- Re-run organize when complete

### "HTTP 400" errors during batch

**Cause:** Wrong API endpoint or invalid PDF

**Solution:**
- Verify endpoint: `/api/v2/remediation/start`
- Check server is running: `curl http://localhost:5008`
- Validate PDF isn't corrupted: open in viewer
- Check file size: must be under 25MB

### Batch script hangs

**Cause:** Server may be processing very large document

**Solution:**
- Be patient with complex PDFs (can take 5+ minutes)
- Check `remediation-best/` for output files
- Monitor server logs for errors
- Kill and restart if truly stuck

### Files not organizing correctly

**Cause:** Missing or incomplete violation reports

**Solution:**
- Verify all PDFs have iter0 AND final iter reports
- Check filename patterns: `[CODE]_[name]_best_iter[N]_*`
- Re-run remediation for missing documents

### "99% compliant but still has 100 violations"

**Cause:** This is expected - VeraPDF's compliance percentage is based on rules, not violations

**Solution:**
- This is normal behavior, not an error
- A document with 2 failed rules (98% rules passed) can have 100 violations
- Check **TABLE 2** in the report to see actual violation reduction
- Example: Document showing "99.1% compliant" but only "4.8% violations fixed"
  means it passed 99% of rules but still needs major remediation work

**Understanding:**
- **TABLE 1** = What % of PDF/UA rules passed (VeraPDF's standard metric)
- **TABLE 2** = What % of actual violations were fixed (real work done)
- Use TABLE 2 to prioritize which documents need more work

---

## Best Practices

### 1. Clean Source Files First
```bash
# Remove duplicates
# Move files >25MB to _too_large/
# Verify all are valid PDFs
```

### 2. Use Meaningful City Codes
- 3 letters
- Abbreviation of city name
- No spaces or special characters
- Document in this guide

### 3. Test Small Batches First
For new cities, test with 2-3 PDFs:
```bash
# Move most PDFs temporarily
mkdir temp_hold
mv CEN_* temp_hold/
mv temp_hold/CEN_test1.pdf .
mv temp_hold/CEN_test2.pdf .

# Process small batch
./process_centennial_batch.sh

# Verify results, then process rest
mv temp_hold/* .
rmdir temp_hold
```

### 4. Keep Logs
All batch scripts generate logs:
```bash
# Save log with timestamp
./process_[city]_batch.sh 2>&1 | tee [city]_batch_$(date +%Y%m%d_%H%M%S).log
```

### 5. Backup Before Organizing
```bash
# Copy remediation results before organization
cp -r remediation-best remediation-best-backup-[city]-$(date +%Y%m%d)
```

---

## City Code Registry

Maintain this list as you process cities:

| Code | City | State | Date Processed | Documents | Notes |
|------|------|-------|----------------|-----------|-------|
| ALX | Alexandria | VA | 2025-11-02 | 43 | Overnight batch |
| CEN | Centennial | CO | 2025-11-03 | 21 | First test of new system |
| SBD | South Bend | IN | 2025-11-12 | 97 | 2 problematic PDFs excluded |
| BUR | Burlingame | CA | | | Pending |
| PLH | Pleasant Hill | CA | | | Pending |
| GRB | Green Bay | WI | | | Pending |

---

## Quick Reference

```bash
# Tag
python3 city_batch_processor.py "StateAssets/[State]/[City]" [CODE] tag

# Process
./process_[city]_batch.sh

# Check
python3 city_batch_processor.py "StateAssets/[State]/[City]" [CODE] check

# Organize
python3 city_batch_processor.py "StateAssets/[State]/[City]" [CODE] organize
```

---

## What Makes This System Magical ✨

1. **One command tagging** - No manual renaming
2. **Automated batch processing** - Submit 20+ PDFs at once
3. **Real-time monitoring** - Check status anytime
4. **Iteration tracking** - See exactly how each PDF progressed
5. **Smart categorization** - Auto-sort by complexity
6. **Clean filenames** - City codes automatically stripped
7. **Two-table reporting** - Both VeraPDF compliance AND actual work metrics
8. **Accurate progress tracking** - Violations-based tiers show real remediation effort
9. **Comprehensive reports** - Full visibility from start to finish
10. **Repeatable workflow** - Same process for every city
11. **Error recovery** - Partial batches can be resumed
12. **Scalable** - Works for 5 PDFs or 500 PDFs

---

## Future Enhancements

Potential improvements to consider:

1. **Auto-monitoring** - Script that watches for completion and auto-organizes
2. **Parallel processing** - Submit multiple PDFs simultaneously
3. **Email notifications** - Alert when batch completes
4. **Web dashboard** - Visual progress tracking
5. **Resume capability** - Restart failed documents only
6. **Batch comparison** - Compare results across cities
7. **Export formats** - JSON, CSV, Excel reports
8. **Priority queue** - Process certain documents first
