# AccessForm PDF Processing Architecture

**Last Updated:** 2025-10-26

## Documentation Index
- **GETTING-STARTED.md** - Quick start guide and overview
- **ARCHITECTURE.md** (this file) - Main processing paths, system overview, known issues
- **ARCHITECTURE-SERVICES-CORE.md** - Core pipeline, AI detection, field management services
- **ARCHITECTURE-SERVICES-REMEDIATION.md** - Remediation orchestration, VeraPDF, fix services
- **ARCHITECTURE-SERVICES-ACCESSIBILITY.md** - Accessibility enhancement and utility services
- **ARCHITECTURE-API.md** - API endpoint documentation
- **ARCHITECTURE-GUI-PAGES.md** - Main pages and user workflows
- **ARCHITECTURE-GUI-MODALS.md** - Modals, panels, and interactive components

---

## System Overview

AccessForm is a .NET 8.0 Blazor Server application that converts Word documents and existing PDFs into PDF/UA compliant, accessible documents. It uses multiple AI services (Claude, GPT-5, Google Document AI), PDF processing libraries (Syncfusion, Aspose, PassportPDF, iText), and advanced algorithms to detect form fields, add accessibility tags, embed fonts, and ensure compliance with WCAG 2.1 AA and Section 508.

### Core Technologies
- **Frontend**: Blazor Server (Server-Side Rendering)
- **Backend**: .NET 8.0 / C#
- **PDF Libraries**: Syncfusion (primary), Aspose, PassportPDF, iText7
- **AI Services**: Anthropic Claude (Vision + API), OpenAI GPT-5, Google Document AI, Groq Llama
- **Word Processing**: Syncfusion DocIO
- **Validation**: VeraPDF (PDF/UA compliance validation)

---

## Three Processing Paths

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

### Path 3: PDF → Remediated PDF (CLOSED-LOOP REMEDIATION)
**Endpoint**: `/api/remediate-pdf` when user uploads non-compliant PDF
**Entry Point**: Program.cs:853
**Status**: ✅ WORKING - automated compliance remediation with VeraPDF validation

**Complete Flow**:

```
1. USER UPLOAD (non-compliant PDF)
   ├─ Program.cs:853 - File validation and byte array extraction
   └─ Supported: .pdf files requiring compliance fixes

2. INITIAL VALIDATION (VeraPDF)
   ├─ Service: VeraPdfService.ValidatePdfAsync()
   ├─ Location: Services/VeraPdfService.cs
   ├─ Purpose: Identify all PDF/UA violations
   ├─ Key Operations:
   │  ├─ Execute VeraPDF validation engine
   │  ├─ Parse XML violation report
   │  ├─ Categorize violations by type and severity
   │  └─ Map violations to available fix services
   └─ Output: ValidationReport with detailed violations

3. REMEDIATION LOOP (iterative until compliant)
   ├─ Service: RemediationOrchestrator.RemediateAsync()
   ├─ Location: Services/Remediation/RemediationOrchestrator.cs
   ├─ Purpose: Apply fixes in priority order until compliant
   ├─ Key Operations:
   │
   │  Step 3a: Violation Analysis
   │  ├─ Service: ViolationAnalyzer.AnalyzeViolations()
   │  ├─ Groups violations by category
   │  ├─ Prioritizes fixes (metadata → structure → content → AI fallback)
   │  └─ Creates remediation execution plan
   │
   │  Step 3b: Execute Fix Services
   │  ├─ Metadata Fixes:
   │  │  └─ PdfUaMetadataService - Add PDF/UA identifier, title, language
   │  ├─ Structure Fixes:
   │  │  ├─ TableStructureValidationService - Fix table structure violations
   │  │  ├─ TableScopeAttributeFixService - Add scope attributes to table headers
   │  │  ├─ FormRoleAttributeFixService - Add Role attributes to form fields
   │  │  └─ FormWidgetNestingFixService - Fix form widget nesting violations
   │  ├─ Content Fixes:
   │  │  ├─ FigureAltTextService - Add alt text to images/figures
   │  │  ├─ ArtifactTaggedContentFixService - Fix artifact violations
   │  │  └─ WhitespaceServiceAdapter - Remove whitespace tagging
   │  └─ AI Fallback:
   │     └─ GptServiceAdapter - Use GPT-4 for complex violations
   │
   │  Step 3c: Validate After Each Iteration
   │  ├─ Re-run VeraPDF validation
   │  ├─ Compare violation count with previous iteration
   │  ├─ Track progress metrics (violations fixed, remaining)
   │  └─ Exit conditions:
   │     ├─ Zero violations (SUCCESS)
   │     ├─ Max iterations reached (PARTIAL SUCCESS)
   │     └─ No progress made (FAILED)
   │
   └─ Output: RemediationResult with final PDF and metrics

4. FINAL VALIDATION
   ├─ Service: VeraPdfService.ValidatePdfAsync()
   ├─ Generate compliance report
   └─ Calculate success metrics (violations fixed, pass rate)

5. RETURN TO CLIENT with compliance report
   └─ Program.cs - Return remediated PDF + validation report
```

