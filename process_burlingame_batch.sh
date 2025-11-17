#!/bin/bash

# Process Burlingame PDFs through remediation API
# Server must be running on port 5008

BASE_URL="http://localhost:5008"
INPUT_DIR="StateAssets/California/Burlingame"
OUTPUT_DIR="remediation-best"

# Create output directory
mkdir -p "$OUTPUT_DIR"

# Array of documents to process
documents=(
    "Building Permit Application (PDF).pdf"
    "Conditional Use Permit Form (PDF).pdf"
    "Final Routing - handout template 6.3.25_202506041028022821.pdf"
)

# Color codes
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo "========================================="
echo "Burlingame PDF Remediation Batch Process"
echo "========================================="
echo ""

for doc in "${documents[@]}"; do
    echo ""
    echo "========================================="
    echo "Processing: $doc"
    echo "========================================="

    # Start remediation
    echo "Starting remediation..."
    RESPONSE=$(curl -s -X POST "$BASE_URL/api/v2/remediation/start" \
      -F "file=@$INPUT_DIR/$doc" \
      -F "maxIterations=10" \
      -F "acceptableComplianceScore=100" \
      -F "detectStagnation=true" \
      -F "stopOnRegression=false")

    echo "Response: $RESPONSE"

    SESSION_ID=$(echo "$RESPONSE" | python3 -c "import sys, json; data=json.load(sys.stdin); print(data.get('sessionId', ''))" 2>/dev/null)

    if [ -z "$SESSION_ID" ]; then
        echo -e "${RED}Failed to start session for $doc${NC}"
        echo "Response: $RESPONSE"
        continue
    fi

    echo "Session ID: $SESSION_ID"
    echo ""

    # Poll status until complete
    MAX_POLLS=60
    POLL_COUNT=0
    STATUS=""

    while [ $POLL_COUNT -lt $MAX_POLLS ]; do
        sleep 5
        POLL_COUNT=$((POLL_COUNT + 1))

        STATUS_RESPONSE=$(curl -s "$BASE_URL/api/v2/remediation/status/$SESSION_ID")
        STATUS=$(echo "$STATUS_RESPONSE" | python3 -c "import sys, json; data=json.load(sys.stdin); print(data.get('status', ''))" 2>/dev/null)
        ITERATION=$(echo "$STATUS_RESPONSE" | python3 -c "import sys, json; data=json.load(sys.stdin); print(data.get('iteration', 0))" 2>/dev/null)
        SCORE=$(echo "$STATUS_RESPONSE" | python3 -c "import sys, json; data=json.load(sys.stdin); print(data.get('bestComplianceScore', 0))" 2>/dev/null)

        echo "[Poll $POLL_COUNT] Status: $STATUS | Iteration: $ITERATION | Best Score: $SCORE%"

        if [ "$STATUS" = "completed" ] || [ "$STATUS" = "failed" ]; then
            break
        fi
    done

    echo ""
    echo "Final Status: $STATUS"

    # Download best PDF
    if [ "$STATUS" = "completed" ]; then
        OUTPUT_FILE="$OUTPUT_DIR/${doc%.pdf}_remediated.pdf"
        echo "Downloading remediated PDF to: $OUTPUT_FILE"
        curl -s "$BASE_URL/api/v2/remediation/download/$SESSION_ID/best" -o "$OUTPUT_FILE"

        if [ -f "$OUTPUT_FILE" ]; then
            FILE_SIZE=$(stat -f%z "$OUTPUT_FILE" 2>/dev/null || stat -c%s "$OUTPUT_FILE" 2>/dev/null)
            echo -e "${GREEN}✓ Downloaded: $OUTPUT_FILE ($FILE_SIZE bytes)${NC}"
            echo "  Best Compliance Score: $SCORE%"

            # Validate with VeraPDF
            echo "  Validating with VeraPDF..."
            verapdf --flavour ua1 --format text "$OUTPUT_FILE" 2>&1 | grep -E "(compliant|validationReport)" || echo "  (VeraPDF validation check completed)"
        else
            echo -e "${RED}✗ Failed to download PDF${NC}"
        fi
    else
        echo -e "${RED}✗ Remediation failed or incomplete${NC}"
        echo "Final status response: $STATUS_RESPONSE"
    fi

    echo ""
done

echo ""
echo "========================================="
echo "Batch Process Complete!"
echo "========================================="
echo ""
echo "Results saved to: $OUTPUT_DIR/"
ls -lh "$OUTPUT_DIR/"
