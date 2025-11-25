#!/bin/bash
# Quick test to verify structure tree preservation fix

INPUT_PDF="StateAssets_archived_20251112_063442/Pennsylvania/Erie/ERA_0001_1 BYLAWS OF THE ERIE REGIONAL AIRPORT AUTHORITY I .pdf"
OUTPUT_PDF="test_structure_fix_output.pdf"

echo "🧪 Testing structure tree preservation fix..."
echo "Input: $INPUT_PDF"
echo ""

# Call remediation API with MCID linking enabled
curl -X POST http://localhost:5008/api/remediate-pdf-full \
  -F "file=@$INPUT_PDF" \
  -F "enableMcidLinking=true" \
  -o "$OUTPUT_PDF" 2>&1

if [ $? -eq 0 ]; then
  echo "✅ API call successful, output saved to: $OUTPUT_PDF"
  echo ""
  echo "📊 Checking structure tree..."
  python3 test_structure_check.py "$OUTPUT_PDF"
else
  echo "❌ API call failed"
  exit 1
fi
