#!/bin/bash
echo "🧪 Testing Corry PDF with MCID Microservice"
echo "============================================"

PDF_PATH="StateAssets_archived_20251112_063442/Pennsylvania/Erie/ERA_0032_105 Corry.pdf"

if [ ! -f "$PDF_PATH" ]; then
    echo "❌ PDF not found: $PDF_PATH"
    exit 1
fi

echo "✅ Found PDF: $PDF_PATH"
echo "📏 Size: $(wc -c < "$PDF_PATH") bytes"
echo ""

# Convert PDF to base64
echo "🔄 Encoding PDF to base64..."
PDF_B64=$(base64 -i "$PDF_PATH" | tr -d '\n')

# Create minimal MCID plan (single test segment)
echo "📋 Creating MCID plan..."
PLAN='{"segments":[{"pageIndex":0,"mcid":0,"bounds":{"x":100,"y":100,"width":400,"height":50},"role":"P","sequenceIndex":0}]}'

# Call microservice
echo "🚀 Calling MCID rewriter microservice..."
RESPONSE=$(curl -s -X POST http://localhost:8000/api/mcid-rewrite \
  -H "Content-Type: application/json" \
  -d "{\"pdfBase64\":\"$PDF_B64\",\"plan\":$PLAN}")

echo ""
echo "📥 Response:"
echo "$RESPONSE" | python3 -m json.tool 2>/dev/null || echo "$RESPONSE"

# Check if successful
if echo "$RESPONSE" | grep -q '"success":true'; then
    echo ""
    echo "✅ Microservice call SUCCESSFUL!"
    
    # Extract and save output PDF
    echo ""
    echo "💾 Extracting output PDF..."
    echo "$RESPONSE" | python3 -c "import sys, json, base64; data=json.load(sys.stdin); open('corry_phase6k_output.pdf', 'wb').write(base64.b64decode(data['pdfBase64']))"
    
    if [ -f "corry_phase6k_output.pdf" ]; then
        echo "✅ Output saved: corry_phase6k_output.pdf"
        echo "📏 Output size: $(wc -c < corry_phase6k_output.pdf) bytes"
        echo ""
        echo "🔍 Checking for BDC/EMC markers..."
        if xxd corry_phase6k_output.pdf | grep -q "MCID\|BDC"; then
            echo "✅ MCID markers found in output!"
        else
            echo "⚠️  No obvious markers found (may need hex inspection)"
        fi
    else
        echo "❌ Failed to save output PDF"
    fi
else
    echo ""
    echo "❌ Microservice call FAILED"
fi
