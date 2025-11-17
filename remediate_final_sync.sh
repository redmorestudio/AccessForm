#!/bin/bash

# Remediate PDFs using synchronous remediation API

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

    echo "Processing: $base_name"
    echo "Input: $pdf_path"
    echo "Output: $output_path"
    echo ""

    # Start remediation
    echo "Starting remediation..."
    local session_json=$(curl -s -X POST \
        -F "file=@${pdf_path}" \
        -F "maxIterations=5" \
        -F "maxDurationMinutes=15" \
        "${SERVER_URL}/api/v2/remediation/start")

    local session_id=$(echo "$session_json" | python3 -c "import sys, json; print(json.load(sys.stdin).get('sessionId', ''))")

    if [ -z "$session_id" ]; then
        echo "❌ Failed to start remediation"
        echo "$session_json"
        return 1
    fi

    echo "Session ID: $session_id"
    echo "Waiting for completion..."

    # Poll for completion
    local max_attempts=180  # 15 minutes with 5-second intervals
    local attempt=0
    while [ $attempt -lt $max_attempts ]; do
        sleep 5
        attempt=$((attempt + 1))

        local status_json=$(curl -s "${SERVER_URL}/api/v2/remediation/status/${session_id}")
        local status=$(echo "$status_json" | python3 -c "import sys, json; print(json.load(sys.stdin).get('status', ''))")

        echo -n "."

        if [ "$status" = "completed" ] || [ "$status" = "failed" ] || [ "$status" = "max_iterations_reached" ]; then
            echo ""
            echo "Status: $status"

            # Get final result
            local result_json=$(curl -s "${SERVER_URL}/api/v2/remediation/result/${session_id}")

            # Extract PDF and write report
            echo "$result_json" | python3 -c "
import json
import base64
import sys

try:
    data = json.load(sys.stdin)

    # Extract PDF if present
    if 'bestPdf' in data and data['bestPdf']:
        pdf_data = base64.b64decode(data['bestPdf'])
        with open('${output_path}', 'wb') as out:
            out.write(pdf_data)
        print('✅ PDF saved to ${output_path}')
    elif 'lastValidatedPdf' in data and data['lastValidatedPdf']:
        pdf_data = base64.b64decode(data['lastValidatedPdf'])
        with open('${output_path}', 'wb') as out:
            out.write(pdf_data)
        print('✅ PDF saved to ${output_path}')
    else:
        print('❌ No PDF data in response')
        sys.exit(1)

    # Print summary
    print(f\"\\n📊 Remediation Summary:\")
    print(f\"  Iterations: {data.get('iterationCount', 0)}\")
    print(f\"  Final Score: {data.get('finalScore', 0):.1f}%\")
    print(f\"  Violations: {data.get('finalViolationCount', 0)}\")
    print(f\"  Status: {data.get('status', 'unknown')}\")

    if data.get('finalViolationCount', 0) > 0:
        print(f\"\\n  Remaining violations:\")
        violations = data.get('violations', [])
        for v in violations[:5]:  # Show first 5
            print(f\"    - Rule {v.get('rule', 'Unknown')}: {v.get('description', '')[:80]}\")
        if len(violations) > 5:
            print(f\"    ... and {len(violations) - 5} more\")

except Exception as e:
    print(f'❌ Error: {e}')
    import traceback
    traceback.print_exc()
    sys.exit(1)
"
            break
        fi

        if [ $attempt -ge $max_attempts ]; then
            echo ""
            echo "⏱️  Timeout waiting for remediation"
            break
        fi
    done

    echo ""
    echo "---"
    echo ""
}

# Process all three PDFs
remediate_pdf "${BASE_DIR}/remediation-best/Hillside Area Construction Permit Application Form (PDF)_best_iter3_20251029-154913_best_iter3_20251031-102948.pdf"
remediate_pdf "${BASE_DIR}/remediation-best/Minor Design Review Application Form (PDF)_best_iter3_20251029-154843_best_iter3_20251031-102957.pdf"
remediate_pdf "${BASE_DIR}/remediation-best/Ricardo Ortiz Form 497 dated 9.26.17 (PDF)_best_iter3_20251029-154927.pdf"

echo "========================================="
echo "Remediation complete!"
echo "========================================="
