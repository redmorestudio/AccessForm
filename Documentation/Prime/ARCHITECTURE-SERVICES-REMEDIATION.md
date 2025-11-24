# AccessForm PDF/UA Remediation Service Architecture

**Last Updated:** 2025-11-19

## Overview

This document details the closed-loop PDF/UA remediation system that automatically fixes accessibility violations through iterative validation and targeted repairs.

**Navigation:**
- [ARCHITECTURE-SERVICES-CORE.md](ARCHITECTURE-SERVICES-CORE.md) - Core pipeline & AI services
- [ARCHITECTURE-SERVICES-ACCESSIBILITY.md](ARCHITECTURE-SERVICES-ACCESSIBILITY.md) - Accessibility & utility services
- [ARCHITECTURE.md](ARCHITECTURE.md) - Main processing paths
- [ARCHITECTURE-API.md](ARCHITECTURE-API.md) - API endpoints

---

## Remediation System Overview

The remediation system uses a **closed-loop architecture**:

```
┌─────────────────────────────────────────────────┐
│  1. Initial Validation (VeraPDF)                │
│     → Establish baseline violations             │
└──────────────────┬──────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────┐
│  2. Violation Analysis                           │
│     → Categorize by type (Structure, Metadata,  │
│       Forms, Content, Whitespace, etc.)         │
└──────────────────┬──────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────┐
│  3. Strategy Selection                           │
│     → Build execution phases                     │
│     → Map services to categories                │
└──────────────────┬──────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────┐
│  4. Remediation Execution                        │
│     → Run phased services                        │
│     → Apply fixes iteratively                    │
└──────────────────┬──────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────┐
│  5. Post-Remediation Cleanup                     │
│     → Fix structural issues introduced by fixes  │
└──────────────────┬──────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────┐
│  6. GPT-5 Fallback (if needed)                  │
│     → AI-powered fixes for complex violations   │
└──────────────────┬──────────────────────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────┐
│  7. Re-Validate                                  │
│     → Exit if compliant OR max iterations        │
│     → Loop back to step 2 if violations remain   │
└─────────────────────────────────────────────────┘
```

---

## Core Orchestration Services

### RemediationOrchestrator

**Location**: `Services/Remediation/RemediationOrchestrator.cs`
**Purpose**: Main orchestrator for closed-loop PDF/UA remediation with Phase 0 structure rebuild and MCID integration
**Dependencies**: VeraPdfService, ViolationAnalyzer, RemediationStrategySelector, RemediationExecutor, ExitConditionEvaluator, ProgressTracker, RemediationReporter, GptRemediationService, StructureRebuildService (optional Phase 0)

**Key Methods**:

#### `RemediateAsync(byte[] inputPdf, RemediationOptions options)`
Main entry point for closed-loop remediation.

**Parameters**:
- `inputPdf` - PDF bytes to remediate
- `options` - Remediation configuration

**RemediationOptions**:
```csharp
public class RemediationOptions
{
    public int MaxIterations { get; set; } = 10;
    public TimeSpan MaxDuration { get; set; } = TimeSpan.FromMinutes(10);
    public string FileName { get; set; }
    public string ProgressSessionId { get; set; }

    // Phase 6K MCID options
    public bool EnableMcidLinking { get; set; } = true;
    public bool EnableMcidContentRewrite { get; set; } = true;

    public static RemediationOptions Production => new()
    {
        MaxIterations = 10,
        MaxDuration = TimeSpan.FromMinutes(10),
        EnableMcidLinking = true,
        EnableMcidContentRewrite = true
    };
}
```

**Processing Flow**:

**Step 0: Initial Validation**
```csharp
// Run initial validation to establish baseline
var initialValidation = await ValidateAsync(session, tempPath);
session.InitialValidation = initialValidation;

// Store as last good validation
session.LastValidatedPdf = await File.ReadAllBytesAsync(tempPath);
session.LastValidation = initialValidation;

_logger.LogInformation(
    $"Baseline established: {initialValidation.Violations.Count} violations, " +
    $"{initialValidation.Summary.ComplianceScore:F1}% compliant");
```

**Main Loop**:
```csharp
while (!session.IsComplete)
{
    session.IterationCount++;

    // Step 0.5: Phase 0 Structure Rebuild (if enabled and first iteration)
    // This runs BEFORE first validation to establish clean structure + MCID links
    if (session.IterationCount == 1 && ShouldRunStructureRebuild(context))
    {
        _logger.LogInformation("Phase 0: Running structure rebuild with MCID integration");

        var structureResult = await _structureRebuildService.FixAsync(
            session.CurrentPdf, context);

        if (structureResult != null && structureResult.Length > 0)
        {
            session.CurrentPdf = structureResult;
            await File.WriteAllBytesAsync(tempPath, structureResult);

            // Structure rebuild sets context flags:
            // - context.StructureRebuildExecuted = true
            // - context.McidContentRewriteExecuted = true (if rewrite enabled)

            _logger.LogInformation(
                $"Phase 0 complete: MCID linking={context.Options.EnableMcidLinking}, " +
                $"Content rewrite={context.McidContentRewriteExecuted}");
        }
    }

    // Step 1: Validate current PDF
    var validation = await ValidateAsync(session, tempPath);
    session.CurrentValidation = validation;

    // Step 2: Check exit conditions
    if (_exitEvaluator.ShouldExit(validation, session, out var exitReason))
    {
        session.ExitReason = exitReason;
        session.Complete(validation);
        break;
    }

    // Step 3: Analyze violations
    var analysis = await _violationAnalyzer.AnalyzeAsync(
        validation.Violations, session);

    // Step 4: Determine if this is final iteration
    session.IsFinalIteration = (session.IterationCount == options.MaxIterations - 1);

    // Step 5: Select remediation strategy
    var strategy = await _strategySelector.SelectAsync(analysis, session);

    // Step 6: Execute remediation
    var execution = await _executor.ExecuteAsync(
        session.CurrentPdf, strategy, session);

    // Step 6.5: Post-Remediation Cleanup (ONLY if MCID content not rewritten)
    // Guard clause prevents cleanup phase from corrupting BDC/EMC markers
    if (execution.Success && execution.OutputPdf != null &&
        !context.McidContentRewriteExecuted)
    {
        var cleanupStrategy = _strategySelector.BuildCleanupStrategy();
        var cleanupResult = await _executor.ExecuteAsync(
            execution.OutputPdf, cleanupStrategy, session);

        if (cleanupResult.Success && cleanupResult.OutputPdf != null)
        {
            execution.OutputPdf = cleanupResult.OutputPdf;
        }
    }
    else if (context.McidContentRewriteExecuted)
    {
        _logger.LogInformation(
            "Skipping cleanup phase - MCID content rewrite executed, protecting markers");
    }

    // Step 7: GPT-5 Fallback (if needed)
    bool shouldUseGpt = _gptService != null &&
        validation.Violations.Count > 0;

    if (shouldUseGpt)
    {
        _gptService.SetViolations(validation.Violations);
        var gptResult = await _gptService.RemediateAsync(execution.OutputPdf);

        if (gptResult.Success && gptResult.ChangesMade)
        {
            execution.OutputPdf = gptResult.OutputPdf;
        }
    }

    // Step 8: Update session
    session.NextIteration(execution);
}

// Generate final report
var result = _reporter.GenerateReport(session);
return result;
```

