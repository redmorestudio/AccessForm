#!/bin/bash

# Final push to 100% compliance with CIDSet fix

BASE_URL="http://localhost:5008"
OUTPUT_DIR="final-100-percent"
mkdir -p "$OUTPUT_DIR"

echo "🚀 FINAL PUSH TO 100% COMPLIANCE"
echo "=================================="
echo ""

# Documents to process (using best versions from previous runs)
declare -A DOCS
DOCS["Building_Permit"]="remediation-best/Building Permit Application (PDF)_best_iter3_20251030-194447.pdf"
DOCS["Conditional_Use_Permit"]="remediation-best/Conditional Use Permit Form (PDF)_best_iter3_20251030-194924.pdf"
DOCS["Final_Routing"]="remediation-best/Final Routing - handout template 6.3.25_202506041028022821_best_iter3_20251030-195446.pdf"

# Process each document
for name in "${!DOCS[@]}"; do
    pdf="${DOCS[$name]}"

    echo ""
    echo "📄 Processing: $name"
    echo "   File: $pdf"

    # Start remediation
    RESPONSE=$(curl -s -X POST "$BASE_URL/api/v2/remediation/start" \
      -F "file=@$pdf" \
      -F "maxIterations=15" \
      -F "acceptableComplianceScore=100" \
      -F "detectStagnation=false" \
      -F "stopOnRegression=false")

    SESSION_ID=$(echo "$RESPONSE" | python3 -c "import sys, json; data=json.load(sys.stdin); print(data.get('sessionId', ''))" 2>/dev/null)

    if [ -z "$SESSION_ID" ]; then
        echo "   ❌ Failed to start session"
        continue
    fi

    echo "   ✅ Session: $SESSION_ID"

    # Monitor progress
    for i in {1..100}; do
        sleep 3
        STATUS_RESPONSE=$(curl -s "$BASE_URL/api/v2/remediation/status/$SESSION_ID")
        STATUS=$(echo "$STATUS_RESPONSE" | python3 -c "import sys, json; data=json.load(sys.stdin); print(data.get('status', ''))" 2>/dev/null)
        ITER=$(echo "$STATUS_RESPONSE" | python3 -c "import sys, json; data=json.load(sys.stdin); print(data.get('currentIteration', 'N/A'))" 2>/dev/null)
        SCORE=$(echo "$STATUS_RESPONSE" | python3 -c "import sys, json; data=json.load(sys.stdin); print(data.get('bestComplianceScore', 'N/A'))" 2>/dev/null)
        VIOLATIONS=$(echo "$STATUS_RESPONSE" | python3 -c "import sys, json; data=json.load(sys.stdin); print(data.get('bestViolationCount', 'N/A'))" 2>/dev/null)

        if [ $((i % 5)) -eq 0 ]; then
            echo "   [Poll $i] Iter: $ITER | Score: $SCORE% | Violations: $VIOLATIONS"
        fi

        if [ "$STATUS" = "Completed" ]; then
            echo "   ✅ Status: $STATUS"
            echo "   📊 Final Score: $SCORE%"
            echo "   📊 Final Violations: $VIOLATIONS"

            # Download result
            OUTPUT_FILE="$OUTPUT_DIR/${name}_FINAL.pdf"
            curl -s "$BASE_URL/api/v2/remediation/download/$SESSION_ID/best" -o "$OUTPUT_FILE"

            if [ -f "$OUTPUT_FILE" ]; then
                FILE_SIZE=$(stat -f%z "$OUTPUT_FILE" 2>/dev/null || stat -c%s "$OUTPUT_FILE" 2>/dev/null)
                echo "   💾 Saved: $OUTPUT_FILE ($FILE_SIZE bytes)"

                # Check if 100%
                if [ "$SCORE" = "100.0" ] || [ "$VIOLATIONS" = "0" ]; then
                    echo "   🎉 🎉 🎉 100% COMPLIANCE ACHIEVED! 🎉 🎉 🎉"
                fi
            fi

            break
        fi
    done

    echo ""
done

echo ""
echo "=================================="
echo "📊 FINAL RESULTS"
echo "=================================="
ls -lh "$OUTPUT_DIR/"
echo ""
echo "Check violation reports in remediation-best/"
