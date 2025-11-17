#!/bin/bash

# Centennial PDF Batch Remediation
# Processes all CEN_*.pdf files through the remediation service

set -e

CITY_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/StateAssets/Colorado/Centennial"
OUTPUT_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/remediation-best"
LOG_FILE="centennial_remediation_$(date +%Y%m%d_%H%M%S).log"
SERVER_PORT=5008
API_URL="http://localhost:${SERVER_PORT}/api/v2/remediation/start"

echo "========================================="
echo "Centennial PDF Remediation - Batch Run"
echo "Started: $(date)"
echo "========================================="
echo ""

# Check if server is running
if ! curl -s "http://localhost:${SERVER_PORT}" > /dev/null 2>&1; then
    echo "❌ Server not running on port ${SERVER_PORT}"
    echo "Please start the server first:"
    echo "  ASPNETCORE_URLS=\"http://localhost:${SERVER_PORT}\" dotnet run --project AccessFormServer.csproj --nologo --verbosity quiet"
    exit 1
fi

echo "✓ Server is running on port ${SERVER_PORT}"
echo ""

# Find all CEN_*.pdf files
cd "$CITY_DIR"
PDF_FILES=(CEN_*.pdf)
TOTAL=${#PDF_FILES[@]}

if [ $TOTAL -eq 0 ]; then
    echo "❌ No CEN_*.pdf files found in $CITY_DIR"
    exit 1
fi

echo "Found ${TOTAL} PDFs to process"
echo ""

# Process each PDF
SUCCESS_COUNT=0
FAIL_COUNT=0

for ((i=0; i<$TOTAL; i++)); do
    PDF="${PDF_FILES[$i]}"
    NUM=$((i+1))

    echo "========================================="
    echo "[$NUM/$TOTAL] Processing: $PDF"
    echo "========================================="

    PDF_PATH="${CITY_DIR}/${PDF}"

    # Call remediation API
    echo "Sending to remediation service..."

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
        echo "Response: $BODY"
        FAIL_COUNT=$((FAIL_COUNT + 1))
    fi

    echo ""

    # Brief pause between requests
    sleep 2
done

echo ""
echo "========================================="
echo "BATCH COMPLETE"
echo "========================================="
echo "Completed: $(date)"
echo ""
echo "Results:"
echo "  Processed: ${SUCCESS_COUNT}/${TOTAL}"
echo "  Failed: ${FAIL_COUNT}"
echo ""
echo "Output directory: ${OUTPUT_DIR}"
echo "Log file: ${LOG_FILE}"
echo "========================================="
echo ""
echo "Next step: Run organization"
echo "  python3 city_batch_processor.py \"${CITY_DIR}\" CEN organize"
echo ""
