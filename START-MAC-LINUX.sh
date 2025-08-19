#!/bin/bash

echo "========================================"
echo "     AccessForm PDF Converter"
echo "========================================"
echo ""
echo "Starting AccessForm server..."
echo "Open your browser to: http://localhost:5008"
echo ""
echo "Press Ctrl+C to stop the server"
echo ""

# Make sure the executable has permissions
chmod +x AccessFormServer 2>/dev/null

# Run the server
./AccessFormServer
