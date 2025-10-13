# AccessForm PDF Processing Architecture

**Last Updated:** 2025-10-12

## Documentation Index
- **ARCHITECTURE.md** (this file) - Main processing paths, service overview, known issues
- **ARCHITECTURE-SERVICES.md** - Detailed service class documentation
- **ARCHITECTURE-API.md** - API endpoint documentation
- **ARCHITECTURE-GUI.md** - GUI components and user workflows

---

## System Overview

AccessForm is a .NET 8.0 Blazor Server application that converts Word documents and existing PDFs into PDF/UA compliant, accessible documents. It uses multiple AI services (Claude, GPT-5, Google Document AI), PDF processing libraries (Syncfusion, Aspose, PassportPDF, iText), and advanced algorithms to detect form fields, add accessibility tags, embed fonts, and ensure compliance with WCAG 2.1 AA and Section 508.

### Core Technologies
- **Frontend**: Blazor Server (Server-Side Rendering)
- **Backend**: .NET 8.0 / C#
- **PDF Libraries**: Syncfusion (primary), Aspose, PassportPDF, iText7
- **AI Services**: Anthropic Claude (Vision + API), OpenAI GPT-5, Google Document AI, Groq Llama
- **Word Processing**: Syncfusion DocIO

---

## Two Processing Paths

### Path 1: Word Document → PDF (PRIMARY PIPELINE)
**Endpoint**: `/api/process-with-passportpdf-auto` when user uploads .docx
**Entry Point**: Program.cs:4143-4292
**Status**: ✅ WORKING - produces PDF/A-2u compliant PDFs with AI-detected fields

**Complete Flow**:

```
1. USER UPLOAD (.docx file)
   ├─ Program.cs:4158-4170 - File validation and byte array extraction
   └─ Supported: .docx files only for this path

2. FIELD DETECTION (AI-POWERED)
   ├─ Service: ConfigurableFieldDetectionService.ConvertWithConfig()
   ├─ Location: Services/ConfigurableFieldDetectionService.cs:85-100
   ├─ Substeps:
   │  ├─ Word → PDF conversion (Syncfusion DocIORenderer)
   │  ├─ PDF → PNG rendering for AI analysis
   │  ├─ Syncfusion native field detection (ContentControl, FormField, etc.)
   │  ├─ Claude Vision API field detection (visual analysis)
   │  ├─ Google Document AI field detection (optional)
   │  ├─ Multi-Stage Validation (Claude + GPT-5 consensus, optional)
   │  └─ Field merging and deduplication
   └─ Output: (byte[] pdfBytes, List<FieldDetectionResult> fields)

3. PRE-PROCESSING (Optional, if Aspose enabled)
   ├─ Service: AsposePdfService.OptimizePdfAsync() via PdfCompleteRebuildService
   ├─ Location: Program.cs:4239-4268
   ├─ Purpose: Font embedding BEFORE PassportPDF (if enabled)
   ├─ Key Operations:
   │  ├─ Load PDF with Aspose.Pdf.Document
   │  ├─ Call EmbedFonts() - embeds all fonts including Liberation fonts
   │  ├─ OptimizeResources() with RemoveUnusedObjects=false (preserve form fields)
   │  └─ Save optimized PDF
   └─ Issue: Currently limited by missing Liberation font files

4. PDF/A CONVERSION (CRITICAL STEP)
   ├─ Service: PassportPdfService.ConvertToPdfAPreservingFieldsAsync()
   ├─ Location: Services/PassportPdfService.cs:183-278
   ├─ Purpose: Convert to PDF/A-2u for full compliance
   ├─ Key Operations:
   │  ├─ Extract field metadata (names, types, positions, tooltips)
   │  ├─ Upload PDF to PassportPDF cloud API
   │  ├─ PdfReduceParameters: Clean fonts, remove hyperlinks
   │  │  └─ RemoveJavaScript = true (⚠️ DESTROYS CALCULATIONS)
   │  ├─ PdfRepairDocument: Fix font encoding issues
   │  ├─ ConvertToPDFA: Convert to PDF/A-2u (Unicode support)
   │  │  ├─ Embeds ALL fonts (including base-14)
   │  │  ├─ Replaces ZapfDingbats with Unicode symbols
   │  │  └─ Validates PDF/A-2u conformance
   │  ├─ Re-inject field metadata back into converted PDF
   │  └─ Download and return converted PDF
   └─ Output: PDF/A-2u compliant PDF with preserved field names

5. RETURN TO CLIENT
   └─ Program.cs:4293-4348 - Return accessible PDF bytes
```

