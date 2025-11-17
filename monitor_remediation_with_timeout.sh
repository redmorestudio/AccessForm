#!/bin/bash

# Monitor remediation progress and detect hangs
# Usage: ./monitor_remediation_with_timeout.sh "StateAssets/Indiana/South Bend" SBD

CITY_DIR="$1"
CITY_CODE="$2"
CHECK_INTERVAL=120  # Check every 2 minutes
HANG_THRESHOLD=600  # Consider hung if no progress for 10 minutes

if [ -z "$CITY_DIR" ] || [ -z "$CITY_CODE" ]; then
    echo "Usage: $0 <city_dir> <city_code>"
    echo "Example: $0 'StateAssets/Indiana/South Bend' SBD"
    exit 1
fi

echo "========================================="
echo "Remediation Monitor with Hang Detection"
echo "City: $CITY_DIR"
echo "Code: $CITY_CODE"
echo "Check Interval: ${CHECK_INTERVAL}s"
echo "Hang Threshold: ${HANG_THRESHOLD}s"
echo "========================================="
echo ""

LAST_COMPLETED=0
LAST_CHANGE_TIME=$(date +%s)

while true; do
    # Get current completion status
    RESULT=$(python3 city_batch_processor.py "$CITY_DIR" "$CITY_CODE" check 2>&1)
    COMPLETED=$(echo "$RESULT" | grep "Completed:" | awk '{print $2}')
    EXPECTED=$(echo "$RESULT" | grep "Expected:" | awk '{print $2}')

    CURRENT_TIME=$(date +%s)
    ELAPSED=$((CURRENT_TIME - LAST_CHANGE_TIME))

    echo "[$(date '+%H:%M:%S')] Status: $COMPLETED/$EXPECTED completed"

    # Check if progress was made
    if [ "$COMPLETED" -gt "$LAST_COMPLETED" ]; then
        echo "  ✓ Progress detected: +$((COMPLETED - LAST_COMPLETED)) completed"
        LAST_COMPLETED=$COMPLETED
        LAST_CHANGE_TIME=$CURRENT_TIME
        ELAPSED=0
    else
        echo "  ⏱  No progress for ${ELAPSED}s"
    fi

    # Check if all done
    if [ "$COMPLETED" = "$EXPECTED" ]; then
        echo ""
        echo "========================================="
        echo "✓ ALL COMPLETE!"
        echo "========================================="
        exit 0
    fi

    # Check for hang
    if [ "$ELAPSED" -ge "$HANG_THRESHOLD" ]; then
        echo ""
        echo "========================================="
        echo "⚠️  HANG DETECTED!"
        echo "No progress for ${ELAPSED}s (threshold: ${HANG_THRESHOLD}s)"
        echo "========================================="
        echo ""
        echo "Recommendation: Kill server and restart"
        echo "  pkill -f 'dotnet run'"
        echo "  # Restart server"
        echo "  # Resubmit missing PDFs"
        exit 1
    fi

    sleep $CHECK_INTERVAL
done
