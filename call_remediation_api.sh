#!/bin/bash
echo "╔══════════════════════════════════════════════════════════╗"
echo "║  FULL REMEDIATION API TEST - Corry PDF                  ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

PDF_PATH="StateAssets_archived_20251112_063442/Pennsylvania/Erie/ERA_0032_105 Corry.pdf"
OUTPUT="corry_API_remediation_output.pdf"

if [ ! -f "$PDF_PATH" ]; then
    echo "❌ PDF not found: $PDF_PATH"
    exit 1
fi

echo "📥 Input: $PDF_PATH ($(wc -c < "$PDF_PATH") bytes)"
echo "🌐 Calling: http://localhost:5008/api/remediate-pdf"
echo ""

# Call the remediation endpoint
echo "🚀 Starting remediation..."
RESPONSE=$(curl -s -X POST http://localhost:5008/api/remediate-pdf \
  -F "file=@$PDF_PATH" \
  -w "\n%{http_code}")

# Split response and status code
HTTP_CODE=$(echo "$RESPONSE" | tail -n1)
BODY=$(echo "$RESPONSE" | head -n-1)

echo ""
if [ "$HTTP_CODE" = "200" ]; then
    echo "✅ API call successful (HTTP $HTTP_CODE)"
    echo ""
    
    # Extract and save the remediated PDF
    echo "$BODY" | python3 -c "
import sys, json, base64
try:
    data = json.load(sys.stdin)
    if 'remediatedPdf' in data:
        pdf_bytes = base64.b64decode(data['remediatedPdf'])
        with open('$OUTPUT', 'wb') as f:
            f.write(pdf_bytes)
        print(f'💾 Saved remediated PDF: $OUTPUT ({len(pdf_bytes):,} bytes)')
        
        # Check for PDF/UA marker
        pdf_str = pdf_bytes.decode('latin1', errors='ignore')
        has_pdfu = '/Part' in pdf_str or 'PDF/UA' in pdf_str
        print(f'PDF/UA marker: {\"✅ FOUND\" if has_pdfu else \"⚠️  NOT FOUND\"}')
        
        if 'accessibilityReport' in data:
            report = data['accessibilityReport']
            print(f\"\\n📊 Remediation Report:\")
            print(f\"   Violations Found: {report.get('violationsFound', 'N/A')}\")
            print(f\"   Violations Fixed: {report.get('violationsFixed', 'N/A')}\")
            print(f\"   Compliance Score: {report.get('complianceScore', 'N/A')}\")
    else:
        print('❌ No remediatedPdf in response')
        print(json.dumps(data, indent=2)[:500])
except Exception as e:
    print(f'❌ Error processing response: {e}')
    print(sys.stdin.read()[:500])
"
else
    echo "❌ API call failed (HTTP $HTTP_CODE)"
    echo "$BODY" | head -50
fi
