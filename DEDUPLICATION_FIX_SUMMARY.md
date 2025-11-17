# PDF Spider Deduplication Fix

## Problem

The original spider processed **all 331 rows** in the CSV, including duplicate entries for the same city+website combination. This resulted in:
- Erie, Pennsylvania being processed 8 times (5x city site, 2x airport, 1x transit)
- Fremont, California being processed 8 times (6x city site, 2x courts)
- Total of 59 duplicate entries being processed

## Solution

Modified both `smart_pdf_spider.py` and `batch_pdf_spider.py` to deduplicate entries based on **(State, City, site domain)** before processing.

## Results

- **Before deduplication**: 331 entries to process
- **After deduplication**: 272 unique entries to process
- **Duplicate rows removed**: 59

## CSV Analysis

45 cities appear multiple times in the CSV with different websites:

### Top Cities with Multiple Entries:
1. **Erie, Pennsylvania** (8 entries): city (5x), airport (2x), transit (1x)
2. **Fremont, California** (8 entries): city (6x), courts (2x)
3. **Pomona, California** (6 entries): city (2x), DWC (2x), county (1x), courts (1x)
4. **Modesto, California** (6 entries): city (1x), schools (2x), courts (1x), county (2x)
5. **Reno, Nevada** (6 entries): city, transit (2x), state, courts, county
6. **Aurora, Colorado** (6 entries): city (3x), health, state, courts
7. **San Bernardino, California** (5 entries): city, county, DWC, university, courts
8. **Riverside, California** (5 entries): city (2x), county HR, transit, courts
9. **Santa Ana, California** (5 entries): city, county (2x), DWC (2x)
10. **Oceanside, California** (4 entries): city (2x), transit, FACT

## What Changed

### Modified Files:
1. **`smart_pdf_spider.py`** - Added deduplication in `_read_cities()` method
2. **`batch_pdf_spider.py`** - Added deduplication in `_read_cities()` method

### Backup Files Created:
- `smart_progress_backup_*.json` - Previous progress file
- `spider_log_backup_*.txt` - Previous detailed log
- `spider_summary_backup_*.txt` - Previous summary

## Previous Collection Stats

The previous spider run (with duplicates) collected:
- **Completed**: 321 entries out of 331
- **Total PDFs**: 37,031
- **Failed**: 0
- **Skipped**: 1

## Next Steps

To run a fresh collection with deduplication:

```bash
# Test run (no download, just search)
python3 smart_pdf_spider.py --csv "StateAssets/ada_contacts - Full List - 1027.csv" --max-results 1000

# Full run with download
python3 smart_pdf_spider.py --csv "StateAssets/ada_contacts - Full List - 1027.csv" --max-results 1000 --download
```

The spider will now:
1. Process only 272 unique city+website combinations
2. Skip duplicate entries automatically
3. Create fresh progress files
4. Only collect PDFs from each unique website once

## Optional: Clean Up Duplicate PDF Directories

If you want to remove PDF directories from duplicate entries (airport, transit, courts, etc.), you'll need to manually identify and remove them from `StateAssets/[State]/[City]/` directories.

The analysis script `analyze_csv_deduplication.py` can help identify which directories contain duplicate data.
