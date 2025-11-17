#!/bin/bash

# Alexandria PDF Remediation Script - Two Passes (Overnight Run)
# Processes 43 PDFs through two remediation passes

BASE_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"
ALEXANDRIA_DIR="$BASE_DIR/StateAssets/Virginia/Alexandria"
API_URL="http://localhost:5008/api/v2/remediation/start"
LOG_FILE="$BASE_DIR/alexandria_remediation_$(date +%Y%m%d_%H%M%S).log"
RESULTS_DIR="$BASE_DIR/remediation-best"

# Ensure we're in the right directory for saving results
cd "$BASE_DIR"

echo "=========================================" | tee "$LOG_FILE"
echo "Alexandria PDF Remediation - Overnight Run" | tee -a "$LOG_FILE"
echo "Started: $(date)" | tee -a "$LOG_FILE"
echo "=========================================" | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"

# Get list of all PDFs (macOS compatible)
pdfs=()
while IFS= read -r file; do
    pdfs+=("$(basename "$file")")
done < <(ls "$ALEXANDRIA_DIR"/*.pdf 2>/dev/null)

echo "Found ${#pdfs[@]} PDFs to process" | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"

# Track statistics
TOTAL_PDFS=${#pdfs[@]}
PROCESSED_FIRST=0
PROCESSED_SECOND=0
FAILED_FIRST=0
FAILED_SECOND=0

# First Pass
echo "===========================================" | tee -a "$LOG_FILE"
echo "=== FIRST PASS ===" | tee -a "$LOG_FILE"
echo "===========================================" | tee -a "$LOG_FILE"

for i in "${!pdfs[@]}"; do
    pdf="${pdfs[$i]}"
    echo "[$(($i+1))/$TOTAL_PDFS] Processing: $pdf" | tee -a "$LOG_FILE"
    echo "  Time: $(date +%H:%M:%S)" | tee -a "$LOG_FILE"

    # Start remediation
    response=$(curl -s -X POST "$API_URL" \
        -F "file=@$ALEXANDRIA_DIR/$pdf" \
        -F "maxIterations=5" \
        -F "enableGptFallback=true" 2>&1)

    session_id=$(echo "$response" | grep -o '"sessionId":"[^"]*' | cut -d'"' -f4)

    if [ -z "$session_id" ]; then
        echo "  ERROR: Failed to start remediation" | tee -a "$LOG_FILE"
        echo "  Response: $response" >> "$LOG_FILE"
        ((FAILED_FIRST++))
        continue
    fi

    echo "  Session ID: $session_id" | tee -a "$LOG_FILE"

    # Wait for completion with timeout (10 minutes max)
    TIMEOUT=600
    ELAPSED=0
    INTERVAL=10

    while [ $ELAPSED -lt $TIMEOUT ]; do
        sleep $INTERVAL
        ELAPSED=$((ELAPSED + INTERVAL))

        status_response=$(curl -s -X GET "http://localhost:5008/api/v2/remediation/status/$session_id" 2>&1)
        status=$(echo "$status_response" | grep -o '"status":"[^"]*' | cut -d'"' -f4)

        if [ "$status" == "Completed" ]; then
            echo "  Status: Completed (${ELAPSED}s)" | tee -a "$LOG_FILE"
            ((PROCESSED_FIRST++))

            # Extract compliance info
            compliance=$(echo "$status_response" | grep -o '"bestComplianceScore":[0-9.]*' | cut -d: -f2)
            violations=$(echo "$status_response" | grep -o '"bestViolationCount":[0-9]*' | cut -d: -f2)

            if [ ! -z "$compliance" ] && [ ! -z "$violations" ]; then
                echo "  Result: ${compliance}% compliant, ${violations} violations" | tee -a "$LOG_FILE"
            fi
            break
        elif [ "$status" == "Failed" ]; then
            echo "  Status: Failed" | tee -a "$LOG_FILE"
            ((FAILED_FIRST++))
            break
        fi

        # Show progress dots
        if [ $((ELAPSED % 30)) -eq 0 ]; then
            echo "    Still processing... (${ELAPSED}s)" >> "$LOG_FILE"
        fi
    done

    if [ $ELAPSED -ge $TIMEOUT ]; then
        echo "  TIMEOUT: Exceeded 10 minutes" | tee -a "$LOG_FILE"
        ((FAILED_FIRST++))
    fi

    echo "" | tee -a "$LOG_FILE"
done

echo "First Pass Summary:" | tee -a "$LOG_FILE"
echo "  Processed: $PROCESSED_FIRST/$TOTAL_PDFS" | tee -a "$LOG_FILE"
echo "  Failed: $FAILED_FIRST" | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"

# Wait before second pass
sleep 10

# Second Pass - Process best results
echo "===========================================" | tee -a "$LOG_FILE"
echo "=== SECOND PASS ===" | tee -a "$LOG_FILE"
echo "===========================================" | tee -a "$LOG_FILE"

PROCESSED_SECOND=0
FAILED_SECOND=0

for i in "${!pdfs[@]}"; do
    pdf="${pdfs[$i]}"
    base_name="${pdf%.*}"

    # Find the best result from first pass
    best_file=$(ls -t "$RESULTS_DIR/${base_name}"*best*.pdf 2>/dev/null | head -1)

    if [ -z "$best_file" ] || [ ! -f "$best_file" ]; then
        echo "[$(($i+1))/$TOTAL_PDFS] Skipping $pdf - no first pass result found" | tee -a "$LOG_FILE"
        continue
    fi

    echo "[$(($i+1))/$TOTAL_PDFS] Second pass: $(basename "$best_file")" | tee -a "$LOG_FILE"
    echo "  Time: $(date +%H:%M:%S)" | tee -a "$LOG_FILE"

    # Start second remediation
    response=$(curl -s -X POST "$API_URL" \
        -F "file=@$best_file" \
        -F "maxIterations=5" \
        -F "enableGptFallback=true" 2>&1)

    session_id=$(echo "$response" | grep -o '"sessionId":"[^"]*' | cut -d'"' -f4)

    if [ -z "$session_id" ]; then
        echo "  ERROR: Failed to start second pass" | tee -a "$LOG_FILE"
        ((FAILED_SECOND++))
        continue
    fi

    echo "  Session ID: $session_id" | tee -a "$LOG_FILE"

    # Wait for completion with timeout
    TIMEOUT=600
    ELAPSED=0
    INTERVAL=10

    while [ $ELAPSED -lt $TIMEOUT ]; do
        sleep $INTERVAL
        ELAPSED=$((ELAPSED + INTERVAL))

        status_response=$(curl -s -X GET "http://localhost:5008/api/v2/remediation/status/$session_id" 2>&1)
        status=$(echo "$status_response" | grep -o '"status":"[^"]*' | cut -d'"' -f4)

        if [ "$status" == "Completed" ]; then
            echo "  Status: Completed (${ELAPSED}s)" | tee -a "$LOG_FILE"
            ((PROCESSED_SECOND++))

            # Extract compliance info
            compliance=$(echo "$status_response" | grep -o '"bestComplianceScore":[0-9.]*' | cut -d: -f2)
            violations=$(echo "$status_response" | grep -o '"bestViolationCount":[0-9]*' | cut -d: -f2)

            if [ ! -z "$compliance" ] && [ ! -z "$violations" ]; then
                echo "  Final: ${compliance}% compliant, ${violations} violations" | tee -a "$LOG_FILE"
            fi
            break
        elif [ "$status" == "Failed" ]; then
            echo "  Status: Failed" | tee -a "$LOG_FILE"
            ((FAILED_SECOND++))
            break
        fi

        if [ $((ELAPSED % 30)) -eq 0 ]; then
            echo "    Still processing... (${ELAPSED}s)" >> "$LOG_FILE"
        fi
    done

    if [ $ELAPSED -ge $TIMEOUT ]; then
        echo "  TIMEOUT: Exceeded 10 minutes" | tee -a "$LOG_FILE"
        ((FAILED_SECOND++))
    fi

    echo "" | tee -a "$LOG_FILE"
done

# Final Summary
echo "===========================================" | tee -a "$LOG_FILE"
echo "FINAL SUMMARY" | tee -a "$LOG_FILE"
echo "===========================================" | tee -a "$LOG_FILE"
echo "Completed: $(date)" | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"
echo "First Pass:" | tee -a "$LOG_FILE"
echo "  Processed: $PROCESSED_FIRST/$TOTAL_PDFS" | tee -a "$LOG_FILE"
echo "  Failed: $FAILED_FIRST" | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"
echo "Second Pass:" | tee -a "$LOG_FILE"
echo "  Processed: $PROCESSED_SECOND" | tee -a "$LOG_FILE"
echo "  Failed: $FAILED_SECOND" | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"
echo "Results saved in: $RESULTS_DIR" | tee -a "$LOG_FILE"
echo "Log file: $LOG_FILE" | tee -a "$LOG_FILE"
echo "===========================================" | tee -a "$LOG_FILE"

# Generate summary report
SUMMARY_FILE="$BASE_DIR/alexandria_summary_$(date +%Y%m%d_%H%M%S).txt"
echo "Generating summary report..." | tee -a "$LOG_FILE"

echo "Alexandria Remediation Summary" > "$SUMMARY_FILE"
echo "==============================" >> "$SUMMARY_FILE"
echo "Date: $(date)" >> "$SUMMARY_FILE"
echo "Total PDFs: $TOTAL_PDFS" >> "$SUMMARY_FILE"
echo "" >> "$SUMMARY_FILE"

# List all final results
echo "Final Compliance Results:" >> "$SUMMARY_FILE"
for pdf in "${pdfs[@]}"; do
    base_name="${pdf%.*}"
    latest=$(ls -t "$RESULTS_DIR/${base_name}"*best*.pdf 2>/dev/null | head -1)
    if [ ! -z "$latest" ] && [ -f "$latest" ]; then
        violations_file="${latest%.pdf}_violations.txt"
        if [ -f "$violations_file" ]; then
            compliance=$(grep "Compliance Score:" "$violations_file" 2>/dev/null | cut -d: -f2 | tr -d ' %')
            violations=$(grep "Total Violations:" "$violations_file" 2>/dev/null | cut -d: -f2 | tr -d ' ')
            if [ ! -z "$compliance" ]; then
                echo "  $pdf: ${compliance}% compliant (${violations} violations)" >> "$SUMMARY_FILE"
            fi
        fi
    fi
done

echo "" >> "$SUMMARY_FILE"
echo "Summary saved to: $SUMMARY_FILE" | tee -a "$LOG_FILE"

echo "Alexandria overnight processing complete!" | tee -a "$LOG_FILE"