# AccessForm System Overview
**Last Updated:** 2024-09-25
**Repository:** https://github.com/redmorestudio/AccessForm
**Current Branch:** main
**Latest Commit:** a95a8d0

## 🎯 Project Mission

AccessForm converts Word documents into Section 508/WCAG 2.1 compliant, accessible PDF forms with AI-powered field detection and interactive editing capabilities.

## 🏗️ System Architecture

### Core Technology Stack
- **Backend:** ASP.NET Core 8.0 (C#)
- **Frontend:** Blazor WebAssembly
- **PDF Processing:** Syncfusion, PyMuPDF, PassportPDF API
- **AI Field Detection:** Claude Vision API
- **Image Processing:** SkiaSharp
- **Development Port:** http://localhost:5002

### Document Processing Pipeline
```
Word Document → PDF Conversion → AI Field Detection → Interactive Editor → Accessible PDF Output
```

## 📂 Key System Components

### 1. Document Conversion Services
- **`Services/PdfCompleteRebuildService.cs`** - Core PDF processing and rebuilding
- **`Services/AsposePdfService.cs`** - Aspose PDF operations and optimization
- **`Services/PassportPdfService.cs`** - PassportPDF API integration for accessibility

### 2. AI Field Detection
- **`Services/ClaudeVisionFieldDetector.cs`** - Claude Vision API integration
- Automatically detects form fields from PDF images
- Extracts field names, types, positions, and properties
- Handles multi-page documents with coordinate mapping

### 3. Interactive Field Editor
- **`Pages/TagModificationModal.razor`** - Main field editing interface
- **`Pages/Index.razor`** - Primary application interface
- Visual PDF preview with field overlays
- Real-time field editing (position, size, type, properties)
- Multi-page navigation and field management

### 4. Coordinate Management
- **`Services/PdfCoordinateConverter.cs`** - Coordinate system transformations
- Handles PDF coordinates (72 DPI, bottom-left origin)
- Converts to display coordinates (150 DPI, top-left origin)
- Manages page-relative vs absolute coordinate systems

### 5. API Endpoints (`Program.cs`)
- **`/api/pdf-page-preview`** - Basic PDF page rendering
- **`/api/pdf-page-with-field-boxes`** - PDF with field overlays + fieldMap for clicks
- **`/api/detect-fields`** - AI field detection endpoint
- **Various utility endpoints** for PDF processing

## 🔧 Current Development Status

### ✅ What's Working
- **Document Upload & Processing** - Word to PDF conversion
- **AI Field Detection** - Claude Vision accurately identifies fields
- **Visual Field Editor** - Interactive field editing with real-time preview
- **Multi-page Support** - Page navigation and field filtering
- **Enhanced Visual Feedback** - Selected fields highly visible (red borders, yellow outlines)
- **Table Row Selection** - Clicking table rows selects/highlights fields
- **Field Property Editing** - Position, size, type, tooltip, validation
- **PDF Output Generation** - Section 508 compliant accessible PDFs

### 🚧 In Progress
- **Clickable Field Boxes** - Enable clicking field boxes in preview to select them
- **Interactive Field Cleaning** - Easy removal of AI detection false positives

### 📋 Planned Features
- **Right-click Context Menus** - Field operations directly on preview
- **Keyboard Shortcuts** - Delete key for field removal
- **Batch Field Operations** - Select multiple fields for bulk editing
- **Field Templates** - Common field configurations
- **Advanced Accessibility** - Enhanced WCAG 2.1 compliance features

## 🎨 User Interface Components

### Main Interface (`Pages/Index.razor`)
- Document upload area
- Processing status indicators
- Field detection controls
- Access to tag modification modal

### Field Editor (`Pages/TagModificationModal.razor`)
- **Left Panel:** Field list table with sorting, filtering, validation
- **Right Panel:** PDF preview with field overlays
- **Bottom Panel:** Field property editor
- **Navigation:** Page controls for multi-page documents

### Visual Field System
- **Color-coded field types:** Text (red), Checkboxes (blue), Dates (green), Signatures (purple)
- **Selection indicators:** Thick red border + yellow dashed outline + semi-transparent overlay
- **Field labels:** Names displayed on field overlays
- **Interactive feedback:** Hover states and click responses

## 🔄 Data Flow

### 1. Document Processing
```
User uploads Word → Conversion to PDF → AI analysis → Field extraction → Display in editor
```

### 2. Field Editing
```
User interactions → Blazor components → Field state updates → Visual preview updates → API calls
```

### 3. Preview Rendering
```
Field data → Server coordinate conversion → SkiaSharp rendering → Base64 image → Client display
```

## 🐛 Known Issues & Technical Debt

### Current Priority Issues
1. **Field Click Detection** - Browser coordinates don't align with fieldMap coordinates
2. **Coordinate System Complexity** - Multiple coordinate transformations can be confusing
3. **Performance** - Large PDFs with many fields can be slow to render

### Technical Debt
- **Obsolete SkiaSharp APIs** - Multiple warnings about deprecated methods
- **CSS Responsiveness** - Interface could be more mobile-friendly
- **Error Handling** - Could be more robust throughout the pipeline
- **Testing** - Limited automated testing coverage

## 🚀 Development Workflow

### Starting the System
```bash
cd "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"
ASPNETCORE_URLS="http://localhost:5002" dotnet run --project AccessFormServer.csproj --nologo --verbosity quiet
```

### Key Development Files
- **Main Application:** `Program.cs` (API endpoints, configuration)
- **UI Components:** `Pages/` directory (Blazor components)
- **Services:** `Services/` directory (Business logic, external APIs)
- **Configuration:** `appsettings.json`, `AccessFormServer.csproj`

### Git Workflow
- **Main branch:** Production-ready code
- **Feature branches:** For major new features
- **Commits:** Include Claude Code attribution in commit messages

## 🔍 Debugging & Troubleshooting

### Browser Console
- Extensive JavaScript debugging for field interactions
- Coordinate mapping debug output
- Click detection diagnostics

### Server Logs
- Field processing information
- Coordinate conversion details
- API endpoint activity
- AI detection results

### Common Issues
- **Field positioning problems:** Usually coordinate conversion issues
- **JavaScript errors:** Often scoping or timing related
- **PDF rendering issues:** Check SkiaSharp or Syncfusion integration
- **AI detection failures:** Verify Claude API configuration

## 🎯 Business Context

### Target Users
- Government agencies requiring Section 508 compliance
- Organizations needing accessible PDF forms
- Document accessibility consultants
- Legal and compliance teams

### Value Proposition
- **Automated accessibility:** AI-powered field detection reduces manual work
- **Visual editing:** Intuitive interface for field management
- **Compliance guarantee:** Outputs meet accessibility standards
- **Efficiency:** Faster than manual accessibility remediation

## 📚 External Dependencies

### APIs & Services
- **Claude Vision API** - AI field detection
- **PassportPDF API** - Accessibility processing
- **Syncfusion PDF** - Core PDF manipulation
- **PyMuPDF** - Alternative PDF processing

### Key NuGet Packages
- `Syncfusion.Pdf.Net.Core`
- `SkiaSharp`
- `Microsoft.AspNetCore.Components.WebAssembly`

## 🔮 Future Vision

### Short-term (Next 2-3 months)
- Complete interactive field cleaning system
- Enhanced accessibility validation
- Performance optimizations
- Mobile-responsive interface

### Long-term (6+ months)
- Batch document processing
- Template system for common forms
- Integration with document management systems
- Advanced AI features (field validation, smart suggestions)

---

## 📞 For New Claude Sessions

When starting a new session:
1. **Read this document** for general system context
2. **Check latest commits** for recent changes
3. **Run the development server** using commands above
4. **Review specific feature docs** if working on particular functionality
5. **Check browser console** for debugging information

The system is mature and stable - most development now focuses on UX improvements and advanced features rather than core functionality.