**Exit Conditions**:
- PDF/UA compliant (zero violations)
- Max iterations reached
- Max duration exceeded
- No progress made (stuck)
- Fatal error occurred

---

### VeraPdfService

**Location**: `Services/VeraPdfService.cs`
**Purpose**: PDF/UA validation using VeraPDF CLI tool
**Dependencies**: VeraPDF CLI (external tool)

**Key Methods**:

#### `ValidatePdfAsync(string pdfPath, string profile = "ua1")`
Validates a PDF file for PDF/UA compliance.

**Returns**: `ValidationResult` with:
- `Status` - ValidationStatus (Running, Completed, Failed)
- `Violations` - List<PdfUAViolation>
- `Summary` - ValidationSummary (IsCompliant, ComplianceScore, PassedChecks, FailedChecks)
- `RawVeraPdfOutput` - Original JSON output from VeraPDF

**Processing**:
```csharp
// Run veraPDF CLI
var startInfo = new ProcessStartInfo
{
    FileName = _veraPdfPath,
    Arguments = $"--flavour {profile} --format json \"{pdfPath}\"",
    RedirectStandardOutput = true,
    RedirectStandardError = true
};

using var process = new Process { StartInfo = startInfo };
process.Start();
await process.WaitForExitAsync();

var jsonOutput = process.StandardOutput.ReadToEnd();

// Parse JSON output
ParseVeraPdfOutput(jsonOutput, result);
```

**JSON Structure**:
```json
{
  "report": {
    "jobs": [{
      "validationResult": [{
        "profileName": "PDF/UA-1",
        "isCompliant": "false",
        "details": {
          "passedRules": 145,
          "failedRules": 12,
          "ruleSummaries": [
            {
              "clause": "7.1",
              "testNumber": 1,
              "status": "failed",
              "description": "Tagged content shall be contained in a real content stream",
              "checks": [{
                "context": "root/document[0]/pages[0]/page[2]",
                "message": "Content is marked as Artifact but contains tagged content"
              }]
            }
          ]
        }
      }]
    }]
  }
}
```

**Violation Classification**:
```csharp
private ViolationSeverity DetermineSeverity(string clause)
{
    // Critical: Missing structure, metadata, language
    if (clause.StartsWith("6.1") || clause.StartsWith("6.2") || clause.StartsWith("7.1"))
        return ViolationSeverity.Critical;

    // Error: Content accessibility issues
    if (clause.StartsWith("7.3") || clause.StartsWith("7.18") || clause.StartsWith("7.21"))
        return ViolationSeverity.Error;

    // Warning: Best practices
    return ViolationSeverity.Warning;
}
```

**Important**: VeraPDF returns exit code 1 when violations are found - this is expected behavior, not an error!

---

### ViolationAnalyzer

**Location**: `Services/Remediation/Analysis/ViolationAnalyzer.cs`
**Purpose**: Analyzes violations to categorize, prioritize, and identify patterns
**Dependencies**: None

**Key Methods**:

#### `AnalyzeAsync(List<PdfUAViolation> violations, RemediationSession session)`
Analyzes violations and returns categorized results.

**Returns**: `ViolationAnalysis` with:
- `Categories` - Dictionary<ViolationCategory, List<PdfUAViolation>>
- `Patterns` - List<ViolationPattern> (recurring issues)
- `Regressions` - List<PdfUAViolation> (previously fixed issues that reappeared)
- `PriorityQueue` - Prioritized list of violations

**Violation Categories**:
```csharp
public enum ViolationCategory
{
    Structure,          // 7.1 - Structure tree issues
    Content,            // 7.3 - Content accessibility
    Whitespace,         // Whitespace in tagged content
    Metadata,           // 5, 6.1, 6.2 - Document metadata
    FormFields,         // 7.18 - Form field issues
    Annotations,        // 7.18 - Non-form annotations
    TableAndList,       // 7.2, 7.5, 7.6 - Table/list structure
    AlternateText,      // Alt text for images
    Links,              // 7.18.5 - Link structure
    Fonts,              // 7.21 - Font embedding
    Language,           // Language specification
    Unknown             // Unclassified
}
```

**Classification Logic**:
```csharp
private ViolationCategory ClassifyViolation(PdfUAViolation violation)
{
    var clause = violation.Clause ?? "";
    var desc = violation.Description?.ToLowerInvariant() ?? "";

    // Table and List issues (7.2, 7.5, 7.6)
    if (clause.StartsWith("7.2") || clause.StartsWith("7.5") ||
        clause.StartsWith("7.6") || desc.Contains("table"))
        return ViolationCategory.TableAndList;

    // Form fields (7.18.4 for widget nesting)
    if (clause.StartsWith("7.18.4") ||
        (clause.StartsWith("7.18") && desc.Contains("widget")))
        return ViolationCategory.FormFields;

    // Structure issues (7.1)
    if (clause.StartsWith("7.1"))
        return ViolationCategory.Structure;

    // Metadata (5, 6.1, 6.2)
    if (clause.StartsWith("5") || clause.StartsWith("6.1") ||
        clause.StartsWith("6.2"))
        return ViolationCategory.Metadata;

    // ... other categories

    return ViolationCategory.Unknown;
}
```

**Pattern Detection**:
```csharp
private List<ViolationPattern> IdentifyPatterns(
    List<PdfUAViolation> currentViolations,
    List<IterationSnapshot> history)
{
    // Identify recurring violations across iterations
    // Detect violations that increase over time
    // Flag regressions (fixed violations that reappear)
}
```

---

### RemediationStrategySelector

