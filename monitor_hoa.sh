#!/bin/bash
SESSION="2064aed783db4846abeb61998bad454b"

for i in {1..20}; do
  sleep 30
  echo "=== Check $i ==="
  curl -s http://localhost:5008/api/v2/remediation/status/$SESSION | python3 -c "import sys, json; d=json.load(sys.stdin); print(f\"Status: {d['status']}, Elapsed: {d['elapsedTime']}, Violations: {d['currentViolations']}\")"

  STATUS=$(curl -s http://localhost:5008/api/v2/remediation/status/$SESSION | python3 -c "import sys, json; print(json.load(sys.stdin)['status'])")

  if [ "$STATUS" = "Completed" ] || [ "$STATUS" = "Failed" ]; then
    echo "=== FINAL STATUS ==="
    curl -s http://localhost:5008/api/v2/remediation/status/$SESSION | python3 -m json.tool
    break
  fi
done
