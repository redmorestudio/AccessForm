# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Server Port Configuration

**CRITICAL**: Always use port 5008 for the PDF remediation server. Do not use other ports.

## Common Commands

### Build and Run
```bash
# Build the project
dotnet build AccessFormServer.csproj --nologo

# Run the server (always use port 5008)
ASPNETCORE_URLS="http://localhost:5008" dotnet run --project AccessFormServer.csproj --nologo --verbosity quiet

# Kill any existing server instances first
pkill -f "dotnet run"
```

### Development Workflow
```bash
# Check git status
git status

# View recent commits
git log --oneline -5

# View server logs
tail -100 Logs/accessform.log
```

## System Architecture

AccessForm is a Blazor Server application (.NET 8.0) that converts Word documents and PDFs into PDF/UA compliant, accessible documents with AI-powered field detection.

### Core Technology Stack
- **Backend**: ASP.NET Core 8.0 Blazor Server (NOT WebAssembly)
- **PDF Libraries**: Syncfusion (primary), Aspose, PassportPDF, iText7
- **AI Services**:
  - Claude Sonnet 4.5 (primary field detection)
  - OpenAI GPT-5 (planned multi-stage validation)
  - Google Document AI (optional)
- **Word Processing**: Syncfusion DocIO
- **Validation**: VeraPDF for PDF/UA compliance

### Main Processing Paths

#### 1. Word Document → PDF (Primary Pipeline)
```
Word Upload → Field Detection (AI) → PDF/A Conversion → Remediation → Accessible PDF
```

**Entry Point**: `Program.cs:4143-4292` (`/api/process-with-passportpdf-auto`)

**Key Services**:
- `ConfigurableFieldDetectionService` - Orchestrates AI field detection
- `PassportPdfService` - PDF/A-2u conversion and font embedding
- `ClaudeVisionFieldDetector` - Visual field detection via Claude Vision API
- `GoogleDocumentAiService` - Optional structured document analysis
- `MultiStageValidationService` - Claude + GPT-5 consensus validation (planned)

#### 2. PDF → PDF (Remediation Only)
```
PDF Upload → VeraPDF Validation → Iterative Remediation → PDF/UA Compliant PDF
```

**Entry Point**: `Program.cs` remediation endpoints

**Key Service**: `RemediationOrchestrator` - Closed-loop remediation system

#### 3. PDF Field Editing (Interactive)
```
PDF Upload → Field Detection → Interactive Editor → Modified PDF
```

**Pages**: `Index.razor`, `TagModificationModal.razor`, `FieldEditorModal.razor`

### Remediation System Architecture

The remediation system uses a **closed-loop architecture** with iterative validation:

```
Initial Validation → Violation Analysis → Strategy Selection →
Remediation Execution → Post-Cleanup → GPT-5 Fallback (if needed) →
Re-Validation → Loop until compliant or max iterations
```

**Core Orchestrator**: `Services/Remediation/RemediationOrchestrator.cs`

**Remediation Phases**:
1. **Structure Phase**: Fix PDF structure issues (tags, hierarchy)
2. **Metadata Phase**: Fix PDF/UA metadata and document properties
3. **Forms Phase**: Fix form field accessibility issues
4. **Content Phase**: Fix content issues (alt text, headings, links)
5. **Whitespace Phase**: Fix whitespace and artifact tagging
6. **Font Phase**: Embed fonts and fix font encoding
7. **Cleanup Phase**: Fix structural issues introduced by earlier fixes

**Key Services**:
- `ViolationAnalyzer` - Categorizes violations by type
- `RemediationStrategySelector` - Builds phased execution plan
- `RemediationExecutor` - Runs remediation services in phases
- `ExitConditionEvaluator` - Determines when to stop (compliant or max iterations)
- `ProgressTracker` - Tracks progress across iterations
- `RemediationReporter` - Generates detailed reports
- `GptRemediationService` - AI-powered fallback for complex violations

### Field Detection Modes

Configured via `ConfigurableFieldDetectionService`:

1. **SYNCFUSION_ONLY**: Detects existing form fields in PDF using Syncfusion library
2. **CLAUDE_ONLY**: Uses Claude Vision API to detect visual form fields
3. **SYNCFUSION_PLUS_CLAUDE (Sequential)**: Combines Syncfusion coordinates with Claude labeling
   - Syncfusion provides accurate coordinates from existing fields
   - Claude provides intelligent field labeling/typing
   - Claude coordinates are ignored (set to 0,0)

### Service Architecture

**Core Pipeline Services** (`Services/`):
- `ConfigurableFieldDetectionService.cs` - Main field detection orchestration (2000+ lines)
- `ClaudeVisionFieldDetector.cs` - Claude Vision API integration
- `WordToPdfWithFieldsService.cs` - Word to PDF conversion with field preservation
- `UnifiedCoordinateService.cs` - Coordinate system transformations
- `MultiSourceFieldCombiner.cs` - Merges fields from multiple detection sources

