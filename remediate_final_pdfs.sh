#!/bin/bash

# Remediate the three PDFs using the updated remediation services

BASE_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"
SERVER_URL="http://localhost:5008"
OUTPUT_DIR="${BASE_DIR}/remediation-best/final-100-percent"

mkdir -p "$OUTPUT_DIR"

echo "========================================="
echo "Final PDF Remediation Pass"
echo "========================================="
echo ""

# Function to remediate a single PDF
remediate_pdf() {
    local pdf_path="$1"
    local pdf_name=$(basename "$pdf_path")
    local base_name="${pdf_name%.*}"
    local output_path="${OUTPUT_DIR}/${base_name}_FINAL.pdf"
    local report_path="${OUTPUT_DIR}/${base_name}_FINAL_report.txt"

    echo "Processing: $base_name"
    echo "Input: $pdf_path"
    echo "Output: $output_path"
    echo ""

    # Call remediation API with v2 endpoint (closed-loop remediation)
    curl -X POST \
        -F "file=@${pdf_path}" \
        -F "maxIterations=5" \
        -F "maxDurationMinutes=15" \
        "${SERVER_URL}/api/v2/remediation/start" \
        -o "${output_path}.json" \
        -w "\nHTTP Status: %{http_code}\n" \
        2>/dev/null

    # Extract the PDF from the JSON response if successful
    if [ -f "${output_path}.json" ]; then
        # Check if response contains PDF data
        python3 -c "
import json
import base64
import sys

try:
    with open('${output_path}.json', 'r') as f:
        data = json.load(f)
        if 'bestPdf' in data and data['bestPdf']:
            pdf_data = base64.b64decode(data['bestPdf'])
            with open('${output_path}', 'wb') as out:
                out.write(pdf_data)
            print('✅ PDF extracted successfully')

            # Write summary report
            with open('${report_path}', 'w') as report:
                report.write('Final Remediation Report\\n')
                report.write('=' * 50 + '\\n\\n')
                report.write(f\"File: ${base_name}\\n\")
                report.write(f\"Iterations: {data.get('iterationCount', 0)}\\n\")
                report.write(f\"Final Score: {data.get('finalScore', 0):.1f}%\\n\")
                report.write(f\"Violations: {data.get('finalViolationCount', 0)}\\n\")
                report.write(f\"Status: {data.get('status', 'unknown')}\\n\\n\")

                if 'violations' in data:
                    report.write('Remaining Violations:\\n')
                    report.write('-' * 50 + '\\n')
                    for v in data.get('violations', []):
                        report.write(f\"Rule: {v.get('rule', 'Unknown')}\\n\")
                        report.write(f\"  {v.get('description', '')}\\n\")
                        report.write(f\"  Location: {v.get('location', '')}\\n\\n\")
            print(f'📄 Report written to: ${report_path}')
        else:
            print('❌ No PDF data in response')
            sys.exit(1)
except Exception as e:
    print(f'❌ Error: {e}')
    sys.exit(1)
" || echo "⚠️  Failed to extract PDF from response"
    fi

    echo ""
    echo "---"
    echo ""
}

# Process Hillside PDF
remediate_pdf "${BASE_DIR}/remediation-best/Hillside Area Construction Permit Application Form (PDF)_best_iter3_20251029-154913_best_iter3_20251031-102948.pdf"

# Process Minor Design PDF
remediate_pdf "${BASE_DIR}/remediation-best/Minor Design Review Application Form (PDF)_best_iter3_20251029-154843_best_iter3_20251031-102957.pdf"

# Process Ricardo Ortiz PDF
remediate_pdf "${BASE_DIR}/remediation-best/Ricardo Ortiz Form 497 dated 9.26.17 (PDF)_best_iter3_20251029-154927.pdf"

echo "========================================="
echo "Remediation complete!"
echo "========================================="
echo ""
echo "Check ${OUTPUT_DIR} for results"
