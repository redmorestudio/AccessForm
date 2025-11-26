#!/bin/bash

# Test script to trigger pikepdf remediation and preserve temp files for debugging

INPUT_PDF="/Users/sethredmore/Library/CloudStorage/GoogleDrive-sredmore@gmail.com/My Drive/StateAssets/Virginia/Alexandria/0001_Virginia Voter Registration Application.pdf"
OUTPUT_PDF="./alexandria_remediated_debug.pdf"

echo "=== Testing Pikepdf Remediation with Temp File Preservation ==="
echo "Input: $INPUT_PDF"
echo "Output: $OUTPUT_PDF"
echo ""

# Call remediation API endpoint
curl -X POST "http://localhost:5008/api/remediate-pdf-pikepdf" \
  -F "pdf=@$INPUT_PDF" \
  -o "$OUTPUT_PDF" \
  -w "\nHTTP Status: %{http_code}\n" \
  -v 2>&1 | grep -E "(HTTP/|Content-Type|pikepdf)" || true

echo ""
echo "=== Remediation Complete ==="
echo "Output saved to: $OUTPUT_PDF"
echo ""
echo "=== Checking for Preserved Temp Directory ==="
find /var/folders -type d -name "pikepdf_*" -mmin -2 2>/dev/null | head -1
