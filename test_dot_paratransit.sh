#!/bin/bash
echo "╔══════════════════════════════════════════════════════════╗"
echo "║  FULL REMEDIATION TEST - DOT Paratransit PDF            ║"
echo "║  With Phase 6K MCID Integration                         ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

PDF_PATH="/Users/sethredmore/Downloads/DOT Paratransit Recertification Application_best_iter3_20251102-221630_best_iter0_20251102-233502 (1).pdf"
OUTPUT="dot_paratransit_FULL_output.pdf"

if [ ! -f "$PDF_PATH" ]; then
    echo "❌ PDF not found: $PDF_PATH"
    exit 1
fi

echo "📥 Input: $(basename "$PDF_PATH") ($(wc -c < "$PDF_PATH") bytes)"
echo "🌐 Calling: http://localhost:5008/api/remediate-pdf-full"
echo ""

# Call the FULL remediation endpoint
echo "🚀 Starting FULL remediation pipeline..."
RESPONSE=$(curl -s -X POST http://localhost:5008/api/remediate-pdf-full \
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
    if data.get('success') and 'remediatedPdf' in data:
        pdf_bytes = base64.b64decode(data['remediatedPdf'])
        with open('$OUTPUT', 'wb') as f:
            f.write(pdf_bytes)
        print(f'💾 Saved remediated PDF: $OUTPUT ({len(pdf_bytes):,} bytes)')

        # Check for PDF/UA markers
        pdf_str = pdf_bytes.decode('latin1', errors='ignore')
        has_part = '/Part' in pdf_str
        has_conformance = '/Conformance' in pdf_str
        has_pdfu = 'PDF/UA' in pdf_str

        print(f'\\n🔍 PDF/UA Compliance Markers:')
        print(f'   /Part metadata: {\"✅\" if has_part else \"❌\"}'
        print(f'   /Conformance: {\"✅\" if has_conformance else \"❌\"}')
        print(f'   PDF/UA XMP: {\"✅\" if has_pdfu else \"❌\"}')

        if 'accessibilityReport' in data:
            report = data['accessibilityReport']
            print(f\"\\n📊 Remediation Report:\")
            print(f\"   Violations Found: {report.get('violationsFound', 'N/A')}\")
            print(f\"   Violations Fixed: {report.get('violationsFixed', 'N/A')}\")
            print(f\"   Final Violations: {report.get('finalViolations', 'N/A')}\")
            print(f\"   Compliance: {report.get('isCompliant', False)}\")
            print(f\"   Iterations: {report.get('iterations', 'N/A')}\")
            print(f\"   Duration: {report.get('durationSeconds', 'N/A'):.1f}s\")
            print(f\"   Exit Reason: {report.get('exitReason', 'N/A')}\")

        print()
        print('🎉 FULL REMEDIATION TEST COMPLETE!')
        print('═══════════════════════════════════════════')
    else:
        print('❌ Remediation failed or no PDF in response')
        print(json.dumps(data, indent=2)[:500])
except Exception as e:
    print(f'❌ Error processing response: {e}')
    print(sys.stdin.read()[:500])
"
else
    echo "❌ API call failed (HTTP $HTTP_CODE)"
    echo "$BODY" | head -50
fi
