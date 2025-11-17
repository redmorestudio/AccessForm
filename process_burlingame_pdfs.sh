#!/bin/bash

# Process all Burlingame PDFs through remediation system
# Usage: ./process_burlingame_pdfs.sh

BASE_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/StateAssets/California/Burlingame"
API_BASE="http://localhost:5008/api/v2/remediation"
RESULTS_FILE="/tmp/burlingame_remediation_results.txt"

echo "========================================" > "$RESULTS_FILE"
echo "Burlingame PDF Remediation Results" >> "$RESULTS_FILE"
echo "Started: $(date)" >> "$RESULTS_FILE"
echo "========================================" >> "$RESULTS_FILE"
echo "" >> "$RESULTS_FILE"

# Array of PDFs to process
declare -a PDFS=(
    "Final Routing - handout template 6.3.25_202506041028022821.pdf"
    "Conditional Use Permit Form (PDF).pdf"
    "Minor Design Review Application Form (PDF).pdf"
    "Building Permit Application (PDF).pdf"
    "Hillside Area Construction Permit Application Form (PDF).pdf"
    "Ricardo Ortiz Form 497 dated 9.26.17 (PDF).pdf"
    "Form Authorizing Change in Preparer, Submitter, or Signer of Rebuttal Arguments (PDF).pdf"
)

process_pdf() {
    local pdf_name="$1"
    local pdf_num="$2"
    local pdf_path="$BASE_DIR/$pdf_name"

    echo "========================================"
    echo "Processing PDF $pdf_num of ${#PDFS[@]}: $pdf_name"
    echo "========================================"

    echo "" >> "$RESULTS_FILE"
    echo "========================================" >> "$RESULTS_FILE"
    echo "PDF $pdf_num: $pdf_name" >> "$RESULTS_FILE"
    echo "========================================" >> "$RESULTS_FILE"

    # Check if file exists
    if [ ! -f "$pdf_path" ]; then
        echo "ERROR: File not found: $pdf_path" | tee -a "$RESULTS_FILE"
        return 1
    fi

    # Start remediation session
    echo "Starting remediation session..."
    local start_response=$(curl -s -X POST "$API_BASE/start" \
        -F "file=@$pdf_path" \
        -F "acceptableComplianceScore=100")

    local session_id=$(echo "$start_response" | grep -o '"sessionId":"[^"]*"' | cut -d'"' -f4)

    if [ -z "$session_id" ]; then
        echo "ERROR: Failed to start session" | tee -a "$RESULTS_FILE"
        echo "Response: $start_response" | tee -a "$RESULTS_FILE"
        return 1
    fi

    echo "Session ID: $session_id"
    echo "Session ID: $session_id" >> "$RESULTS_FILE"

    # Extract initial state
    local initial_violations=$(echo "$start_response" | grep -o '"totalViolations":[0-9]*' | head -1 | cut -d':' -f2)
    local initial_compliance=$(echo "$start_response" | grep -o '"complianceScore":[0-9.]*' | head -1 | cut -d':' -f2)

    echo "Initial State:" | tee -a "$RESULTS_FILE"
    echo "  Violations: $initial_violations" | tee -a "$RESULTS_FILE"
    echo "  Compliance: $initial_compliance%" | tee -a "$RESULTS_FILE"

    # Poll status until complete
    local max_polls=120  # 10 minutes max (5 second intervals)
    local poll_count=0
    local status="in_progress"

    while [ "$status" = "in_progress" ] && [ $poll_count -lt $max_polls ]; do
        sleep 5
        poll_count=$((poll_count + 1))

        local status_response=$(curl -s "$API_BASE/status/$session_id")
        status=$(echo "$status_response" | grep -o '"status":"[^"]*"' | cut -d'"' -f4)

        local current_iteration=$(echo "$status_response" | grep -o '"currentIteration":[0-9]*' | cut -d':' -f2)
        local current_violations=$(echo "$status_response" | grep -o '"totalViolations":[0-9]*' | tail -1 | cut -d':' -f2)
        local current_compliance=$(echo "$status_response" | grep -o '"complianceScore":[0-9.]*' | tail -1 | cut -d':' -f2)

        echo "  Iteration $current_iteration: $current_violations violations ($current_compliance% compliant)"

        if [ "$status" != "in_progress" ]; then
            break
        fi
    done

    # Get final status
    echo ""
    echo "Fetching final status..."
    local final_status=$(curl -s "$API_BASE/status/$session_id")

    # Extract final metrics
    local final_violations=$(echo "$final_status" | grep -o '"totalViolations":[0-9]*' | tail -1 | cut -d':' -f2)
    local final_compliance=$(echo "$final_status" | grep -o '"complianceScore":[0-9.]*' | tail -1 | cut -d':' -f2)
    local final_iteration=$(echo "$final_status" | grep -o '"currentIteration":[0-9]*' | cut -d':' -f2)
    local final_status_text=$(echo "$final_status" | grep -o '"status":"[^"]*"' | cut -d'"' -f4)

    echo "" >> "$RESULTS_FILE"
    echo "Final State:" >> "$RESULTS_FILE"
    echo "  Status: $final_status_text" >> "$RESULTS_FILE"
    echo "  Iterations: $final_iteration" >> "$RESULTS_FILE"
    echo "  Violations: $final_violations" >> "$RESULTS_FILE"
    echo "  Compliance: $final_compliance%" >> "$RESULTS_FILE"

    # Show improvement
    if [ -n "$initial_violations" ] && [ -n "$final_violations" ]; then
        local violations_fixed=$((initial_violations - final_violations))
        local compliance_gain=$(echo "$final_compliance - $initial_compliance" | bc)
        echo "  Violations Fixed: $violations_fixed" >> "$RESULTS_FILE"
        echo "  Compliance Gain: +$compliance_gain%" >> "$RESULTS_FILE"
    fi

    # Save full status to separate file
    echo "$final_status" > "/tmp/burlingame_pdf_${pdf_num}_status.json"
    echo "  Full status saved to: /tmp/burlingame_pdf_${pdf_num}_status.json" >> "$RESULTS_FILE"

    echo ""
    echo "Final Status for $pdf_name:"
    echo "  Status: $final_status_text"
    echo "  Iterations: $final_iteration"
    echo "  Violations: $initial_violations → $final_violations"
    echo "  Compliance: $initial_compliance% → $final_compliance%"
    echo ""
}

# Process each PDF
for i in "${!PDFS[@]}"; do
    pdf_num=$((i + 1))
    process_pdf "${PDFS[$i]}" "$pdf_num"

    # Add delay between PDFs to avoid overload
    if [ $pdf_num -lt ${#PDFS[@]} ]; then
        echo "Waiting 10 seconds before next PDF..."
        sleep 10
    fi
done

echo "" >> "$RESULTS_FILE"
echo "========================================" >> "$RESULTS_FILE"
echo "Completed: $(date)" >> "$RESULTS_FILE"
echo "========================================" >> "$RESULTS_FILE"

echo ""
echo "========================================"
echo "All PDFs processed!"
echo "Results saved to: $RESULTS_FILE"
echo "========================================"
cat "$RESULTS_FILE"
