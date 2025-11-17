#!/bin/bash

# Test fix services on the three PDFs

BASE_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"
SERVER_URL="http://localhost:5008"

echo "========================================="
echo "Testing PDF Fix Services"
echo "========================================="
echo ""

# Function to test a single PDF
test_pdf() {
    local pdf_path="$1"
    local pdf_name=$(basename "$pdf_path")
    local output_path="${BASE_DIR}/remediation-best/${pdf_name%.*}_FIXED.pdf"

    echo "Testing: $pdf_name"
    echo "Input: $pdf_path"
    echo "Output: $output_path"
    echo ""

    # Call remediation API
    curl -X POST \
        -F "file=@${pdf_path}" \
        -o "$output_path" \
        "${SERVER_URL}/api/remediate-pdf-basic" \
        -w "\nHTTP Status: %{http_code}\n" \
        2>/dev/null

    echo ""
    echo "---"
    echo ""
}

# Test Hillside PDF
test_pdf "${BASE_DIR}/remediation-best/Hillside Area Construction Permit Application Form (PDF)_best_iter3_20251029-154913_best_iter3_20251031-102948.pdf"

# Test Minor Design PDF
test_pdf "${BASE_DIR}/remediation-best/Minor Design Review Application Form (PDF)_best_iter3_20251029-154843_best_iter3_20251031-102957.pdf"

# Test Ricardo Ortiz PDF
test_pdf "${BASE_DIR}/remediation-best/Ricardo Ortiz Form 497 dated 9.26.17 (PDF)_best_iter3_20251029-154927.pdf"

echo "========================================="
echo "Testing complete!"
echo "========================================="
