# AccessForm - PDF Accessibility Tool

## AI-Powered Document Accessibility for Government Forms

AccessForm transforms Word documents and existing PDFs into fully accessible, Section 508 and WCAG 2.1 AA compliant PDF forms with intelligent field detection and automatic accessibility enhancements.

## 🎯 Key Features

- **Dual Input Support**: Process Word documents (.docx) or existing PDF files
- **Smart Field Detection**: Automatically identifies and creates form fields from underscores and brackets
- **Accessibility Compliance**: Ensures Section 508 and WCAG 2.1 AA standards
- **No AI Required**: Core functionality works without external AI services
- **Privacy-First**: All processing happens locally, no data leaves your machine
- **Dual Output**: Generates both original and accessible versions

## 📦 Installation

### Prerequisites

- .NET 8.0 SDK or later
- Windows, macOS, or Linux
- 4GB RAM minimum (8GB recommended for large forms)

### Quick Install

#### Option 1: Download Release (Recommended)

1. Go to the [Releases](https://github.com/redmorestudio/AccessForm/releases) page
2. Download the latest version for your operating system
3. Extract the ZIP file
4. Run the executable:
   - **Windows**: Double-click `AccessForm.exe`
   - **macOS**: Run `./AccessForm` in Terminal
   - **Linux**: Run `./AccessForm` in Terminal

#### Option 2: Build from Source

```bash
# Clone the repository
git clone https://github.com/redmorestudio/AccessForm.git
cd AccessForm

# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run --project AccessFormServer.csproj --urls "http://localhost:5008"
```

## 🚀 Usage

1. **Start the Application**
   - The app will open in your default browser at `http://localhost:5008`

2. **Upload Your Document**
   - Drag and drop a Word (.docx) or PDF file
   - Or click to browse and select your file

3. **Review Detected Fields**
   - The system automatically identifies form fields
   - Review and adjust field types as needed

4. **Generate Accessible PDFs**
   - Click "Generate PDFs" to create both versions
   - Download the accessible version with `_accessible` suffix

## 📋 Supported Field Types

- Text fields (single and multi-line)
- Checkboxes and radio buttons
- Dropdowns and combo boxes
- Date/time fields
- Signature fields
- Calculated fields
- And 25+ more specialized types

## 🔄 Version History

See [CHANGELOG.md](CHANGELOG.md) for detailed version history.

## 🤝 Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## 📄 License

This project is proprietary software. See [LICENSE](LICENSE) for details.

## 🆘 Support

- **Documentation**: [docs/](docs/)
- **Issues**: [GitHub Issues](https://github.com/redmorestudio/AccessForm/issues)
- **Email**: support@redmorestudio.com

## 🏢 About Redmore Studio

Redmore Studio specializes in accessibility solutions for government and enterprise organizations.

---

© 2025 Redmore Studio. All rights reserved.