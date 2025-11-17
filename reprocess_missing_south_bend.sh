#!/bin/bash

set -e

CITY_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/StateAssets/Indiana/South Bend"
SERVER_PORT=5008
API_URL="http://localhost:${SERVER_PORT}/api/v2/remediation/start"

# List of missing PDFs
MISSING_PDFS=(
  "SBD_0007_City of South Bend Special Event Application City .pdf"
  "SBD_0016_EXCAVATION PERMIT REQUEST City of South Bend  Div.pdf"
  "SBD_0032_Introduction to the Inspection Checklist for Rooft.pdf"
  "SBD_0039_Article 21-09 Site Development.pdf"
  "SBD_0040_McKinley Terrace Neighborhood Study.pdf"
  "SBD_0042_Climate Risk  Vulnerability Assessment.pdf"
  "SBD_0053_South Bend Water Quality Parameters.pdf"
  "SBD_0056_PROCESS FOR ENERGY CODE COMPLIANCE RESIDENTIAL.pdf"
  "SBD_0062_GAS SAFETY PRESSURIZATION TEST.pdf"
  "SBD_0065_F-1 International Student Visa to Full-Time Worker.pdf"
  "SBD_0067_HOUSING COMMUNITY DEVELOPMENT.pdf"
  "SBD_0071_Owner Application for Service.pdf"
  "SBD_0072_Major Projects Updates.pdf"
  "SBD_0073_Gun Violence Report.pdf"
  "SBD_0075_Mental Health Resources.pdf"
  "SBD_0077_BOARD OF PUBLIC WORKS.pdf"
  "SBD_0082_Potawatomi Park Plan.pdf"
  "SBD_0090_City of South Bend.pdf"
  "SBD_0096_Public Safety Video Surveillance System.pdf"
  "SBD_0101_Zoning Application Fees.pdf"
  "SBD_0106_Article 21-07 Access  Parking.pdf"
)

echo "========================================="
echo "South Bend - Reprocessing Missing PDFs"
echo "Started: $(date)"
echo "Total: ${#MISSING_PDFS[@]} PDFs"
echo "========================================="
echo ""

# Check if server is running
if ! curl -s "http://localhost:${SERVER_PORT}" > /dev/null 2>&1; then
    echo "❌ Server not running on port ${SERVER_PORT}"
    exit 1
fi

echo "✓ Server is running"
echo ""

SUCCESS_COUNT=0
FAIL_COUNT=0

for ((i=0; i<${#MISSING_PDFS[@]}; i++)); do
    PDF="${MISSING_PDFS[$i]}"
    NUM=$((i+1))
    TOTAL=${#MISSING_PDFS[@]}

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
echo "REPROCESSING COMPLETE"
echo "========================================="
echo "Results:"
echo "  Processed: ${SUCCESS_COUNT}/${TOTAL}"
echo "  Failed: ${FAIL_COUNT}"
echo ""
