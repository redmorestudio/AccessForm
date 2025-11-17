#!/bin/bash

# Monitor active remediation sessions
API_BASE="http://localhost:5008/api/v2/remediation"

# Session IDs from the batch run
declare -a SESSIONS=(
    "ac0acee8859e46da98b55a98deae280e:1:Final Routing"
    "b4c499378aeb46a580861b7acc16cfe5:2:Conditional Use Permit"
    "e1b3c19aa75d41bea8b694149a4504ca:3:Minor Design Review"
    "ef45f8399c8f4f7788bba7e8105b2e49:4:Building Permit"
    "1549a10051314805abaf04da1b0bf270:5:Hillside Area"
    "7c3b91993a394a3a98204f84a5046c9d:6:Ricardo Ortiz Form 497"
)

echo "Monitoring remediation sessions..."
echo ""

for session_info in "${SESSIONS[@]}"; do
    IFS=':' read -r session_id pdf_num pdf_name <<< "$session_info"

    echo "========================================="
    echo "PDF $pdf_num: $pdf_name"
    echo "Session: $session_id"
    echo "========================================="

    # Poll until complete
    max_polls=240  # 20 minutes max
    poll_count=0
    status="Running"

    while [ "$status" = "Running" ] && [ $poll_count -lt $max_polls ]; do
        sleep 5
        poll_count=$((poll_count + 1))

        response=$(curl -s "$API_BASE/status/$session_id")
        status=$(echo "$response" | python3 -c "import sys, json; data=json.load(sys.stdin); print(data.get('status', 'Unknown'))" 2>/dev/null)

        if [ -z "$status" ]; then
            status="Unknown"
        fi

        iteration=$(echo "$response" | python3 -c "import sys, json; data=json.load(sys.stdin); print(data.get('currentIteration', 'N/A'))" 2>/dev/null)
        violations=$(echo "$response" | python3 -c "import sys, json; data=json.load(sys.stdin); print(data.get('currentViolations', 'N/A'))" 2>/dev/null)
        compliance=$(echo "$response" | python3 -c "import sys, json; data=json.load(sys.stdin); print(data.get('complianceScore', 'N/A'))" 2>/dev/null)

        echo "  Poll $poll_count: Status=$status, Iteration=$iteration, Violations=$violations, Compliance=$compliance%"

        if [ "$status" != "Running" ]; then
            # Save final response
            echo "$response" > "/tmp/session_${pdf_num}_final.json"
            break
        fi

        # Brief update every 30 seconds
        if [ $((poll_count % 6)) -eq 0 ]; then
            echo "  ... still processing (${poll_count} polls, $((poll_count * 5))s elapsed)"
        fi
    done

    if [ "$status" = "Running" ]; then
        echo "  WARNING: Session still running after timeout"
    fi

    echo ""
done

echo "========================================="
echo "All sessions monitored"
echo "========================================="
