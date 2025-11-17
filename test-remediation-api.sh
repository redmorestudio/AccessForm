#!/bin/bash

# Test script for Remediation API v2
# Make sure the server is running on port 5008

BASE_URL="http://localhost:5008"

echo "========================================="
echo "Testing Remediation API v2"
echo "========================================="
echo ""

# Test 1: Health check
echo "1. Testing health endpoint..."
curl -s "$BASE_URL/api/v2/remediation/health" | python3 -m json.tool
echo ""
echo ""

# Test 2: Start remediation (you need a test PDF file)
# Uncomment and update the path to your PDF
# echo "2. Starting remediation..."
# SESSION_ID=$(curl -s -X POST "$BASE_URL/api/v2/remediation/start" \
#   -F "file=@/path/to/your/test.pdf" \
#   -F "maxIterations=5" \
#   -F "acceptableComplianceScore=95" \
#   -F "detectStagnation=true" | python3 -c "import sys, json; print(json.load(sys.stdin)['sessionId'])")
#
# echo "Session ID: $SESSION_ID"
# echo ""
# echo ""
#
# # Test 3: Poll status
# echo "3. Polling status..."
# for i in {1..5}; do
#   echo "Poll $i:"
#   curl -s "$BASE_URL/api/v2/remediation/status/$SESSION_ID" | python3 -m json.tool
#   echo ""
#   sleep 2
# done
#
# # Test 4: Download best PDF
# echo "4. Downloading best PDF..."
# curl -s "$BASE_URL/api/v2/remediation/download/$SESSION_ID/best" -o "output_best.pdf"
# echo "Downloaded to output_best.pdf"
# echo ""

echo "========================================="
echo "Test completed!"
echo "========================================="
echo ""
echo "Next steps:"
echo "1. Start the server: ASPNETCORE_URLS=\"http://localhost:5008\" dotnet run --project AccessFormServer.csproj --nologo --verbosity quiet"
echo "2. Open browser: http://localhost:5008/remediation-test.html"
echo "3. Or use curl with a test PDF file (uncomment lines in this script)"
