#!/bin/bash

# Batch processor with automatic hang detection and recovery
# Usage: ./batch_with_hang_detection.sh "StateAssets/Indiana/South Bend" SBD

set -e

CITY_DIR="$1"
CITY_CODE="$2"
SERVER_PORT=5008
API_URL="http://localhost:${SERVER_PORT}/api/v2/remediation/start"
HANG_THRESHOLD=600  # 10 minutes without log activity = hung
CHECK_INTERVAL=60   # Check every minute

if [ -z "$CITY_DIR" ] || [ -z "$CITY_CODE" ]; then
    echo "Usage: $0 <city_dir> <city_code>"
    exit 1
fi

LOG_FILE="Logs/accessform.log"
LAST_LOG_SIZE=0
HANG_COUNT=0

# Function to check if server is hung
check_for_hang() {
    CURRENT_LOG_SIZE=$(wc -c < "$LOG_FILE" 2>/dev/null || echo 0)

    if [ "$CURRENT_LOG_SIZE" -eq "$LAST_LOG_SIZE" ]; then
        HANG_COUNT=$((HANG_COUNT + CHECK_INTERVAL))
        echo "  ⚠️  No log activity for ${HANG_COUNT}s"

        if [ "$HANG_COUNT" -ge "$HANG_THRESHOLD" ]; then
            echo ""
            echo "========================================="
            echo "💥 HANG DETECTED! Restarting server..."
            echo "========================================="

            # Kill server
            pkill -f "dotnet run" || true
            sleep 3

            # Restart server in background
            ASPNETCORE_URLS="http://localhost:${SERVER_PORT}" dotnet run --project AccessFormServer.csproj --nologo --verbosity quiet > /dev/null 2>&1 &

            echo "Waiting for server to restart..."
            sleep 15

            # Verify server is up
            if curl -s "http://localhost:${SERVER_PORT}" > /dev/null 2>&1; then
                echo "✓ Server restarted successfully"
                HANG_COUNT=0
                return 0
            else
                echo "❌ Server failed to restart"
                exit 1
            fi
        fi
    else
        # Log activity detected, reset counter
        if [ "$HANG_COUNT" -gt 0 ]; then
            echo "  ✓ Log activity resumed"
        fi
        HANG_COUNT=0
        LAST_LOG_SIZE=$CURRENT_LOG_SIZE
    fi

    return 1
}

# Get list of PDFs to process
cd "$CITY_DIR"
PDF_FILES=(${CITY_CODE}_*.pdf)
TOTAL=${#PDF_FILES[@]}

echo "========================================="
echo "Batch Processing with Hang Detection"
echo "City: $CITY_DIR"
echo "Code: $CITY_CODE"
echo "PDFs: $TOTAL"
echo "Hang Threshold: ${HANG_THRESHOLD}s"
echo "========================================="
echo ""

# Check if server is running
cd "../../../"
if ! curl -s "http://localhost:${SERVER_PORT}" > /dev/null 2>&1; then
    echo "❌ Server not running. Starting..."
    ASPNETCORE_URLS="http://localhost:${SERVER_PORT}" dotnet run --project AccessFormServer.csproj --nologo --verbosity quiet > /dev/null 2>&1 &
    sleep 15
fi

echo "✓ Server is running"
echo ""

SUCCESS_COUNT=0
FAIL_COUNT=0
RESTART_COUNT=0

for ((i=0; i<$TOTAL; i++)); do
    PDF="${PDF_FILES[$i]}"
    NUM=$((i+1))

    echo "========================================="
    echo "[$NUM/$TOTAL] Processing: $PDF"
    echo "========================================="

    PDF_PATH="${CITY_DIR}/${PDF}"

    # Submit PDF
    TEMP_FILE=$(mktemp)
    HTTP_CODE=$(curl -s -w "%{http_code}" -o "$TEMP_FILE" -X POST \
        -F "file=@${PDF_PATH}" \
        "${API_URL}" 2>&1 | tail -n1)

    rm -f "$TEMP_FILE"

    if [ "$HTTP_CODE" = "200" ]; then
        echo "✓ Submitted: $PDF"

        # Monitor for completion or hang
        echo "Monitoring for completion..."
        LAST_LOG_SIZE=$(wc -c < "$LOG_FILE" 2>/dev/null || echo 0)
        HANG_COUNT=0

        while true; do
            sleep $CHECK_INTERVAL

            # Check if this PDF completed
            if grep -q "REMEDIATION COMPLETE.*${PDF}" "$LOG_FILE" 2>/dev/null; then
                echo "✓ Completed: $PDF"
                SUCCESS_COUNT=$((SUCCESS_COUNT + 1))
                break
            fi

            # Check for hang
            if check_for_hang; then
                RESTART_COUNT=$((RESTART_COUNT + 1))
                echo "Resubmitting $PDF after restart..."

                # Resubmit
                TEMP_FILE=$(mktemp)
                HTTP_CODE=$(curl -s -w "%{http_code}" -o "$TEMP_FILE" -X POST \
                    -F "file=@${PDF_PATH}" \
                    "${API_URL}" 2>&1 | tail -n1)
                rm -f "$TEMP_FILE"

                if [ "$HTTP_CODE" = "200" ]; then
                    echo "✓ Resubmitted successfully"
                    LAST_LOG_SIZE=$(wc -c < "$LOG_FILE" 2>/dev/null || echo 0)
                    HANG_COUNT=0
                else
                    echo "❌ Resubmit failed"
                    FAIL_COUNT=$((FAIL_COUNT + 1))
                    break
                fi
            fi
        done
    else
        echo "❌ Submit failed: $PDF (HTTP $HTTP_CODE)"
        FAIL_COUNT=$((FAIL_COUNT + 1))
    fi

    echo ""
    sleep 2
done

echo ""
echo "========================================="
echo "BATCH COMPLETE"
echo "========================================="
echo "Results:"
echo "  Processed: ${SUCCESS_COUNT}/${TOTAL}"
echo "  Failed: ${FAIL_COUNT}"
echo "  Server Restarts: ${RESTART_COUNT}"
echo ""
