#!/bin/bash

# Process Green Bay PDFs through remediation system
# All PDFs are in: StateAssets/Wisconsin/Green Bay/

BASE_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"
GREEN_BAY_DIR="$BASE_DIR/StateAssets/Wisconsin/Green Bay"
SERVER_URL="http://localhost:5008"
REPORT_FILE="$BASE_DIR/green_bay_remediation_report.txt"

# Array of PDFs to process (in order)
declare -a PDFS=(
    "SOG 10 Call-In for Custodians - (rev.) Final_202411250945373753.pdf"
    "BASIC FMLA Guide_202409131109189018.pdf"
    "Remote Work & Alt. Schedule Request Form (Fillable Ver.)_202510021602086175.pdf"
    "City Trail Map (PDF).pdf"
)

echo "======================================" > "$REPORT_FILE"
echo "GREEN BAY PDF REMEDIATION REPORT" >> "$REPORT_FILE"
echo "Generated: $(date)" >> "$REPORT_FILE"
echo "======================================" >> "$REPORT_FILE"
echo "" >> "$REPORT_FILE"

total_initial_violations=0
total_final_violations=0
total_fixed=0
total_processing_time=0
successful_count=0

for PDF_NAME in "${PDFS[@]}"; do
    echo ""
    echo "=========================================="
    echo "Processing: $PDF_NAME"
    echo "=========================================="

    PDF_PATH="$GREEN_BAY_DIR/$PDF_NAME"
    OUTPUT_NAME="${PDF_NAME%.pdf}_REMEDIATED.pdf"
    OUTPUT_PATH="$GREEN_BAY_DIR/$OUTPUT_NAME"

    if [ ! -f "$PDF_PATH" ]; then
        echo "ERROR: File not found: $PDF_PATH"
        echo "ERROR: File not found: $PDF_PATH" >> "$REPORT_FILE"
        continue
    fi

    echo "Starting remediation..."
    START_TIME=$(date +%s)

    # Start remediation with maxIterations=5, enableGptFallback=false
    RESPONSE=$(curl -s -X POST "$SERVER_URL/api/v2/remediation/start" \
        -F "file=@$PDF_PATH" \
        -F "maxIterations=5")

    echo "Response received"

    # Extract session ID from JSON response
    SESSION_ID=$(echo "$RESPONSE" | python3 -c "import sys, json; print(json.load(sys.stdin).get('sessionId', ''))" 2>/dev/null)

    if [ -z "$SESSION_ID" ]; then
        echo "ERROR: Failed to start remediation session"
        echo "Response: $RESPONSE"
        echo "ERROR: Failed to start remediation for $PDF_NAME" >> "$REPORT_FILE"
        echo "" >> "$REPORT_FILE"
        continue
    fi

    echo "Session ID: $SESSION_ID"
    echo "Monitoring progress..."

    # Poll for completion
    STATUS="InProgress"
    LAST_ITERATION=0
    while [ "$STATUS" != "Completed" ] && [ "$STATUS" != "Failed" ]; do
        sleep 3
        STATUS_RESPONSE=$(curl -s "$SERVER_URL/api/v2/remediation/status/$SESSION_ID")

        STATUS=$(echo "$STATUS_RESPONSE" | python3 -c "import sys, json; print(json.load(sys.stdin).get('status', ''))" 2>/dev/null)
        CURRENT_ITER=$(echo "$STATUS_RESPONSE" | python3 -c "import sys, json; print(json.load(sys.stdin).get('currentIteration', 0))" 2>/dev/null)
        MAX_ITER=$(echo "$STATUS_RESPONSE" | python3 -c "import sys, json; print(json.load(sys.stdin).get('maxIterations', 0))" 2>/dev/null)
        CURRENT_VIOLS=$(echo "$STATUS_RESPONSE" | python3 -c "import sys, json; print(json.load(sys.stdin).get('currentViolations', 0))" 2>/dev/null)

        if [ "$CURRENT_ITER" != "$LAST_ITERATION" ]; then
            echo "  Iteration: $CURRENT_ITER/$MAX_ITER - Current violations: $CURRENT_VIOLS"
            LAST_ITERATION=$CURRENT_ITER
        fi

        if [ "$STATUS" == "Failed" ]; then
            echo "ERROR: Remediation failed"
            break
        fi
    done
    echo ""

    END_TIME=$(date +%s)
    PROCESSING_TIME=$((END_TIME - START_TIME))

    if [ "$STATUS" == "Failed" ]; then
        echo "ERROR: Remediation failed for $PDF_NAME" >> "$REPORT_FILE"
        echo "" >> "$REPORT_FILE"
        continue
    fi

    # Get final status with all metrics
    FINAL_STATUS=$(curl -s "$SERVER_URL/api/v2/remediation/status/$SESSION_ID")

    # Extract metrics using Python JSON parser
    INITIAL_VIOLATIONS=$(echo "$FINAL_STATUS" | python3 -c "import sys, json; print(json.load(sys.stdin).get('initialViolations', 0))" 2>/dev/null)
    BEST_VIOLATIONS=$(echo "$FINAL_STATUS" | python3 -c "import sys, json; print(json.load(sys.stdin).get('bestViolationCount', 0))" 2>/dev/null)
    BEST_SCORE=$(echo "$FINAL_STATUS" | python3 -c "import sys, json; print(json.load(sys.stdin).get('bestComplianceScore', 0))" 2>/dev/null)
    EXIT_REASON=$(echo "$FINAL_STATUS" | python3 -c "import sys, json; print(json.load(sys.stdin).get('exitReason', 'Unknown'))" 2>/dev/null)
    ITERATIONS=$(echo "$FINAL_STATUS" | python3 -c "import sys, json; print(json.load(sys.stdin).get('currentIteration', 0))" 2>/dev/null)

    # Calculate initial score (rough estimate based on violations)
    if [ "$INITIAL_VIOLATIONS" -gt 0 ]; then
        INITIAL_SCORE=$(echo "scale=2; 100 - ($INITIAL_VIOLATIONS / 10)" | bc)
        if (( $(echo "$INITIAL_SCORE < 0" | bc -l) )); then
            INITIAL_SCORE=0
        fi
    else
        INITIAL_SCORE=100
    fi

    FIXED_VIOLATIONS=$((INITIAL_VIOLATIONS - BEST_VIOLATIONS))

    # Update totals
    total_initial_violations=$((total_initial_violations + INITIAL_VIOLATIONS))
    total_final_violations=$((total_final_violations + BEST_VIOLATIONS))
    total_fixed=$((total_fixed + FIXED_VIOLATIONS))
    total_processing_time=$((total_processing_time + PROCESSING_TIME))
    successful_count=$((successful_count + 1))

    echo "Results:"
    echo "  Initial Violations: $INITIAL_VIOLATIONS (Estimated Score: $INITIAL_SCORE)"
    echo "  Best Violations: $BEST_VIOLATIONS (Score: $BEST_SCORE)"
    echo "  Fixed: $FIXED_VIOLATIONS violations"
    echo "  Iterations: $ITERATIONS"
    echo "  Exit Reason: $EXIT_REASON"
    echo "  Processing Time: ${PROCESSING_TIME}s"

    # Write to report
    echo "========================================" >> "$REPORT_FILE"
    echo "PDF: $PDF_NAME" >> "$REPORT_FILE"
    echo "========================================" >> "$REPORT_FILE"
    echo "Initial Compliance Score: ~$INITIAL_SCORE" >> "$REPORT_FILE"
    echo "Initial Violation Count: $INITIAL_VIOLATIONS" >> "$REPORT_FILE"
    echo "Final Compliance Score: $BEST_SCORE" >> "$REPORT_FILE"
    echo "Final Violation Count: $BEST_VIOLATIONS" >> "$REPORT_FILE"
    echo "Violations Fixed: $FIXED_VIOLATIONS" >> "$REPORT_FILE"
    echo "Iterations Completed: $ITERATIONS" >> "$REPORT_FILE"
    echo "Exit Reason: $EXIT_REASON" >> "$REPORT_FILE"
    echo "Processing Time: ${PROCESSING_TIME}s" >> "$REPORT_FILE"
    echo "" >> "$REPORT_FILE"

    # Extract violation types from iteration history
    echo "Iteration History:" >> "$REPORT_FILE"
    echo "$FINAL_STATUS" | python3 -c "