**Key Services Used**:
- `RemediationOrchestrator` - Coordinates the remediation loop
- `VeraPdfService` - PDF/UA validation (VeraPDF integration)
- `ViolationAnalyzer` - Categorizes and prioritizes violations
- `RemediationExecutor` - Executes fix services in order
- `PdfUaMetadataService` - Metadata compliance fixes
- `TableStructureValidationService` - Table structure fixes
- `FormRoleAttributeFixService` - Form field compliance
- `FigureAltTextService` - Alt text generation
- `GptRemediationService` - AI-powered fallback for complex violations

**Remediation Features**:
- ✅ Iterative validation and fixing (up to max iterations)
- ✅ Priority-based fix execution (metadata → structure → content → AI)
- ✅ Progress tracking with real-time updates
- ✅ VeraPDF integration for standards-compliant validation
- ✅ Detailed compliance reporting
- ✅ GPT-4 fallback for complex violations
- ✅ Service adapters for legacy fix services

**Exit Conditions**:
- **Success**: All violations fixed (VeraPDF reports compliant)
- **Partial**: Max iterations reached, some violations remain
- **Failed**: No progress made or critical errors encountered

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

### Issue 6: Remediation Loop Convergence
**Status**: 🟡 MEDIUM - Monitoring for edge cases

**Problem**:
Some PDFs may have interdependent violations where fixing one violation creates or reveals another,
potentially causing the remediation loop to oscillate or fail to converge.

**Mitigation Strategies**:
- Max iteration limit prevents infinite loops
- Progress tracking detects when no forward progress is made
- Priority-based execution order reduces interdependencies
- Detailed logging helps identify problematic violation patterns

**Monitoring**:
- Track iteration counts across PDF corpus
- Identify PDFs that consistently hit max iterations
- Analyze violation patterns that don't converge
- Refine fix service order and logic based on findings

---

## Service Architecture Overview

AccessForm uses 50+ service classes organized into functional groups:

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

### Remediation Services
- **RemediationOrchestrator** - Coordinates iterative remediation loop
- **VeraPdfService** - PDF/UA validation using VeraPDF engine
- **ViolationAnalyzer** - Categorizes and prioritizes violations
- **RemediationExecutor** - Executes fix services in priority order
- **RemediationReporter** - Generates compliance reports
- **PdfUaMetadataService** - Adds PDF/UA-1 identifier and metadata
- **TableStructureValidationService** - Validates and fixes table structures
- **TableScopeAttributeFixService** - Adds scope attributes to table headers
- **FormRoleAttributeFixService** - Adds Role attributes to form fields
- **FormWidgetNestingFixService** - Fixes form widget nesting violations
- **FigureAltTextService** - Generates alt text for images and figures
- **ArtifactTaggedContentFixService** - Fixes artifact tagging violations
- **GptRemediationService** - AI-powered fallback for complex violations

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

**See ARCHITECTURE-SERVICES-CORE.md, ARCHITECTURE-SERVICES-REMEDIATION.md, and ARCHITECTURE-SERVICES-ACCESSIBILITY.md for detailed service documentation.**

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

### VeraPDF
**Purpose**: PDF/UA and PDF/A validation
**Used For**:
- Standards-compliant validation (PDF/UA-1, PDF/A-2u, PDF/A-3u)
- Violation detection and reporting
- Compliance verification for remediation loop

**Strengths**:
- ✅ Industry-standard validation tool
- ✅ Comprehensive violation reporting
- ✅ Open-source and actively maintained
- ✅ Detailed XML output for programmatic parsing

**Limitations**:
- ⚠️ Requires local installation or Docker container
- ⚠️ Java-based (requires JRE)
- ⚠️ XML parsing overhead for large reports

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

### When to Use Path 3 (Remediation)
✅ Source document is non-compliant PDF
✅ Need automated compliance fixing
✅ VeraPDF validation required
✅ Iterative remediation approach needed
✅ Want detailed compliance reporting

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
- VeraPDF installation (for remediation validation)

### Logging
- File-based logs: `/Logs/accessform.log`
- Console logging enabled
- Debug cache for AI responses: DebugCacheService

---

**For detailed documentation, see:**
- **ARCHITECTURE-SERVICES-CORE.md** - Core pipeline and AI detection service details
- **ARCHITECTURE-SERVICES-REMEDIATION.md** - Remediation orchestration and fix services
- **ARCHITECTURE-SERVICES-ACCESSIBILITY.md** - Accessibility enhancement services
- **ARCHITECTURE-API.md** - API endpoint documentation
- **ARCHITECTURE-GUI-PAGES.md** - Main pages and user workflows
- **ARCHITECTURE-GUI-MODALS.md** - Modals, panels, and interactive components
