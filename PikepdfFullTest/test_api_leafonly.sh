#!/bin/bash

INPUT_PDF="/Users/sethredmore/Library/CloudStorage/GoogleDrive-sredmore@gmail.com/My Drive/StateAssets/Virginia/Alexandria/0001_Virginia Voter Registration Application.pdf"
OUTPUT_PDF="./alexandria_leafonly_api.pdf"

echo "=== Testing Leaf-Only MCID Mapping via API ==="
echo "Input: $(basename "$INPUT_PDF")"
echo ""

# Use the remediation orchestrator API endpoint
echo "Calling remediation API..."
curl -X POST "http://localhost:5008/api/remediate" \
  -F "pdf=@$INPUT_PDF" \
  -o "$OUTPUT_PDF" \
  -w "\nHTTP Status: %{http_code}\n" \
  --max-time 300 \
  2>&1

echo ""
echo "=== Checking Output ==="
if [ -f "$OUTPUT_PDF" ] && [ -s "$OUTPUT_PDF" ]; then
    SIZE=$(ls -lh "$OUTPUT_PDF" | awk '{print $5}')
    echo "✓ Output created: $OUTPUT_PDF ($SIZE)"
    
    echo ""
    echo "=== Running VeraPDF Validation ==="
    verapdf --flavour 1b "$OUTPUT_PDF" > verapdf_leafonly.txt 2>&1
    
    echo "Original violations:"
    cat verapdf_original.txt 2>/dev/null || echo "Not found"
    
    echo ""
    echo "New violations:"
    cat verapdf_leafonly.txt | grep -E "(PASS|FAIL)"
    
    # Count violations if any
    VIOLATIONS=$(grep -c "failedChecks" verapdf_leafonly.txt 2>/dev/null || echo "0")
    echo "Total violation checks: $VIOLATIONS"
else
    echo "✗ Output not created or empty"
    ls -la "$OUTPUT_PDF" 2>/dev/null || echo "File does not exist"
fi