**Location**: `Services/Remediation/Strategy/RemediationStrategySelector.cs`
**Purpose**: Selects appropriate remediation strategies based on violation analysis
**Dependencies**: IServiceProvider (for DI resolution)

**Key Methods**:

#### `SelectAsync(ViolationAnalysis analysis, RemediationSession session)`
Builds execution strategy with phased approach.

**Returns**: `RemediationStrategy` with ordered phases

**Execution Phases** (in order):

0. **Structure Rebuild** (Order: 0, MaxIter: 1, First Iteration Only)
   - StructureRebuildServiceAdapter (Phase 6K MCID integration)
   - Only runs if `RemediationPipeline:EnableStructureRebuild = true` in config

1. **Whitespace Cleanup** (Order: 1, MaxIter: 2)
   - WhitespaceServiceAdapter

2. **Content Remediation** (Order: 2, MaxIter: 3)
   - ContentServiceAdapter

3. **Structure Enhancement** (Order: 3, MaxIter: 1)
   - CircularRoleMappingFixService
   - ArtifactTaggedContentFixService

4. **Form Field Remediation** (Order: 4, MaxIter: 2)
   - FormWidgetNestingFixService
   - GptServiceAdapter (fallback)

5. **Link Structure Fixes** (Order: 5, MaxIter: 2)
   - LinkServiceAdapter

6. **Font/PDF-A Conversion** (Order: 6, MaxIter: 1)
   - FontEmbeddingServiceAdapter

7. **Table and List Structure** (Order: 7, MaxIter: 2)
   - TableScopeAttributeFixService
   - TableStructureValidationService

8. **Alternative Text** (Order: 8, MaxIter: 1)
   - FigureAltTextService

9. **Metadata Finalization** (Order: 99, MaxIter: 1, ALWAYS LAST)
   - PdfUaMetadataService

**Phase Structure**:
```csharp
public class RemediationPhase
{
    public string Name { get; set; }
    public int Order { get; set; }
    public ViolationCategory TargetCategory { get; set; }
    public int MaxIterations { get; set; }
    public List<IRemediationService> Services { get; set; }
    public bool Required { get; set; }
}
```

#### `BuildCleanupStrategy()`
Builds a special cleanup strategy that runs all fix services to clean up issues introduced during remediation.

**Important**: Cleanup phase is SKIPPED if `context.McidContentRewriteExecuted = true` to protect BDC/EMC markers.

**Cleanup Services** (runs after main remediation):
1. ArtifactTaggedContentFixService
2. FormWidgetNestingFixService
3. TableStructureValidationService
4. TableScopeAttributeFixService
5. FigureAltTextService
6. PdfUaMetadataService

---

### RemediationExecutor

**Location**: `Services/Remediation/Execution/RemediationExecutor.cs`
**Purpose**: Executes remediation phases and services
**Dependencies**: None

**Key Methods**:

#### `ExecuteAsync(byte[] pdfBytes, RemediationStrategy strategy, RemediationSession session)`
Executes all phases in the strategy.

**Returns**: `ExecutionResult` with:
- `Success` - bool
- `OutputPdf` - byte[]
- `Phases` - List<PhaseResult>
- `Duration` - TimeSpan

**Execution Flow**:
```csharp
byte[] currentPdf = pdfBytes;

foreach (var phase in strategy.Phases)
{
    _logger.LogInformation(
        $"Starting phase: {phase.Name} (Order: {phase.Order}, MaxIter: {phase.MaxIterations})");

    var phaseResult = await ExecutePhaseAsync(currentPdf, phase, session);

    result.Phases.Add(phaseResult);

    if (phaseResult.Success && phaseResult.OutputPdf != null)
    {
        currentPdf = phaseResult.OutputPdf;
    }
    else if (phase.Required)
    {
        // Required phase failed - abort
        result.Success = false;
        break;
    }
}

result.OutputPdf = currentPdf;
return result;
```

**Phase Execution**:
```csharp
private async Task<PhaseResult> ExecutePhaseAsync(
    byte[] pdfBytes,
    RemediationPhase phase,
    RemediationSession session)
{
    byte[] currentPdf = pdfBytes;
    int iteration = 0;

    while (iteration < phase.MaxIterations)
    {
        iteration++;
        bool madeProgress = false;

        foreach (var service in phase.Services)
        {
            var serviceResult = await service.RemediateAsync(currentPdf);

            if (serviceResult.Success && serviceResult.ChangesMade)
            {
                currentPdf = serviceResult.OutputPdf;
                madeProgress = true;

                _logger.LogInformation(
                    $"✓ {service.ServiceName}: Fixed {serviceResult.IssuesFixed}/{serviceResult.IssuesFound} issues");
            }
        }

        // Stop iterating if no progress
        if (!madeProgress)
            break;
    }

    return phaseResult;
}
```

---

## AI-Powered Remediation

### GptRemediationService

**Location**: `Services/Remediation/AI/GptRemediationService.cs`
**Purpose**: GPT-5 powered remediation for complex violations that standard services cannot fix
**Dependencies**: OpenAIService, ScriptExecutor, SolutionCache

**Key Methods**:

#### `RemediateAsync(byte[] pdfBytes)`
Main remediation method using GPT-5.

**Processing Flow**:

**1. Group Violations by Category**
```csharp
var violationsByCategory = violations
    .GroupBy(v => GetViolationCategory(v))
    .ToDictionary(g => g.Key, g => g.ToList());

_logger.LogInformation(
    $"Processing {violations.Count} violations across {violationsByCategory.Count} categories");
```

**2. Process Each Category**
```csharp
foreach (var category in violationsByCategory)
{
    var categoryResult = await RemediateCategoryAsync(
        currentPdf,
        category.Key,
        category.Value);

    if (categoryResult.Success && categoryResult.OutputPdf != null)
    {
        currentPdf = categoryResult.OutputPdf;
        totalFixed += categoryResult.FixedCount;
    }
}
```

**3. Check Solution Cache**
```csharp
// Check if we've seen this violation before
var cachedSolution = _solutionCache.GetSolution(violations.First().Description);
if (cachedSolution != null)
{
    _logger.LogInformation("Using cached solution");
    gptResponse = cachedSolution.Solution;
    usedCache = true;
}
```

