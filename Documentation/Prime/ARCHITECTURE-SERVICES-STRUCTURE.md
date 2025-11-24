# Structure Rebuild & MCID Services Architecture

**Last Updated**: 2025-11-19
**Related Documents**:
- ARCHITECTURE-SERVICES-REMEDIATION.md (Remediation pipeline)
- ARCHITECTURE.md (Main processing paths)
- CLAUDE.md (Phase 6K implementation notes)

---

## Table of Contents

1. [Overview](#1-overview)
2. [Two-Sided MCID Architecture](#2-two-sided-mcid-architecture)
3. [Core Services](#3-core-services)
4. [PDF Structure Writers](#4-pdf-structure-writers)
5. [MCID Services](#5-mcid-services)
6. [Models & Context](#6-models--context)
7. [Configuration](#7-configuration)
8. [Testing](#8-testing)
9. [Troubleshooting](#9-troubleshooting)

---

## 1. Overview

The structure rebuild system performs AI-driven PDF structure analysis and reconstruction with optional MCID (Marked Content Identifier) linking for PDF/UA compliance.

**Key Capabilities**:
- AI-powered layout analysis using Claude Vision
- Clean structure tree generation from logical blocks
- Form field metadata enrichment
- MCID allocation and MCR (Marked Content Reference) creation
- External content stream rewriting for BDC/EMC markers
- Write protection against marker corruption

**Primary Entry Point**: `StructureRebuildService` (Services/Remediation/StructureRebuildService.cs)

---

## 2. Two-Sided MCID Architecture

MCID linking requires coordination between structure tree and page content:

### Structure Side (Phase 6K)

**Location**: PDF structure tree (logical structure)
**Format**: MCR (Marked Content Reference) objects as structure element kids
**Created By**: `ITextPdfStructureWriter`
**Example**:
```xml
<P>
  <MCR Pg="0" MCID="5"/>
  <MCR Pg="0" MCID="6"/>
</P>
```

**Purpose**: Links structure element to specific content on specific page

### Content Side (Phase 6H)

**Location**: PDF page content streams (actual rendered content)
**Format**: BDC/EMC (Begin/End Marked Content) operators with MCID
**Created By**: Python microservice via `ExternalMcidRewriterService`
**Example**:
```
/P <</MCID 5>> BDC
  (Hello World) Tj
EMC
```

**Purpose**: Marks actual content with MCID matching structure MCR

### The Connection

When both sides exist:
- Screen reader reads structure tree
- For each structure element, finds MCR kids
- MCR points to (Page=0, MCID=5)
- Looks up page 0 content stream
- Finds `BDC ... EMC` block with matching MCID
- Reads content inside that block
- Result: Proper reading order and structure-content association

---

## 3. Core Services

### 3.1 StructureRebuildService

**Location**: `Services/Remediation/StructureRebuildService.cs`
**Interface**: `IRemediationService`
**DI Lifetime**: Scoped
**Target Category**: Structure
**Priority**: Phase 0 (First service in pipeline)

**Dependencies**:
```csharp
private readonly LogicalLayoutAnalysisService _layoutAnalysisService;
private readonly FormFieldEnrichmentService _enrichmentService;
private readonly StructureTreeBuilder _builder;
private readonly StructureTreeCleaner _cleaner;
private readonly ITaggedPdfFinalizer _finalizer;
private readonly RemediationJobContext _jobContext;
```

**Main Method**:
```csharp
public async Task<byte[]> FixAsync(byte[] pdfBytes, RemediationJobContext context)
{
    // 1. AI Layout Analysis
    var logicalDocument = await _layoutAnalysisService.AnalyzeLayoutAsync(pdfBytes);

    // 2. Form Field Enrichment
    var enrichedDocument = await _enrichmentService.EnrichWithFormFieldsAsync(
        pdfBytes, logicalDocument);

    // 3. Build Structure Tree
    var structure = _builder.Build(enrichedDocument);

    // 4. Clean Structure
    structure = _cleaner.Clean(structure);

    // 5. Finalize with MCID (if enabled)
    var result = await _finalizer.FinalizeTaggedPdf(pdfBytes, structure, context);

    // 6. Set context flags
    context.StructureRebuildExecuted = true;
    if (context.Options.EnableMcidContentRewrite)
        context.McidContentRewriteExecuted = true;

    return result;
}
```

**Configuration**:
- Reads `context.Options.EnableMcidLinking`
- Reads `context.Options.EnableMcidContentRewrite`
- Delegates MCID behavior to `ITaggedPdfFinalizer`

**State Management**:
- Sets `context.StructureRebuildExecuted = true` after completion
- Sets `context.McidContentRewriteExecuted = true` if content rewrite performed
- These flags prevent subsequent corruption

---

### 3.2 LogicalLayoutAnalysisService

**Location**: `Services/LogicalLayoutAnalysisService.cs`
**Purpose**: Analyzes PDF layout using Claude Vision API to produce logical document structure

**Output**: `LogicalDocument` containing:
- Pages with logical blocks (heading, paragraph, list, table, figure, form)
- Reading order
- Hierarchy relationships
- Bounding boxes

**AI Model**: Claude Sonnet 4.5 (via AnthropicService)

**Process**:
1. Render PDF pages to images
2. Send to Claude Vision with structure analysis prompt
3. Parse JSON response into `LogicalDocument`
4. Validate and clean structure

---

### 3.3 FormFieldEnrichmentService

**Location**: `Services/FormFieldEnrichmentService.cs`
**Purpose**: Enriches logical document with form field metadata

**Responsibilities**:
- Detect form fields in PDF (via Syncfusion)
- Match form fields to logical blocks by coordinates
- Attach field metadata (name, type, value, bounds) to blocks
- Update block types to FormField where appropriate

**Output**: Enhanced `LogicalDocument` with form field information

---

### 3.4 StructureTreeBuilder

**Location**: `Services/StructureTreeBuilder.cs`
**Purpose**: Converts logical document into StructureTree

**Mapping**:
- LogicalDocument → StructureTree
- LogicalBlock → StructureNode
- Preserves hierarchy and reading order
- Assigns PDF structure roles (P, H1-H6, L, LI, Table, Figure, Form)

**Output**: `StructureTree` with properly nested nodes

---

### 3.5 StructureTreeCleaner

**Location**: `Services/StructureTreeCleaner.cs`
**Purpose**: Cleans and optimizes structure tree

**Operations**:
- Remove empty nodes
- Flatten unnecessary nesting
- Merge adjacent text blocks
- Validate role usage
- Fix common structure violations

**Output**: Clean, optimized `StructureTree`

---

### 3.6 TaggedPdfFinalizer

**Location**: `Services/Pdf/TaggedPdfFinalizer.cs`
**Interface**: `ITaggedPdfFinalizer`
**Purpose**: Orchestrates final PDF structure writing

**Responsibilities**:
1. Validate structure tree
2. Delegate to `IPdfStructureWriter.Rewrite()`
3. Handle errors gracefully

**Implementation**:
```csharp
public async Task<byte[]> FinalizeTaggedPdf(
    byte[] originalPdf,
    StructureTree structure,
    RemediationJobContext context)
{
    // Validate
    if (structure == null || structure.Root == null)
        throw new InvalidOperationException("Invalid structure tree");

    // Write
    return await _structureWriter.Rewrite(originalPdf, structure, context);
}
```

**Current Implementation**: Uses `ITextPdfStructureWriter` (full Phase 6K support)

---

## 4. PDF Structure Writers

### 4.1 IPdfStructureWriter Interface

**Location**: `Services/Pdf/IPdfStructureWriter.cs`

```csharp
public interface IPdfStructureWriter
{
    Task<byte[]> Rewrite(byte[] pdfBytes, StructureTree tree, RemediationJobContext context);
}
```

**Implementations**:
1. **ITextPdfStructureWriter** - Production (Phase 6K MCID support)
2. **SyncfusionPdfStructureWriter** - Alternative (no MCID support)
3. **StubPdfStructureWriter** - Testing stub

---

### 4.2 ITextPdfStructureWriter (Phase 6K Implementation)

**Location**: `Services/Pdf/ITextPdfStructureWriter.cs`
**Library**: iText7
**Purpose**: Writes structure tree with MCR kids for MCID linking

**Key Method**:
```csharp
public async Task<byte[]> Rewrite(
    byte[] pdfBytes,
    StructureTree tree,
    RemediationJobContext context)
{
    // 1. Open PDF with iText7
    using var ms = new MemoryStream(pdfBytes);
    using var pdfDocument = new PdfDocument(new PdfReader(ms), new PdfWriter(outputMs));

    // 2. Check if MCID linking enabled
    if (!context.Options.EnableMcidLinking)
    {
        return WriteStructureOnly(pdfDocument, tree);
    }

    // 3. Allocate MCIDs
    AllocateMcids(tree);

    // 4. Create structure tree with MCR kids
    var rootElement = CreateStructureTree(pdfDocument, tree);

    // 5. Save PDF
    pdfDocument.Close();
    var pdfWithMcrs = outputMs.ToArray();

    // 6. External MCID rewriter (if enabled)
    if (context.Options.EnableMcidContentRewrite)
    {
        var plan = _planBuilder.BuildPlan(tree);
        return await _mcidRewriter.RewriteMcidsAsync(pdfWithMcrs, plan);
    }

    return pdfWithMcrs;
}
```

**MCID Allocation** (lines 463-490):
```csharp
private void AllocateMcids(StructureTree tree)
{
    var mcidCounterPerPage = new Dictionary<int, int>();

    foreach (var node in tree.TraversePreOrder())
    {
        if (node.BoundingBox != null && node.PageIndex.HasValue)
        {
            int pageIndex = node.PageIndex.Value;
            int mcid = mcidCounterPerPage.GetValueOrDefault(pageIndex, 0);

            node.McidReferences = new List<McidReference>
            {
                new McidReference(pageIndex, mcid)
            };

            mcidCounterPerPage[pageIndex] = mcid + 1;
        }
    }
}
```

**MCR Kid Creation** (lines 491-511):
```csharp
private void CreateMcrKids(PdfStructElem structElement, StructureNode node, PdfDocument document)
{
    if (node.McidReferences == null || node.McidReferences.Count == 0)
        return;

    foreach (var mcidRef in node.McidReferences)
    {
        var page = document.GetPage(mcidRef.PageIndex + 1); // 1-indexed
        var mcr = new PdfMcrNumber(page, mcidRef.Mcid);
        structElement.AddKid(mcr);
    }
}
```

---

## 5. MCID Services

### 5.1 McidRewritePlanBuilder

**Location**: `Services/Phase6K/McidRewritePlanBuilder.cs` (inferred location)
**Purpose**: Converts StructureTree into McidRewritePlan for Python service

**Input**: `StructureTree` with allocated MCIDs
**Output**: `McidRewritePlan` with coordinate-based segments

**Plan Structure**:
```csharp
public class McidRewritePlan
{
    public List<McidSegment> Segments { get; set; }
}

public class McidSegment
{
    public int PageIndex { get; set; }
    public int Mcid { get; set; }
    public BoundingBox Bounds { get; set; }
    public string Role { get; set; }  // P, H1, etc.
    public int SequenceIndex { get; set; }
}
```

**Algorithm**:
1. Traverse structure tree in reading order
2. For each node with McidReferences:
   - Extract (PageIndex, MCID, Bounds, Role)
   - Create McidSegment
   - Add to plan with sequence index
3. Sort by sequence index
4. Return plan

---

### 5.2 ExternalMcidRewriterService

**Location**: `Services/Phase6H/ExternalMcidRewriterService.cs`
**Purpose**: HTTP client for Python MCID rewriter microservice

**Configuration**:
```json
{
  "McidRewriter": {
    "BaseUrl": "http://localhost:8000",
    "TimeoutSeconds": 30
  }
}
```

**API Call**:
```csharp
public async Task<byte[]> RewriteMcidsAsync(byte[] pdfBytes, McidRewritePlan plan)
{
    var request = new McidRewriteRequest
    {
        PdfBase64 = Convert.ToBase64String(pdfBytes),
        Plan = plan
    };

    var response = await _httpClient.PostAsJsonAsync("/api/mcid-rewrite", request);

    if (!response.IsSuccessStatusCode)
    {
        _logger.LogWarning("MCID rewriter returned {Status}", response.StatusCode);
        return pdfBytes; // Return original on failure
    }

    var result = await response.Content.ReadFromJsonAsync<McidRewriteResponse>();
    return Convert.FromBase64String(result.PdfBase64);
}
```

**Error Handling**:
- Logs warnings on failure
- Returns original PDF (with MCR kids but no BDC/EMC markers)
- Allows partial MCID support (structure-side only)

---

### 5.3 Python MCID Rewriter Microservice

**Location**: `McidRewriterMicroservice/main.py`
**Language**: Python 3.10+
**Framework**: FastAPI
**PDF Library**: pikepdf
**Port**: 8000 (default)

**Endpoint**: `POST /api/mcid-rewrite`

**Request Format**:
```json
{
  "pdfBase64": "<base64-encoded PDF>",
  "plan": {
    "segments": [...]
  }
}
```

**Response Format**:
```json
{
  "pdfBase64": "<base64-encoded PDF with markers>",
  "success": true,
  "markersAdded": 42
}
```

**Implementation**:
- Decodes PDF from base64
- Opens with pikepdf
- For each segment in plan:
  - Locate content in bounding box
  - Insert `BDC` operator before content
  - Insert `EMC` operator after content
- Save modified PDF
- Encode to base64 and return

**Deployment**:
```bash
cd McidRewriterMicroservice
pip install -r requirements.txt
uvicorn main:app --host 0.0.0.0 --port 8000
```

**Docker**:
```dockerfile
FROM python:3.10-slim
WORKDIR /app
COPY requirements.txt .
RUN pip install -r requirements.txt
COPY main.py .
CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]
```

---

## 6. Models & Context

### 6.1 RemediationJobContext

**Location**: `Models/Remediation/RemediationJobContext.cs`
**DI Lifetime**: Scoped (one instance per HTTP request / remediation job)

```csharp
public class RemediationJobContext
{
    public RemediationOptions Options { get; set; }
    public StructureRebuildContext StructureContext { get; set; }

    // Convenience properties
    public bool StructureRebuildExecuted
    {
        get => StructureContext.StructureRebuildExecuted;
        set => StructureContext.StructureRebuildExecuted = value;
    }

    public bool McidContentRewriteExecuted
    {
        get => StructureContext.McidContentRewriteExecuted;
        set => StructureContext.McidContentRewriteExecuted = value;
    }
}
```

**Purpose**: Shared state container for all services in a remediation job

---

### 6.2 StructureRebuildContext

```csharp
public class StructureRebuildContext
{
    public bool StructureRebuildExecuted { get; set; }
    public bool McidContentRewriteExecuted { get; set; }
    public StructureTree GeneratedStructure { get; set; }
}
```

**Purpose**: Tracks structure rebuild and MCID pipeline state

---

### 6.3 StructureTree & StructureNode

**Location**: `Models/Structure/StructureTree.cs`, `StructureNode.cs`

```csharp
public class StructureNode
{
    public string Role { get; set; }  // P, H1-H6, L, LI, Table, etc.
    public int? PageIndex { get; set; }
    public BoundingBox BoundingBox { get; set; }
    public List<McidReference> McidReferences { get; set; }
    public List<StructureNode> Children { get; set; }
    public string Text { get; set; }
}
```

---

### 6.4 McidReference

```csharp
public class McidReference
{
    public int PageIndex { get; set; }
    public int Mcid { get; set; }

    public McidReference(int pageIndex, int mcid)
    {
        PageIndex = pageIndex;
        Mcid = mcid;
    }
}
```

**Purpose**: Stores (Page, MCID) pair for structure-to-content linking

---

## 7. Configuration

### 7.1 appsettings.json

```json
{
  "AccessibilityRemediation": {
    "EnableMcidLinking": true,
    "EnableMcidContentRewrite": true,
    "AsposeOptimizationMode": "PreStructureOnly",
    "ArtifactFixMode": "PreStructureOnly"
  },
  "RemediationPipeline": {
    "EnableStructureRebuild": true,
    "StructureRebuildPriority": 0
  },
  "McidRewriter": {
    "BaseUrl": "http://localhost:8000",
    "TimeoutSeconds": 30
  }
}
```

### 7.2 Configuration Flow

```
appsettings.json
  ↓
IOptions<RemediationOptions> (DI)
  ↓
RemediationOrchestrator.RemediateAsync()
  ↓
Merge with passed options
  ↓
Store in RemediationJobContext.Options
  ↓
All services read from context.Options
```

---

## 8. Testing

### 8.1 Test Files

**Integration Test**: `TestPhase6KFullPipeline.cs`
**Unit Tests**: (TBD)
**Test PDFs**: `StateAssets_archived_20251112_063442/Pennsylvania/Erie/ERA_0051_Route 5 August 2025.pdf`

### 8.2 Test Execution

```bash
# Run full integration test
dotnet test --filter "TestPhase6KFullPipeline"

# Output: erie_phase6k_output.pdf
```

### 8.3 Verification Steps

**1. Microservice Logs**:
```
INFO: Received MCID rewrite request
INFO: Processing 42 segments
INFO: Added 42 BDC markers
INFO: Added 42 EMC markers
```

**2. Hexdump Check**:
```bash
xxd erie_phase6k_output.pdf | grep -A2 -B2 "/MCID"
```

Expected output:
```
... /P <</MCID 5>> BDC ...
... EMC ...
```

**3. Adobe Acrobat Verification**:
- Open PDF in Acrobat Pro
- View → Show/Hide → Navigation Panes → Tags
- Expand structure tree
- Look for MCR entries: `<MCR Pg="0" MCID="5"/>`
- Tools → Accessibility → Reading Order
- Verify content blocks have correct reading order

**4. VeraPDF Validation**:
```bash
verapdf --flavour ua1 erie_phase6k_output.pdf
```

Should show reduced MCID-related violations.

### 8.4 Expected Results

Per CLAUDE.md Phase 6K verification (2025-11-19):
- Python microservice reports BDC/EMC insertion
- Markers persist through save/reload
- No PDF corruption
- Structure tree shows MCR kids in Acrobat
- Reading order improved

---

## 9. Troubleshooting

### 9.1 MCID Markers Not Found in Output

**Symptoms**:
- Hexdump shows no `/MCID` references
- Acrobat shows structure tree but no MCR kids

**Diagnosis**:
```bash
# Check microservice health
curl http://localhost:8000/health

# Check logs
tail -100 Logs/accessform.log | grep MCID
```

**Fixes**:
- Verify microservice is running: `ps aux | grep uvicorn`
- Check `EnableMcidContentRewrite = true` in config
- Review `ExternalMcidRewriterService` logs for errors
- Ensure no firewall blocking localhost:8000

---

### 9.2 Structure Rebuild Runs But No MCIDs

**Symptoms**:
- Structure tree rebuilt
- But no MCID allocation

**Diagnosis**:
```csharp
// Check configuration
var enableMcid = context.Options.EnableMcidLinking;
var enableRewrite = context.Options.EnableMcidContentRewrite;
```

**Fixes**:
- Verify `EnableMcidLinking = true` in appsettings.json
- Check `RemediationPipeline:EnableStructureRebuild = true`
- Ensure `StructureRebuildServiceAdapter` registered in DI
- Review `ITextPdfStructureWriter` configuration

---

### 9.3 Content Overwrites MCID Markers

**Symptoms**:
- Initial output has markers
- Final output missing markers
- Corruption in cleanup phase

**Diagnosis**:
```bash
# Check guard clause logs
grep "Skipping.*MCID content already rewritten" Logs/accessform.log
```

**Fixes**:
- Verify `context.McidContentRewriteExecuted` flag set
- Check guard clauses in cleanup services:
  - `ArtifactTaggedContentFixService`
  - `WhitespaceTaggingService`
  - Custom content stream services
- Review `RemediationOrchestrator` cleanup phase logic (line 263)
- Ensure cleanup phase skipped when MCID rewrite executed

---

### 9.4 Python Microservice Connection Failures

**Symptoms**:
- "Connection refused" errors
- Timeouts on `/api/mcid-rewrite`

**Diagnosis**:
```bash
# Test direct connection
curl -X POST http://localhost:8000/api/mcid-rewrite \
  -H "Content-Type: application/json" \
  -d '{"pdfBase64":"","plan":{"segments":[]}}'
```

**Fixes**:
- Start microservice: `cd McidRewriterMicroservice && uvicorn main:app`
- Check port 8000 not in use: `lsof -i :8000`
- Update `McidRewriter:BaseUrl` in appsettings.json
- Increase `TimeoutSeconds` for large documents
- Deploy microservice as systemd service or Docker container

---

### 9.5 Performance Issues

**Symptoms**:
- Structure rebuild takes >30 seconds
- MCID allocation very slow

**Diagnosis**:
```bash
# Check document size
ls -lh input.pdf

# Review Claude API latency
grep "Claude Vision API" Logs/accessform.log
```

**Optimization**:
- Claude API is bottleneck (5-10s typical)
- Consider caching layout analysis results
- Process smaller page ranges
- Use async/await properly
- Deploy microservice closer to API server

---

## Summary

The Structure Rebuild & MCID system provides comprehensive PDF structure remediation with accessibility linking. Key success factors:

1. **Proper Configuration**: All MCID flags enabled
2. **Microservice Availability**: Python rewriter running and accessible
3. **Guard Clauses**: Protect content streams from overwrites
4. **Testing**: Verify with Acrobat and VeraPDF
5. **Error Handling**: Graceful degradation if microservice fails

For additional details, see ARCHITECTURE-SERVICES-REMEDIATION.md sections on Phase 6K services.