**AI Services** (`Services/`):
- `AnthropicService.cs` - Claude API client
- `OpenAIService.cs` - GPT-5 API client (planned multi-stage validation)
- `GoogleDocumentAiService.cs` - Google Document AI integration
- `LlamaGroqService.cs` - Groq Llama API client
- `MultiStageValidationService.cs` - Consensus validation across AI providers

**Remediation Services** (`Services/Remediation/`):
- `RemediationOrchestrator.cs` - Main orchestrator
- `Fixes/` - Individual fix services (PdfUaMetadataService, FormRoleAttributeFixService, etc.)
- `Adapters/` - Service adapters for different fix types
- `AI/GptRemediationService.cs` - AI-powered remediation

**Accessibility Services** (`Services/`):
- `AccessibilityService.cs` - PDF accessibility enhancement
- `PdfAccessibilityEnhancer.cs` - Tag structure enhancement
- `TableStructureRemediationService.cs` - Table accessibility fixes
- `TocStructureRemediationService.cs` - Table of contents fixes
- `LinkAnnotationAccessibilityService.cs` - Link annotation fixes

**Validation Services**:
- `VeraPdfService.cs` - PDF/UA compliance validation via VeraPDF

**Font Services**:
- `PdfFontEmbeddingService.cs` - Font embedding
- `FontSubstitutionService.cs` - Font substitution
- `TwcFontComplianceService.cs` - TWC-specific font compliance

### Coordinate System

**PDF Coordinates**: 72 DPI, bottom-left origin (Y=0 at bottom)
**Display Coordinates**: 150 DPI, top-left origin (Y=0 at top)
**Scale Factor**: 2.083... (150/72)

**Service**: `PdfCoordinateConverter.cs` handles all coordinate transformations

### Dependency Injection

All services are registered in `Program.cs:39-96`:
- Most services are scoped (per-request lifetime)
- `InteractiveCascadeCorrector` is singleton (maintains sessions)
- `CostTrackingService` and `DebugCacheService` are singleton

### Key API Endpoints

**Field Detection**:
- `POST /api/detect/pdf` - Main field detection endpoint
- `POST /api/pdf-page-with-field-boxes` - PDF with field overlays + fieldMap
- `POST /api/extract-fields-from-tags` - Extract fields from PDF tag structure

**Processing**:
- `POST /api/process-with-passportpdf-auto` - Main Word→PDF processing pipeline
- `POST /api/convert` - Word to PDF conversion

**Cascade Correction**:
- `POST /api/cascade-correction/start` - Start correction session
- `POST /api/cascade-correction/execute` - Execute correction command
- `GET /api/cascade-correction/status` - Get session status

**Remediation**:
- Various remediation endpoints in `Program.cs`

### Known Issues and Important Patterns

#### Tag Extraction Ghost Fields
**Problem**: Legacy fields from previous processing runs appear at wrong positions
**Detection**: Fields without `[PAGE:X]` tooltips
**Fix**: Skip fields without proper tooltips in tag extraction (`Program.cs:3344-3345, 3377-3378, 3407-3409, 3437-3439`)

**Log Markers**:
- `🚨 [TAG-STRUCTURE-TEXT]` - Processing text field from tags
- `🚫 [TAG-STRUCTURE-SKIP]` - Skipping field without tooltip
- `✅✅✅ [MATCH_NAME]` - Sequential mode name matching
- `🚫🚫🚫 [SEQUENTIAL_MODE]` - Sequential mode coordinate handling

#### Sequential Mode Coordinate Handling
**CRITICAL**: In SYNCFUSION_PLUS_CLAUDE mode, ONLY use Syncfusion coordinates, NEVER Claude coordinates
- Claude coordinates are set to dummy (0,0)
- This is intentional to prevent ghost fields

#### Field Detection with Tooltips
All fields from current system have `[PAGE:X]` tooltips added for page tracking. This is used to:
- Identify which page a field belongs to
- Distinguish new fields from legacy fields
- Prevent ghost fields from appearing

### File Locations

**Main Application**: `Program.cs` (3500+ lines)
**Configuration**: `appsettings.json`, `appsettings.template.json`
**Logs**: `Logs/accessform.log`
**Documentation**: `Documentation/Prime/` directory contains architecture docs
**UI Components**: `Pages/` and `Components/`

### Testing

Always test changes with real documents from `TestAssets/` or `StateAssets/` directories.

### Phase 6 MCID Architecture (Marked Content Integration)

**Purpose**: Link PDF structure elements to actual page content via MCIDs (Marked Content Identifiers) for proper accessibility.

**Two-Sided MCID Story**:
1. **Structure Side (Phase 6K)** - MCR (Marked Content Reference) objects in structure tree pointing to (Page, MCID)
2. **Content Side (Phase 6H/6J)** - BDC/EMC markers in PDF content streams with matching MCIDs