**Key Services Used**:
- `ConfigurableFieldDetectionService` - AI field detection orchestration
- `PassportPdfService` - PDF/A-2u conversion and font embedding
- `AsposePdfService` - Optional pre-processing font optimization
- `ClaudeVisionFieldDetector` - Visual field detection
- `GoogleDocumentAiService` - Structured document analysis (optional)
- `MultiStageValidationService` - Claude + GPT-5 consensus validation (optional)

---

### Path 2: Existing PDF → Accessible PDF (FIELD PRESERVATION PATH)
**Endpoint**: `/api/process-with-passportpdf-auto` when user uploads .pdf
**Entry Point**: Program.cs:4198-4232
**Status**: ⚠️ PARTIAL - Preserves fields but may have compliance issues

**Complete Flow**:

```
1. USER UPLOAD (.pdf file)
   ├─ Program.cs:4158-4170 - File validation and byte array extraction
   └─ Supported: .pdf files with or without existing fields

2. FIELD EXTRACTION
   ├─ Service: PdfPreservationService.GetExistingFields()
   ├─ Location: Services/PdfPreservationService.cs:211-324
   ├─ Purpose: Extract existing form fields from PDF (preserve calculations!)
   ├─ Key Operations:
   │  ├─ Load PDF with Syncfusion PdfLoadedDocument
   │  ├─ Iterate through loadedDoc.Form.Fields
   │  ├─ Extract field metadata:
   │  │  ├─ Name, Type (TextBox, CheckBox, RadioButton, ComboBox, etc.)
   │  │  ├─ Bounds (X, Y, Width, Height)
   │  │  ├─ Tooltip, ReadOnly, Required flags
   │  │  ├─ JavaScript actions (if any)
   │  │  └─ Radio button grouping (ButtonValue property)
   │  └─ Convert to List<FieldDetectionResult>
   └─ Output: List<FieldDetectionResult> fields

3. ACCESSIBILITY ENHANCEMENT
   ├─ Service: PdfPreservationService.ProcessExistingPdfAsync()
   ├─ Location: Services/PdfPreservationService.cs:48-177
   ├─ Purpose: Add accessibility without destroying fields
   ├─ Key Operations:
   │
   │  Step 3a: Fix Artifact Violations
   │  ├─ Service: ArtifactViolationFixService.FixArtifactViolationsAsync()
   │  ├─ Purpose: Fix tagged content inside Artifact tags (PDF/UA violation)
   │  ├─ Uses: iText7 PdfDocument manipulation
   │  └─ Returns: Fixed PDF if violations found
   │
   │  Step 3b: Fix Tagged Whitespace Violations
   │  ├─ Service: TaggedWhitespaceFixService.FixTaggedWhitespaceAsync()
   │  ├─ Purpose: Remove tagging from whitespace-only elements (PDF/UA violation)
   │  ├─ Detects: Elements containing only spaces/newlines/tabs
   │  └─ Returns: Fixed PDF with unmarked whitespace
   │
   │  Step 3c: Apply Accessibility Metadata
   │  ├─ Service: AccessibilityService.MakeAccessible()
   │  ├─ Location: Services/AccessibilityService.cs:257-268
   │  ├─ Sets document metadata:
   │  │  ├─ Title, Language (en-US), Subject
   │  │  ├─ Keywords: "accessible, PDF/UA, WCAG 2.1 AA"
   │  │  └─ Custom metadata: Accessibility_Standard, WCAG_Level
   │  ├─ Enhances form fields:
   │  │  ├─ Sets tooltips (required for PDF/UA)
   │  │  ├─ Sets tab order
   │  │  └─ Normalizes checkbox sizes (13.8 x 13.8 points)
   │  └─ Returns: Enhanced PDF bytes
   │
   │  Step 3d: Apply Algorithmic Retrofitting
   │  ├─ Service: AccessibilityRetrofitService.RetrofitAccessibility()
   │  ├─ Location: Services/AccessibilityRetrofitService.cs:18-25
   │  ├─ Analyzes field patterns (date, phone, email, SSN, etc.)
   │  ├─ Applies pattern-based labels
   │  └─ Generates bookmarks from content
   │
   │  Step 3e: Final Enhancements
   │  ├─ Service: PdfAccessibilityEnhancer.EnhanceAccessibility()
   │  ├─ Location: Services/PdfAccessibilityEnhancer.cs:56-80
   │  ├─ Sets PDF/UA-1 metadata
   │  └─ Creates structure element hierarchy (if missing)
   │
   └─ Output: Accessible PDF with preserved fields

4. RETURN TO CLIENT
   └─ Program.cs:4232 - Return accessible PDF bytes
```

