# AccessForm Accessibility & Utility Service Architecture

**Last Updated:** 2025-10-26

## Overview

This document details accessibility enhancement services, font management, and utility services that support the core AccessForm functionality.

**Navigation:**
- [ARCHITECTURE-SERVICES-CORE.md](ARCHITECTURE-SERVICES-CORE.md) - Core pipeline & AI services
- [ARCHITECTURE-SERVICES-REMEDIATION.md](ARCHITECTURE-SERVICES-REMEDIATION.md) - PDF/UA remediation services
- [ARCHITECTURE.md](ARCHITECTURE.md) - Main processing paths
- [ARCHITECTURE-API.md](ARCHITECTURE-API.md) - API endpoints

---

## Accessibility Services

Services focused on PDF/UA compliance and accessibility enhancements.

### AccessibilityService

**Location**: `Services/AccessibilityService.cs`
**Purpose**: Basic accessibility enhancements (metadata, tooltips, tab order)
**Dependencies**: None (uses Syncfusion)

**Key Methods**:

#### `MakeAccessible(PdfLoadedDocument document, string fileName)`
Applies basic accessibility enhancements to a loaded PDF.

**Operations**:

**1. Set Document Metadata**
```csharp
var docInfo = document.DocumentInformation;
docInfo.Title = Path.GetFileNameWithoutExtension(fileName);
docInfo.Language = "en-US";
docInfo.Subject = "Accessible Government Form";
docInfo.Keywords = "accessible, PDF/UA, WCAG 2.1 AA, Section 508";

// Custom metadata for compliance tracking
docInfo.CustomMetadata["Accessibility_Standard"] = "PDF/UA-1";
docInfo.CustomMetadata["WCAG_Level"] = "AA";
docInfo.CustomMetadata["Section508"] = "Compliant";
docInfo.CustomMetadata["ProcessedDate"] = DateTime.UtcNow.ToString("O");
```

**2. Enhance Form Fields**
```csharp
if (document.Form != null)
{
    int tabIndex = 1;
    foreach (var field in document.Form.Fields.Cast<PdfLoadedField>())
    {
        // Set tooltip if missing (required for PDF/UA)
        if (string.IsNullOrEmpty(field.ToolTip))
        {
            field.ToolTip = GenerateTooltipFromFieldName(field.Name);
        }

        // Set tab order
        field.TabIndex = tabIndex++;

        // Normalize checkbox sizes
        if (field is PdfLoadedCheckBoxField checkbox)
        {
            NormalizeCheckboxSize(checkbox);
        }
    }
}
```

**3. Normalize Checkbox Sizes**
```csharp
private void NormalizeCheckboxSize(PdfLoadedCheckBoxField checkbox)
{
    const float STANDARD_SIZE = 13.8f;  // Points (approximately 0.19 inches)

    var bounds = checkbox.Bounds;
    if (bounds.Width != STANDARD_SIZE || bounds.Height != STANDARD_SIZE)
    {
        // Center the resized checkbox in original bounds
        float centerX = bounds.X + bounds.Width / 2;
        float centerY = bounds.Y + bounds.Height / 2;

        checkbox.Bounds = new RectangleF(
            centerX - STANDARD_SIZE / 2,
            centerY - STANDARD_SIZE / 2,
            STANDARD_SIZE,
            STANDARD_SIZE
        );
    }
}
```

**Why 13.8 points?**: This is approximately 0.19 inches, a standard checkbox size that renders well at all zoom levels and meets Section 508 requirements for minimum target size.

---

### AccessibilityRetrofitService

**Location**: `Services/AccessibilityRetrofitService.cs`
**Purpose**: Algorithmic field pattern detection and smart labeling
**Dependencies**: None (pattern matching only)

**Key Methods**:

#### `RetrofitAccessibility(PdfLoadedDocument document)`
Analyzes form fields and applies pattern-based enhancements.

