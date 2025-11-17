#!/bin/bash

echo "Testing remediation on Building Permit and Conditional Use Permit..."
echo ""

# Test Building Permit
echo "1. Building Permit Application..."
sessionId1=$(curl -s -X POST http://localhost:5008/api/v2/remediation/start \
  -F "pdfFile=@remediation-best/Building Permit Application (PDF)_best_iter3_20251031-055638.pdf" \
  -F "maxIterations=5" \
  -F "enableGptFallback=false" | grep -o '"sessionId":"[^"]*"' | cut -d'"' -f4)
echo "   Session ID: $sessionId1"

# Test Conditional Use Permit
echo "2. Conditional Use Permit Form..."
sessionId2=$(curl -s -X POST http://localhost:5008/api/v2/remediation/start \
  -F "pdfFile=@remediation-best/Conditional Use Permit Form (PDF)_best_iter3_20251031-055856.pdf" \
  -F "maxIterations=5" \
  -F "enableGptFallback=false" | grep -o '"sessionId":"[^"]*"' | cut -d'"' -f4)
echo "   Session ID: $sessionId2"
echo ""

# Monitor both
echo "Monitoring progress..."
for i in {1..60}; do
    status1=$(curl -s "http://localhost:5008/api/v2/remediation/status/$sessionId1" | grep -o '"status":"[^"]*"' | cut -d'"' -f4)
    status2=$(curl -s "http://localhost:5008/api/v2/remediation/status/$sessionId2" | grep -o '"status":"[^"]*"' | cut -d'"' -f4)

    echo "[$i] Building Permit: $status1 | Conditional Use: $status2"

    if [[ "$status1" == "Completed" || "$status1" == "Failed" ]] && \
       [[ "$status2" == "Completed" || "$status2" == "Failed" ]]; then
        echo ""
        echo "Both sessions finished!"
        break
    fi

    sleep 5
done

echo ""
echo "=== RESULTS ==="
echo ""

# Get final status
echo "Building Permit Application:"
curl -s "http://localhost:5008/api/v2/remediation/status/$sessionId1" | python3 -m json.tool 2>/dev/null || echo "Error getting status"
echo ""

echo "Conditional Use Permit Form:"
curl -s "http://localhost:5008/api/v2/remediation/status/$sessionId2" | python3 -m json.tool 2>/dev/null || echo "Error getting status"
echo ""

# Try to download
echo "Attempting to download remediated PDFs..."
curl -s "http://localhost:5008/api/v2/remediation/download/$sessionId1" -o "remediation-best/Building_Permit_REMEDIATED.pdf"
curl -s "http://localhost:5008/api/v2/remediation/download/$sessionId2" -o "remediation-best/Conditional_Use_REMEDIATED.pdf"
echo "Done!"
