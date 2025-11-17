#!/bin/bash

# Test script for remediation v2 API
echo "============================================"
echo "Testing PDF Remediation with 7.1-3 Fixes"
echo "============================================"

# Input file
INPUT_PDF="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/remediation-best/BASIC FMLA Guide_202409131109189018a_best_iter3_20251031-131817.pdf"
OUTPUT_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/remediation-best"
TIMESTAMP=$(date +%Y%m%d-%H%M%S)

echo "Input PDF: $INPUT_PDF"
echo ""

# Step 1: Start remediation session
echo "Starting remediation session..."
RESPONSE=$(curl -X POST http://localhost:5008/api/v2/remediation/start \
  -H "Content-Type: multipart/form-data" \
  -F "pdf=@${INPUT_PDF}" \
  -F "maxIterations=5" \
  --silent --show-error)

echo "Response: $RESPONSE"

# Extract session ID from response
SESSION_ID=$(echo $RESPONSE | grep -oE '"sessionId":"[^"]+' | cut -d'"' -f4)

if [ -z "$SESSION_ID" ]; then
    echo "❌ Failed to start remediation session"
    exit 1
fi

echo "✅ Session started: $SESSION_ID"
echo ""

# Step 2: Poll for status
echo "Monitoring remediation progress..."
MAX_WAIT=300  # 5 minutes
ELAPSED=0
INTERVAL=5

while [ $ELAPSED -lt $MAX_WAIT ]; do
    sleep $INTERVAL
    ELAPSED=$((ELAPSED + INTERVAL))

    STATUS=$(curl -X GET http://localhost:5008/api/v2/remediation/status/$SESSION_ID \
        --silent --show-error)

    echo "[$ELAPSED s] Status: $STATUS"

    # Check if completed
    if echo "$STATUS" | grep -q '"status":"completed"'; then
        echo "✅ Remediation completed!"
        break
    fi

    # Check if failed
    if echo "$STATUS" | grep -q '"status":"failed"'; then
        echo "❌ Remediation failed!"
        exit 1
    fi
done

# Step 3: Download the best result
OUTPUT_PDF="${OUTPUT_DIR}/BASIC_FMLA_Guide_fixed_${TIMESTAMP}.pdf"
echo ""
echo "Downloading remediated PDF..."
curl -X GET http://localhost:5008/api/v2/remediation/download/$SESSION_ID/best \
    --output "$OUTPUT_PDF" \
    --silent --show-error

if [ -f "$OUTPUT_PDF" ]; then
    echo "✅ PDF saved to: $OUTPUT_PDF"

    # Get file size
    FILE_SIZE=$(ls -lh "$OUTPUT_PDF" | awk '{print $5}')
    echo "File size: $FILE_SIZE"
else
    echo "❌ Failed to download PDF"
    exit 1
fi

# Step 4: Run VeraPDF validation
echo ""
echo "Running VeraPDF validation..."

if command -v verapdf &> /dev/null; then
    verapdf --format text --flavour ua1 "$OUTPUT_PDF" > "${OUTPUT_PDF}_violations.txt" 2>&1

    # Check for 7.1-3 violations specifically
    echo ""
    echo "Checking for 7.1-3 violations..."
    if grep -q "7.1-3" "${OUTPUT_PDF}_violations.txt"; then
        echo "⚠️  7.1-3 violations found:"
        grep -A2 "7.1-3" "${OUTPUT_PDF}_violations.txt"
    else
        echo "✅ No 7.1-3 violations found!"
    fi

    # Count total violations
    echo ""
    TOTAL_VIOLATIONS=$(grep -c "testAssertion.*status=\"Failed\"" "${OUTPUT_PDF}_violations.txt" 2>/dev/null || echo "0")
    echo "Total violations: $TOTAL_VIOLATIONS"

    echo ""
    echo "Full validation report saved to: ${OUTPUT_PDF}_violations.txt"
else
    echo "⚠️  VeraPDF not installed. Cannot validate output."
    echo "Install with: brew install verapdf"
fi

echo ""
echo "============================================"
echo "Test complete"
echo "============================================"