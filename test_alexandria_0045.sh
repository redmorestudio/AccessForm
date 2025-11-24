#!/bin/bash
echo "╔══════════════════════════════════════════════════════════╗"
echo "║  FULL REMEDIATION - Alexandria DOT 0045                 ║"
echo "║  With Phase 6K MCID Integration                         ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

PDF_PATH="/Users/sethredmore/Library/CloudStorage/GoogleDrive-sredmore@gmail.com/My Drive/StateAssets/Virginia/Alexandria/0045_DOT Paratransit Certification Application.pdf"
OUTPUT="alexandria_0045_output.pdf"

if [ ! -f "$PDF_PATH" ]; then
    echo "❌ PDF not found: $PDF_PATH"
    exit 1
fi

echo "📥 Input: 0045_DOT Paratransit Certification Application.pdf"
echo "   Size: $(wc -c < "$PDF_PATH") bytes, 10 pages"
echo "🌐 Calling: http://localhost:5008/api/remediate-pdf-full"
echo ""

echo "🚀 Starting FULL remediation pipeline..."
RESPONSE=$(curl -s -X POST http://localhost:5008/api/remediate-pdf-full \
  -F "file=@$PDF_PATH" \
  -w "\n%{http_code}")

HTTP_CODE=$(echo "$RESPONSE" | tail -n1)
BODY=$(echo "$RESPONSE" | head -n-1)

echo ""
if [ "$HTTP_CODE" = "200" ]; then
    echo "✅ API call successful (HTTP $HTTP_CODE)"
    echo ""

    echo "$BODY" | python3 -c "
import sys, json, base64
try:
    data = json.load(sys.stdin)
    if data.get('success') and 'remediatedPdf' in data:
        pdf_bytes = base64.b64decode(data['remediatedPdf'])
        with open('$OUTPUT', 'wb') as f:
            f.write(pdf_bytes)
        print(f'💾 Saved: $OUTPUT ({len(pdf_bytes):,} bytes)')

        if 'accessibilityReport' in data:
            report = data['accessibilityReport']
            print(f'')
            print(f'📊 REMEDIATION RESULTS:')
            print(f'═══════════════════════════════════════════')
            print(f'  Initial Violations: {report.get(\"violationsFound\", \"N/A\")}')
            print(f'  Violations Fixed:   {report.get(\"violationsFixed\", \"N/A\")}')
            print(f'  Final Violations:   {report.get(\"finalViolations\", \"N/A\")}')
            print(f'  Compliance Score:   {report.get(\"complianceScore\", \"N/A\")}%')
            print(f'  Iterations:         {report.get(\"iterations\", \"N/A\")}')
            print(f'  Duration:           {report.get(\"durationSeconds\", \"N/A\"):.1f}s')
            print(f'  Exit Reason:        {report.get(\"exitReason\", \"N/A\")}')
            print(f'═══════════════════════════════════════════')
    else:
        print('❌ Remediation failed')
        print(json.dumps(data, indent=2)[:500])
except Exception as e:
    print(f'❌ Error: {e}')
"
else
    echo "❌ API call failed (HTTP $HTTP_CODE)"
    echo "$BODY" | head -50
fi
