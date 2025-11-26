#!/bin/bash

# Test multiple documents from Alexandria directory
# Verify pikepdf integration and AI services

set -e

ALEX_DIR="/Users/sethredmore/Library/CloudStorage/GoogleDrive-sredmore@gmail.com/My Drive/StateAssets/Virginia/Alexandria"
OUTPUT_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/PikepdfMultiTest"
VERAPDF_PATH="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/TestAssets/verapdf/verapdf"
PORT=5008

echo "========================================="
echo "Multi-Document Pikepdf Test"
echo "========================================="
echo ""

# Clean output directory
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

# Manually specify 3 test PDFs
PDFS=(
    "$ALEX_DIR/0001_Virginia Voter Registration Application.pdf"
    "$ALEX_DIR/0002_ELECT-427 Registration Report of Death of Register.pdf"
    "$ALEX_DIR/0003_Virginia Department of Social Services REQUEST FOR.pdf"
)

echo "Testing ${#PDFS[@]} documents:"
for i in "${!PDFS[@]}"; do
    echo "  $((i+1)). $(basename "${PDFS[$i]}")"
done
echo ""

# Track summary
TOTAL_INITIAL=0
TOTAL_FINAL=0
TOTAL_NESTED=0
TOTAL_DUPLICATE=0
TOTAL_TIME=0

# Process each PDF
for i in "${!PDFS[@]}"; do
    PDF="${PDFS[$i]}"
    NUM=$((i+1))
    BASENAME=$(basename "$PDF" .pdf)

    echo "========================================="
    echo "Test $NUM/3: $BASENAME"
    echo "========================================="

    # Validate original
    "$VERAPDF_PATH" --format text "$PDF" > "$OUTPUT_DIR/${NUM}_original.txt" 2>&1 || true
    INITIAL=$(grep -c "^FAIL " "$OUTPUT_DIR/${NUM}_original.txt" || echo "0")
    echo "Initial violations: $INITIAL"

    # Remediate
    START=$(date +%s)
    curl -s -X POST \
      -F "file=@${PDF}" \
      "http://localhost:${PORT}/api/remediate-pdf-full" \
      -o "$OUTPUT_DIR/${NUM}_remediated.pdf"
    END=$(date +%s)
    DURATION=$((END - START))

    # Check if valid PDF
    if ! file "$OUTPUT_DIR/${NUM}_remediated.pdf" | grep -q PDF; then
        echo "❌ ERROR: Not a valid PDF"
        continue
    fi

    # Validate remediated
    "$VERAPDF_PATH" --format text "$OUTPUT_DIR/${NUM}_remediated.pdf" > "$OUTPUT_DIR/${NUM}_remediated.txt" 2>&1 || true
    FINAL=$(grep -c "^FAIL " "$OUTPUT_DIR/${NUM}_remediated.txt" || echo "0")
    NESTED=$(grep -c "Nested MCID" "$OUTPUT_DIR/${NUM}_remediated.txt" || echo "0")
    DUPLICATE=$(grep -c "Duplicate MCID" "$OUTPUT_DIR/${NUM}_remediated.txt" || echo "0")

    # Update totals
    TOTAL_INITIAL=$((TOTAL_INITIAL + INITIAL))
    TOTAL_FINAL=$((TOTAL_FINAL + FINAL))
    TOTAL_NESTED=$((TOTAL_NESTED + NESTED))
    TOTAL_DUPLICATE=$((TOTAL_DUPLICATE + DUPLICATE))
    TOTAL_TIME=$((TOTAL_TIME + DURATION))

    # Report
    echo "Final violations:   $FINAL"
    if [ "$FINAL" -lt "$INITIAL" ]; then
        IMPROVEMENT=$((INITIAL - FINAL))
        echo "Improvement:        -$IMPROVEMENT ✅"
    elif [ "$FINAL" -eq "$INITIAL" ]; then
        echo "Change:             0 (no change) ⚠️"
    else
        REGRESSION=$((FINAL - INITIAL))
        echo "Regression:         +$REGRESSION ❌"
    fi
    echo "Nested MCID:        $NESTED"
    echo "Duplicate MCID:     $DUPLICATE"
    echo "Time:               ${DURATION}s"
    echo ""
done

echo "========================================="
echo "Final Summary"
echo "========================================="
echo "Documents tested:        ${#PDFS[@]}"
echo "Total initial violations: $TOTAL_INITIAL"
echo "Total final violations:   $TOTAL_FINAL"
if [ "$TOTAL_FINAL" -lt "$TOTAL_INITIAL" ]; then
    IMPROVEMENT=$((TOTAL_INITIAL - TOTAL_FINAL))
    PERCENT=$((IMPROVEMENT * 100 / TOTAL_INITIAL))
    echo "Total improvement:        -$IMPROVEMENT (-${PERCENT}%)"
    echo "Overall status:           ✅ IMPROVED"
elif [ "$TOTAL_FINAL" -eq "$TOTAL_INITIAL" ]; then
    echo "Total change:             0 (NO CHANGE)"
    echo "Overall status:           ⚠️  NO IMPROVEMENT"
else
    REGRESSION=$((TOTAL_FINAL - TOTAL_INITIAL))
    echo "Total regression:         +$REGRESSION"
    echo "Overall status:           ❌ WORSENED"
fi
echo "Total nested MCID:        $TOTAL_NESTED"
echo "Total duplicate MCID:     $TOTAL_DUPLICATE"
echo "Total processing time:    ${TOTAL_TIME}s (avg $((TOTAL_TIME / ${#PDFS[@]}))s)"
echo ""
if [ "$TOTAL_NESTED" -eq 0 ] && [ "$TOTAL_DUPLICATE" -eq 0 ]; then
    echo "🎉 NO MCID MARKER CONFLICTS - Pikepdf integration successful!"
else
    echo "⚠️  Warning: Found MCID marker conflicts"
fi
echo "========================================="
