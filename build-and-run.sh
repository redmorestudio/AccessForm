#!/bin/bash

# Build and Run script for AccessForm Word to PDF Converter
# Using .NET 9 from standard macOS location

DOTNET="/usr/local/share/dotnet/dotnet"
PROJECT_DIR="$(dirname "$0")"

echo "🚀 AccessForm Word to PDF Converter"
echo "===================================="
echo ""

# Check if .NET is accessible
if [ ! -f "$DOTNET" ]; then
    echo "❌ .NET not found at $DOTNET"
    echo "Please ensure .NET 9 SDK is installed"
    exit 1
fi

echo "✅ Using .NET at: $DOTNET"
$DOTNET --version
echo ""

cd "$PROJECT_DIR"

echo "📦 Restoring NuGet packages..."
echo "This will download Syncfusion libraries from NuGet..."
$DOTNET restore

if [ $? -ne 0 ]; then
    echo "❌ Failed to restore packages"
    exit 1
fi

echo ""
echo "🔨 Building project..."
$DOTNET build -c Release

if [ $? -ne 0 ]; then
    echo "❌ Build failed"
    exit 1
fi

echo ""
echo "✅ Build successful!"
echo ""
echo "🌐 Starting web server..."
echo "📍 URL: http://localhost:5000"
echo ""
echo "👉 Open http://localhost:5000 in your browser"
echo "👉 Press Ctrl+C to stop the server"
echo ""

# Run the application
$DOTNET run --urls "http://localhost:5000"