**4. Build Remediation Prompt**
```csharp
var prompt = BuildRemediationPrompt(category, violations);

// Example prompt structure:
// "You are a PDF/UA compliance expert using GPT-5.
//  Analyze these violations and provide remediation.
//
//  VIOLATION CATEGORY: FormFields
//  TOTAL VIOLATIONS: 5
//
//  VIOLATION DETAILS:
//  - Rule: 7.18.4-1
//    Description: Widget annotation not nested in Form tag
//    Context: root/document[0]/pages[0]/page[2]
//
//  You have TWO options:
//  OPTION 1: Provide a Python script (pikepdf/PyPDF2)
//  OPTION 2: Provide JSON instructions for simple fixes"
```

**5. Parse GPT Response**
```csharp
if (IsScriptResponse(gptResponse))
{
    // GPT provided Python script
    var script = ExtractScript(gptResponse);
    var scriptResult = await ExecuteScriptWithRetryAsync(
        script, pdfBytes, category, maxRetries: 2);

    if (scriptResult.Success && scriptResult.OutputPdf != null)
    {
        result.OutputPdf = scriptResult.OutputPdf;

        // Cache successful solution
        _solutionCache.StoreGptSolution(
            violations.First().Description,
            category,
            script,
            SolutionCache.SolutionType.PythonScript);
    }
}
else
{
    // GPT provided JSON instructions
    var instructions = ParseRemediationInstructions(gptResponse);
    result.OutputPdf = await ApplyRemediationInstructionsAsync(
        pdfBytes, instructions);
}
```

**GPT Prompt - Special Form/Widget Instructions**:
```csharp
if (hasFormViolations)
{
    sb.AppendLine("⚠️ FORM/WIDGET VIOLATION DETECTED - SPECIAL INSTRUCTIONS:");
    sb.AppendLine("This is a common PDF/UA violation where Form elements lack proper Role attributes");
    sb.AppendLine("or don't have correct widget annotation references.");
    sb.AppendLine();
    sb.AppendLine("The fix typically involves:");
    sb.AppendLine("1. Ensuring each Form tag has a Role='/Form' attribute");
    sb.AppendLine("2. Ensuring Form tags contain proper widget annotation references");
    sb.AppendLine("3. Fixing the parent-child relationship between Form tags and widgets");
}
```

**GPT Prompt - Critical pikepdf API Requirements**:
```python
# WRONG: meta['pdfuaid:part'] = 1  (TypeError!)
# RIGHT: meta['pdfuaid:part'] = '1'  (String required!)

# Working example:
import pikepdf

pdf = pikepdf.open(INPUT_PDF)

with pdf.open_metadata() as meta:
    meta.register_xml_namespace('pdfuaid', 'http://www.aiim.org/pdfua/ns/id/')

    # CRITICAL: ALL metadata values MUST be STRINGS!
    want_part = '1'  # STRING not integer!
    meta['pdfuaid:part'] = want_part

pdf.save(OUTPUT_PDF)
```

#### `ExecuteScriptWithRetryAsync(script, pdfBytes, category, maxRetries)`
Executes Python script with automatic retry and error-feedback correction.

**Error-Feedback Loop**:
```csharp
for (int attempt = 1; attempt <= maxRetries; attempt++)
{
    var scriptResult = await _scriptExecutor.ExecutePythonScriptAsync(
        currentScript, pdfBytes, $"gpt5-{category}-attempt{attempt}");

    if (scriptResult.Success)
    {
        return scriptResult;
    }

    // Script failed - ask GPT to fix it
    if (attempt < maxRetries)
    {
        _logger.LogWarning("Asking GPT to analyze error and fix script...");

        var fixedScript = await _openAiService.FixScriptFromErrorAsync(
            currentScript,
            scriptResult.StandardError,
            scriptResult.StandardOutput);

        if (!string.IsNullOrEmpty(fixedScript))
        {
            currentScript = fixedScript;
            _logger.LogWarning("Received corrected script from GPT, retrying...");
        }
    }
}
```

**Script Execution Environment**:
- INPUT_PDF: path to input PDF file
- OUTPUT_PDF: path where fixed PDF should be saved
- Libraries: pikepdf, PyPDF2, reportlab, pypdf

---

## Fix Services

### PdfUaMetadataService

**Location**: `Services/Remediation/Fixes/PdfUaMetadataService.cs`
**Purpose**: Ensures PDF has proper PDF/UA metadata and conformance identifiers
**Target Category**: Metadata
**Priority**: 1 (High - should run LAST)

**Key Methods**:

#### `RemediateAsync(byte[] pdfBytes)`
Adds or updates XMP metadata to declare PDF/UA compliance.

**Operations**:

**1. Ensure Document is Tagged**
```csharp
if (!pdfDoc.IsTagged())
{
    _logger.LogInformation("Marking document as tagged");
    pdfDoc.SetTagged();
    changesMade = true;
}
```

**2. Set PDF/UA Identifier**
```csharp
// Register PDF/UA namespace
const string PDFUA_NS = "http://www.aiim.org/pdfua/ns/id/";
const string PDFUA_PREFIX = "pdfuaid";

XMPMetaFactory.GetSchemaRegistry().RegisterNamespace(PDFUA_NS, PDFUA_PREFIX);

// Get or create XMP metadata
var xmpMeta = XMPMetaFactory.Create();

// Set PDF/UA-1 part identifier
xmpMeta.SetProperty(PDFUA_NS, "part", "1");

// Save updated XMP metadata
pdfDoc.SetXmpMetadata(xmpMeta);
```

**3. Set Document Metadata**
```csharp
var info = pdfDoc.GetDocumentInfo();

// Ensure title is set (required for PDF/UA)
if (string.IsNullOrWhiteSpace(info.GetTitle()))
{
    info.SetTitle("Accessible PDF Document");
}

// Set language
var catalog = pdfDoc.GetCatalog();
if (catalog.GetLang() == null)
{
    catalog.SetLang(new PdfString("en-US"));
}

// Set creator and producer
info.SetCreator("AccessForm PDF Converter");
info.SetProducer("AccessForm PDF/UA Remediation Service");
```