**What This Path DOES**:
- ✅ Preserves existing form fields (including JavaScript calculations)
- ✅ Fixes artifact violations (tagged content in artifacts)
- ✅ Fixes tagged whitespace violations
- ✅ Adds PDF/UA metadata
- ✅ Normalizes checkbox sizes
- ✅ Sets field tooltips and tab order
- ✅ Applies algorithmic field pattern detection

**What This Path DOES NOT DO**:
- ❌ NO PassportPDF PDF/A-2u conversion
- ❌ NO comprehensive font embedding (only basic Syncfusion embedding)
- ❌ NO ZapfDingbats → Unicode conversion (unless manually added)
- ⚠️ May not achieve full PDF/UA compliance without font embedding

**Why This Path Exists**:
The Word→PDF path uses PassportPDF which **removes JavaScript** to create PDF/A-2u files.
For PDFs with calculated fields (SUM, formulas, etc.), we need to preserve JavaScript,
so this path skips PassportPDF and uses Syncfusion-only processing.

---

## Known High-Priority Issues

### Issue 1: PassportPDF JavaScript Removal Conflict
**Status**: 🔴 CRITICAL - Affects calculated fields

**Problem**:
PassportPDF's `ConvertToPdfAAsync()` always removes JavaScript for security:
```csharp
// Services/PassportPdfService.cs:82
reduceParams.RemoveJavaScript = true;
```

**Impact**:
- ❌ Destroys calculated fields (SUM, formulas, dynamic calculations)
- ❌ Removes form validation scripts
- ❌ Any PDF with JavaScript calculations will lose functionality

**Current Workaround**:
Path 2 (Existing PDF → Accessible PDF) skips PassportPDF entirely to preserve JavaScript,
but this means it also skips:
- Full font embedding (PDF/A-2u conversion)
- ZapfDingbats → Unicode conversion
- PDF/A-2u conformance validation

**Potential Solutions**:
1. **Add `preserveJavaScript` parameter to PassportPDF service**
   - Modify `ConvertToPdfAAsync()` to accept boolean parameter
   - Set `removeParams.RemoveJavaScript = !preserveJavaScript`
   - ⚠️ May have security implications

2. **Use separate PassportPDF account/config for calculated PDFs**
   - Configure PassportPDF API to allow JavaScript preservation
   - Requires API key management and conditional logic

3. **Manual font embedding without PassportPDF**
   - Use Aspose + iText for font embedding + ZapfDingbats removal
   - More complex, may not achieve full PDF/A-2u compliance

---

### Issue 2: Aspose Font Embedding Disabled (Missing Liberation Fonts)
**Status**: 🟡 MEDIUM - Limits font compliance options

**Problem**:
TwcFontComplianceService requires Liberation font files (LiberationSans, LiberationSerif)
which are not present in the deployment.

**Location**: Services/TwcFontComplianceService.cs

**Impact**:
- ❌ Cannot replace Times-Roman/Arial with Liberation fonts
- ❌ Aspose font optimization is disabled
- ⚠️ Limits alternative font embedding strategies

**Code Evidence**:
```csharp
// Comment in AsposePdfService indicates this is disabled:
// "TwcFontComplianceService requires Liberation font files which don't exist"
```

**Solution**:
1. Download Liberation fonts from: https://github.com/liberationfonts/liberation-fonts
2. Install fonts in system fonts directory OR
3. Bundle fonts with application in `/Fonts` directory
4. Update TwcFontComplianceService to use bundled font paths

**Required Font Files**:
- LiberationSans-Regular.ttf
- LiberationSans-Bold.ttf
- LiberationSerif-Regular.ttf
- LiberationSerif-Bold.ttf

---

### Issue 3: Radio Button Grouping with ButtonValue Property
**Status**: 🟢 LOW - Recently fixed, monitoring for regressions

**Problem**:
Radio button groups were not properly preserving their grouping when extracted from existing PDFs.

**Solution Implemented** (commit a43fe8b):
- Added `ButtonValue` property extraction in PdfPreservationService.GetExistingFields()
- Radio buttons with same `Name` but different `ButtonValue` are now grouped correctly
- Location: Services/PdfPreservationService.cs:211-324

