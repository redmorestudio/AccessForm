#!/bin/bash

# Pleasant Hill PDF Remediation Script - Two Passes
# This script processes all PDFs in the Pleasant Hill directory through two remediation passes

BASE_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/StateAssets/California/Pleasant Hill"
API_URL="http://localhost:5008/api/v2/remediation/start"

echo "========================================="
echo "Pleasant Hill PDF Remediation - Two Passes"
echo "========================================="
echo ""

# Create output directory for first pass
mkdir -p "$BASE_DIR/first_pass"
mkdir -p "$BASE_DIR/second_pass"

# Array of PDF files
declare -a pdfs=(
    "ADA Grievance Procedures & Form_202304141330489967.pdf"
    "ADA Request for Accommodation Policy, Procedures & Form_202304141329133585.pdf"
    "Complaint Form (English).pdf"
    "FILLABLE CLAIM FORM_2025_202507111151446698.pdf"
    "TITLE VI CIVIL RIGHTS ACT OF 1964 PROGRAM.pdf"
)

echo "Found ${#pdfs[@]} PDFs to process"
echo ""

# First Pass
echo "=== FIRST PASS ==="
for pdf in "${pdfs[@]}"; do
    echo "Processing: $pdf"

    # Start remediation
    response=$(curl -s -X POST "$API_URL" \
        -F "file=@$BASE_DIR/$pdf" \
        -F "maxIterations=5" \
        -F "enableGptFallback=true")

    session_id=$(echo "$response" | grep -o '"sessionId":"[^"]*' | cut -d'"' -f4)

    if [ -z "$session_id" ]; then
        echo "  ERROR: Failed to start remediation for $pdf"
        continue
    fi

    echo "  Session ID: $session_id"
    echo -n "  Processing"

    # Wait for completion
    while true; do
        sleep 5
        echo -n "."

        status_response=$(curl -s -X GET "http://localhost:5008/api/v2/remediation/status/$session_id")
        status=$(echo "$status_response" | grep -o '"status":"[^"]*' | cut -d'"' -f4)

        if [ "$status" == "Completed" ]; then
            echo " Done!"
            break
        elif [ "$status" == "Failed" ]; then
            echo " Failed!"
            break
        fi
    done

    echo ""
done

echo ""
echo "First pass complete. Check ./remediation-best/ for results."
echo ""

# Wait a moment before second pass
sleep 5

echo "=== SECOND PASS ==="
echo "Processing best results from first pass through remediation again..."

# Get the best results from first pass
cd "$BASE_DIR/../../remediation-best"

for pdf in "${pdfs[@]}"; do
    # Find the best result from first pass
    base_name="${pdf%.*}"
    best_file=$(ls -t "${base_name}"*best*.pdf 2>/dev/null | head -1)

    if [ -z "$best_file" ]; then
        echo "No best result found for $pdf, skipping second pass"
        continue
    fi

    echo "Processing: $best_file"

    # Start second remediation
    response=$(curl -s -X POST "$API_URL" \
        -F "file=@$best_file" \
        -F "maxIterations=5" \
        -F "enableGptFallback=true")

    session_id=$(echo "$response" | grep -o '"sessionId":"[^"]*' | cut -d'"' -f4)

    if [ -z "$session_id" ]; then
        echo "  ERROR: Failed to start second remediation for $best_file"
        continue
    fi

    echo "  Session ID: $session_id"
    echo -n "  Processing"

    # Wait for completion
    while true; do
        sleep 5
        echo -n "."

        status_response=$(curl -s -X GET "http://localhost:5008/api/v2/remediation/status/$session_id")
        status=$(echo "$status_response" | grep -o '"status":"[^"]*' | cut -d'"' -f4)

        if [ "$status" == "Completed" ]; then
            echo " Done!"
            break
        elif [ "$status" == "Failed" ]; then
            echo " Failed!"
            break
        fi
    done

    echo ""
done

echo ""
echo "========================================="
echo "Two-pass remediation complete!"
echo "Results saved in ./remediation-best/"
echo "========================================="