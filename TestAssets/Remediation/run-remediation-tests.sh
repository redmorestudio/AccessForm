#!/bin/bash
# Closed-Loop Remediation Test Harness
# Tests PDFs through the web API and generates a report

set -e

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Configuration
API_URL="http://localhost:5001/api/convert-with-config"
TIMESTAMP=$(date +"%Y-%m-%d_%H-%M-%S")
RESULTS_DIR="TestAssets/Remediation/Results/$TIMESTAMP"
SUMMARY_FILE="$RESULTS_DIR/summary.json"

# Counters
TOTAL=0
SUCCESS=0
PARTIAL=0
FAILED=0
ERROR=0

# Create results directory
mkdir -p "$RESULTS_DIR"

echo "=================================================="
echo "   Closed-Loop Remediation Test Suite"
echo "=================================================="
echo "Started: $(date)"
echo "Results: $RESULTS_DIR"
echo ""

# Function to test a single PDF
test_pdf() {
    local input_pdf="$1"
    local category="$2"  # "Known-Pass" or "Known-Fail"
    local test_name=$(basename "$input_pdf" .pdf)
    local test_dir="$RESULTS_DIR/$test_name"

    mkdir -p "$test_dir"
    cp "$input_pdf" "$test_dir/input.pdf"

    echo -n "Testing: $test_name ... "

    TOTAL=$((TOTAL + 1))

    # Call API with curl
    local response_file="$test_dir/api-response.json"
    local http_code=$(curl -s -w "%{http_code}" -o "$response_file" \
        -X POST "$API_URL" \
        -F "file=@$input_pdf" \
        -F "useSyncfusion=false" \
        -F "useClaudeVision=false" \
        -F "useGoogle=false" \
        -F "useClaudeValidation=false" \
        -F "useAsposeFontEmbed=true" \
        --max-time 600)  # 10 minute timeout

    # Check HTTP response
    if [ "$http_code" != "200" ]; then
        echo -e "${RED}ERROR${NC} (HTTP $http_code)"
        ERROR=$((ERROR + 1))
        echo "{\"error\": \"HTTP $http_code\", \"category\": \"$category\"}" > "$test_dir/result.json"
        return
    fi

    # Parse response using jq (if available)
    if command -v jq &> /dev/null; then
        local compliant=$(jq -r '.report.pdfUACompliant // false' "$response_file")
        local violations_fixed=$(jq -r '.report.violationsFixed // 0' "$response_file")
        local violations_remaining=$(jq -r '.report.violationsRemaining // 0' "$response_file")
        local iterations=$(jq -r '.report.remediationIterations // 0' "$response_file")
        local exit_reason=$(jq -r '.report.exitReason // "Unknown"' "$response_file")
        local duration=$(jq -r '.report.processingTime // 0' "$response_file")

        # Extract PDFs
        jq -r '.accessiblePdf.data' "$response_file" | base64 -d > "$test_dir/output.pdf" 2>/dev/null || true

        # Determine result
        if [ "$compliant" = "true" ]; then
            echo -e "${GREEN}SUCCESS${NC} ($iterations iterations, $violations_fixed fixed)"
            SUCCESS=$((SUCCESS + 1))
            result_status="success"
        elif [ "$violations_fixed" -gt 0 ]; then
            echo -e "${YELLOW}PARTIAL${NC} ($iterations iterations, $violations_fixed fixed, $violations_remaining remain)"
            PARTIAL=$((PARTIAL + 1))
            result_status="partial"
        else
            echo -e "${RED}FAILED${NC} (0 violations fixed)"
            FAILED=$((FAILED + 1))
            result_status="failed"
        fi

        # Write result summary
        cat > "$test_dir/result.json" <<EOF
{
  "testName": "$test_name",
  "category": "$category",
  "status": "$result_status",
  "compliant": $compliant,
  "violationsFixed": $violations_fixed,
  "violationsRemaining": $violations_remaining,
  "iterations": $iterations,
  "exitReason": "$exit_reason",
  "duration": $duration
}
EOF
    else
        # jq not available, just check if response exists
        if [ -s "$response_file" ]; then
            echo -e "${YELLOW}COMPLETED${NC} (install jq for detailed analysis)"
            PARTIAL=$((PARTIAL + 1))
        else
            echo -e "${RED}FAILED${NC} (empty response)"
            FAILED=$((FAILED + 1))
        fi
    fi
}

# Check if server is running
echo "Checking server availability..."
if ! curl -s -f "$API_URL" > /dev/null 2>&1; then
    if ! curl -s -f "http://localhost:5001" > /dev/null 2>&1; then
        echo -e "${RED}ERROR:${NC} Server not running at http://localhost:5001"
        echo "Start the server with:"
        echo "  ASPNETCORE_URLS=\"http://localhost:5001\" dotnet run --project AccessFormServer.csproj"
        exit 1
    fi
fi
echo -e "${GREEN}✓${NC} Server is running"
echo ""

# Test Known-Pass PDFs
KNOWN_PASS_DIR="TestAssets/Remediation/Known-Pass"
if [ -d "$KNOWN_PASS_DIR" ] && [ "$(ls -A $KNOWN_PASS_DIR/*.pdf 2>/dev/null)" ]; then
    echo "=================================================="
    echo "Testing Known-Pass PDFs (Should stay compliant)"
    echo "=================================================="
    for pdf in "$KNOWN_PASS_DIR"/*.pdf; do
        [ -e "$pdf" ] || continue
        test_pdf "$pdf" "Known-Pass"
    done
    echo ""
fi

# Test Known-Fail PDFs
KNOWN_FAIL_DIR="TestAssets/Remediation/Known-Fail"
if [ -d "$KNOWN_FAIL_DIR" ] && [ "$(ls -A $KNOWN_FAIL_DIR/*.pdf 2>/dev/null)" ]; then
    echo "=================================================="
    echo "Testing Known-Fail PDFs (Should be remediated)"
    echo "=================================================="
    for pdf in "$KNOWN_FAIL_DIR"/*.pdf; do
        [ -e "$pdf" ] || continue
        test_pdf "$pdf" "Known-Fail"
    done
    echo ""
fi

# Generate summary
echo "=================================================="
echo "   Test Summary"
echo "=================================================="
echo -e "Total:   $TOTAL"
echo -e "${GREEN}Success: $SUCCESS${NC}"
echo -e "${YELLOW}Partial: $PARTIAL${NC}"
echo -e "${RED}Failed:  $FAILED${NC}"
echo -e "${RED}Errors:  $ERROR${NC}"
echo ""

# Calculate success rate
if [ $TOTAL -gt 0 ]; then
    SUCCESS_RATE=$(( (SUCCESS * 100) / TOTAL ))
    echo "Success Rate: $SUCCESS_RATE%"
    echo ""
fi

# Write summary JSON
if command -v jq &> /dev/null; then
    cat > "$SUMMARY_FILE" <<EOF
{
  "timestamp": "$TIMESTAMP",
  "total": $TOTAL,
  "success": $SUCCESS,
  "partial": $PARTIAL,
  "failed": $FAILED,
  "errors": $ERROR,
  "successRate": $([ $TOTAL -gt 0 ] && echo "$(( (SUCCESS * 100) / TOTAL ))" || echo "0")
}
EOF
    echo "Summary written to: $SUMMARY_FILE"
fi

echo "Results directory: $RESULTS_DIR"
echo ""
echo "Completed: $(date)"
echo "=================================================="

# Exit with error if any tests failed
if [ $ERROR -gt 0 ] || [ $FAILED -gt 0 ]; then
    exit 1
fi

exit 0
