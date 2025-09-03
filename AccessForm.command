#!/bin/bash

# AccessForm Launcher for Mac
# This .command file can be double-clicked in Finder!

# Get the directory where this script is located
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$DIR"

# Clear the terminal for a clean start
clear

echo "╔════════════════════════════════════════╗"
echo "║        AccessForm PDF Converter        ║"
echo "╚════════════════════════════════════════╝"
echo ""
echo "📋 Starting AccessForm server..."
echo ""

# Make sure the executable has permissions
chmod +x AccessFormServer 2>/dev/null

# Check if the server file exists
if [ ! -f "AccessFormServer" ]; then
    echo "❌ Error: AccessFormServer not found!"
    echo "Please make sure you extracted all files from the ZIP."
    echo ""
    echo "Press any key to exit..."
    read -n 1
    exit 1
fi

echo "✅ Server starting..."
echo ""
echo "🌐 Opening your browser to: http://localhost:5001"
echo ""

# Try to open the browser automatically
sleep 2
open http://localhost:5001 2>/dev/null || true

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  Ready! Your browser should open automatically."
echo "  If not, manually go to: http://localhost:5001"
echo ""
echo "  To stop: Close this window or press Ctrl+C"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

# Run the server
./AccessFormServer

# If the server stops, show a message
echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  AccessForm has stopped."
echo "  You can close this window now."
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
echo "Press any key to exit..."
read -n 1
