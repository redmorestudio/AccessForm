# AccessForm - Quick Start Guide

## What is AccessForm?
AccessForm converts Word documents to fully accessible PDF forms that meet government compliance standards (WCAG 2.1 AA and Section 508).

## Installation & Running

### Windows
1. **Download** `AccessForm-v1.0.0-windows.zip`
2. **Extract** to any folder (e.g., `C:\AccessForm`)
3. **Run** `AccessFormServer.exe`
4. **Open browser** to http://localhost:5008

### macOS (Intel)
1. **Download** `AccessForm-v1.0.0-macos-x64.tar.gz`
2. **Extract**: Double-click or run:
   ```bash
   tar -xzf AccessForm-v1.0.0-macos-x64.tar.gz
   ```
3. **Make executable** (first time only):
   ```bash
   chmod +x AccessFormServer
   ```
4. **Run**:
   ```bash
   ./AccessFormServer
   ```
5. **Open browser** to http://localhost:5008

### macOS (Apple Silicon/M1/M2)
1. **Download** `AccessForm-v1.0.0-macos-arm64.tar.gz`
2. **Extract**: Double-click or run:
   ```bash
   tar -xzf AccessForm-v1.0.0-macos-arm64.tar.gz
   ```
3. **Make executable** (first time only):
   ```bash
   chmod +x AccessFormServer
   ```
4. **Run**:
   ```bash
   ./AccessFormServer
   ```
5. **Open browser** to http://localhost:5008

### Linux
1. **Download** `AccessForm-v1.0.0-linux-x64.tar.gz`
2. **Extract**:
   ```bash
   tar -xzf AccessForm-v1.0.0-linux-x64.tar.gz
   ```
3. **Make executable**:
   ```bash
   chmod +x AccessFormServer
   ```
4. **Run**:
   ```bash
   ./AccessFormServer
   ```
5. **Open browser** to http://localhost:5008

## How to Use

1. **Open your browser** to http://localhost:5008
2. **Drag and drop** your Word document (.docx) or PDF file
3. **Click Convert** or **Remediate**
4. **Download** both files:
   - Original PDF (standard version)
   - Accessible PDF (compliant version)

## Features

✅ **Dual Output**
- Get both standard and accessible versions
- Choose which to use based on your needs

✅ **Full Compliance**
- WCAG 2.1 AA compliant
- Section 508 compliant
- Works with screen readers

✅ **Smart Processing**
- Automatically detects form fields
- Adds proper labels and descriptions
- Sets correct tab order

## Troubleshooting

### "Cannot execute binary file" (macOS/Linux)
Make the file executable:
```bash
chmod +x AccessFormServer
```

### "Port 5008 already in use"
Another program is using port 5008. Either:
1. Close the other program, or
2. Run on a different port:
   ```bash
   ./AccessFormServer --urls "http://localhost:5009"
   ```

### "Windows protected your PC" (Windows)
1. Click "More info"
2. Click "Run anyway"
(This happens with unsigned executables)

### Browser can't connect
1. Make sure AccessFormServer is running (check terminal/command prompt)
2. Try http://localhost:5008 (not https)
3. Check firewall isn't blocking the connection

### Files not converting
- Only .docx and .pdf files are supported
- Maximum file size: 50MB
- Files must not be password-protected

## System Requirements

- **Windows**: Windows 10 or later
- **macOS**: macOS 10.15 (Catalina) or later
- **Linux**: Ubuntu 20.04 or equivalent
- **Browser**: Chrome (recommended), Edge, or Firefox
- **RAM**: 2GB minimum, 4GB recommended
- **Disk**: 500MB free space

## Getting Help

- **Documentation**: https://github.com/redmorestudio/AccessForm
- **Issues**: https://github.com/redmorestudio/AccessForm/issues
- **Latest Version**: https://github.com/redmorestudio/AccessForm/releases

## Privacy & Security

- All processing happens locally on your computer
- No files are uploaded to any server
- No internet connection required after download
- Your documents remain completely private

## License

MIT License - Free to use and modify

---

**Version**: 1.0.0  
**Website**: https://github.com/redmorestudio/AccessForm