**Code Snippet**:
```csharp
if (radioButton != null)
{
    // Extract button value for radio button groups
    result.ButtonValue = radioButton.Value;
    _logger.LogInformation($"Radio button: {field.Name}, ButtonValue={result.ButtonValue}");
}
```

**Monitoring**:
- ✅ Basic grouping works
- ⚠️ Need to verify Y-axis drag direction in field editor UI
- ⚠️ Need to verify radio button appearance rendering

---

### Issue 4: PDF/UA Artifact Violations
**Status**: 🟢 LOW - Fixed but may occur in new PDFs

**Problem**:
Some PDFs contain tagged content inside `<Artifact>` tags, which is a PDF/UA violation.
PAC (PDF Accessibility Checker) reports: "Real content is tagged as an artifact"

**Solution Implemented** (commit ac7b2cd):
- Created `ArtifactViolationFixService` using iText7
- Automatically detects and fixes artifact violations in Path 2
- Location: Services/ArtifactViolationFixService.cs

**How It Works**:
1. Scans PDF structure tree for `<Artifact>` tags
2. Checks if artifact contains actual content (text/images)
3. Unmarks content as artifact or promotes to proper structural tag
4. Re-saves PDF with corrections

**Integration**:
- Runs as Step 3a in Path 2 (PdfPreservationService.ProcessExistingPdfAsync)
- Automatically applied to all uploaded PDFs

---

### Issue 5: Tagged Whitespace Violations
**Status**: 🟢 LOW - Fixed with octal encoding support

**Problem**:
PDF/UA compliance requires that whitespace-only elements (spaces, newlines, tabs)
should NOT be tagged as content. PAC reports: "Empty element" violations.

**Solution Implemented** (commit ac7b2cd):
- Created `TaggedWhitespaceFixService` with octal encoding detection
- Detects whitespace in various encodings (ASCII, octal like \040, \n, etc.)
- Location: Services/TaggedWhitespaceFixService.cs

**How It Works**:
```csharp
// Detects patterns like:
// - " " (spaces)
// - "\n" (newlines)
// - "\t" (tabs)
// - "\040" (octal space)
// - "\012" (octal newline)
```

**Integration**:
- Runs as Step 3b in Path 2 (PdfPreservationService.ProcessExistingPdfAsync)
- Uses iText7 for precise structure tree manipulation

---

## Service Architecture Overview

AccessForm uses 40+ service classes organized into functional groups:

### Core Pipeline Services
These orchestrate the main processing flows:
- **ConfigurableFieldDetectionService** - Orchestrates AI field detection (Syncfusion + Claude + Google)
- **PdfPreservationService** - Preserves existing PDF fields while adding accessibility
- **PassportPdfService** - PDF/A-2u conversion, font embedding (cloud API)
- **PdfCompleteRebuildService** - Complete PDF rebuild with field injection

### AI Detection Services
- **ClaudeVisionFieldDetector** - Visual field detection via Claude Vision API
- **GoogleDocumentAiService** - Structured document analysis via Google Document AI
- **MultiStageValidationService** - Claude + GPT-5 consensus validation
- **ClaudeBoundingBoxValidator** - Validates and corrects field bounding boxes
- **AnthropicService** - Low-level Claude API client
- **OpenAIService** - Low-level OpenAI/GPT API client
- **LlamaGroqService** - Groq Llama API for label validation (deprecated)

### Accessibility Services
- **AccessibilityService** - Basic accessibility (metadata, tooltips, tab order)
- **AccessibilityRetrofitService** - Algorithmic field pattern detection
- **PdfAccessibilityEnhancer** - PDF/UA compliance enhancements
- **ArtifactViolationFixService** - Fixes tagged content in artifacts (iText7)
- **TaggedWhitespaceFixService** - Removes tagging from whitespace (iText7)
- **PdfUAComplianceService** - PDF/UA validation and reporting

### Font & Optimization Services
- **AsposePdfService** - Font embedding and optimization (Aspose.Pdf)
- **TwcFontComplianceService** - Liberation font replacement (DISABLED)
- **FontSubstitutionService** - Font substitution utilities

### Field Management Services
- **FormFieldCreationService** - Creates form fields in PDFs
- **FieldAnalysisService** - Analyzes field layouts and relationships
- **PdfFieldTagEditorService** - Edits fields and tags (Syncfusion)
- **ITextFieldRebuildService** - Rebuilds fields with proper structure (iText7)
- **FieldPreservationService** - Preserves field state during processing

### Utility Services
- **DebugCacheService** - Caches debug data for AI responses
- **CostTrackingService** - Tracks AI API costs
- **SimpleFileLoggerProvider** - File-based logging
- **CoordinateDiagnosticService** - Debugging coordinate systems

