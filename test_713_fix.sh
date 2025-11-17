#!/bin/bash

# Test script for fixing 7.1-3 violations
echo "============================================"
echo "Testing 7.1-3 Violation Fix"
echo "============================================"

# Input and output files
INPUT_PDF="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/remediation-best/BASIC FMLA Guide_202409131109189018a_best_iter3_20251031-131817.pdf"
OUTPUT_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/remediation-best"
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
OUTPUT_PDF="${OUTPUT_DIR}/BASIC_FMLA_Guide_fixed_${TIMESTAMP}.pdf"

echo "Input PDF: $INPUT_PDF"
echo "Output PDF: $OUTPUT_PDF"
echo ""

# Call the remediation API
echo "Calling remediation API..."
curl -X POST http://localhost:5008/api/remediate \
  -H "Content-Type: application/pdf" \
  --data-binary "@${INPUT_PDF}" \
  --output "${OUTPUT_PDF}" \
  -w "\nHTTP Status: %{http_code}\n" \
  --silent --show-error

if [ $? -eq 0 ]; then
    echo "✅ Remediation completed successfully"
    echo "Output saved to: ${OUTPUT_PDF}"

    # Run VeraPDF validation
    echo ""
    echo "Running VeraPDF validation..."

    # Assuming verapdf is installed
    if command -v verapdf &> /dev/null; then
        verapdf --format text --flavour ua1 "${OUTPUT_PDF}" > "${OUTPUT_PDF}_violations.txt" 2>&1

        # Check for violations
        if grep -q "FAIL" "${OUTPUT_PDF}_violations.txt"; then
            echo "⚠️  Violations still found:"
            grep -A2 "Rule.*7.1-3" "${OUTPUT_PDF}_violations.txt" || echo "No 7.1-3 violations found"
            echo ""
            echo "Total violations:"
            grep -c "FAIL" "${OUTPUT_PDF}_violations.txt"
        else
            echo "✅ PDF is fully compliant!"
        fi

        echo ""
        echo "Full validation report saved to: ${OUTPUT_PDF}_violations.txt"
    else
        echo "⚠️  VeraPDF not installed. Cannot validate output."
        echo "Install with: brew install verapdf"
    fi
else
    echo "❌ Remediation failed"
fi

echo ""
echo "============================================"
echo "Test complete"
echo "============================================"