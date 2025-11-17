#!/bin/bash

SESSION_ID="2cd70527ad0640d584f0667276e90da5"

echo "Monitoring second pass remediation..."
echo ""

for i in {1..40}; do
  sleep 3
  STATUS=$(curl -s "http://localhost:5008/api/v2/remediation/status/$SESSION_ID")
  ITER=$(echo "$STATUS" | python3 -c "import sys, json; data=json.load(sys.stdin); print(data.get('currentIteration', 'N/A'))" 2>/dev/null)
  SCORE=$(echo "$STATUS" | python3 -c "import sys, json; data=json.load(sys.stdin); print(data.get('bestComplianceScore', 'N/A'))" 2>/dev/null)
  STATE=$(echo "$STATUS" | python3 -c "import sys, json; data=json.load(sys.stdin); print(data.get('status', 'N/A'))" 2>/dev/null)
  VIOLATIONS=$(echo "$STATUS" | python3 -c "import sys, json; data=json.load(sys.stdin); print(data.get('bestViolationCount', 'N/A'))" 2>/dev/null)
  echo "[Poll $i] Status: $STATE | Iteration: $ITER | Best Score: $SCORE% | Violations: $VIOLATIONS"

  if [ "$STATE" = "Completed" ]; then
    echo ""
    echo "=== FINAL RESULT ==="
    echo "$STATUS" | python3 -m json.tool
    break
  fi
done

echo ""
echo "Downloading result..."
curl -s "http://localhost:5008/api/v2/remediation/download/$SESSION_ID/best" -o "remediation-best/second_pass_best.pdf"
echo "Downloaded to: remediation-best/second_pass_best.pdf"
