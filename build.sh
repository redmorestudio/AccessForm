#!/bin/bash

# Build script for AccessForm Word to PDF Converter

echo "🚀 AccessForm Word to PDF Converter - Build Script"
echo "=================================================="

# Check if .NET is installed
if ! command -v dotnet &> /dev/null; then
    echo "❌ .NET SDK not found. Installing..."
    echo "Please enter your password when prompted:"
    brew install dotnet-sdk
    
    if [ $? -ne 0 ]; then
        echo ""
        echo "❌ Failed to install .NET SDK"
        echo "Please install manually:"
        echo "  brew install dotnet-sdk"
        echo "Or download from: https://dotnet.microsoft.com/download"
        exit 1
    fi
else
    echo "✅ .NET SDK found: $(dotnet --version)"
fi

# Navigate to project directory
cd "$(dirname "$0")"

echo ""
echo "📦 Restoring NuGet packages..."
dotnet restore

if [ $? -ne 0 ]; then
    echo "❌ Failed to restore packages"
    exit 1
fi

echo ""
echo "🔨 Building project..."
dotnet build -c Release

if [ $? -ne 0 ]; then
    echo "❌ Build failed"
    exit 1
fi

echo ""
echo "📋 Publishing project..."
dotnet publish -c Release -o ./publish

if [ $? -ne 0 ]; then
    echo "❌ Publish failed"
    exit 1
fi

echo ""
echo "✅ Build complete!"
echo ""
echo "📁 Output location: ./publish"
echo ""
echo "To run the application:"
echo "  ./run.sh"
echo ""
echo "Or use dotnet directly:"
echo "  dotnet run"
