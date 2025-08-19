# AccessForm

**Easy-to-use tool for creating accessible PDF forms from Word documents**

[![Version](https://img.shields.io/github/v/release/redmorestudio/AccessForm)](https://github.com/redmorestudio/AccessForm/releases)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## 🎯 Download Ready-to-Use Version

### [⬇️ Download Latest Release](https://github.com/redmorestudio/AccessForm/releases/latest)

Choose your platform:
- **Windows**: Download `AccessForm-v1.0.0-windows.zip`
- **macOS Intel**: Download `AccessForm-v1.0.0-macos-x64.zip`  
- **macOS Apple Silicon (M1/M2/M3)**: Download `AccessForm-v1.0.0-macos-arm64.zip`
- **Linux**: Download `AccessForm-v1.0.0-linux.tar.gz`

### Quick Start (No Installation Required!)

#### Windows
1. Download and extract the ZIP
2. Double-click `START-WINDOWS.bat`
3. Your browser opens automatically

#### Mac
1. Download and extract the ZIP (just double-click it)
2. Double-click `AccessForm.command`
3. If prompted, click "Open" to allow running
4. Your browser opens automatically

**No terminal, no installation, no complex setup!**

---

## ✨ What It Does

AccessForm converts Word documents (.docx) and PDFs into fully accessible PDFs that meet:
- ✅ **WCAG 2.1 AA** compliance
- ✅ **Section 508** compliance  
- ✅ **Screen reader** compatibility
- ✅ **Keyboard navigation** support

### Key Features

🎯 **Dual Output**
- Get both standard and accessible versions
- Choose based on your needs

🔒 **100% Private**
- Runs entirely on your computer
- No files uploaded anywhere
- No internet required after download

⚡ **Smart Processing**
- Auto-detects form fields
- Adds proper labels and descriptions
- Sets correct tab order
- Fixes common accessibility issues

---

## 📊 Use Cases

Perfect for:
- Government agencies requiring Section 508 compliance
- Organizations meeting ADA requirements
- Educational institutions ensuring equal access
- Businesses committed to inclusivity
- Anyone creating forms that everyone can use

---

## 🚀 Getting Started

### Option 1: Download Pre-Built Version (Recommended)
**[Download from Releases](https://github.com/redmorestudio/AccessForm/releases/latest)** - Ready to run, no setup needed!

### Option 2: Build from Source (Developers)

Requirements:
- .NET 9.0 SDK
- Syncfusion license (free community or commercial)

```bash
# Clone repository
git clone https://github.com/redmorestudio/AccessForm.git
cd AccessForm

# Add your Syncfusion license key to Program.cs

# Build and run
dotnet build AccessFormServer.csproj
dotnet run --project AccessFormServer.csproj --urls "http://localhost:5008"
```

---

## 📖 Documentation

- **[User Guide](README-RELEASE-v2.md)** - For end users
- **[Distribution Guide](DISTRIBUTION.md)** - Deployment options
- **[Development Workflow](WORKFLOW.md)** - For contributors
- **[Mac Distribution](MAC-DISTRIBUTION-GUIDE.md)** - Mac-specific info

---

## 🤝 Contributing

We welcome contributions! See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

### Development Setup
```bash
# Daily development
./push.sh  # Save work to GitHub

# Create release
./release-v3.sh  # Build and package for distribution
```

---

## 📜 License

MIT License - see [LICENSE](LICENSE) file for details.

---

## 🛟 Support

- **Issues**: [GitHub Issues](https://github.com/redmorestudio/AccessForm/issues)
- **Discussions**: [GitHub Discussions](https://github.com/redmorestudio/AccessForm/discussions)

---

## 🔄 Version History

See [CHANGELOG.md](CHANGELOG.md) for version history.

**Current Version**: v1.0.0

---

## ⭐ Why AccessForm?

- **No Installation**: Just download, extract, and run
- **User-Friendly**: No terminal or command line needed
- **Privacy-First**: All processing happens locally
- **Professional Results**: Government-compliant PDFs
- **Free & Open Source**: MIT licensed

---

**Ready to make your PDFs accessible?**  
**[Download AccessForm Now →](https://github.com/redmorestudio/AccessForm/releases/latest)**
