#!/bin/bash

set -e

CITY_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/StateAssets/Indiana/South Bend"
OUTPUT_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/remediation-best"
SERVER_PORT=5008
API_URL="http://localhost:${SERVER_PORT}/api/v2/remediation/start"

echo "========================================="
echo "South Bend PDF Remediation - Batch Run"
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
PDF_FILES=(SBD_*.pdf)
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
echo "Next: python3 city_batch_processor.py \"${CITY_DIR}\" SBD organize"
