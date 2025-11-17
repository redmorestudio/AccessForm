#!/bin/bash

# Process the final 6 PDFs with hang detection
# These were the ones that got stuck

set -e

CITY_DIR="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/StateAssets/Indiana/South Bend"
SERVER_PORT=5008
API_URL="http://localhost:${SERVER_PORT}/api/v2/remediation/start"
LOG_FILE="/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/Logs/accessform.log"
HANG_THRESHOLD=600
CHECK_INTERVAL=60

MISSING_PDFS=(
  "SBD_0042_Climate Risk  Vulnerability Assessment.pdf"
  "SBD_0053_South Bend Water Quality Parameters.pdf"
  "SBD_0056_PROCESS FOR ENERGY CODE COMPLIANCE RESIDENTIAL.pdf"
  "SBD_0075_Mental Health Resources.pdf"
  "SBD_0096_Public Safety Video Surveillance System.pdf"
  "SBD_0106_Article 21-07 Access  Parking.pdf"
)

check_for_hang() {
    local last_size=$1
    local current_size=$(wc -c < "$LOG_FILE" 2>/dev/null || echo 0)
    local elapsed=$2

    if [ "$current_size" -eq "$last_size" ]; then
        return 0  # No change
    else
        return 1  # Activity detected
    fi
}

cd "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"

echo "========================================="
echo "Processing Final 6 PDFs with Hang Detection"
echo "Started: $(date)"
echo "========================================="
echo ""

# Ensure server is running
if ! curl -s "http://localhost:${SERVER_PORT}" > /dev/null 2>&1; then
    echo "Starting server..."
    ASPNETCORE_URLS="http://localhost:${SERVER_PORT}" dotnet run --project AccessFormServer.csproj --nologo --verbosity quiet > /dev/null 2>&1 &
    sleep 15
fi

echo "✓ Server is running"
echo ""

SUCCESS_COUNT=0
RESTART_COUNT=0

for PDF in "${MISSING_PDFS[@]}"; do
    PDF_NAME=$(basename "$PDF" .pdf)
    echo "========================================="
    echo "Processing: $PDF_NAME"
    echo "========================================="

    # Submit PDF
    LAST_LOG_SIZE=$(wc -c < "$LOG_FILE" 2>/dev/null || echo 0)

    curl -s -X POST -F "file=@${CITY_DIR}/${PDF}" "${API_URL}" > /dev/null

    echo "✓ Submitted, monitoring..."

    ELAPSED=0
    while true; do
        sleep $CHECK_INTERVAL
        ELAPSED=$((ELAPSED + CHECK_INTERVAL))

        CURRENT_LOG_SIZE=$(wc -c < "$LOG_FILE" 2>/dev/null || echo 0)

        # Check if completed
        if grep -q "REMEDIATION COMPLETE.*${PDF_NAME}" "$LOG_FILE" 2>/dev/null; then
            echo "✓ Completed: $PDF_NAME"
            SUCCESS_COUNT=$((SUCCESS_COUNT + 1))
            break
        fi

        # Check for hang
        if [ "$CURRENT_LOG_SIZE" -eq "$LAST_LOG_SIZE" ]; then
            if [ "$ELAPSED" -ge "$HANG_THRESHOLD" ]; then
                echo "💥 HANG DETECTED after ${ELAPSED}s - Skipping $PDF_NAME"
                mkdir -p "${CITY_DIR}/_problematic"
                mv "${CITY_DIR}/${PDF}" "${CITY_DIR}/_problematic/"
                echo "Moved to _problematic/ folder"

                # Restart server for next PDF
                pkill -f "dotnet run" || true
                sleep 3
                ASPNETCORE_URLS="http://localhost:${SERVER_PORT}" dotnet run --project AccessFormServer.csproj --nologo --verbosity quiet > /dev/null 2>&1 &
                sleep 15
                RESTART_COUNT=$((RESTART_COUNT + 1))
                break
            fi
        else
            LAST_LOG_SIZE=$CURRENT_LOG_SIZE
            ELAPSED=0  # Reset on activity
        fi
    done

    echo ""
done

echo "========================================="
echo "FINAL 6 PROCESSING COMPLETE"
echo "========================================="
echo "Completed: ${SUCCESS_COUNT}/6"
echo "Server Restarts: ${RESTART_COUNT}"
echo ""