**Pattern Detection**:
```csharp
private static readonly Dictionary<string, Regex> PATTERNS = new()
{
    ["date"] = new Regex(@"(date|fecha|mm|dd|yyyy|^\d{1,2}[-/]\d{1,2})", RegexOptions.IgnoreCase),
    ["time"] = new Regex(@"(time|hora|am|pm|:00|start|end)", RegexOptions.IgnoreCase),
    ["phone"] = new Regex(@"(phone|tel|cell|mobile|fax|\d{3}[-.]?\d{3})", RegexOptions.IgnoreCase),
    ["email"] = new Regex(@"(email|e-mail|@|mail)", RegexOptions.IgnoreCase),
    ["ssn"] = new Regex(@"(ssn|social|security|xxx-xx-xxxx)", RegexOptions.IgnoreCase),
    ["ein"] = new Regex(@"(ein|employer|tax|federal|id|\d{2}-\d{7})", RegexOptions.IgnoreCase),
    ["address"] = new Regex(@"(address|street|city|state|zip)", RegexOptions.IgnoreCase),
    ["name"] = new Regex(@"(name|nombre|first|last|middle)", RegexOptions.IgnoreCase),
    ["amount"] = new Regex(@"(amount|total|sum|price|cost|\$)", RegexOptions.IgnoreCase)
};
```

**Pattern Application**:
```csharp
foreach (var field in document.Form.Fields)
{
    foreach (var pattern in PATTERNS)
    {
        if (pattern.Value.IsMatch(field.Name))
        {
            ApplyPatternEnhancements(field, pattern.Key);
            break;
        }
    }
}

private void ApplyPatternEnhancements(PdfLoadedField field, string patternType)
{
    switch (patternType)
    {
        case "date":
            field.ToolTip = $"Enter date (MM/DD/YYYY): {field.Name}";
            // Could add format validation JavaScript here
            break;
        case "phone":
            field.ToolTip = $"Enter phone number (###-###-####): {field.Name}";
            break;
        case "ssn":
            field.ToolTip = $"Enter Social Security Number (###-##-####): {field.Name}";
            // Mark as sensitive data
            break;
        // ... other patterns
    }
}
```

**Bookmark Generation**:
```csharp
private void GenerateBookmarksFromContent(PdfLoadedDocument document)
{
    var bookmarks = document.Bookmarks;

    // Create section bookmarks based on content analysis
    // Example: "Section 1: Personal Information", "Section 2: Employment", etc.

    for (int i = 0; i < document.Pages.Count; i++)
    {
        bookmarks.Add($"Page {i + 1}").Destination = new PdfDestination(document.Pages[i]);
    }
}
```

**Use Case**: Provides intelligent field enhancements without requiring AI, useful for batch processing.

---

### PdfAccessibilityEnhancer

**Location**: `Services/PdfAccessibilityEnhancer.cs`
**Purpose**: PDF/UA compliance enhancements and structure tagging
**Dependencies**: None (uses Syncfusion)

**Key Methods**:

#### `EnhanceAccessibility(PdfLoadedDocument loadedPdf, string fileName)`
Applies final accessibility enhancements before saving.

**Operations**:

**1. Ensure Structure Root**
```csharp
var rootElement = loadedPdf.StructureElement;
if (rootElement == null)
{
    rootElement = new PdfStructureElement(PdfTagType.Document);
    loadedPdf.StructureElement = rootElement;
}
```

**2. Tag Form Fields**
```csharp
if (loadedPdf.Form != null)
{
    foreach (var field in loadedPdf.Form.Fields.Cast<PdfLoadedField>())
    {
        // Create structure element for field
        var fieldElement = new PdfStructureElement(PdfTagType.Form);
        fieldElement.Title = field.ToolTip ?? field.Name;

        // Link field widget to structure
        var widget = field.Page.Annotations
            .OfType<PdfLoadedFormFieldAnnotation>()
            .FirstOrDefault(a => a.Name == field.Name);

        if (widget != null)
        {
            fieldElement.Attributes.Add(new PdfStructureAttribute(PdfAttributeKeys.FieldType,
                GetFieldTypeString(field)));
        }

        rootElement.AppendChild(fieldElement);
    }
}
```

**3. Verify Metadata**
```csharp
// Ensure all required PDF/UA metadata is present
EnsureMetadata(loadedPdf.DocumentInformation, fileName);
```

