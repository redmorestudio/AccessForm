#!/bin/bash

# AccessForm Release Script v2
# Includes user documentation in release packages

echo "🚀 AccessForm Release Manager"
echo "=============================="
echo ""

# Get current version from VERSION file
CURRENT_VERSION=$(cat VERSION 2>/dev/null || echo "1.0.0")
echo "Current version: $CURRENT_VERSION"
echo ""

# Ask for new version
echo "Enter new version number (or press Enter to keep $CURRENT_VERSION):"
read NEW_VERSION

if [ -z "$NEW_VERSION" ]; then
    NEW_VERSION=$CURRENT_VERSION
else
    # Update VERSION file
    echo $NEW_VERSION > VERSION
    echo "✅ Updated VERSION file to $NEW_VERSION"
fi

# Ask for release notes
echo ""
echo "Enter a brief description of what's new in this release:"
echo "(Press Enter twice when done)"
RELEASE_NOTES=""
while IFS= read -r line; do
    [ -z "$line" ] && break
    RELEASE_NOTES="${RELEASE_NOTES}${line}\n"
done

# Update CHANGELOG
echo "" >> CHANGELOG.md
echo "## v$NEW_VERSION - $(date +%Y-%m-%d)" >> CHANGELOG.md
echo "$RELEASE_NOTES" >> CHANGELOG.md
echo "✅ Updated CHANGELOG.md"

# Commit changes
echo ""
echo "📦 Committing changes..."
git add .
git commit -m "Release v$NEW_VERSION

$RELEASE_NOTES"

# Push to GitHub
echo "📤 Pushing to GitHub..."
git push origin master:main

# Create a git tag
echo "🏷️  Creating version tag..."
git tag -a "v$NEW_VERSION" -m "Release v$NEW_VERSION

$RELEASE_NOTES"
git push origin "v$NEW_VERSION"

# Build release packages (optional)
echo ""
echo "Would you like to build distribution packages? (y/n)"
read BUILD_PACKAGES

if [ "$BUILD_PACKAGES" = "y" ]; then
    echo "🔨 Building distribution packages..."
    
    # Create dist directory
    mkdir -p dist
    rm -rf dist/release-$NEW_VERSION
    
    # Build self-contained executable for current platform
    echo "  Building self-contained executable..."
    dotnet publish AccessFormServer.csproj \
        -c Release \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:PublishTrimmed=false \
        -o "dist/release-$NEW_VERSION"
    
    # Copy user-friendly files to release
    echo "  Adding documentation and scripts..."
    cp README-RELEASE.md "dist/release-$NEW_VERSION/README.md"
    cp START-WINDOWS.bat "dist/release-$NEW_VERSION/" 2>/dev/null || true
    cp START-MAC-LINUX.sh "dist/release-$NEW_VERSION/START.sh" 2>/dev/null || true
    
    # Make the start script executable
    chmod +x "dist/release-$NEW_VERSION/START.sh" 2>/dev/null || true
    
    # Create version file for users
    echo "AccessForm v$NEW_VERSION" > "dist/release-$NEW_VERSION/VERSION.txt"
    echo "Released: $(date +%Y-%m-%d)" >> "dist/release-$NEW_VERSION/VERSION.txt"
    
    # Create quick start file
    cat > "dist/release-$NEW_VERSION/QUICK-START.txt" << EOF
AccessForm v$NEW_VERSION - Quick Start
=====================================

1. Run the application:
   - Windows: Double-click START-WINDOWS.bat
   - Mac/Linux: Run ./START.sh

2. Open your browser to:
   http://localhost:5008

3. Drag and drop your Word or PDF file

4. Click Convert or Remediate

5. Download your accessible PDF!

For detailed instructions, see README.md
EOF
    
    # Create platform-specific archive
    echo "  Creating archive..."
    cd dist
    if [[ "$OSTYPE" == "darwin"* ]]; then
        # macOS
        if [[ $(uname -m) == "arm64" ]]; then
            PLATFORM="macos-arm64"
        else
            PLATFORM="macos-x64"
        fi
        tar -czf "AccessForm-$NEW_VERSION-$PLATFORM.tar.gz" "release-$NEW_VERSION"
        PACKAGE_FILE="AccessForm-$NEW_VERSION-$PLATFORM.tar.gz"
    elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
        # Linux
        tar -czf "AccessForm-$NEW_VERSION-linux-x64.tar.gz" "release-$NEW_VERSION"
        PACKAGE_FILE="AccessForm-$NEW_VERSION-linux-x64.tar.gz"
    else
        # Windows (if running on Windows Git Bash)
        zip -r "AccessForm-$NEW_VERSION-windows-x64.zip" "release-$NEW_VERSION"
        PACKAGE_FILE="AccessForm-$NEW_VERSION-windows-x64.zip"
    fi
    cd ..
    
    echo "✅ Package created: dist/$PACKAGE_FILE"
    echo ""
    echo "📦 Package contents:"
    echo "  - AccessFormServer (main executable)"
    echo "  - README.md (user instructions)"
    echo "  - QUICK-START.txt (simple guide)"
    echo "  - START scripts (easy launch)"
    echo "  - VERSION.txt (version info)"
fi

echo ""
echo "✨ Release v$NEW_VERSION complete!"
echo ""
echo "📋 Next steps:"
echo "  1. Go to: https://github.com/redmorestudio/AccessForm/releases/new"
echo "  2. Choose tag: v$NEW_VERSION"
echo "  3. Release title: AccessForm v$NEW_VERSION"
echo "  4. Copy this release description:"
echo ""
echo "------- COPY BELOW -------"
echo "## What's New"
echo -e "$RELEASE_NOTES"
echo ""
echo "## Downloads"
echo ""
echo "Download the appropriate package for your system:"
echo "- **Windows**: AccessForm-$NEW_VERSION-windows-x64.zip"
echo "- **macOS Intel**: AccessForm-$NEW_VERSION-macos-x64.tar.gz"
echo "- **macOS Apple Silicon**: AccessForm-$NEW_VERSION-macos-arm64.tar.gz"
echo "- **Linux**: AccessForm-$NEW_VERSION-linux-x64.tar.gz"
echo ""
echo "## Quick Start"
echo "1. Extract the downloaded package"
echo "2. Windows: Double-click START-WINDOWS.bat"
echo "   Mac/Linux: Run ./START.sh"
echo "3. Open browser to http://localhost:5008"
echo ""
echo "See README.md in the package for detailed instructions."
echo "------- COPY ABOVE -------"
echo ""
if [ "$BUILD_PACKAGES" = "y" ]; then
    echo "  5. Upload package: dist/$PACKAGE_FILE"
fi
echo ""
echo "🔗 Repository: https://github.com/redmorestudio/AccessForm"
echo "🏷️  Version tag: v$NEW_VERSION"
