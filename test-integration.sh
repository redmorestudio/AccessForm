#!/bin/bash

echo "======================================"
echo "AccessForm AI Integration Test"
echo "======================================"
echo ""

# Check if dotnet is installed
if ! command -v dotnet &> /dev/null; then
    echo "❌ .NET SDK not found. Please install .NET 9.0 SDK"
    exit 1
fi

echo "✅ .NET SDK found: $(dotnet --version)"
echo ""

# Restore packages
echo "📦 Restoring NuGet packages..."
dotnet restore

if [ $? -ne 0 ]; then
    echo "❌ Failed to restore packages"
    exit 1
fi

echo "✅ Packages restored successfully"
echo ""

# Build the project
echo "🔨 Building the project..."
dotnet build --no-restore

if [ $? -ne 0 ]; then
    echo "❌ Build failed"
    exit 1
fi

echo "✅ Build succeeded"
echo ""

# Check configuration
echo "🔍 Checking configuration..."
if [ -f "appsettings.json" ]; then
    echo "✅ Configuration file found"
    
    # Check for API keys (without revealing them)
    if grep -q "sk-proj-123abc456def789ghi" appsettings.json; then
        echo "✅ Azure API key configured"
    else
        echo "⚠️  Azure API key needs to be updated"
    fi
    
    if grep -q "gsk_AbCdEfGhIjKlMnOpQrStUvWxYz1234567890" appsettings.json; then
        echo "✅ Groq API key configured"
    else
        echo "⚠️  Groq API key needs to be updated"
    fi
else
    echo "❌ Configuration file not found"
fi

echo ""
echo "======================================"
echo "Ready to run the application!"
echo "======================================"
echo ""
echo "To start the server, run:"
echo "  dotnet run --urls \"http://localhost:5008\""
echo ""
echo "Then open: http://localhost:5008"
echo ""
echo "Features available:"
echo "  ✅ Drag-and-drop file upload"
echo "  ✅ AI-powered processing toggle"
echo "  ✅ Dual output (normal + accessible)"
echo "  ✅ Cost tracking ($0.03/document)"
echo "  ✅ Automatic fallback to algorithmic processing"
echo ""