import sys, json
try:
    data = json.load(sys.stdin)
    history = data.get('iterationHistory', [])
    for item in history:
        iter_num = item.get('iterationNumber', 0)
        viols = item.get('violationCount', 0)
        score = item.get('complianceScore', 0)
        fixes = item.get('fixesApplied', 0)
        phases = ', '.join(item.get('phasesExecuted', []))
        print(f'  Iteration {iter_num}: {viols} violations (score: {score:.2f}) - {fixes} fixes applied')
        if phases:
            print(f'    Phases: {phases}')
except:
    pass
" >> "$REPORT_FILE"
    echo "" >> "$REPORT_FILE"

    # Download best PDF
    echo "Downloading remediated PDF..."
    curl -s -o "$OUTPUT_PATH" "$SERVER_URL/api/v2/remediation/download/$SESSION_ID/best"

    if [ -f "$OUTPUT_PATH" ]; then
        FILE_SIZE=$(stat -f%z "$OUTPUT_PATH" 2>/dev/null || stat -c%s "$OUTPUT_PATH" 2>/dev/null)
        echo "  Output saved: $OUTPUT_PATH ($FILE_SIZE bytes)"
        echo "Output File: $OUTPUT_NAME ($FILE_SIZE bytes)" >> "$REPORT_FILE"
    else
        echo "  WARNING: Output file not found"
        echo "WARNING: Output file not created" >> "$REPORT_FILE"
    fi
    echo "" >> "$REPORT_FILE"
done

# Write summary
echo "========================================" >> "$REPORT_FILE"
echo "OVERALL SUMMARY" >> "$REPORT_FILE"
echo "========================================" >> "$REPORT_FILE"
echo "Total PDFs Processed: $successful_count/${#PDFS[@]}" >> "$REPORT_FILE"
echo "Total Initial Violations: $total_initial_violations" >> "$REPORT_FILE"
echo "Total Final Violations: $total_final_violations" >> "$REPORT_FILE"
echo "Total Violations Fixed: $total_fixed" >> "$REPORT_FILE"
echo "Total Processing Time: ${total_processing_time}s" >> "$REPORT_FILE"

if [ $successful_count -gt 0 ]; then
    AVG_TIME=$((total_processing_time / successful_count))
    echo "Average Processing Time: ${AVG_TIME}s per PDF" >> "$REPORT_FILE"
fi

echo "" >> "$REPORT_FILE"

if [ $total_initial_violations -gt 0 ]; then
    IMPROVEMENT_PCT=$(echo "scale=2; ($total_fixed * 100) / $total_initial_violations" | bc)
    echo "Overall Improvement: ${IMPROVEMENT_PCT}% of violations fixed" >> "$REPORT_FILE"
fi

echo ""
echo "=========================================="
echo "PROCESSING COMPLETE"
echo "=========================================="
echo "Total PDFs: ${#PDFS[@]}"
echo "Successfully Processed: $successful_count"
echo "Total Violations Fixed: $total_fixed"
echo "Report saved to: $REPORT_FILE"
echo ""
cat "$REPORT_FILE"