**See ARCHITECTURE-SERVICES.md for detailed service documentation.**

---

## Library Usage & Responsibilities

### Syncfusion (Primary Library)
**Purpose**: Document manipulation, form field creation, PDF generation
**Used For**:
- Word → PDF conversion (DocIORenderer)
- PDF loading and parsing (PdfLoadedDocument)
- Form field creation and manipulation
- Basic accessibility tagging
- Coordinate detection and layout analysis

**Strengths**:
- ✅ Stable, well-documented API
- ✅ Good form field support
- ✅ Works well with Word documents

**Limitations**:
- ⚠️ Limited font embedding (doesn't handle base-14 fonts)
- ⚠️ No ZapfDingbats replacement
- ⚠️ No PDF/A-2u conversion

---

### PassportPDF (Cloud API)
**Purpose**: PDF/A-2u conversion, comprehensive font embedding
**Used For**:
- Converting PDFs to PDF/A-2u format
- Embedding ALL fonts (including base-14)
- Replacing ZapfDingbats with Unicode symbols
- Repairing font encoding issues
- PDF/A validation

**Strengths**:
- ✅ Full PDF/A-2u compliance
- ✅ Embeds all fonts correctly
- ✅ Handles ZapfDingbats replacement
- ✅ Cloud-based (no local dependencies)

**Limitations**:
- ❌ Removes JavaScript (breaks calculated fields)
- ⚠️ Cloud API (requires internet, costs per page)
- ⚠️ May rename form fields (workaround implemented)

---

### Aspose.Pdf
**Purpose**: Advanced font embedding, optimization
**Used For**:
- Font embedding with custom fonts (Liberation)
- PDF optimization
- Advanced font manipulation

**Strengths**:
- ✅ Powerful font embedding capabilities
- ✅ Can use custom font files
- ✅ Local processing (no cloud costs)

**Limitations**:
- ❌ Currently disabled (missing Liberation fonts)
- ⚠️ Can destroy form fields if not careful (OptimizeResources with wrong settings)
- ⚠️ Requires license (using trial/free version)

---

### iText7
**Purpose**: Low-level PDF structure manipulation
**Used For**:
- Fixing artifact violations
- Removing whitespace tagging
- ZapfDingbats removal from checkboxes
- Field rebuilding with proper structure
- Direct PDF structure tree manipulation

**Strengths**:
- ✅ Precise control over PDF structure
- ✅ Excellent for PDF/UA compliance fixes
- ✅ Strong community support

**Limitations**:
- ⚠️ Complex API (steep learning curve)
- ⚠️ Requires AGPL license or commercial license
- ⚠️ Can break PDFs if not used carefully

---

## Decision-Making Guide

### When to Use Path 1 (Word→PDF)
✅ Source document is Word (.docx)
✅ No calculated fields needed
✅ Full PDF/A-2u compliance required
✅ Need comprehensive font embedding
✅ AI field detection desired

### When to Use Path 2 (PDF→PDF)
✅ Source document is PDF
✅ Has existing form fields with calculations
✅ JavaScript preservation required
⚠️ Accept partial compliance (no full PDF/A-2u)

### Choosing AI Detection Services

**Use Syncfusion Only** (Fast):
- Simple forms with clear fields
- Development/testing
- Low-cost processing

**Use Syncfusion + Claude Vision** (Accurate):
- Complex forms with unclear field boundaries
- Need label detection
- Production quality

**Use All Services** (Comprehensive):
- Critical forms requiring highest accuracy
- Need multi-model consensus
- Cost is not a concern

---

## Configuration & Environment

### Required API Keys
- `ANTHROPIC_API_KEY` - Claude API (Vision + Text)
- `OPENAI_API_KEY` - GPT API (Multi-stage validation)
- `GOOGLE_APPLICATION_CREDENTIALS` - Google Document AI
- PassportPDF credentials (configured in service)

### Optional Components
- Liberation fonts (for Aspose font replacement)
- Adobe Document Cloud credentials (Auto-tag service, currently disabled)

### Logging
- File-based logs: `/Logs/accessform.log`
- Console logging enabled
- Debug cache for AI responses: DebugCacheService

---

**For detailed documentation, see:**
- **ARCHITECTURE-SERVICES.md** - Service class details
- **ARCHITECTURE-API.md** - API endpoint documentation
- **ARCHITECTURE-GUI.md** - GUI components and workflows
