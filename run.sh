#!/bin/bash

# Run script for AccessForm Word to PDF Converter

echo "🚀 Starting AccessForm Word to PDF Converter"
echo "==========================================="

cd "$(dirname "$0")"

# Check if .NET is installed
if ! command -v dotnet &> /dev/null; then
    echo "❌ .NET SDK not found"
    echo "Please run ./build.sh first to install dependencies"
    exit 1
fi

# Check if project has been built
if [ ! -d "bin" ]; then
    echo "⚠️  Project not built. Building now..."
    ./build.sh
    
    if [ $? -ne 0 ]; then
        echo "❌ Build failed"
        exit 1
    fi
fi

echo ""
echo "🌐 Starting web server..."
echo "📍 URL: http://localhost:5000"
echo "📍 Secure: https://localhost:5001"
echo ""
echo "Press Ctrl+C to stop the server"
echo ""

# Run the application
dotnet run --urls "http://localhost:5000;https://localhost:5001"