#### Phase 6 Implementation Phases

**Phase 6F-6H**: External Python microservice (`McidRewriterMicroservice/main.py`) that inserts BDC/EMC markers
**Phase 6K**: .NET implementation that creates MCR kids in structure tree

#### Key Components

**Models**:
- `RemediationJobContext` - Scoped DI context shared across all services in a remediation job
- `StructureRebuildContext` - Tracks MCID pipeline state (StructureRebuildExecuted, McidContentRewriteExecuted)
- `McidReference` - Stores (PageIndex, Mcid) pairs on StructureNode
- `StructureNode.McidReferences` - List of MCID references per structure node

**Services**:
- `ITextPdfStructureWriter` - Creates MCR kids in structure tree (Phase 6K implementation)
- `McidRewritePlanBuilder` - Builds plans from McidReferences for external rewriter
- `ExternalMcidRewriterService` - Calls Python microservice to insert BDC/EMC markers
- `StructureRebuildService` - Orchestrates AI layout analysis + structure rebuild + MCID integration

#### MCID Pipeline Flow

```
1. AI Layout Analysis (StructureRebuildService)
2. Structure Tree Building (StructureTreeBuilder)
3. Structure Cleaning (StructureTreeCleaner)
4. MCID Allocation (ITextPdfStructureWriter.AllocateMcids) - assigns MCID numbers in reading order
5. MCR Creation (Phase 6K) - creates PdfMcrNumber kids in structure elements
6. Plan Building (McidRewritePlanBuilder) - generates coordinate-based rewrite plan
7. External Rewrite (ExternalMcidRewriterService) - Python inserts BDC/EMC markers
8. Write Protection (context.McidContentRewriteExecuted = true) - prevents marker overwrites
```

#### Critical Flags

**`RemediationJobContext.Options`**:
- `EnableMcidLinking` - Enable MCID allocation and MCR creation
- `EnableMcidContentRewrite` - Enable external Python rewriter for BDC/EMC markers

**`StructureRebuildContext` Flags**:
- `StructureRebuildExecuted` - Prevents duplicate structure rebuilds
- `McidContentRewriteExecuted` - Write-protects PDF from content stream mutations after BDC/EMC insertion

#### Guard Clauses

Services that touch content streams MUST check:
```csharp
if (context.McidContentRewriteExecuted)
    return pdfBytes; // DO NOT alter content streams
```

This applies to:
- `ArtifactTaggedContentFixService` (when ArtifactFixMode = "PostStructure")
- Any post-processing service that modifies content streams

#### Python Microservice

**Location**: `McidRewriterMicroservice/main.py`
**Port**: 8000 (default)
**API**: POST /rewrite-mcids
**Input**: PDF bytes + McidRewritePlan JSON
**Output**: PDF with BDC/EMC markers inserted

**Known Issue**: Microservice reports adding markers but they don't persist (Phase 6H/6J bug, NOT Phase 6K)

#### Testing Files

- `TestPhase6KFullPipeline.cs` - Full pipeline test with Erie Route 5 PDF
- `test_phase6k_erie.py` - Python test script for microservice
- `RunTestPhase6KFull.cs` - Entry point launcher

**Test PDF**: `StateAssets_archived_20251112_063442/Pennsylvania/Erie/ERA_0051_Route 5 August 2025.pdf`

#### Phase 6K Implementation Status

✅ **FULLY COMPLETE AND WORKING**:
- MCR kid creation in `ITextPdfStructureWriter.cs:463-511`
- Integration with `StructureRebuildService`
- Guard clauses to prevent overwrites
- Test infrastructure
- Python microservice BDC/EMC marker insertion **confirmed working**
- End-to-end pipeline **verified with Erie Route 5 PDF**

**Verification Results** (Erie test, Nov 19 2025):
- Python microservice reports: 3 BDC, 4 EMC markers inserted
- Output PDF (`erie_phase6k_output.pdf`): Confirmed BDC/EMC markers present via hexdump
- Markers persist through save/reload cycle
- No corruption or data loss

**Testing**: Use `TestPhase6KFullPipeline.cs` with Erie Route 5 PDF to verify full pipeline

### Additional Documentation

For detailed architecture information, refer to:
- `Documentation/Prime/ARCHITECTURE.md` - Main processing paths
- `Documentation/Prime/ARCHITECTURE-SERVICES-CORE.md` - Core pipeline services
- `Documentation/Prime/ARCHITECTURE-SERVICES-REMEDIATION.md` - Remediation system
- `Documentation/Prime/ARCHITECTURE-SERVICES-ACCESSIBILITY.md` - Accessibility services
- `Documentation/Prime/ARCHITECTURE-API.md` - API endpoints
- `Documentation/SYSTEM_OVERVIEW.md` - System overview and current state
- `PHASE-6K-REINTEGRATION-SPEC-v1.md` - Phase 6K integration specification