---

### ArtifactViolationFixService

**Location**: `Services/ArtifactViolationFixService.cs`
**Purpose**: Fixes PDF/UA violations using Python/PyMuPDF script
**Dependencies**: Python3, PyMuPDF (fitz), `fix_artifact_violations.py`

**Implementation**: **PYTHON-BASED** - Uses external Python script for content stream manipulation.

**Key Methods**:

#### `FixArtifactViolationsAsync(byte[] pdfBytes)`
Executes Python script to fix violations in PDF content streams.

**Returns**: `FixResult` with:
- `Success` - bool
- `ViolationsFound` - int (tagged content inside artifacts)
- `ViolationsFixed` - int (total fixes including whitespace cleanup)
- `FixedPdf` - byte[] (if fixes applied)
- `ErrorMessage` - string (if failed)

**Processing Flow**:
```csharp
1. Save PDF to temp file
2. Execute: python3 fix_artifact_violations.py input.pdf
3. Parse JSON output
4. CRITICAL: Check violationsFixed (not violationsFound) to determine if fixes were made
5. Read fixed PDF from output path
6. Return fixed bytes
```

**Important Bug Fix (2025-01-10)**:
```csharp
// BEFORE (BUG):
if (violationsFound == 0)
    return new FixResult { FixedPdf = pdfBytes };  // Returns original!

// AFTER (FIXED):
if (violationsFixed == 0)
    return new FixResult { FixedPdf = pdfBytes };  // Correct check
```

