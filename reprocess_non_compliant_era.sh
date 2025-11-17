#!/bin/bash

set -e

CITY_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/StateAssets_archived_20251112_063442/Pennsylvania/Erie"
SERVER_PORT=5008
API_URL="http://localhost:${SERVER_PORT}/api/v2/remediation/start"

echo "========================================="
echo "Reprocessing Non-100% Compliant ERA PDFs with GPT"
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

# List of non-100% compliant PDFs (99%, 97-98%, below 95%)
PDFS=(
# 99% tier (18 docs)
"ERA_0008_Erie 12th Street.pdf"
"ERA_0010_Erie 12th Street.pdf"
"ERA_0002_APPLICATION REDUCED TRANSIT FARE IDENTIFICATION CA.pdf"
"ERA_0031_Route 20L_Cultural Loop 2025.pdf"
"ERA_0005_Addendum No. 1 Erie International Airport Realignm.pdf"
"ERA_0002_Addendum No. 1 Erie International Airport Realignm.pdf"
"ERA_0063_2022 systems map revision Final.pdf"
"ERA_0051_Route 5 August 2025.pdf"
"ERA_0009_the city of erie - 2023 calendar  recycling guide.pdf"
"ERA_0007_the city of erie - 2023 calendar  recycling guide.pdf"
"ERA_0007_Erie Metropolitan Transit Authority.pdf"
"ERA_0064_LIFT COUNTY ROUTE TIMES.pdf"
"ERA_0007_PUBLIC NOTICE.pdf"
"ERA_0069_EMTA LETTERHEAD.pdf"
"ERA_0014_PUBLIC NOTICE.pdf"
"ERA_0044_EMTA  Emergency Operations Procedure Reference Gu.pdf"
"ERA_0045_Erie Metropolitan Transit Authority 127 East 14th .pdf"
"ERA_0052_Erie Metropolitan Transit Authority 127 East 14th .pdf"
# 97-98% tier (17 docs)
"ERA_0001_JAN O 8 2025.pdf"
"ERA_0003_JAN O 8 2025.pdf"
"ERA_0036_Edinboro Express_August 2025-26.pdf"
"ERA_0019_Route 29_August 2025.pdf"
"ERA_0020_Route 11_August 2025.pdf"
"ERA_0013_Route 22_August 2025.pdf"
"ERA_0004_Route 21_August 2025.pdf"
"ERA_0070_east county route 261.pdf"
"ERA_0042_Route 229 August 2025.pdf"
"ERA_0028_2025 systems map revision.pdf"
"ERA_0003_construction safety phasing plan - realignment and.pdf"
"ERA_0012_MS4 STORM SEWER SYSTEM MAP CITY OF ERIE ....pdf"
"ERA_0006_the city of erie - 2025 calendar  recycling guide.pdf"
"ERA_0002_AGENDA FOR THE CITY COUNCIL MEETING OF WEDNESDAY ..pdf"
"ERA_0001_AGENDA FOR THE CITY COUNCIL MEETING OF WEDNESDAY ..pdf"
"ERA_0002_City of Erie Environmental Advisory Council.pdf"
"ERA_0003_City of Erie Environmental Advisory Council.pdf"
# Below 95% tier (1 doc)
"ERA_0025_Route 28_ August 2025.ai.pdf"
)

TOTAL=${#PDFS[@]}
echo "Reprocessing ${TOTAL} non-100% compliant PDFs"
echo ""

SUCCESS_COUNT=0
FAIL_COUNT=0

for ((i=0; i<$TOTAL; i++)); do
    PDF="${PDFS[$i]}"
    NUM=$((i+1))

    echo "========================================="
    echo "[$NUM/$TOTAL] Processing: $PDF"
    echo "========================================="

    PDF_PATH="${CITY_DIR}/${PDF}"

    if [ ! -f "$PDF_PATH" ]; then
        echo "❌ File not found: $PDF"
        FAIL_COUNT=$((FAIL_COUNT + 1))
        echo ""
        continue
    fi

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
echo "REPROCESSING COMPLETE"
echo "========================================="
echo "Results:"
echo "  Processed: ${SUCCESS_COUNT}/${TOTAL}"
echo "  Failed: ${FAIL_COUNT}"
echo ""
echo "Note: These PDFs will now be remediated with GPT fallback enabled."
echo "Check progress with: python3 city_batch_processor.py \"${CITY_DIR}\" ERA check"
