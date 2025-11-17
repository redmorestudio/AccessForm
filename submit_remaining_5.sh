#!/bin/bash

cd "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"

API_URL="http://localhost:5008/api/v2/remediation/start"
CITY_DIR="StateAssets/Indiana/South Bend"

PDFS=(
  "SBD_0042_Climate Risk  Vulnerability Assessment.pdf"
  "SBD_0053_South Bend Water Quality Parameters.pdf"
  "SBD_0056_PROCESS FOR ENERGY CODE COMPLIANCE RESIDENTIAL.pdf"
  "SBD_0096_Public Safety Video Surveillance System.pdf"
  "SBD_0106_Article 21-07 Access  Parking.pdf"
)

echo "Submitting remaining 5 PDFs..."
echo ""

for PDF in "${PDFS[@]}"; do
    echo "Submitting: $PDF"
    curl -s -X POST -F "file=@${CITY_DIR}/${PDF}" "${API_URL}" | grep -o '"status":"[^"]*"'
    echo ""
    sleep 3
done

echo "All submitted!"