**4. Set Catalog Entries**
```csharp
// Ensure MarkInfo dictionary exists
var markInfo = catalogDict.GetAsDictionary(PdfName.MarkInfo);
if (markInfo == null)
{
    markInfo = new PdfDictionary();
    catalogDict.Put(PdfName.MarkInfo, markInfo);
}

// Set Marked to true
markInfo.Put(PdfName.Marked, PdfBoolean.TRUE);
markInfo.Put(new PdfName("UserProperties"), PdfBoolean.FALSE);
markInfo.Put(new PdfName("Suspects"), PdfBoolean.FALSE);

// Ensure StructTreeRoot exists
if (!catalogDict.ContainsKey(PdfName.StructTreeRoot))
{
    var structTreeRoot = new PdfDictionary();
    structTreeRoot.Put(PdfName.Type, PdfName.StructTreeRoot);
    catalogDict.Put(PdfName.StructTreeRoot, structTreeRoot);
}

// Add OutputIntent for PDF/UA
var outputIntent = new PdfDictionary();
outputIntent.Put(PdfName.Type, PdfName.OutputIntent);
outputIntent.Put(PdfName.S, new PdfName("GTS_PDFUA1"));
outputIntent.Put(new PdfName("OutputConditionIdentifier"), new PdfString("sRGB"));

var intentsArray = new PdfArray();
intentsArray.Add(outputIntent);
catalogDict.Put(PdfName.OutputIntents, intentsArray);
```

**5. Set Viewer Preferences**
```csharp
var viewerPrefs = new PdfViewerPreferences();
catalog.SetViewerPreferences(viewerPrefs);

// Show document title instead of filename
viewerPrefs.SetDisplayDocTitle(true);

// Show bookmarks if they exist
if (outlines != null)
{
    prefsDict.Put(PdfName.NonFullScreenPageMode, PdfName.UseOutlines);
}
```

---

### FormWidgetNestingFixService

**Location**: `Services/Remediation/Fixes/FormWidgetNestingFixService.cs`
**Purpose**: Fixes 7.18.4-1 violation - Widget annotations must be nested within Form tags
**Target Category**: FormFields
**Priority**: 5 (Critical)

**Key Methods**:

#### `RemediateAsync(byte[] pdfBytes)`
Ensures all form widgets are properly nested in Form structure elements.

**Processing**:

**1. Get All Form Fields**
```csharp
var form = PdfAcroForm.GetAcroForm(pdfDoc, false);
if (form == null || !pdfDoc.IsTagged())
{
    return result;
}

var fields = form.GetAllFormFields();
```

**2. Fix Widget Nesting**
```csharp
foreach (var field in fields)
{
    var widgets = field.Value.GetWidgets();

    foreach (var widget in widgets)
    {
        var widgetObj = widget.GetPdfObject();
        var structParent = widgetObj.GetAsNumber(PdfName.StructParent);

        if (structParent == null)
        {
            // Widget not in structure tree
            var formElement = await FindOrCreateFormElement(rootTag);
            await AddWidgetToFormElement(widget, formElement, pdfDoc);
            fixedCount++;
        }
        else
        {
            // Widget in structure, check if parent is Form
            var parent = await GetStructureParent(widgetObj, rootTag);

            if (parent != null && !IsFormElement(parent))
            {
                // Move widget to Form structure
                var formElement = await FindOrCreateFormElement(rootTag);
                await MoveWidgetToFormElement(widget, parent, formElement, pdfDoc);
                fixedCount++;
            }
        }
    }
}
```

**3. Create Missing Form Elements**
```csharp
// Scan all pages for widgets not in structure tree
for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
{
    var page = pdfDoc.GetPage(i);
    var annotations = page.GetAnnotations();

    foreach (var annot in annotations)
    {
        if (annot.GetSubtype() == PdfName.Widget)
        {
            var widgetObj = annot.GetPdfObject();
            var structParent = widgetObj.GetAsNumber(PdfName.StructParent);

            if (structParent == null)
            {
                // Create structure parent index
                var nextParentIndex = GetNextStructParentIndex(pdfDoc);
                widgetObj.Put(PdfName.StructParent, new PdfNumber(nextParentIndex));

                // Add to parent tree
                var formElement = await FindOrCreateFormElement(rootTag);
                // ... link widget to form element
                fixedCount++;
            }
        }
    }
}
```

**Helper Methods**:

```csharp
private async Task<PdfStructElem> FindOrCreateFormElement(PdfStructElem rootTag)
{
    // Try to find existing Form element
    var formElement = await FindFormElement(rootTag);
    if (formElement != null)
        return formElement;

    // Create new Form structure element
    var doc = rootTag.GetPdfObject().GetIndirectReference()?.GetDocument();
    var formTag = new PdfStructElem(doc, PdfName.Form);
    rootTag.AddKid(formTag);

    // Set proper attributes
    formTag.GetPdfObject().Put(PdfName.S, PdfName.Form);

    return formTag;
}

private bool IsFormElement(PdfStructElem element)
{
    var role = element.GetRole();
    return role != null && (role.Equals(PdfName.Form) ||
                           role.GetValue() == "Form");
}
```

---

### ArtifactTaggedContentFixService

**Location**: `Services/Remediation/Fixes/ArtifactTaggedContentFixService.cs`
**Purpose**: Fixes 7.1 violation - Tagged content shall not be contained in artifacts
**Target Category**: Structure
**Priority**: 8 (High)

This service wraps the Python-based artifact fix script (see ARCHITECTURE-SERVICES-ACCESSIBILITY.md for ArtifactViolationFixService details).

---

### TableScopeAttributeFixService

**Location**: `Services/Remediation/Fixes/TableScopeAttributeFixService.cs`
**Purpose**: Adds Scope attributes to table header cells (TH elements)
**Target Category**: TableAndList
**Priority**: 6 (High)

**Key Methods**:

#### `RemediateAsync(byte[] pdfBytes)`
Adds Scope="Row" or Scope="Column" attributes to TH elements.

**Processing**:
```csharp
// Find all TH (table header) elements in structure tree
var thElements = FindAllThElements(pdfDoc.GetStructTreeRoot());

foreach (var thElement in thElements)
{
    // Determine if this is a row header or column header
    var scope = DetermineScope(thElement);

    // Add Scope attribute
    var attributes = new PdfStructureAttributes("Table");
    attributes.AddAttribute(new PdfStructureAttribute("Scope", scope));
    thElement.AddAttribute(attributes);

    fixedCount++;
}
```

**Scope Detection**:
```csharp
private string DetermineScope(PdfStructElem thElement)
{
    // Heuristic: If TH is in first row, it's a column header
    // If TH is in first column, it's a row header

    var parent = thElement.GetParent();
    if (parent != null && IsTableRow(parent))
    {
        var row = parent;
        var table = row.GetParent();

        // Get position in table
        var rowIndex = GetRowIndex(table, row);
        var colIndex = GetColumnIndex(row, thElement);

        if (rowIndex == 0)
            return "Column";  // First row = column headers
        else if (colIndex == 0)
            return "Row";     // First column = row headers
    }

    return "Column";  // Default to column
}
```

