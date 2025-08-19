# How to Create and Upload AccessForm Releases

## Step 1: Build the Release Packages

Run this in your terminal:
```bash
cd "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"

# Make scripts executable (one time only)
chmod +x release-v3.sh

# Run the release script
./release-v3.sh
```

When prompted:
- Enter version: `1.0.0` (or keep current)
- Enter description: Brief notes about this release
- Build packages: Type `y`

This creates packages in the `dist/` folder.

## Step 2: Create GitHub Release

### Go to GitHub Releases Page
https://github.com/redmorestudio/AccessForm/releases/new

### Fill in the Release Details

**Choose a tag**: Create new tag `v1.0.0`

**Release title**: `AccessForm v1.0.0 - Ready to Use`

**Release description** (copy and paste this):

```markdown
## 🎉 AccessForm v1.0.0 - First Public Release

### Download for Your Platform

Choose the right version for your system and download:

| Platform | Download | Instructions |
|----------|----------|--------------|
| **Windows** | [`AccessForm-v1.0.0-windows.zip`](link) | Extract → Double-click `START-WINDOWS.bat` |
| **macOS Intel** | [`AccessForm-v1.0.0-macos-x64.zip`](link) | Extract → Double-click `AccessForm.command` |
| **macOS M1/M2/M3** | [`AccessForm-v1.0.0-macos-arm64.zip`](link) | Extract → Double-click `AccessForm.command` |
| **Linux** | [`AccessForm-v1.0.0-linux.tar.gz`](link) | Extract → Run `./AccessForm.command` |

### ✨ What's New
- Initial release
- Word to PDF conversion with accessibility
- PDF remediation for existing files
- WCAG 2.1 AA and Section 508 compliance
- No installation required - just download and run!

### 🚀 Quick Start

#### Windows Users
1. Download the Windows ZIP
2. Extract to any folder
3. Double-click `START-WINDOWS.bat`
4. Your browser opens to AccessForm

#### Mac Users  
1. Download the Mac ZIP for your processor type
2. Double-click to extract
3. Double-click `AccessForm.command`
4. If prompted about "unidentified developer", click Open

**No terminal or command line needed!**

### 📋 Features
- ✅ Convert Word documents to accessible PDFs
- ✅ Remediate existing PDFs for accessibility
- ✅ Dual output (standard + accessible versions)
- ✅ 100% local processing (privacy-first)
- ✅ No installation required
- ✅ Government compliance (WCAG 2.1 AA, Section 508)

### 🔒 Privacy & Security
All processing happens on your computer. No files are uploaded anywhere. No internet connection required after download.

### 📖 Documentation
- See `README.md` in the download for detailed instructions
- Report issues: https://github.com/redmorestudio/AccessForm/issues

### 💡 Not sure which Mac version?
- Apple menu → About This Mac
- Look for "Chip" or "Processor"
- Apple M1/M2/M3 = ARM64 version
- Intel = x64 version
```

### Upload the Package Files

Click "Attach binaries by dropping them here" and upload:
- `dist/AccessForm-v1.0.0-windows.zip`
- `dist/AccessForm-v1.0.0-macos-x64.zip`
- `dist/AccessForm-v1.0.0-macos-arm64.zip`
- `dist/AccessForm-v1.0.0-linux.tar.gz`

### Publish
- Check "Set as the latest release"
- Click "Publish release"

## Step 3: Update Repository README

Replace the current README.md with README-NEW.md:
```bash
cd "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"
cp README-NEW.md README.md
git add README.md
git commit -m "Update README to focus on downloads, not building"
git push origin master:main
```

## What Gets Created

Each platform package contains:
```
AccessForm-v1.0.0/
├── AccessFormServer       # Main executable
├── AccessForm.command     # Mac double-click launcher
├── START-WINDOWS.bat      # Windows double-click launcher
├── README.md              # Simple user instructions
├── QUICK-START.txt        # 5-step guide
└── VERSION.txt            # Version info
```

## Building Packages Manually (if script fails)

### For current Mac (ARM64):
```bash
cd "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"
mkdir -p dist/AccessForm-v1.0.0

# Build
dotnet publish AccessFormServer.csproj \
  -c Release \
  --self-contained true \
  -p:PublishSingleFile=true \
  -r osx-arm64 \
  -o dist/AccessForm-v1.0.0

# Add user files
cp AccessForm.command dist/AccessForm-v1.0.0/
cp README-RELEASE-v2.md dist/AccessForm-v1.0.0/README.md
cp START-WINDOWS.bat dist/AccessForm-v1.0.0/
echo "AccessForm v1.0.0" > dist/AccessForm-v1.0.0/VERSION.txt

# Make executable
chmod +x dist/AccessForm-v1.0.0/AccessFormServer
chmod +x dist/AccessForm-v1.0.0/AccessForm.command

# Create ZIP
cd dist
zip -r AccessForm-v1.0.0-macos-arm64.zip AccessForm-v1.0.0
```

### For Windows (cross-compile from Mac):
```bash
dotnet publish AccessFormServer.csproj \
  -c Release \
  --self-contained true \
  -p:PublishSingleFile=true \
  -r win-x64 \
  -o dist/AccessForm-v1.0.0-windows

# Add files and zip
```

## Testing Before Release

1. Extract your ZIP as a user would
2. Try double-clicking the launcher
3. Verify browser opens
4. Test converting a file
5. Check both PDFs download

## After Publishing

1. Test download links work
2. Update any documentation that references versions
3. Announce the release (if you have users waiting)

---

**Remember**: Users want to download and run, not build from source. The release page should make it dead simple to get started.
