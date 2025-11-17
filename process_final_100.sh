#!/bin/bash

# Process all 3 Burlingame PDFs with improved artifact and figure services
# Goal: 100% PDF/UA compliance

BASE_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"
OUTPUT_DIR="$BASE_DIR/final-100-percent"
mkdir -p "$OUTPUT_DIR"

echo "🚀 Starting final push to 100% compliance with improved services"
echo "=================================================="

# Define files
FILES=(
    "StateAssets/California/Burlingame/Building Permit Application (PDF).pdf"
    "StateAssets/California/Burlingame/Conditional Use Permit Form (PDF).pdf"
    "StateAssets/California/Burlingame/Final Routing - handout template 6.3.25_202506041028022821.pdf"
)

# Process each file
for FILE in "${FILES[@]}"; do
    BASENAME=$(basename "$FILE" .pdf)
    echo ""
    echo "📄 Processing: $BASENAME"
    echo "----------------------------------------"

    # Upload and process using v2 API
    RESPONSE=$(curl -s -X POST \
        -F "file=@$BASE_DIR/$FILE" \
        http://localhost:5008/api/v2/remediation/start)

    # Check if session ID was returned
    SESSION_ID=$(echo "$RESPONSE" | grep -o '"sessionId":"[^"]*"' | cut -d'"' -f4)

    if [ -z "$SESSION_ID" ]; then
        echo "❌ Failed to start processing for $BASENAME"
        continue
    fi

    echo "✓ Session started: $SESSION_ID"
    echo "⏳ Waiting for completion..."

    # Poll for completion (max 5 minutes)
    MAX_WAIT=300
    ELAPSED=0
    COMPLETED=false

    while [ $ELAPSED -lt $MAX_WAIT ]; do
        STATUS=$(curl -s "http://localhost:5008/api/v2/remediation/status/$SESSION_ID")

        if echo "$STATUS" | grep -q '"status":"Completed"'; then
            COMPLETED=true
            echo "✅ Processing completed!"
            break
        elif echo "$STATUS" | grep -q '"status":"Failed"'; then
            echo "❌ Processing failed"
            break
        fi

        sleep 3
        ELAPSED=$((ELAPSED + 3))
        echo -n "."
    done

    echo ""

    if [ "$COMPLETED" = true ]; then
        # Download result
        OUTPUT_FILE="$OUTPUT_DIR/${BASENAME}_remediated.pdf"
        curl -s -o "$OUTPUT_FILE" "http://localhost:5008/api/v2/remediation/download/$SESSION_ID"

        if [ -f "$OUTPUT_FILE" ]; then
            FILE_SIZE=$(stat -f%z "$OUTPUT_FILE" 2>/dev/null || stat -c%s "$OUTPUT_FILE" 2>/dev/null)
            echo "💾 Saved: $OUTPUT_FILE ($FILE_SIZE bytes)"

            # Validate with VeraPDF
            echo "🔍 Validating with VeraPDF..."
            verapdf --format json --flavour ua1 "$OUTPUT_FILE" > "$OUTPUT_DIR/${BASENAME}_validation.json" 2>&1

            # Extract compliance percentage
            PASSED=$(grep -o '"passedRules":[0-9]*' "$OUTPUT_DIR/${BASENAME}_validation.json" | cut -d':' -f2)
            FAILED=$(grep -o '"failedRules":[0-9]*' "$OUTPUT_DIR/${BASENAME}_validation.json" | cut -d':' -f2)

            if [ -n "$PASSED" ] && [ -n "$FAILED" ]; then
                TOTAL=$((PASSED + FAILED))
                if [ $TOTAL -gt 0 ]; then
                    COMPLIANCE=$(awk "BEGIN {printf \"%.1f\", ($PASSED / $TOTAL) * 100}")
                    echo "📊 Compliance: $COMPLIANCE% ($PASSED passed, $FAILED failed)"

                    if [ "$FAILED" -eq 0 ]; then
                        echo "🎉 100% COMPLIANT!"
                    fi
                fi
            fi
        else
            echo "❌ Failed to download result"
        fi
    fi
done

echo ""
echo "=================================================="
echo "✅ All documents processed"
echo "📁 Results saved to: $OUTPUT_DIR"