---

### TableStructureValidationService

**Location**: `Services/Remediation/Fixes/TableStructureValidationService.cs`
**Purpose**: Validates and fixes table structure (ensures TR, TD, TH hierarchy)
**Target Category**: TableAndList
**Priority**: 7 (High)

**Key Methods**:

#### `RemediateAsync(byte[] pdfBytes)`
Validates table structure and fixes hierarchy issues.

**Validation Checks**:

1. **Table must contain TR elements**
```csharp
foreach (var table in FindAllTableElements(rootTag))
{
    var children = table.GetKids();
    bool hasTableRow = children.Any(k => k is PdfStructElem elem &&
                                         IsTableRow(elem));

    if (!hasTableRow)
    {
        // Wrap direct TD/TH children in TR
        var tr = new PdfStructElem(pdfDoc, PdfName.TR);
        table.AddKid(tr);

        // Move TD/TH elements under TR
        foreach (var child in children)
        {
            if (IsTableCell(child))
            {
                tr.AddKid(child);
            }
        }

        fixedCount++;
    }
}
```

2. **TR must only contain TD/TH elements**
```csharp
foreach (var tr in FindAllTrElements(rootTag))
{
    var children = tr.GetKids();

    foreach (var child in children)
    {
        if (child is PdfStructElem elem &&
            !IsTableCell(elem))
        {
            // Invalid child - wrap in TD
            var td = new PdfStructElem(pdfDoc, PdfName.TD);
            td.AddKid(elem);
            tr.ReplaceKid(child, td);
            fixedCount++;
        }
    }
}
```

---

### FigureAltTextService

**Location**: `Services/Remediation/Fixes/FigureAltTextService.cs`
**Purpose**: Ensures Figure elements have Alt text
**Target Category**: AlternateText
**Priority**: 7 (High)

**Key Methods**:

#### `RemediateAsync(byte[] pdfBytes)`
Adds Alt attributes to Figure elements.

**Processing**:
```csharp
// Find all Figure elements
var figureElements = FindAllFigureElements(pdfDoc.GetStructTreeRoot());

foreach (var figure in figureElements)
{
    var figureDict = figure.GetPdfObject();

    // Check if Alt attribute exists
    var alt = figureDict.GetAsString(PdfName.Alt);

    if (alt == null || string.IsNullOrWhiteSpace(alt.GetValue()))
    {
        // Add default Alt text
        figureDict.Put(PdfName.Alt, new PdfString("Image"));
        fixedCount++;

        _logger.LogInformation("Added Alt text to Figure element");
    }
}
```

**Enhanced Alt Text** (with AI):
```csharp
// Optional: Use Claude Vision to generate descriptive Alt text
if (_claudeVisionService != null)
{
    var imageBytes = ExtractImageFromFigure(figure);
    var description = await _claudeVisionService.DescribeImageAsync(imageBytes);

    figureDict.Put(PdfName.Alt, new PdfString(description));
}
```

---

## TOC Fix Services

### TocLinkFixService

**Location**: `Services/TocLinkFixService.cs`
**Purpose**: Fixes table of contents link structure and accessibility
**Dependencies**: None (uses iText7)

**Key Methods**:

#### `FixTocLinksAsync(byte[] pdfBytes)`
Fixes TOC links to be PDF/UA compliant.

**Processing**:

1. **Identify TOC Links**
```csharp
// Find all Link annotations that appear to be TOC entries
var tocLinks = new List<PdfLinkAnnotation>();

for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
{
    var page = pdfDoc.GetPage(i);
    var annotations = page.GetAnnotations();

    foreach (var annot in annotations)
    {
        if (annot is PdfLinkAnnotation link && IsTocLink(link))
        {
            tocLinks.Add(link);
        }
    }
}
```

2. **Fix Link Structure**
```csharp
foreach (var link in tocLinks)
{
    // Ensure link has Contents (required for accessibility)
    var linkDict = link.GetPdfObject();
    if (!linkDict.ContainsKey(PdfName.Contents))
    {
        var linkText = ExtractLinkText(link);
        linkDict.Put(PdfName.Contents, new PdfString(linkText));
    }

    // Ensure link is tagged
    if (!IsTaggedLink(link))
    {
        TagLinkAsReference(link, pdfDoc);
    }
}
```

### TocLinkFixServiceEnhanced

**Location**: `Services/TocLinkFixServiceEnhanced.cs`
**Purpose**: Enhanced TOC fix with structure tree manipulation
**Dependencies**: None (uses iText7)

**Additional Features**:
- Creates proper TOCI (Table of Contents Item) structure elements
- Links TOC entries to document bookmarks
- Adds proper heading hierarchy (H1, H2, H3)
- Validates destination references

---

## MCID Content Linking Services (Phase 6)

### Overview

Phase 6K implements the "two-sided MCID story" for PDF/UA compliance:
- **Structure Side (Phase 6K)**: MCR (Marked Content Reference) objects in structure tree
- **Content Side (Phase 6H)**: BDC/EMC markers in PDF content streams

This links PDF structure elements to actual page content via MCIDs (Marked Content Identifiers).

### StructureRebuildService

**Location**: `Services/Remediation/StructureRebuildService.cs`
**Interface**: `IRemediationService`
**Target Category**: Structure
**Priority**: Phase 0 (First service in remediation pipeline)
**Dependencies**:
- LogicalLayoutAnalysisService (AI layout analysis)
- FormFieldEnrichmentService
- StructureTreeBuilder
- StructureTreeCleaner
- ITaggedPdfFinalizer (Phase 6K orchestrator)

**Purpose**: Performs AI-driven PDF structure rebuild with optional MCID linking based on RemediationOptions configuration.

**Key Responsibilities**:
1. Analyze PDF layout using Claude AI to produce LogicalDocument
2. Enrich structure with form field metadata
3. Build clean structure tree from logical blocks
4. Clean and optimize structure tree
5. **Finalize with MCID linking** (if enabled via RemediationOptions.EnableMcidLinking)

**Implementation Details**:

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

**Configuration via RemediationOptions**:
- `EnableMcidLinking` - Enable MCID allocation and MCR creation (default: true)
- `EnableMcidContentRewrite` - Enable external Python rewriter for BDC/EMC markers (default: true)

**Execution Order**: Always runs as Phase 0 (first) when `RemediationPipeline:EnableStructureRebuild = true` in config.

