#!/bin/bash
# Test full remediation pipeline with pikepdf enabled

set -e

echo "======================================================================"
echo "FULL REMEDIATION PIPELINE TEST WITH PIKEPDF"
echo "======================================================================"

# Pick one test PDF
TEST_PDF="/Users/sethredmore/Library/CloudStorage/GoogleDrive-sredmore@gmail.com/My Drive/StateAssets/Virginia/Alexandria/0076_Guidelines to students.pdf"
OUTPUT_DIR="FullPipelineTest"
mkdir -p "$OUTPUT_DIR"

echo ""
echo "📄 Test PDF: $(basename "$TEST_PDF")"
echo "📁 Output directory: $OUTPUT_DIR"

# Call full remediation endpoint
echo ""
echo "🚀 Calling /api/remediate-pdf-full..."
curl -s -X POST \
  -F "file=@${TEST_PDF}" \
  http://localhost:5008/api/remediate-pdf-full \
  -o "$OUTPUT_DIR/remediated_full.pdf"

if [ -f "$OUTPUT_DIR/remediated_full.pdf" ]; then
  SIZE=$(stat -f%z "$OUTPUT_DIR/remediated_full.pdf")
  echo "✅ Output received: $SIZE bytes"
else
  echo "❌ No output received"
  exit 1
fi

# Validate with VeraPDF
echo ""
echo "🔍 Running VeraPDF validation..."
export JAVA_HOME="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/TestAssets/jdk-21.0.8.jdk/Contents/Home"
./TestAssets/verapdf/verapdf --format text "$OUTPUT_DIR/remediated_full.pdf" > "$OUTPUT_DIR/verapdf_output.txt" 2>&1

# Check for compliance
if grep -q "PASS" "$OUTPUT_DIR/verapdf_output.txt" || grep -q 'compliant="true"' "$OUTPUT_DIR/verapdf_output.txt"; then
  echo "✅ COMPLIANT (PDF/UA)"
  echo ""
  cat "$OUTPUT_DIR/verapdf_output.txt" | grep -A 5 "validationReport"
elif grep -q "FAIL" "$OUTPUT_DIR/verapdf_output.txt" || grep -q 'compliant="false"' "$OUTPUT_DIR/verapdf_output.txt"; then
  echo "❌ NOT COMPLIANT"
  echo ""
  echo "Failed checks:"
  grep -i "failed" "$OUTPUT_DIR/verapdf_output.txt" | head -20
else
  echo "⚠️  Could not determine compliance status"
  echo ""
  cat "$OUTPUT_DIR/verapdf_output.txt" | head -30
fi

echo ""
echo "📝 Full VeraPDF output: $OUTPUT_DIR/verapdf_output.txt"

echo ""
echo "======================================================================"
