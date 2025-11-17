# City Domains Redo Summary

## Overview

This document summarizes the process of identifying and re-collecting PDFs from city government domains only, excluding non-city entities (airports, transit, courts, counties, schools, universities).

## Problem Identified

The original spider run collected PDFs from **all** domains listed in the CSV's "city site" column, including:
- Airport websites
- Transit authority websites
- Court websites
- County websites
- School district websites
- University websites

This resulted in PDFs from non-city sources being mixed with city PDFs, making it impossible to determine which documents came from the city government vs. other entities.

## Solution Implemented

### 1. Domain Filtering (`filter_city_domains.py`)

Created a script to analyze the CSV and filter out non-city domains by:
- Identifying patterns like "airport", "transit", "court", "county", ".edu", "school", etc.
- Keeping only city government domains (cityname.gov, cityname.org, etc.)
- Generating a report of excluded domains

**Results:**
- Total unique cities: 233
- Cities with multiple domains: 31
- Cities with only city domains kept: 18
- Cities with only non-city domains (excluded entirely): 13

### 2. Affected Cities

**18 cities with city domains re-collected:**

| City | State | Archived PDFs | City Domain |
|------|-------|---------------|-------------|
| Bakersfield | California | 95 | bakersfieldcity.us |
| Burbank | California | 6 | burbankca.gov |
| Elk Grove | California | 343 | elkgrove.gov |
| Fontana | California | 160 | fontanaca.gov |
| Fremont | California | 265 | fremont.gov |
| Modesto | California | 431 | modestogov.com |
| Moreno Valley | California | 305 | moval.gov |
| Oceanside | California | 23 | ci.oceanside.ca.us |
| Pomona | California | 345 | pomonaca.gov |
| Redding | California | 2 | records.ci.redding.ca.us |
| Riverside | California | 488 | riversideca.gov |
| San Bernardino | California | 527 | ci.san-bernardino.ca.us |
| San Mateo | California | 272 | cityofsanmateo.org |
| Santa Ana | California | 182 | santa-ana.org |
| Ventura | California | 411 | ventura.org |
| Aurora | Colorado | 214 | auroragov.org |
| Reno | Nevada | 494 | reno.gov |
| Erie | Pennsylvania | 122 | erie.pa.us |

**Total archived: 4,685 PDFs from non-city sources**

**13 cities excluded entirely (only had non-city domains):**
- Brandon, FL (only hcfl.gov - Hillsborough County)
- Lexington-Fayette, KY (only lextran.com - transit)
- Louisville-Jefferson County, KY (only louisville.edu - university)
- Metairie, LA (only jeffparish.net - Jefferson Parish)
- Lansing, MI (only michigan.gov - state)
- Lincoln, NE (only nebraskajudicial.gov - courts)
- Enterprise, NV (only clarkcountynv.gov - county)
- Paradise, NV (only clarkcountynv.gov - county)
- Spring Valley, NV (only clarkcountynv.gov - county)
- Sunrise Manor, NV (only clarkcountynv.gov - county)
- Manchester, NH (only snhpc.org - regional planning)
- Laredo, TX (only webbcountytx.gov - Webb County)
- Provo, UT (only provo.edu - Provo School District)

### 3. Archiving Process

**Archive Location:** `StateAssets_archived_20251112_063442/`

All original PDFs from the 18 affected cities were copied to the archive before deletion, preserving:
- All PDF files
- Metadata (pdf_results.json)
- Directory structure
- Archive manifest with details

### 4. Re-Collection Process

**Spider Run:** Using filtered `cities_to_redo.csv` with 18 cities

**Progress:** Currently running (background process 1b1361)
- Using same search strategy: forms (5yr), all PDFs (2yr), expand to 5yr if <100
- Max 1000 PDFs per city
- Downloading PDFs directly to StateAssets/[State]/[City]/
- Progress tracked in `smart_progress_redo.json`

### 5. Domain Exclusion Patterns

The filter script excluded domains containing:
- airport
- transit, transportation, metro
- court, judicial
- county, parish
- school, district, .edu, university, college
- Specific known entities (clarkcountynv.gov, jeffparish.net, etc.)

## Files Created

1. **filter_city_domains.py** - Domain filtering script
2. **filter_report_*.txt** - Report of excluded domains
3. **city_domains_only.csv** - Filtered CSV (220 rows, all cities with city domains only)
4. **cities_to_redo.csv** - CSV with 18 affected cities
5. **archive_affected_cities.py** - Archiving script
6. **create_redo_csv.py** - Script to extract cities for redo
7. **ARCHIVE_MANIFEST.txt** - Detailed archive manifest
8. **smart_progress_redo.json** - Progress tracking for redo run

## Files Modified

1. **smart_pdf_spider.py** - Reverted incorrect deduplication changes
2. **batch_pdf_spider.py** - Reverted incorrect deduplication changes

## Outcome

After completion, the StateAssets directories will contain PDFs **only** from official city government websites, with:
- Non-city PDFs archived for reference
- Clear separation between city and non-city sources
- Ability to contact the correct city ADA coordinator about city documents specifically

## Monitoring

Check spider progress:
```bash
# View current progress
cat smart_progress_redo.json

# Check detailed output
python3 -c "from WordToPdfConverter import BashOutput; BashOutput('1b1361')"
```

## Next Steps

1. Wait for spider to complete all 18 cities
2. Verify PDF counts and domains
3. Compare with archived counts to assess difference
4. Update documentation with final results
