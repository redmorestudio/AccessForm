#!/bin/bash

set -e

CITY_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/StateAssets_archived_20251112_063442/Pennsylvania/Erie"
SERVER_PORT=5008
API_URL="http://localhost:${SERVER_PORT}/api/v2/remediation/start"

echo "========================================="
echo "Resubmitting 21 Incomplete ERA PDFs"
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

# List of incomplete PDFs
PDFS=(
"ERA_0003_JAN O 8 2025.pdf"
"ERA_0004_AIRPORT CUSTODIAN.pdf"
"ERA_0007_2024 Meetings by month JANUARY  12 - Urban Fore.pdf"
"ERA_0007_Erie Metropolitan Transit Authority.pdf"
"ERA_0008_Erie 12th Street.pdf"
"ERA_0010_Erie 12th Street.pdf"
"ERA_0010_Route 31_March 2025.ai.pdf"
"ERA_0011_Erie Regional Airport Authority Board Meeting Agen.pdf"
"ERA_0012_Route 21_August 2025.pdf"
"ERA_0015_Erie Regional Airport Authority Board Meeting Agen.pdf"
"ERA_0021_Route 3_August 2025.ai.pdf"
"ERA_0023_INFORMACION PARA SERVICIO DE TRANSPORTACION PUBLIC.pdf"
"ERA_0032_105 Corry.pdf"
"ERA_0037_Route 31_March 2025.ai.pdf"
"ERA_0040_Route 18_August 2025-26.pdf"
"ERA_0044_EMTA  Emergency Operations Procedure Reference Gu.pdf"
"ERA_0051_Route 5 August 2025.pdf"
"ERA_0055_Route 15_June 2022.ai.pdf"
"ERA_0058_ADVERTISE WITH ERIE METRO TRANSIT.pdf"
"ERA_0059_Route 12_May 23.ai.pdf"
"ERA_0063_2022 systems map revision Final.pdf"
)

TOTAL=${#PDFS[@]}
echo "Resubmitting ${TOTAL} incomplete PDFs"
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
echo "RESUBMISSION COMPLETE"
echo "========================================="
echo "Results:"
echo "  Processed: ${SUCCESS_COUNT}/${TOTAL}"
echo "  Failed: ${FAIL_COUNT}"
echo ""
echo "Next: python3 city_batch_processor.py \"${CITY_DIR}\" ERA check"
