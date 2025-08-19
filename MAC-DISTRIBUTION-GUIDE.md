# Creating DMG Files for Mac Distribution

## Why DMG?

DMG files are the gold standard for Mac app distribution because:
- **Professional appearance** - Users expect DMG for Mac apps
- **Drag-and-drop installation** - User drags app to Applications folder
- **Background image** - Can include instructions visually
- **Code signing** - Can be notarized by Apple for security
- **No terminal** - Everything is visual

## Current Solution: ZIP with .command file

For now, we're using ZIP files with a `.command` launcher because:
- ✅ **No terminal needed** - Double-click to extract and run
- ✅ **Simple** - No additional tools required
- ✅ **Works immediately** - No code signing required
- ✅ **Cross-platform build** - Can build on any OS

## How Our Current Mac Solution Works

1. User downloads `AccessForm-v1.0.0-macos.zip`
2. Double-clicks ZIP to extract (built into macOS)
3. Double-clicks `AccessForm.command` to run
4. If security prompt appears, clicks "Open"
5. Browser opens automatically

## Future: Creating a DMG

### Option 1: Simple DMG (Manual)
```bash
# Create a folder with your app
mkdir -p dmg-contents
cp -r dist/release-* dmg-contents/AccessForm

# Create DMG
hdiutil create -volname "AccessForm" \
  -srcfolder dmg-contents \
  -ov -format UDZO \
  AccessForm-v1.0.0.dmg
```

### Option 2: Professional DMG with Background
```bash
# Use create-dmg tool
brew install create-dmg

create-dmg \
  --volname "AccessForm" \
  --volicon "AccessForm.icns" \
  --background "installer-background.png" \
  --window-pos 200 120 \
  --window-size 600 400 \
  --icon-size 100 \
  --icon "AccessForm" 175 190 \
  --hide-extension "AccessForm" \
  --app-drop-link 425 190 \
  "AccessForm-v1.0.0.dmg" \
  "dmg-contents/"
```

### Option 3: Using DMG Canvas (GUI Tool)
- Purchase DMG Canvas from https://www.araelium.com/dmgcanvas
- Drag and drop interface
- Professional templates
- Automatic code signing

## Making a Proper Mac App Bundle

To create a real `.app` that goes in a DMG:

### 1. Create App Bundle Structure
```
AccessForm.app/
├── Contents/
│   ├── Info.plist
│   ├── MacOS/
│   │   ├── AccessFormServer     # Your executable
│   │   └── launcher.sh          # Shell script wrapper
│   └── Resources/
│       └── icon.icns
```

### 2. Info.plist
```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" 
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>launcher.sh</string>
    <key>CFBundleIdentifier</key>
    <string>com.redmorestudio.accessform</string>
    <key>CFBundleName</key>
    <string>AccessForm</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
</dict>
</plist>
```

### 3. Launcher Script
```bash
#!/bin/bash
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$DIR"
./AccessFormServer
```

## Code Signing (Avoiding "Unidentified Developer")

### Free Option: Ad-hoc Signing
```bash
# Sign the app (requires Apple Developer account)
codesign --force --deep --sign - AccessForm.app
```

### Paid Option: Developer ID ($99/year)
```bash
# Sign with Developer ID
codesign --force --deep --sign "Developer ID Application: Your Name" AccessForm.app

# Notarize with Apple
xcrun altool --notarize-app \
  --primary-bundle-id "com.redmorestudio.accessform" \
  --username "your@email.com" \
  --password "@keychain:AC_PASSWORD" \
  --file AccessForm.dmg
```

## Why We're Sticking with ZIP for Now

1. **Simplicity** - Works immediately, no setup
2. **No Apple Developer Account** - Saves $99/year
3. **Cross-platform builds** - Can build on Windows/Linux
4. **Good enough** - .command file is double-clickable
5. **Fast iteration** - No notarization wait times

## Future Roadmap

### Phase 1 (Current) ✅
- ZIP distribution
- .command launcher (double-clickable)
- No terminal required

### Phase 2 (Next)
- Create proper .app bundle
- Simple DMG with drag-to-Applications

### Phase 3 (Later)
- Apple Developer account
- Code signing
- Notarization
- Mac App Store (maybe)

## User Experience Comparison

### Current (ZIP + .command):
1. Download ZIP
2. Double-click to extract
3. Double-click AccessForm.command
4. Click "Open" if security prompt
✅ 4 steps, no terminal

### Future (DMG + .app):
1. Download DMG
2. Double-click to mount
3. Drag to Applications
4. Double-click in Applications
✅ 4 steps, more "Mac-like"

### Best (Mac App Store):
1. Click "Get" in App Store
2. Click app in Launchpad
✅ 2 steps, fully trusted

## For Now: Our Solution Works!

The ZIP + .command approach:
- ✅ No terminal required
- ✅ Double-click to run
- ✅ Auto-opens browser
- ✅ Professional enough
- ✅ Can upgrade to DMG later

## Creating DMG When Ready

When you want to create DMGs:
```bash
# Install create-dmg
brew install create-dmg

# Add to release script
create-dmg \
  --volname "AccessForm $VERSION" \
  --window-size 600 400 \
  --hide-extension "AccessForm.command" \
  --app-drop-link 450 200 \
  "AccessForm-$VERSION.dmg" \
  "dist/release-$VERSION/"
```

This will create a professional DMG without requiring an Apple Developer account.
