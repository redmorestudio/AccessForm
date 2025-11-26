#!/bin/bash

# Comprehensive test for pikepdf full remediation pipeline
# Tests for BDC/EMC marker conflicts and validates with VeraPDF

set -e

ALEX_DIR="/Users/sethredmore/Library/CloudStorage/GoogleDrive-sredmore@gmail.com/My Drive/StateAssets/Virginia/Alexandria"
OUTPUT_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/PikepdfFullTest"
VERAPDF_PATH="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/TestAssets/verapdf/verapdf"
TEST_PDF="$ALEX_DIR/0001_Virginia Voter Registration Application.pdf"
PORT=5008

echo "========================================="
echo "Pikepdf Full Remediation Test"
echo "========================================="
echo ""
echo "Test PDF: $(basename "$TEST_PDF")"
echo "Output directory: $OUTPUT_DIR"
echo ""

# Clean output directory
rm -f "$OUTPUT_DIR"/*

echo "Step 1: Validate original PDF with VeraPDF..."
echo "-------------------------------------------"
"$VERAPDF_PATH" --format text "$TEST_PDF" > "$OUTPUT_DIR/verapdf_original.txt" 2>&1 || true

# Extract initial violation count
INITIAL_VIOLATIONS=$(grep -c "^FAIL " "$OUTPUT_DIR/verapdf_original.txt" || echo "0")
echo "✓ Original PDF violations: $INITIAL_VIOLATIONS"
echo ""

echo "Step 2: Run full remediation pipeline..."
echo "-------------------------------------------"
START_TIME=$(date +%s)

curl -s -X POST \
  -F "file=@${TEST_PDF}" \
  "http://localhost:${PORT}/api/remediate-pdf-full" \
  -o "$OUTPUT_DIR/remediated.pdf"

END_TIME=$(date +%s)
DURATION=$((END_TIME - START_TIME))

# Check if output is valid PDF
if file "$OUTPUT_DIR/remediated.pdf" | grep -q PDF; then
    FILE_SIZE=$(ls -lh "$OUTPUT_DIR/remediated.pdf" | awk '{print $5}')
    echo "✓ Remediation complete (${DURATION}s, ${FILE_SIZE})"
else
    echo "❌ ERROR: Output is not a valid PDF"
    head -100 "$OUTPUT_DIR/remediated.pdf"
    exit 1
fi
echo ""

echo "Step 3: Validate remediated PDF with VeraPDF..."
echo "-------------------------------------------"
"$VERAPDF_PATH" --format text "$OUTPUT_DIR/remediated.pdf" > "$OUTPUT_DIR/verapdf_remediated.txt" 2>&1 || true

# Extract final violation count
FINAL_VIOLATIONS=$(grep -c "^FAIL " "$OUTPUT_DIR/verapdf_remediated.txt" || echo "0")
echo "✓ Remediated PDF violations: $FINAL_VIOLATIONS"
echo ""

# Check for nested/duplicate MCID warnings
NESTED_MCID=$(grep -c "Nested MCID" "$OUTPUT_DIR/verapdf_remediated.txt" || echo "0")
DUPLICATE_MCID=$(grep -c "Duplicate MCID" "$OUTPUT_DIR/verapdf_remediated.txt" || echo "0")

echo "Step 4: Check for BDC/EMC marker conflicts..."
echo "-------------------------------------------"
if [ "$NESTED_MCID" -gt 0 ] || [ "$DUPLICATE_MCID" -gt 0 ]; then
    echo "⚠️  WARNING: Found marker conflicts:"
    echo "   - Nested MCID warnings: $NESTED_MCID"
    echo "   - Duplicate MCID warnings: $DUPLICATE_MCID"
    echo ""
    echo "   First 10 warnings:"
    grep -E "(Nested MCID|Duplicate MCID)" "$OUTPUT_DIR/verapdf_remediated.txt" | head -10
else
    echo "✅ No nested/duplicate MCID warnings found!"
fi
echo ""

echo "Step 5: Final Results"
echo "========================================="
echo "Initial violations:     $INITIAL_VIOLATIONS"
echo "Final violations:       $FINAL_VIOLATIONS"
if [ "$FINAL_VIOLATIONS" -lt "$INITIAL_VIOLATIONS" ]; then
    IMPROVEMENT=$((INITIAL_VIOLATIONS - FINAL_VIOLATIONS))
    PERCENT=$((IMPROVEMENT * 100 / INITIAL_VIOLATIONS))
    echo "Improvement:            -$IMPROVEMENT (-${PERCENT}%)"
    echo "Status:                 ✅ IMPROVED"
elif [ "$FINAL_VIOLATIONS" -eq "$INITIAL_VIOLATIONS" ]; then
    echo "Change:                 0 (NO CHANGE)"
    echo "Status:                 ⚠️  NO IMPROVEMENT"
else
    REGRESSION=$((FINAL_VIOLATIONS - INITIAL_VIOLATIONS))
    echo "Change:                 +$REGRESSION (REGRESSION)"
    echo "Status:                 ❌ WORSENED"
fi
echo ""
echo "Nested MCID warnings:   $NESTED_MCID"
echo "Duplicate MCID warnings: $DUPLICATE_MCID"
echo ""
echo "Processing time:        ${DURATION}s"
echo "Output size:            $(ls -lh "$OUTPUT_DIR/remediated.pdf" | awk '{print $5}')"
echo ""
echo "Files saved to: $OUTPUT_DIR/"
echo "  - verapdf_original.txt"
echo "  - verapdf_remediated.txt"
echo "  - remediated.pdf"
echo "========================================="
