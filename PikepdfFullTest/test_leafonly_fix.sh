#!/bin/bash

INPUT_PDF="/Users/sethredmore/Library/CloudStorage/GoogleDrive-sredmore@gmail.com/My Drive/StateAssets/Virginia/Alexandria/0001_Virginia Voter Registration Application.pdf"
OUTPUT_PDF="./alexandria_leafonly.pdf"

echo "=== Testing Leaf-Only MCID Mapping Fix ==="
echo "Input: $INPUT_PDF"
echo "Output: $OUTPUT_PDF"
echo ""

# Call remediation via TestPikepdfIntegration
echo "Running remediation..."
dotnet run --project AccessFormServer.csproj --no-build -- TestPikepdfIntegration "$INPUT_PDF" "$OUTPUT_PDF" 2>&1 | grep -E "(ORCHESTRATOR|PIKEPDF|leaf|container|segments|MCR|Structure tree)" | tail -50

echo ""
echo "=== Checking Output ==="
if [ -f "$OUTPUT_PDF" ]; then
    SIZE=$(ls -lh "$OUTPUT_PDF" | awk '{print $5}')
    echo "✓ Output created: $OUTPUT_PDF ($SIZE)"
    
    echo ""
    echo "=== Running VeraPDF Validation ==="
    verapdf --flavour 1b "$OUTPUT_PDF" 2>&1 | grep -E "(PASS|FAIL)"
else
    echo "✗ Output not created"
fi
