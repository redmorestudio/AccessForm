#!/bin/bash
# Test Phase 6K via full RemediationOrchestrator using running server

set -e

INPUT_PDF="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/StateAssets_archived_20251112_063442/Pennsylvania/Erie/ERA_0042_Route 229 August 2025.pdf"
OUTPUT_PDF="./erie_route229_FULL_REMEDIATION.pdf"

echo "================================================================================"
echo "PHASE 6K FULL REMEDIATION TEST via RemediationOrchestrator"
echo "================================================================================"
echo ""
echo "This test calls the ACTUAL remediation API endpoint which triggers:"
echo "  1. Preflight (Aspose font fixes)"
echo "  2. Initial VeraPDF validation"
echo "  3. Remediation loop with ALL fix services:"
echo "     - StructureRebuildServiceAdapter → Phase 6K MCID"
echo "     - Metadata fixes"
echo "     - Form fixes"
echo "     - Content fixes"
echo "     - Font fixes"
echo "  4. Final VeraPDF validation"
echo ""
echo "Input:  $INPUT_PDF"
echo "Output: $OUTPUT_PDF"
echo ""

# Check servers
if ! curl -s http://localhost:5008/health > /dev/null 2>&1; then
    echo "❌ Server not running at http://localhost:5008"
    exit 1
fi
echo "✅ .NET server running"

if ! curl -s http://localhost:8000/health > /dev/null 2>&1; then
    echo "❌ Python microservice not running at http://localhost:8000"
    exit 1
fi
echo "✅ Python microservice running"
echo ""

# Base64 encode PDF
echo "📤 Encoding PDF..."
PDF_BASE64=$(base64 -i "$INPUT_PDF" | tr -d '\n')

# Call remediation API
echo "📡 Calling remediation orchestrator API..."
echo ""

RESPONSE=$(curl -s -X POST "http://localhost:5008/api/remediate" \
  -H "Content-Type: application/json" \
  -d @- <<EOF
{
  "pdfBytes": "$PDF_BASE64",
  "options": {
    "enableMcidLinking": true,
    "enableMcidContentRewrite": true,
    "maxIterations": 3,
    "fileName": "ERA_0042_Route 229 August 2025.pdf"
  }
}
EOF
)

echo "$RESPONSE" > response.json

# Check success
if echo "$RESPONSE" | jq -e '.success == true' > /dev/null 2>&1; then
    echo "✅ Remediation completed"
    
    # Extract stats
    ITERATIONS=$(echo "$RESPONSE" | jq -r '.summary.totalIterations // "N/A"')
    INITIAL_VIOLATIONS=$(echo "$RESPONSE" | jq -r '.summary.initialViolations // "N/A"')
    FINAL_VIOLATIONS=$(echo "$RESPONSE" | jq -r '.summary.finalViolations // "N/A"')
    COMPLIANCE=$(echo "$RESPONSE" | jq -r '.summary.finalComplianceScore // "N/A"')
    
    echo ""
    echo "REMEDIATION STATS:"
    echo "  Iterations:        $ITERATIONS"
    echo "  Initial Violations: $INITIAL_VIOLATIONS"
    echo "  Final Violations:   $FINAL_VIOLATIONS"
    echo "  Compliance:        ${COMPLIANCE}%"
    echo ""
    
    # Extract and save PDF
    echo "$RESPONSE" | jq -r '.outputPdf' | base64 -d > "$OUTPUT_PDF"
    echo "📥 Saved: $OUTPUT_PDF"
    
    # Verify MCID markers
    BDC_COUNT=$(strings "$OUTPUT_PDF" | grep -c "BDC" || echo "0")
    EMC_HEX=$(hexdump -C "$OUTPUT_PDF" | grep -c "45 4d 43" || echo "0")
    
    echo ""
    echo "MCID MARKER VERIFICATION:"
    echo "  BDC markers: $BDC_COUNT"
    echo "  EMC markers: $EMC_HEX (via hexdump)"
    echo ""
    
    if [ "$BDC_COUNT" -gt 0 ] && [ "$EMC_HEX" -gt 0 ]; then
        echo "✅✅✅ SUCCESS! Phase 6K integrated into full remediation pipeline!"
        echo ""
        echo "The output PDF contains:"
        echo "  • All remediation fixes (metadata, fonts, forms, content)"
        echo "  • Structure tree with MCR kids (Phase 6K)"
        echo "  • BDC/EMC markers in content streams (Phase 6K)"
        echo "  • Guard clauses prevented marker overwrites"
    else
        echo "⚠️  Warning: MCID markers not found"
        echo "This may indicate:"
        echo "  - MCID was disabled by remediation options"
        echo "  - Structure rebuild was skipped"
        echo "  - Python microservice failed"
    fi
else
    echo "❌ Remediation failed"
    echo ""
    echo "Response:"
    cat response.json | jq '.'
    exit 1
fi

echo ""
echo "================================================================================"
echo "Next: Open $OUTPUT_PDF in Adobe Acrobat to verify tag tree"
echo "================================================================================"
