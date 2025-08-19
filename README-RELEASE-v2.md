# AccessForm - Quick Start Guide

## What is AccessForm?
AccessForm converts Word documents to fully accessible PDF forms that meet government compliance standards (WCAG 2.1 AA and Section 508).

## Installation & Running

### 🪟 Windows
1. **Download** `AccessForm-v1.0.0-windows.zip`
2. **Extract** by right-clicking → "Extract All"
3. **Double-click** `START-WINDOWS.bat`
4. **Your browser opens** to http://localhost:5008

**That's it! No terminal needed.**

---

### 🍎 Mac (Both Intel and Apple Silicon)
1. **Download** the right version:
   - Intel Macs: `AccessForm-v1.0.0-macos-x64.zip`
   - Apple Silicon (M1/M2/M3): `AccessForm-v1.0.0-macos-arm64.zip`
2. **Double-click** the ZIP file to extract
3. **Double-click** `AccessForm.command`
4. **If macOS asks**, click "Open" to confirm you want to run it
5. **Your browser opens** to http://localhost:5008

**That's it! No terminal needed.**

> **First time only**: macOS may show "AccessForm.command can't be opened because it is from an unidentified developer." Just right-click the file, choose "Open", then click "Open" again.

---

### 🐧 Linux
1. **Download** `AccessForm-v1.0.0-linux-x64.tar.gz`
2. **Extract**: Right-click → "Extract Here" or:
   ```bash
   tar -xzf AccessForm-v1.0.0-linux-x64.tar.gz
   ```
3. **Run**:
   ```bash
   ./AccessForm.command
   ```
4. **Open browser** to http://localhost:5008

---

## How to Use

1. **Your browser opens automatically** (or go to http://localhost:5008)
2. **Drag and drop** your Word document (.docx) or PDF file
3. **Click Convert** or **Remediate**
4. **Download** both files:
   - Original PDF (standard version)
   - Accessible PDF (compliant version)

## Features

✅ **No Installation Required**
- Just extract and run
- No system changes
- Completely portable

✅ **Privacy First**
- Runs entirely on your computer
- No files uploaded anywhere
- No internet required after download

✅ **Full Compliance**
- WCAG 2.1 AA compliant
- Section 508 compliant
- Works with screen readers

## Troubleshooting

### Mac: "Unidentified developer" warning
- **Solution**: Right-click `AccessForm.command` → Open → Click "Open"
- This is normal for apps not from the App Store
- You only need to do this once

### Windows: "Windows protected your PC"
1. Click "More info"
2. Click "Run anyway"
- This is normal for new applications

### Browser can't connect
1. Make sure AccessForm is running (check the terminal window)
2. Try http://localhost:5008 (not https)
3. Wait 5 seconds and refresh

### Port already in use
Another program is using port 5008:
- **Windows**: Close the other program or restart
- **Mac/Linux**: Close the other program or use:
  ```bash
  lsof -i :5008  # Find what's using the port
  ```

### Files not converting
- Only .docx and .pdf files are supported
- Maximum file size: 50MB
- Files must not be password-protected

## Stopping the Server

- **Windows**: Close the command window
- **Mac**: Close the Terminal window or press Cmd+C
- **Linux**: Press Ctrl+C in the terminal

## System Requirements

- **Windows**: Windows 10 or later
- **Mac**: macOS 10.15 (Catalina) or later
- **Linux**: Ubuntu 20.04 or equivalent
- **Browser**: Chrome (recommended), Edge, or Firefox
- **RAM**: 2GB minimum, 4GB recommended
- **Disk**: 500MB free space

## Getting Help

- **Documentation**: https://github.com/redmorestudio/AccessForm
- **Issues**: https://github.com/redmorestudio/AccessForm/issues
- **Latest Version**: https://github.com/redmorestudio/AccessForm/releases

## Updates

To check for updates:
1. Visit https://github.com/redmorestudio/AccessForm/releases
2. Download the latest version
3. Replace your current folder with the new one

## Privacy & Security

✅ **100% Local Processing**
- All conversion happens on your computer
- No data is sent to any server
- No tracking or analytics
- No account required
- Your documents stay completely private

## License

MIT License - Free to use and modify

---

**Version**: 1.0.0  
**Website**: https://github.com/redmorestudio/AccessForm

## Quick Tips

💡 **Tip**: Keep the terminal/command window open while using AccessForm. Closing it stops the server.

💡 **Tip**: Bookmark http://localhost:5008 for quick access next time.

💡 **Tip**: You can process multiple files - just drag and drop the next one after downloading the first.
