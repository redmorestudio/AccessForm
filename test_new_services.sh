#!/bin/bash

# Test script for new remediation services
# Tests 3 PDFs with 2 complete remediation passes each

BASE_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"
OUTPUT_DIR="$BASE_DIR/test-output-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$OUTPUT_DIR"

API_URL="http://localhost:5008/api/v2/remediation/start"

echo "================================"
echo "Testing New Remediation Services"
echo "================================"
echo "Output directory: $OUTPUT_DIR"
echo ""

# Function to start remediation and wait for completion
remediate_pdf() {
    local name=$1
    local input_pdf=$2
    local pass=$3
    local output_prefix="$OUTPUT_DIR/${name}_pass${pass}"

    echo "[$name Pass $pass] Starting remediation..."
    echo "  Input: $(basename "$input_pdf")"

    # Start remediation
    response=$(curl -s -X POST "$API_URL" \
        -F "file=@$input_pdf" \
        -F "maxIterations=5")

    session_id=$(echo "$response" | jq -r '.sessionId')

    if [ -z "$session_id" ] || [ "$session_id" == "null" ]; then
        echo "  ERROR: Failed to start remediation"
        echo "  Response: $response"
        return 1
    fi

    echo "  Session ID: $session_id"

    # Poll for completion
    max_wait=600  # 10 minutes
    elapsed=0
    while [ $elapsed -lt $max_wait ]; do
        status_response=$(curl -s "http://localhost:5008/api/v2/remediation/status/$session_id")
        status=$(echo "$status_response" | jq -r '.status')

        if [ "$status" == "Completed" ]; then
            echo "  Status: Completed!"

            # Get final results
            final_violations=$(echo "$status_response" | jq -r '.currentViolations // "N/A"')
            initial_violations=$(echo "$status_response" | jq -r '.initialViolations // "N/A"')
            best_violations=$(echo "$status_response" | jq -r '.bestViolationCount // "N/A"')
            compliance=$(echo "$status_response" | jq -r '.complianceScore // "N/A"')
            best_compliance=$(echo "$status_response" | jq -r '.bestComplianceScore // "N/A"')
            iterations=$(echo "$status_response" | jq -r '.currentIteration // "N/A"')
            exit_reason=$(echo "$status_response" | jq -r '.exitReason // "N/A"')
            is_compliant=$(echo "$status_response" | jq -r '.isCompliant // false')

            echo "  Initial Violations: $initial_violations"
            echo "  Final Violations: $final_violations"
            echo "  Best Violations: $best_violations"
            echo "  Final Compliance: $compliance%"
            echo "  Best Compliance: $best_compliance%"
            echo "  Iterations: $iterations"
            echo "  Exit Reason: $exit_reason"
            echo "  Is Compliant: $is_compliant"

            # Download the best PDF
            curl -s "http://localhost:5008/api/v2/remediation/download/$session_id/best" -o "${output_prefix}_output.pdf"

            # Save status for later analysis
            echo "$status_response" > "${output_prefix}_status.json"

            echo "  Output PDF: ${output_prefix}_output.pdf"
            echo "  Status JSON: ${output_prefix}_status.json"

            # Return the output PDF path for next pass
            echo "${output_prefix}_output.pdf"
            return 0
        elif [ "$status" == "Failed" ]; then
            echo "  Status: Failed"
            return 1
        else
            # Still running
            current_iter=$(echo "$status_response" | jq -r '.currentIteration // "?"')
            current_violations=$(echo "$status_response" | jq -r '.currentViolations // "?"')
            echo "  Status: $status (Iteration $current_iter, Violations: $current_violations)"
        fi

        sleep 10
        elapsed=$((elapsed + 10))
    done

    echo "  TIMEOUT: Remediation did not complete in $max_wait seconds"
    return 1
}

# Test PDF 1: Hillside
echo ""
echo "========================================"
echo "Testing: Hillside"
echo "========================================"
PDF1="$BASE_DIR/remediation-best/Hillside Area Construction Permit Application Form (PDF)_best_iter3_20251029-154913.pdf"
output_pdf_pass1=$(remediate_pdf "Hillside" "$PDF1" 1)
if [ $? -eq 0 ] && [ -n "$output_pdf_pass1" ]; then
    echo ""
    echo "  Pass 1 completed successfully!"
    echo ""
    sleep 5
    output_pdf_pass2=$(remediate_pdf "Hillside" "$output_pdf_pass1" 2)
    if [ $? -eq 0 ]; then
        echo ""
        echo "  Pass 2 completed successfully!"
    else
        echo ""
        echo "  Pass 2 failed or timed out"
    fi
else
    echo ""
    echo "  Pass 1 failed or timed out - skipping Pass 2"
fi
echo ""
sleep 5

# Test PDF 2: Minor Design Review
echo ""
echo "========================================"
echo "Testing: MinorDesign"
echo "========================================"
PDF2="$BASE_DIR/remediation-best/Minor Design Review Application Form (PDF)_best_iter3_20251029-154843.pdf"
output_pdf_pass1=$(remediate_pdf "MinorDesign" "$PDF2" 1)
if [ $? -eq 0 ] && [ -n "$output_pdf_pass1" ]; then
    echo ""
    echo "  Pass 1 completed successfully!"
    echo ""
    sleep 5
    output_pdf_pass2=$(remediate_pdf "MinorDesign" "$output_pdf_pass1" 2)
    if [ $? -eq 0 ]; then
        echo ""
        echo "  Pass 2 completed successfully!"
    else
        echo ""
        echo "  Pass 2 failed or timed out"
    fi
else
    echo ""
    echo "  Pass 1 failed or timed out - skipping Pass 2"
fi
echo ""
sleep 5

# Test PDF 3: Ricardo Ortiz Form 497
echo ""
echo "========================================"
echo "Testing: Ricardo"
echo "========================================"
PDF3="$BASE_DIR/remediation-best/Ricardo Ortiz Form 497 dated 9.26.17 (PDF)_best_iter3_20251029-154927.pdf"
output_pdf_pass1=$(remediate_pdf "Ricardo" "$PDF3" 1)
if [ $? -eq 0 ] && [ -n "$output_pdf_pass1" ]; then
    echo ""
    echo "  Pass 1 completed successfully!"
    echo ""
    sleep 5
    output_pdf_pass2=$(remediate_pdf "Ricardo" "$output_pdf_pass1" 2)
    if [ $? -eq 0 ]; then
        echo ""
        echo "  Pass 2 completed successfully!"
    else
        echo ""
        echo "  Pass 2 failed or timed out"
    fi
else
    echo ""
    echo "  Pass 1 failed or timed out - skipping Pass 2"
fi
echo ""

echo ""
echo "================================"
echo "Testing Complete"
echo "================================"
echo ""
echo "Now checking logs for new service activity..."
echo ""

# Check logs for evidence of new services
LOG_FILE="$BASE_DIR/Logs/accessform.log"

echo "Searching for [EMPTY-FORM-FIX] messages..."
grep "\[EMPTY-FORM-FIX\]" "$LOG_FILE" | tail -30
echo ""

echo "Searching for [XOBJECT-FIX] messages..."
grep "\[XOBJECT-FIX\]" "$LOG_FILE" | tail -30
echo ""

echo "All results saved to: $OUTPUT_DIR"
echo ""
echo "Summary of status files:"
ls -lh "$OUTPUT_DIR"/*.json 2>/dev/null || echo "No status files generated"
