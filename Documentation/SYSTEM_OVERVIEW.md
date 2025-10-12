# AccessForm System Overview
**Last Updated:** 2025-01-03
**Repository:** https://github.com/redmorestudio/AccessForm
**Current Branch:** feature/cherry-pick-improvements
**Development Port:** http://localhost:5001

## 🎯 Project Mission

AccessForm converts Word documents into Section 508/WCAG 2.1 compliant, accessible PDF forms with AI-powered field detection and interactive editing capabilities.

## 🏗️ System Architecture

### Core Technology Stack
- **Backend:** ASP.NET Core 8.0 (C#)
- **Frontend:** Blazor Server (not WebAssembly)
- **PDF Processing:** Aspose.PDF, Syncfusion PDF
- **AI Field Detection:** Claude Sonnet 4.5 (claude-sonnet-4-20250514), planned GPT-5 Vision
- **Image Processing:** SkiaSharp
- **Development Port:** http://localhost:5001

### Recent Major Updates (2025-01-03)
- ✅ **Upgraded all Claude API calls to Sonnet 4.5** for improved accuracy
- ✅ **Fixed signature field detection** - now preserves visual signature fields (e.g., "Signature: X")
- ✅ **Added OpenAI API key** for planned multi-stage validation pipeline
- 📋 **Multi-Stage Validation Plan** - See MULTI_STAGE_VALIDATION_PLAN.md for details

### Document Processing Pipeline
```
Word Document → PDF Conversion → Field Detection (Syncfusion/Claude/Sequential) → Interactive Editor → Accessible PDF Output
```

## 📂 Key System Components

### 1. Field Detection Services (The Core!)
- **`Services/ConfigurableFieldDetectionService.cs`** - Main orchestration of all detection modes
  - **SYNCFUSION_ONLY** - Uses Syncfusion PDF library to detect existing form fields
  - **CLAUDE_ONLY** - Uses Claude Vision API to detect fields from PDF images
  - **SYNCFUSION_PLUS_CLAUDE (Sequential Mode)** - Syncfusion for coordinates, Claude for intelligent labeling
- **`Services/ClaudeVisionFieldDetector.cs`** - Claude Vision API integration
- **`Services/SyncfusionFieldDetector.cs`** - Syncfusion-based field detection

### 2. Interactive Field Correction Tools
- **`Services/InteractiveCascadeCorrector.cs`** - CLI-style cascade correction system
- **`Components/CascadeCorrectionPanel.razor`** - UI for cascade correction
- **`Controllers/CascadeCorrectionController.cs`** - API endpoints for cascade correction

### 3. PDF Processing & Accessibility
- **`Services/AsposePdfService.cs`** - Aspose PDF operations and optimization
- **`Services/TwcFontComplianceService.cs`** - TWC-specific font embedding compliance
- **`Services/TableLinkAccessibilityService.cs`** - Table/link accessibility cleanup

### 4. Interactive Field Editor
- **`Pages/TagModificationModal.razor`** - Main field editing interface
- **`Pages/Index.razor`** - Primary application interface
- Visual PDF preview with field overlays
- Real-time field editing (position, size, type, properties)
- Multi-page navigation and field management

### 5. Coordinate Management
- **`Services/PdfCoordinateConverter.cs`** - Coordinate system transformations
- **PDF coordinates:** 72 DPI, bottom-left origin (Y=0 at bottom)
- **Display coordinates:** 150 DPI, top-left origin (Y=0 at top)
- Scale factor: 2.083... (150/72)

### 6. API Endpoints (`Program.cs`)
- **`/api/detect/pdf`** - Main field detection endpoint (POST)
- **`/api/pdf-page-with-field-boxes`** - PDF with field overlays + fieldMap
- **`/api/cascade-correction/*`** - Cascade correction endpoints
- **`/api/extract-fields-from-tags`** - Extract fields from PDF tag structure

## 🔧 Field Detection Modes Explained

### Mode 1: SYNCFUSION_ONLY
- Detects existing form fields embedded in the PDF
- Very accurate coordinates (comes from actual PDF field objects)
- Limited to detecting fields that already exist
- Good for: PDFs that already have some form fields

### Mode 2: CLAUDE_ONLY
- Uses Claude Vision API to analyze PDF images
- Detects visual form fields (boxes, lines, labels)
- Can detect fields that don't exist in PDF structure yet
- Good for: Scanned documents, Word conversions without fields

### Mode 3: SYNCFUSION_PLUS_CLAUDE (Sequential Mode)
- **Best of both worlds**
- Syncfusion provides accurate coordinates from existing fields
- Claude provides intelligent field labeling and typing
- Claude coordinates are IGNORED (set to dummy 0,0)
- Good for: PDFs with some fields that need better names

## 🐛 Current Known Issues

### Critical: Ghost Fields Appearing at Top of Page
**Status:** Fix attempted but not yet verified
**Document:** vr1307-twc.docx (Background Checks Attestation form)

**Problem:**
- When processing in Syncfusion + Claude Vision mode
- Ghost address fields (Home Street Address, Physical Street Address, etc.) appear at TOP of page
- Should appear in middle/bottom where they actually exist
- User confirmed: "Syncfusion alone places them correctly"

**Root Cause Found:**
- Ghost fields are NOT from Sequential mode processing
- They come from TAG EXTRACTION in Program.cs (line ~3330)
- These are legacy fields embedded in PDF tag structure from previous processing runs
- They don't have `[PAGE:X]` tooltips (which current system adds)
- Without tooltips, they were falling back to page 1 with wrong coordinates

**Fix Applied:**
- Modified Program.cs lines 3344-3345, 3377-3378, 3407-3409, 3437-3439
- Changed from falling back to page 1 → skip fields without `[PAGE:X]` tooltips entirely
- Uses `continue;` to skip legacy fields
- Only processes fields with proper tooltips from current system

**Testing Status:** Not yet verified by user - needs fresh document processing

### Other Issues
1. **Click-to-select fields** - Clicking field boxes in preview doesn't select them in table
2. **Cascade correction display order** - FIXED (now sorts correctly by Y coordinate descending)

## 🔄 Data Flow

### 1. Document Processing
```
User uploads Word → Conversion to PDF → Field Detection (configurable mode) → Display in editor
```

### 2. Field Detection Flow (Sequential Mode)
```
1. Syncfusion detects existing fields (accurate coordinates)
2. Convert PDF pages to images (150 DPI)
3. Send to Claude Vision API with field detection prompt
4. Claude returns field names/types with DUMMY coordinates (0,0)
5. Match Claude labels to Syncfusion fields by name/proximity
6. Create enhanced fields: Syncfusion coords + Claude labels
7. Add [PAGE:X] tooltips to all fields
```

### 3. Tag Extraction Flow
```
1. When reading processed PDF, check tag structure
2. Extract fields from tags
3. Check for [PAGE:X] tooltip
4. If tooltip exists: use that page number
5. If NO tooltip: SKIP field (it's from old processing)
```

## 🚀 Development Workflow

### Starting the System
```bash
cd "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"
pkill -f "dotnet run"
dotnet build AccessFormServer.csproj --nologo
ASPNETCORE_URLS="http://localhost:5001" dotnet run --project AccessFormServer.csproj --nologo --verbosity quiet
```

### Key Development Files
- **Main Application:** `Program.cs` (3500+ lines - API endpoints, tag extraction)
- **Field Detection:** `Services/ConfigurableFieldDetectionService.cs` (2000+ lines)
- **UI Components:** `Pages/` directory (Blazor Server components)
- **Services:** `Services/` directory (20+ service files)

### Git Workflow
- **Main branch:** main
- **Current feature branch:** feature/cherry-pick-improvements
- **Commit messages:** Include Claude Code attribution

## 🔍 Debugging & Troubleshooting

### Important Log Markers
- `🚨 [TAG-STRUCTURE-TEXT]` - Processing text field from tags
- `🚫 [TAG-STRUCTURE-SKIP]` - Skipping field without tooltip
- `✅✅✅ [MATCH_NAME]` - Sequential mode name matching
- `🚫🚫🚫 [SEQUENTIAL_MODE]` - Sequential mode coordinate handling

### Common Issues
- **Ghost fields:** Check for fields without `[PAGE:X]` tooltips in tag extraction
- **Wrong coordinates:** Check which detection mode is being used
- **Field positioning:** Usually coordinate conversion issues between PDF/display
- **AI detection failures:** Verify Claude API configuration and image quality

### Server Logs Location
```
./Logs/accessform.log
```

## 📚 External Dependencies

### APIs & Services
- **Claude Vision API** - AI field detection (Anthropic)
- **Aspose.PDF** - Core PDF manipulation and tagging
- **Syncfusion PDF** - Form field detection and manipulation

### Key NuGet Packages
- `Aspose.Pdf`
- `Syncfusion.Pdf.Net.Core`
- `SkiaSharp`
- `Microsoft.AspNetCore.Components.Web`

## 🎯 Recent Major Features

### Cascade Correction System (Oct 2025)
- Interactive CLI-style interface for fixing field alignment issues
- Commands: `phantom`, `cascade`, `rename`, `swap`, `auto`, `preview`, `undo`, `reset`
- Detects and fixes common field misalignment patterns
- Real-time preview of corrections before applying

### Sequential Mode Enhancement (Oct 2025)
- Improved coordinate handling (Syncfusion only, never Claude)
- Better field matching algorithms (name-based, then proximity)
- Explicit logging for debugging field merging

### Tag Structure Safety (Oct 2025)
- Skip legacy fields without proper tooltips
- Prevent ghost fields from appearing in processed documents
- Better page number detection from tooltips

## 📞 For New Claude Sessions

When starting a new session, read:
1. **This document** - System overview and current state
2. **CURRENT_ISSUES.md** - Active bugs and TODOs
3. **Check git status** - See what branch you're on
4. **Check server logs** - Review recent processing attempts

### Quick Start Commands
```bash
# Check git status
git status

# View recent commits
git log --oneline -5

# Start server
cd "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter"
ASPNETCORE_URLS="http://localhost:5001" dotnet run --project AccessFormServer.csproj --nologo --verbosity quiet

# View logs
tail -100 Logs/accessform.log
```

---

**System Status:** Mature and functional. Primary focus is bug fixes and UX improvements rather than new features.