**State Management**:
- Sets `context.StructureRebuildExecuted = true` after completion
- Sets `context.McidContentRewriteExecuted = true` if content rewrite performed
- These flags prevent subsequent services from corrupting MCID markers

---

### TaggedPdfFinalizer

**Location**: `Services/Pdf/TaggedPdfFinalizer.cs`
**Interface**: `ITaggedPdfFinalizer`
**Purpose**: Orchestrates final PDF structure writing with optional MCID integration

**Responsibilities**:
1. Validate structure tree
2. Call IPdfStructureWriter.Rewrite() to write structure + MCIDs
3. Handle errors gracefully

**Key Code Path**:
```csharp
public async Task<byte[]> FinalizeTaggedPdf(
    byte[] originalPdf,
    StructureTree structure,
    RemediationJobContext context)
{
    // Delegate to structure writer (ITextPdfStructureWriter for Phase 6K)
    return await _structureWriter.Rewrite(originalPdf, structure, context);
}
```

**Implementation Selection**:
- Production uses `ITextPdfStructureWriter` (full Phase 6K MCID support)
- Can swap to `SyncfusionPdfStructureWriter` or `StubPdfStructureWriter` via DI

---

### ITextPdfStructureWriter (Phase 6K Implementation)

**Location**: `Services/Pdf/ITextPdfStructureWriter.cs`
**Interface**: `IPdfStructureWriter`
**Purpose**: Writes PDF structure tree with MCR (Marked Content Reference) kids for Phase 6K

**MCID Pipeline** (lines 463-511):

```csharp
public async Task<byte[]> Rewrite(byte[] pdfBytes, StructureTree tree, RemediationJobContext context)
{
    // 1. Check if MCID linking enabled
    if (!context.Options.EnableMcidLinking)
    {
        // Write structure without MCIDs
        return WriteStructureOnly(pdfBytes, tree);
    }

    // 2. Allocate MCIDs in reading order
    AllocateMcids(tree);

    // 3. Create MCR kids in structure elements
    foreach (var node in tree.TraversePreOrder())
    {
        foreach (var mcidRef in node.McidReferences)
        {
            var page = document.GetPage(mcidRef.PageIndex + 1);
            var mcr = new PdfMcrNumber(page, mcidRef.Mcid);
            structElement.AddKid(mcr);
        }
    }

    // 4. Save PDF with MCR kids
    var pdfWithMcrs = SaveDocument();

    // 5. External MCID rewriter (if enabled)
    if (context.Options.EnableMcidContentRewrite)
    {
        var plan = _planBuilder.BuildPlan(tree);
        return await _mcidRewriter.RewriteMcidsAsync(pdfWithMcrs, plan);
    }

    return pdfWithMcrs;
}
```

**MCID Allocation Strategy**:
- Traverses structure tree in reading order
- Assigns sequential MCID numbers (0, 1, 2, ...) per page
- Stores allocations in `StructureNode.McidReferences` list

**MCR Kid Creation**:
- Creates `PdfMcrNumber` objects linking structure to content
- Each MCR references (Page, MCID) pair
- iText7 automatically serializes to PDF structure tree

---

### ExternalMcidRewriterService

**Location**: `Services/Phase6H/ExternalMcidRewriterService.cs`
**Purpose**: HTTP client for Python microservice that inserts BDC/EMC markers in content streams

**Microservice Details**:
- **Endpoint**: `http://localhost:8000/api/mcid-rewrite`
- **Method**: POST
- **Language**: Python 3.10+ with FastAPI
- **PDF Library**: pikepdf
- **Location**: `McidRewriterMicroservice/main.py`

**Request Format**:
```json
{
  "pdfBase64": "<base64-encoded PDF>",
  "plan": {
    "segments": [
      {
        "pageIndex": 0,
        "mcid": 0,
        "bounds": { "x": 100, "y": 200, "width": 400, "height": 50 },
        "role": "P",
        "sequenceIndex": 0
      }
    ]
  }
}
```

**Implementation**:
```csharp
public async Task<byte[]> RewriteMcidsAsync(byte[] pdfBytes, McidRewritePlan plan)
{
    var base64Pdf = Convert.ToBase64String(pdfBytes);
    var request = new { pdfBase64 = base64Pdf, plan };

    var response = await _httpClient.PostAsJsonAsync("/api/mcid-rewrite", request);
    response.EnsureSuccessStatusCode();

    var result = await response.Content.ReadFromJsonAsync<McidRewriteResponse>();
    return Convert.FromBase64String(result.PdfBase64);
}
```

**Error Handling**:
- Logs warnings if microservice unavailable
- Returns original PDF (with MCR kids but no content markers)
- Allows partial MCID support (structure-side only)

**Verification**: Per Phase 6K testing (Erie Route 5 PDF), BDC/EMC markers persist correctly through save/reload cycle.

---

### McidRewritePlanBuilder

**Location**: `Services/Phase6K/McidRewritePlanBuilder.cs`
**Purpose**: Converts StructureTree with McidReferences into McidRewritePlan for Python service

**Responsibilities**:
1. Extract MCID assignments from structure nodes
2. Map to page coordinates and bounding boxes
3. Generate sequential processing order
4. Include structure role information (P, H1, Figure, etc.)

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
    public string Role { get; set; }  // PDF structure role
    public int SequenceIndex { get; set; }
}
```

---

### Guard Clauses for MCID Protection

**Purpose**: Prevent content stream overwrites after BDC/EMC marker insertion

**Implementation Pattern**:
```csharp
public async Task<byte[]> FixAsync(byte[] pdfBytes, RemediationJobContext context)
{
    // Check if MCID content rewrite already executed
    if (context.McidContentRewriteExecuted)
    {
        _logger.LogInformation("Skipping {ServiceName} - MCID content already rewritten",
            GetType().Name);
        return pdfBytes;
    }

    // Safe to proceed with content stream mutations
    return await PerformFix(pdfBytes);
}
```

**Protected Services**:
- `ArtifactTaggedContentFixService` - Only runs in "PreStructureOnly" mode after MCID rewrite
- `WhitespaceTaggingService` - Skips if content rewritten
- Any custom service that modifies content streams

**Guard Locations**:
- `RemediationOrchestrator.cs:263-293` - Prevents cleanup phase after MCID rewrite
- `TaggedPdfFinalizer.cs:62-77` - Checks before finalization
- Individual fix services (as needed)

**Why This Matters**: BDC/EMC markers are inserted directly into PDF content streams. Any subsequent content stream modification could corrupt or remove these markers, breaking the MCID link between structure and content.

---

### RemediationJobContext Integration

**Model**: `Models/Remediation/RemediationJobContext.cs`
**DI Lifetime**: Scoped (one instance per remediation job)

**Key Properties**:
```csharp
public class RemediationJobContext
{
    public RemediationOptions Options { get; set; }
    public StructureRebuildContext StructureContext { get; set; }