**Reason**: Python script reports:
- `violations_found`: Count of tagged content inside `/Artifact BMC...EMC` blocks
- `violations_fixed`: Total fixes (includes whitespace cleanup which doesn't count as "violations found")

**Python Script** (`fix_artifact_violations.py`):

**Three-Pass Processing**:

1. **Artifact Unwrapping** (Lines 64-168):
   - Finds `/Artifact BMC...EMC` blocks containing `/MCID` (tagged content)
   - Removes artifact wrapper, preserving tagged content

2. **Whitespace Cleanup** (Lines 170-207):
   - Pattern: `\((\\(40|11|12|15|n|r|t)|\s)*\)\s*Tj`
   - Matches: `(\40) Tj` (octal space), `( ) Tj`, `(\n) Tj`, etc.
   - Checks if outside all marked content (depth = 0)
   - Deletes untagged whitespace entirely

3. **Content Stream Update**:
   - Uses `doc.update_stream(xref, new_stream)` to apply changes
   - Saves with compression: `garbage=4, deflate=True, clean=True`

**Example Fixes**:

**Fix Type 1: Unwrap Tagged Content from Artifacts**
```pdf
# BEFORE (VIOLATION)
/Artifact BMC
  /P <</MCID 5>> BDC
    BT (Hello) Tj ET
  EMC
EMC

# AFTER (FIXED)
/P <</MCID 5>> BDC
  BT (Hello) Tj ET
EMC
```

**Fix Type 2: Remove Untagged Whitespace**
```pdf
# BEFORE (VIOLATION)
(\040) Tj  # Space outside any marked content

# AFTER (FIXED)
# [deleted]
```

---

### TaggedWhitespaceFixService

**Location**: `Services/TaggedWhitespaceFixService.cs`
**Purpose**: Removes tagging from whitespace-only elements
**Dependencies**: iText7

**Key Methods**:

#### `FixTaggedWhitespaceAsync(byte[] pdfBytes)`
Detects and unmarks whitespace-only tagged content.

**Whitespace Detection Patterns**:
```csharp
private static readonly Regex WHITESPACE_PATTERNS = new Regex(
    @"^[\s\n\r\t\040\012\015]+$",  // ASCII + Octal encodings
    RegexOptions.Compiled
);

// Octal codes:
// \040 = space
// \012 = newline (LF)
// \015 = carriage return (CR)
```

**Processing**:
```csharp
void ScanForWhitespace(PdfStructElem elem)
{
    var content = GetElementContent(elem);

    // Check if content is only whitespace
    if (WHITESPACE_PATTERNS.IsMatch(content))
    {
        violationsFound++;

        // Fix: Mark as artifact (not real content)
        elem.SetRole(PdfName.Artifact);
        violationsFixed++;

        _logger.LogInformation($"Fixed whitespace element: '{EscapeWhitespace(content)}'");
    }

    // Recurse
    foreach (var child in elem.GetKids())
        ScanForWhitespace(child);
}
```

**Example Fix**:
```xml
<!-- BEFORE (VIOLATION) -->
<P>   </P>  <!-- Tagged space -->
<P>\040\040</P>  <!-- Tagged octal spaces -->

<!-- AFTER (FIXED) -->
<Artifact>   </Artifact>  <!-- Now marked as artifact -->
<Artifact>\040\040</Artifact>
```

---

### PdfUAComplianceService

**Location**: `Services/PdfUAComplianceService.cs`
**Purpose**: PDF/UA validation and compliance reporting
**Dependencies**: Syncfusion (uses built-in PDF/UA validator)

**Key Methods**:

#### `ValidateComplianceAsync(byte[] pdfBytes)`
Validates PDF against PDF/UA-1 standard.

**Returns**: `ComplianceReport` with:
- `IsCompliant` - bool
- `Violations` - List<ComplianceViolation>
- `Warnings` - List<string>
- `Summary` - string

**Validation Checks**:
```csharp
var report = new ComplianceReport();

using var doc = new PdfLoadedDocument(new MemoryStream(pdfBytes));

// 1. Check document metadata
if (string.IsNullOrEmpty(doc.DocumentInformation.Title))
    report.Violations.Add(new ComplianceViolation("Missing document title"));

if (string.IsNullOrEmpty(doc.DocumentInformation.Language))
    report.Violations.Add(new ComplianceViolation("Missing document language"));

// 2. Check structure tree
if (doc.StructureElement == null)
    report.Violations.Add(new ComplianceViolation("Missing structure tree"));

// 3. Check form fields
if (doc.Form != null)
{
    foreach (var field in doc.Form.Fields)
    {
        if (string.IsNullOrEmpty(field.ToolTip))
            report.Violations.Add(new ComplianceViolation(
                $"Field '{field.Name}' missing tooltip"));
    }
}

// 4. Check fonts
foreach (var page in doc.Pages)
{
    var fonts = GetFontsUsedOnPage(page);
    foreach (var font in fonts)
    {
        if (!font.IsEmbedded)
            report.Violations.Add(new ComplianceViolation(
                $"Font '{font.Name}' not embedded on page {page.PageIndex + 1}"));
    }
}

report.IsCompliant = report.Violations.Count == 0;
return report;
```

---

## Font & Optimization Services

### AsposePdfService

**Location**: `Services/AsposePdfService.cs`
**Purpose**: Font embedding and PDF optimization using Aspose.Pdf library
**Dependencies**: TwcFontComplianceService, TableLinkAccessibilityService

**Status**: ⚠️ Partially disabled due to missing Liberation fonts

**Key Methods**:

#### `OptimizePdfAsync(byte[] pdfBytes)`
Embeds fonts and optimizes PDF structure.

**Processing**:
```csharp
using var document = new Aspose.Pdf.Document(new MemoryStream(pdfBytes));

// 1. Check existing form fields
int formFieldsBefore = document.Form?.Fields?.Length ?? 0;
_logger.LogInformation($"Form fields before optimization: {formFieldsBefore}");

// 2. Embed all fonts
EmbedFonts(document);

// 3. Optimize resources (CAREFUL - can destroy form fields!)
var options = new Aspose.Pdf.Optimization.OptimizationOptions
{
    RemoveUnusedObjects = false,   // CRITICAL: Keep form objects
    RemoveUnusedStreams = false,   // CRITICAL: Keep form streams
    UnembedFonts = false,          // Don't unembed fonts
    SubsetFonts = false,           // Don't subset fonts
    CompressImages = false         // Don't compress (can fail)
};

document.OptimizeResources(options);

// 4. Verify form fields preserved
int formFieldsAfter = document.Form?.Fields?.Length ?? 0;
if (formFieldsAfter != formFieldsBefore)
{
    _logger.LogError($"Form fields destroyed! Before: {formFieldsBefore}, After: {formFieldsAfter}");
    throw new Exception("Form fields were destroyed during optimization");
}

// 5. Save
using var outputStream = new MemoryStream();
document.Save(outputStream);
return outputStream.ToArray();
```

#### `EmbedFonts(Document document)`
Embeds all fonts in the document.

**Font Embedding Logic**:
```csharp
private void EmbedFonts(Document document)
{
    var fontsEmbedded = 0;

    foreach (var page in document.Pages)
    {
        foreach (var resource in page.Resources.Fonts)
        {
            var font = resource.Value;

            if (!font.IsEmbedded)
            {
                try
                {
                    // Attempt to embed font
                    font.IsEmbedded = true;
                    fontsEmbedded++;

                    _logger.LogInformation($"Embedded font: {font.FontName}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not embed font {font.FontName}: {ex.Message}");
                }
            }
        }
    }

    _logger.LogInformation($"Total fonts embedded: {fontsEmbedded}");
}
```

**Known Issues**:
- ⚠️ Cannot embed Liberation fonts (files not present)
- ⚠️ OptimizeResources() can destroy form fields if settings wrong
- ⚠️ Some fonts may fail to embed (licensing issues)

---

### TwcFontComplianceService

**Location**: `Services/TwcFontComplianceService.cs`
**Purpose**: Replace Times-Roman/Arial with Liberation fonts for TWC compliance
**Status**: ❌ DISABLED - Liberation font files not available

**Intended Functionality**:
```csharp
public async Task<byte[]> ReplaceWithLiberationFonts(byte[] pdfBytes)
{
    using var document = new Aspose.Pdf.Document(new MemoryStream(pdfBytes));

    var liberationSans = LoadFont("/Fonts/LiberationSans-Regular.ttf");
    var liberationSerif = LoadFont("/Fonts/LiberationSerif-Regular.ttf");

    foreach (var page in document.Pages)
    {
        foreach (var font in page.Resources.Fonts)
        {
            if (font.Value.FontName.Contains("Times") ||
                font.Value.FontName.Contains("TimesNewRoman"))
            {
                // Replace with Liberation Serif
                ReplaceFont(page, font.Value, liberationSerif);
            }
            else if (font.Value.FontName.Contains("Arial") ||
                     font.Value.FontName.Contains("Helvetica"))
            {
                // Replace with Liberation Sans
                ReplaceFont(page, font.Value, liberationSans);
            }
        }
    }

    return SaveDocument(document);
}
```

**To Enable**:
1. Download Liberation fonts from https://github.com/liberationfonts/liberation-fonts
2. Place in `/Fonts` directory:
   - LiberationSans-Regular.ttf
   - LiberationSans-Bold.ttf
   - LiberationSerif-Regular.ttf
   - LiberationSerif-Bold.ttf
3. Update TwcFontComplianceService to use bundled fonts
4. Re-enable in AsposePdfService

---

## Utility Services

### DebugCacheService

**Location**: `Services/DebugCacheService.cs`
**Purpose**: Cache AI responses for debugging and analysis
**Dependencies**: None (in-memory caching)

**Key Methods**:

#### `CacheAiResponse(string debugId, AiDebugData data)`
Stores AI response data with a unique ID.

**Cached Data Structure**:
```csharp
public class AiDebugData
{
    public string RequestId { get; set; }
    public DateTime Timestamp { get; set; }
    public string Service { get; set; }  // "ClaudeVision", "Google", etc.
    public string Prompt { get; set; }
    public string Response { get; set; }
    public List<byte[]>? Images { get; set; }  // Base64 images sent to AI
    public int TokensUsed { get; set; }
    public decimal Cost { get; set; }
    public TimeSpan Duration { get; set; }
}
```

**Storage**:
```csharp
private static readonly ConcurrentDictionary<string, AiDebugData> _cache = new();

public void CacheAiResponse(string debugId, AiDebugData data)
{
    _cache.TryAdd(debugId, data);

    // Limit cache size
    if (_cache.Count > 100)
    {
        var oldest = _cache.OrderBy(kvp => kvp.Value.Timestamp).First();
        _cache.TryRemove(oldest.Key, out _);
    }
}
```

#### `GetCachedResponse(string debugId)`
Retrieves cached AI response.

**Access via API**: `/api/debug/{debugId}` endpoint

---

### CostTrackingService

**Location**: `Services/CostTrackingService.cs`
**Purpose**: Track AI API usage costs
**Dependencies**: None (singleton service)

**Key Methods**:

#### `TrackCost(string service, int tokens, decimal cost)`
Records API usage.

**Cost Structure**:
```csharp
public class ApiUsage
{
    public string Service { get; set; }
    public int TokensUsed { get; set; }
    public decimal Cost { get; set; }
    public DateTime Timestamp { get; set; }
}

private readonly List<ApiUsage> _usageLog = new();
```

#### `GetTotalCost(TimeSpan? period = null)`
Gets total costs for specified period.

```csharp
public decimal GetTotalCost(TimeSpan? period = null)
{
    var cutoff = period.HasValue
        ? DateTime.UtcNow - period.Value
        : DateTime.MinValue;

    return _usageLog
        .Where(u => u.Timestamp >= cutoff)
        .Sum(u => u.Cost);
}
```

**Pricing** (as of 2025-10-26):
```csharp
private static readonly Dictionary<string, decimal> TOKEN_COSTS = new()
{
    ["claude-3-5-sonnet"] = 0.000003m,     // $3 per 1M tokens
    ["claude-3-opus"] = 0.000015m,         // $15 per 1M tokens
    ["gpt-4-turbo"] = 0.00001m,            // $10 per 1M tokens
    ["gpt-5"] = 0.00002m,                  // $20 per 1M tokens (estimated)
    ["google-documentai"] = 0.0015m        // $1.50 per 1,000 pages
};
```

**Access via API**: `/api/health` endpoint includes cost tracking

---

### SimpleFileLoggerProvider

**Location**: `Services/SimpleFileLoggerProvider.cs`
**Purpose**: File-based logging for debugging
**Configuration**: Logs to `/Logs/accessform.log`

**Implementation**:
```csharp
public class SimpleFileLoggerProvider : ILoggerProvider
{
    private readonly string _logFilePath;
    private readonly object _lock = new();

    public SimpleFileLoggerProvider(string logFilePath)
    {
        _logFilePath = logFilePath;

        // Ensure directory exists
        var directory = Path.GetDirectoryName(logFilePath);
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new SimpleFileLogger(categoryName, _logFilePath, _lock);
    }
}

private class SimpleFileLogger : ILogger
{
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                            Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [{logLevel}] {formatter(state, exception)}";

        if (exception != null)
            message += $"\n{exception}";

        lock (_lock)
        {
            File.AppendAllText(_logFilePath, message + "\n");
        }
    }
}
```

**Log Format**:
```
[2025-10-26 15:30:45] [Information] Processing form.docx with PassportPDF
[2025-10-26 15:30:46] [Information] Field detection started: Syncfusion=true, Claude=true
[2025-10-26 15:31:20] [Information] Claude Vision detected 25 fields
[2025-10-26 15:31:21] [Information] Merged results: 27 total fields
```

---

### CircularRoleMappingFixService

**Location**: `Services/Remediation/CircularRoleMappingFixService.cs`
**Purpose**: Fixes circular role mapping definitions in PDF structure tree
**Target Category**: Structure
**Priority**: 10 (Critical)

**Key Methods**:

#### `RemediateAsync(byte[] pdfBytes)`
Detects and fixes circular role mappings.

**Circular Role Mapping Example**:
```xml
<!-- VIOLATION: Circular definition -->
<RoleMap>
  <Sect>Section</Sect>
  <Section>Sect</Section>
</RoleMap>

<!-- FIXED: One direction only -->
<RoleMap>
  <Section>Sect</Section>
</RoleMap>
```

**Detection Logic**:
```csharp
private bool IsCircular(string role1, string role2, Dictionary<string, string> roleMap)
{
    // Check if role1 maps to role2 AND role2 maps to role1
    if (roleMap.TryGetValue(role1, out var mapped1) &&
        roleMap.TryGetValue(role2, out var mapped2))
    {
        return mapped1 == role2 && mapped2 == role1;
    }
    return false;
}
```

**Fix Strategy**:
```csharp
foreach (var mapping in roleMappings)
{
    if (IsCircular(mapping.Key, mapping.Value, roleMappings))
    {
        // Remove one direction of the circular mapping
        // Keep standard roles, remove custom roles
        if (IsStandardRole(mapping.Value))
        {
            roleMappings.Remove(mapping.Key);
        }
        else
        {
            roleMappings.Remove(mapping.Value);
        }

        fixedCount++;
    }
}
```

---

### ProcessingProgressService

**Location**: `Services/ProcessingProgressService.cs`
**Purpose**: Real-time progress tracking for long-running operations
**Dependencies**: None (in-memory singleton)

**Key Methods**:

#### `CreateSession(string sessionId)`
Creates a new progress tracking session.

```csharp
public class ProgressSession
{
    public string SessionId { get; set; }
    public string Status { get; set; }
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }
    public string CurrentOperation { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
}
```

#### `UpdateProgress(string sessionId, int currentStep, string operation)`
Updates progress for a session.

```csharp
public void UpdateProgress(string sessionId, int currentStep, string operation)
{
    var session = GetSession(sessionId);
    if (session != null)
    {
        session.CurrentStep = currentStep;
        session.CurrentOperation = operation;

        // Broadcast to connected clients via SignalR
        _hubContext.Clients.Group(sessionId).SendAsync("ProgressUpdate", session);
    }
}
```

#### `UpdateRemediationProgress(string sessionId, int iteration, int totalViolations, int fixedViolations, string currentPhase)`
Specialized method for remediation progress.

**Used by**: RemediationOrchestrator for real-time progress updates in the UI

**Progress Data**:
```csharp
{
    "sessionId": "rem-12345",
    "iteration": 3,
    "totalIterations": 10,
    "totalViolations": 45,
    "fixedViolations": 32,
    "remainingViolations": 13,
    "currentPhase": "Form Field Remediation",
    "complianceScore": 71.1,
    "elapsedTime": "00:02:15"
}
```

---

## Performance Optimization Services

### PdfCompressionService

**Location**: `Services/PdfCompressionService.cs`
**Purpose**: Compress PDFs without losing form fields or accessibility
**Dependencies**: iText7

**Key Methods**:

#### `CompressPdfAsync(byte[] pdfBytes, CompressionOptions options)`
Compresses PDF while preserving all functionality.

**Compression Options**:
```csharp
public class CompressionOptions
{
    public bool CompressImages { get; set; } = true;
    public int ImageQuality { get; set; } = 85;  // 0-100
    public bool RemoveUnusedObjects { get; set; } = true;
    public bool CompressStreams { get; set; } = true;
    public bool PreserveFormFields { get; set; } = true;  // CRITICAL
}
```

**Compression Techniques**:
1. Image compression (JPEG quality reduction)
2. Stream compression (Flate encoding)
3. Object deduplication
4. Font subsetting (careful with form fields!)
5. Metadata cleanup

**Safety Checks**:
```csharp
// Verify form fields preserved
var fieldsBefore = GetFormFieldCount(originalPdf);
var fieldsAfter = GetFormFieldCount(compressedPdf);

if (fieldsBefore != fieldsAfter)
{
    _logger.LogWarning("Form fields lost during compression - reverting");
    return originalPdf;  // Return original if fields lost
}
```

---

**End of ARCHITECTURE-SERVICES-ACCESSIBILITY.md**

**See also:**
- **ARCHITECTURE-SERVICES-CORE.md** - Core pipeline & AI services
- **ARCHITECTURE-SERVICES-REMEDIATION.md** - PDF/UA remediation services
- **ARCHITECTURE.md** - Main processing paths
- **ARCHITECTURE-API.md** - API endpoints
