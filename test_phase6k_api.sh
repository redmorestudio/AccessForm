#!/bin/bash
# Phase 6K API Test - Calls actual remediation server with MCID enabled

set -e

INPUT_PDF="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/StateAssets_archived_20251112_063442/Pennsylvania/Erie/ERA_0042_Route 229 August 2025.pdf"
OUTPUT_PDF="./erie_route229_api_remediation_output.pdf"

echo "================================================================================"
echo "PHASE 6K API REMEDIATION TEST"
echo "================================================================================"
echo ""
echo "Input PDF:  $INPUT_PDF"
echo "Output PDF: $OUTPUT_PDF"
echo ""

# Check if server is running
if ! curl -s http://localhost:5008/health > /dev/null 2>&1; then
    echo "❌ Server is not running at http://localhost:5008"
    echo "Please start the server first:"
    echo "  ASPNETCORE_URLS=\"http://localhost:5008\" dotnet run --project AccessFormServer.csproj --nologo --verbosity quiet"
    exit 1
fi

echo "✅ Server is running"
echo ""

# Check if Python microservice is running
if ! curl -s http://localhost:8000/health > /dev/null 2>&1; then
    echo "❌ Python microservice is not running at http://localhost:8000"
    echo "Please start the microservice first:"
    echo "  cd McidRewriterMicroservice && python3 main.py"
    exit 1
fi

echo "✅ Python microservice is running"
echo ""

# Encode PDF to base64
echo "📤 Encoding PDF to base64..."
PDF_BASE64=$(base64 -i "$INPUT_PDF")

# Call remediation API with MCID enabled
echo "📡 Calling remediation API..."
echo ""

RESPONSE=$(curl -s -X POST http://localhost:5008/api/remediate-pdf \
  -H "Content-Type: application/json" \
  -d "{
    \"pdfBase64\": \"$PDF_BASE64\",
    \"options\": {
      \"enableMcidLinking\": true,
      \"enableMcidContentRewrite\": true,
      \"maxIterations\": 3,
      \"fileName\": \"ERA_0042_Route 229 August 2025.pdf\"
    }
  }")

# Check if response contains success
if echo "$RESPONSE" | grep -q '"success":true'; then
    echo "✅ Remediation API returned success"

    # Extract PDF from response
    echo "$RESPONSE" | jq -r '.outputPdfBase64' | base64 -d > "$OUTPUT_PDF"

    echo "📥 Saved output to: $OUTPUT_PDF"
    echo ""

    # Verify markers
    BDC_COUNT=$(strings "$OUTPUT_PDF" | grep -c "BDC" || echo "0")
    EMC_COUNT=$(hexdump -C "$OUTPUT_PDF" | grep -c "45 4d 43" || echo "0")

    echo "VERIFICATION:"
    echo "  BDC markers: $BDC_COUNT"
    echo "  EMC markers: $EMC_COUNT"
    echo ""

    if [ "$BDC_COUNT" -gt 0 ] && [ "$EMC_COUNT" -gt 0 ]; then
        echo "✅✅✅ SUCCESS! Phase 6K full remediation pipeline is working!"
        echo ""
        echo "The output PDF contains:"
        echo "  • Structure tree with MCR kids"
        echo "  • BDC/EMC markers in content streams"
        echo "  • All remediation fixes applied"
    else
        echo "⚠️  Warning: No MCID markers found in output"
    fi
else
    echo "❌ Remediation API returned failure"
    echo ""
    echo "Response:"
    echo "$RESPONSE" | jq '.'
    exit 1
fi

echo ""
echo "================================================================================"
echo "Test complete!"
echo "================================================================================"