    // Convenience accessors
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

**Usage Pattern**:
- Injected into all `IRemediationService` implementations
- Shared across all services in single remediation job
- Stores MCID pipeline state flags
- Provides access to `RemediationOptions`

---

### Configuration

**appsettings.json** (AccessibilityRemediation section):
```json
{
  "AccessibilityRemediation": {
    "EnableMcidLinking": true,
    "EnableMcidContentRewrite": true,
    "AsposeOptimizationMode": "PreStructureOnly",
    "ArtifactFixMode": "PreStructureOnly"
  },
  "RemediationPipeline": {
    "EnableStructureRebuild": true
  }
}
```

**Configuration Flow**:
1. `appsettings.json` → `IOptions<RemediationOptions>` (via DI)
2. `RemediationOrchestrator` merges config into `RemediationOptions`
3. Options stored in `RemediationJobContext`
4. All services read from `context.Options`

---

### Testing

**Test File**: `TestPhase6KFullPipeline.cs`
**Test PDF**: Erie Route 5 (Pennsylvania/Erie/ERA_0051_Route 5 August 2025.pdf)

**Verification Steps**:
1. Run test → produces `erie_phase6k_output.pdf`
2. Check microservice logs: Should report "Added X BDC, Y EMC markers"
3. Hexdump output PDF: `xxd erie_phase6k_output.pdf | grep -A2 -B2 "/MCID"`
4. Open in Adobe Acrobat: Check Reading Order panel for MCID-linked structure

**Expected Results** (from CLAUDE.md verification):
- ✅ Python microservice inserts BDC/EMC markers
- ✅ Markers persist through save/reload cycle
- ✅ No PDF corruption
- ✅ Structure tree shows MCR kids in tag inspector

---

### Troubleshooting

**Issue**: BDC/EMC markers not found in output
- Check microservice is running: `curl http://localhost:8000/health`
- Verify `EnableMcidContentRewrite = true` in config
- Check logs for ExternalMcidRewriterService warnings

**Issue**: Structure rebuild runs but no MCIDs
- Verify `EnableMcidLinking = true`
- Check `RemediationPipeline:EnableStructureRebuild = true`
- Ensure StructureRebuildServiceAdapter registered in DI

**Issue**: Content overwrites MCID markers
- Check guard clauses in cleanup services
- Verify `context.McidContentRewriteExecuted` flag set
- Review service execution order

---

## Service Adapters

Service adapters wrap existing services to implement the IRemediationService interface.

### WhitespaceServiceAdapter

**Location**: `Services/Remediation/Adapters/WhitespaceServiceAdapter.cs`
**Wraps**: TaggedWhitespaceFixService
**Target Category**: Whitespace

### ContentServiceAdapter

**Location**: `Services/Remediation/Adapters/ContentServiceAdapter.cs`
**Wraps**: ArtifactViolationFixService
**Target Category**: Content

### LinkServiceAdapter

**Location**: `Services/Remediation/Adapters/LinkServiceAdapter.cs`
**Wraps**: TocLinkFixServiceEnhanced
**Target Category**: Links

### FontEmbeddingServiceAdapter

**Location**: `Services/Remediation/Adapters/FontEmbeddingServiceAdapter.cs`
**Wraps**: PassportPdfService
**Target Category**: Fonts

### GptServiceAdapter

**Location**: `Services/Remediation/Adapters/GptServiceAdapter.cs`
**Wraps**: GptRemediationService
**Target Category**: Unknown (fallback for all categories)

---

## Supporting Services

### ProgressTracker

**Location**: `Services/Remediation/Tracking/ProgressTracker.cs`
**Purpose**: Tracks progress across remediation iterations

**Tracks**:
- Violations fixed per iteration
- Compliance score progression
- Service execution times
- Success/failure patterns

### ExitConditionEvaluator

**Location**: `Services/Remediation/Decision/ExitConditionEvaluator.cs`
**Purpose**: Determines when to exit remediation loop

**Exit Reasons**:
```csharp
public enum ExitReason
{
    Compliant,          // Zero violations
    MaxIterations,      // Iteration limit reached
    MaxDuration,        // Time limit exceeded
    NoProgress,         // Stuck (no violations fixed)
    FatalError          // Unrecoverable error
}
```

### RemediationReporter

**Location**: `Services/Remediation/Reporting/RemediationReporter.cs`
**Purpose**: Generates comprehensive remediation reports

**Report Contents**:
- Initial vs. final violation counts
- Violations fixed by category
- Execution time per phase
- Success/failure details
- Recommendations for remaining violations

### SolutionCache

**Location**: `Services/Remediation/AI/SolutionCache.cs`
**Purpose**: Caches successful GPT solutions for reuse

**Cache Structure**:
```csharp
public class CachedSolution
{
    public string ViolationDescription { get; set; }
    public string Category { get; set; }
    public string Solution { get; set; }  // Python script or JSON
    public SolutionType Type { get; set; }
    public DateTime CachedAt { get; set; }
    public int TimesUsed { get; set; }
}
```

### ScriptExecutor

**Location**: `Services/Remediation/AI/ScriptExecutor.cs`
**Purpose**: Executes Python scripts in isolated environment

**Key Methods**:

#### `ExecutePythonScriptAsync(string script, byte[] pdfBytes, string scriptId)`
Executes Python script with INPUT_PDF and OUTPUT_PDF paths.

**Execution Environment**:
```bash
# Install dependencies (first run only)
pip install pikepdf PyPDF2 reportlab pypdf

# Execute script
python3 /tmp/script_{scriptId}.py
```

**Script Template**:
```python
import pikepdf

# INPUT_PDF and OUTPUT_PDF are injected by ScriptExecutor
pdf = pikepdf.open(INPUT_PDF)

# ... remediation logic ...

pdf.save(OUTPUT_PDF)
```

---

**End of ARCHITECTURE-SERVICES-REMEDIATION.md**

**See also:**
- **ARCHITECTURE-SERVICES-CORE.md** - Core pipeline & AI services
- **ARCHITECTURE-SERVICES-ACCESSIBILITY.md** - Accessibility & utility services
- **ARCHITECTURE.md** - Main processing paths
