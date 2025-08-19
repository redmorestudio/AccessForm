#!/bin/bash

# AccessForm Release Script
# Run this when you have significant updates ready to release

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
    
    # Build self-contained executable for current platform
    echo "  Building self-contained executable..."
    dotnet publish AccessFormServer.csproj \
        -c Release \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:PublishTrimmed=false \
        -o "dist/release-$NEW_VERSION"
    
    # Create zip archive
    echo "  Creating archive..."
    cd dist
    if [[ "$OSTYPE" == "darwin"* ]]; then
        # macOS
        zip -r "AccessForm-$NEW_VERSION-macos.zip" "release-$NEW_VERSION"
        PACKAGE_FILE="AccessForm-$NEW_VERSION-macos.zip"
    elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
        # Linux
        tar -czf "AccessForm-$NEW_VERSION-linux.tar.gz" "release-$NEW_VERSION"
        PACKAGE_FILE="AccessForm-$NEW_VERSION-linux.tar.gz"
    else
        # Windows
        zip -r "AccessForm-$NEW_VERSION-windows.zip" "release-$NEW_VERSION"
        PACKAGE_FILE="AccessForm-$NEW_VERSION-windows.zip"
    fi
    cd ..
    
    echo "✅ Package created: dist/$PACKAGE_FILE"
fi

echo ""
echo "✨ Release v$NEW_VERSION complete!"
echo ""
echo "📋 Next steps:"
echo "  1. Go to: https://github.com/redmorestudio/AccessForm/releases/new"
echo "  2. Choose tag: v$NEW_VERSION"
echo "  3. Release title: AccessForm v$NEW_VERSION"
echo "  4. Add release notes from above"
if [ "$BUILD_PACKAGES" = "y" ]; then
    echo "  5. Upload package: dist/$PACKAGE_FILE"
fi
echo ""
echo "🔗 Repository: https://github.com/redmorestudio/AccessForm"
echo "🏷️  Version tag: v$NEW_VERSION"
