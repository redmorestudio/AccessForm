#!/bin/bash

# Start remediation for all three PDFs
echo "Starting remediation on all three PDFs..."

sessionId1=$(curl -s -X POST http://localhost:5008/api/v2/remediation/start \
  -F "pdfFile=@remediation-best/Building Permit Application (PDF)_best_iter3_20251031-055638.pdf" \
  -F "maxIterations=5" \
  -F "enableGptFallback=false" | grep -o '"sessionId":"[^"]*"' | cut -d'"' -f4)

sessionId2=$(curl -s -X POST http://localhost:5008/api/v2/remediation/start \
  -F "pdfFile=@remediation-best/Conditional Use Permit Form (PDF)_best_iter3_20251031-055856.pdf" \
  -F "maxIterations=5" \
  -F "enableGptFallback=false" | grep -o '"sessionId":"[^"]*"' | cut -d'"' -f4)

sessionId3=$(curl -s -X POST http://localhost:5008/api/v2/remediation/start \
  -F "pdfFile=@remediation-best/Final Routing - handout template 6.3.25_202506041028022821_best_iter3_20251031-060125.pdf" \
  -F "maxIterations=5" \
  -F "enableGptFallback=false" | grep -o '"sessionId":"[^"]*"' | cut -d'"' -f4)

echo "Building Permit Session: $sessionId1"
echo "Conditional Use Session: $sessionId2"
echo "Final Routing Session: $sessionId3"
echo ""

# Monitor all three sessions
echo "Monitoring remediation progress..."
for i in {1..120}; do
    status1=$(curl -s "http://localhost:5008/api/v2/remediation/status/$sessionId1" | grep -o '"status":"[^"]*"' | cut -d'"' -f4)
    status2=$(curl -s "http://localhost:5008/api/v2/remediation/status/$sessionId2" | grep -o '"status":"[^"]*"' | cut -d'"' -f4)
    status3=$(curl -s "http://localhost:5008/api/v2/remediation/status/$sessionId3" | grep -o '"status":"[^"]*"' | cut -d'"' -f4)

    echo "[$i] Building Permit: $status1 | Conditional Use: $status2 | Final Routing: $status3"

    # Check if all are completed or failed
    if [[ "$status1" == "completed" || "$status1" == "failed" ]] && \
       [[ "$status2" == "completed" || "$status2" == "failed" ]] && \
       [[ "$status3" == "completed" || "$status3" == "failed" ]]; then
        echo ""
        echo "All sessions finished!"
        break
    fi

    sleep 5
done

echo ""
echo "=== Final Results ==="
echo ""

# Get final status for all three
echo "Building Permit Application:"
curl -s "http://localhost:5008/api/v2/remediation/status/$sessionId1" | python3 -m json.tool
echo ""

echo "Conditional Use Permit Form:"
curl -s "http://localhost:5008/api/v2/remediation/status/$sessionId2" | python3 -m json.tool
echo ""

echo "Final Routing handout template:"
curl -s "http://localhost:5008/api/v2/remediation/status/$sessionId3" | python3 -m json.tool
echo ""

# Download the remediated PDFs
echo "Downloading remediated PDFs..."
curl -s "http://localhost:5008/api/v2/remediation/download/$sessionId1" -o "remediation-best/Building_Permit_FINAL_TEST.pdf"
curl -s "http://localhost:5008/api/v2/remediation/download/$sessionId2" -o "remediation-best/Conditional_Use_FINAL_TEST.pdf"
curl -s "http://localhost:5008/api/v2/remediation/download/$sessionId3" -o "remediation-best/Final_Routing_FINAL_TEST.pdf"

echo "Done! PDFs saved to remediation-best directory"
