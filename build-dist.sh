#!/bin/bash

# AccessForm Distribution Build Script
# Creates distribution packages for different platforms

VERSION="1.0.0"
PROJECT_NAME="AccessForm"
BUILD_DIR="./dist"
SOURCE_DIR="."

echo "🔧 AccessForm Distribution Builder v$VERSION"
echo "========================================="

# Clean previous builds
echo "📦 Cleaning previous builds..."
rm -rf $BUILD_DIR
mkdir -p $BUILD_DIR

# Build for different platforms
echo "🏗️ Building distribution packages..."

# 1. Self-contained Windows x64
echo "  → Building Windows x64..."
dotnet publish AccessFormServer.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true \
  -o "$BUILD_DIR/windows-x64"

# Create Windows zip
cd "$BUILD_DIR/windows-x64"
zip -r "../${PROJECT_NAME}-${VERSION}-windows-x64.zip" .
cd ../..

# 2. Self-contained macOS x64
echo "  → Building macOS x64..."
dotnet publish AccessFormServer.csproj \
  -c Release \
  -r osx-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true \
  -o "$BUILD_DIR/macos-x64"

# Create macOS tar.gz
cd "$BUILD_DIR/macos-x64"
tar -czf "../${PROJECT_NAME}-${VERSION}-macos-x64.tar.gz" .
cd ../..

# 3. Self-contained macOS ARM64 (Apple Silicon)
echo "  → Building macOS ARM64..."
dotnet publish AccessFormServer.csproj \
  -c Release \
  -r osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true \
  -o "$BUILD_DIR/macos-arm64"

# Create macOS ARM64 tar.gz
cd "$BUILD_DIR/macos-arm64"
tar -czf "../${PROJECT_NAME}-${VERSION}-macos-arm64.tar.gz" .
cd ../..

# 4. Self-contained Linux x64
echo "  → Building Linux x64..."
dotnet publish AccessFormServer.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true \
  -o "$BUILD_DIR/linux-x64"

# Create Linux tar.gz
cd "$BUILD_DIR/linux-x64"
tar -czf "../${PROJECT_NAME}-${VERSION}-linux-x64.tar.gz" .
cd ../..

# 5. Docker image
echo "  → Building Docker image..."
docker build -t redmorestudio/accessform:$VERSION -t redmorestudio/accessform:latest -f docker/Dockerfile .

# 6. Create source code archive
echo "  → Creating source archive..."
git archive --format=tar.gz --output="$BUILD_DIR/${PROJECT_NAME}-${VERSION}-source.tar.gz" HEAD

# Create checksums
echo "📝 Generating checksums..."
cd $BUILD_DIR
shasum -a 256 *.zip *.tar.gz > checksums.txt
cd ..

# Summary
echo ""
echo "✅ Distribution packages created successfully!"
echo "========================================="
echo "📁 Packages available in: $BUILD_DIR"
echo ""
ls -lh $BUILD_DIR/*.{zip,tar.gz} 2>/dev/null
echo ""
echo "📋 Checksums:"
cat $BUILD_DIR/checksums.txt
echo ""
echo "🚀 Next steps:"
echo "  1. Test each package on target platform"
echo "  2. Upload to GitHub Releases"
echo "  3. Push Docker image: docker push redmorestudio/accessform:$VERSION"
echo "  4. Update documentation with download links"
