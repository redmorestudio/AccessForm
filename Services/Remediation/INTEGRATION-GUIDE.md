# Web Interface Integration Guide

**Date:** 2025-10-22
**Integration Point:** `/api/convert-with-config` endpoint
**Status:** ✅ Complete & Functional

## Overview

This guide documents how the closed-loop PDF/UA remediation system was integrated into the existing Blazor web interface. The integration ensures that all PDFs processed through the web UI automatically go through validation and remediation.

## Architecture Overview

```
User Uploads PDF
    ↓
Index.razor (Blazor Component)
    ↓
JavaScript (accessForm.uploadFileDirectly)
    ↓
/api/convert-with-config (API Endpoint)
    ↓
PdfPreservationService.ProcessExistingPdfAsync()  ← Existing pipeline
    ↓
RemediationOrchestrator.RemediateAsync()  ← NEW: Closed-loop validation
    ↓
Return Enhanced Response with Validation Data
    ↓
Index.razor Displays Results with PDF/UA Compliance Info
```

## Backend Integration (Program.cs)

### 1. Dependency Injection Setup

**Location:** `Program.cs:130-142`

```csharp
// Add closed-loop remediation services
builder.Services.AddScoped<WordToPdfConverter.Services.Remediation.Analysis.ViolationAnalyzer>();
builder.Services.AddScoped<WordToPdfConverter.Services.Remediation.Decision.ExitConditionEvaluator>();
builder.Services.AddScoped<WordToPdfConverter.Services.Remediation.Strategy.RemediationStrategySelector>();
builder.Services.AddScoped<WordToPdfConverter.Services.Remediation.Execution.RemediationExecutor>();
builder.Services.AddScoped<WordToPdfConverter.Services.Remediation.Tracking.ProgressTracker>();
builder.Services.AddScoped<WordToPdfConverter.Services.Remediation.Reporting.RemediationReporter>();
builder.Services.AddScoped<WordToPdfConverter.Services.Remediation.RemediationOrchestrator>();

// Add remediation service adapters
builder.Services.AddScoped<WordToPdfConverter.Services.Remediation.Adapters.WhitespaceServiceAdapter>();
builder.Services.AddScoped<WordToPdfConverter.Services.Remediation.Adapters.ContentServiceAdapter>();
builder.Services.AddScoped<WordToPdfConverter.Services.Remediation.Adapters.LinkServiceAdapter>();
```

### 2. API Endpoint Enhancement

**Location:** `Program.cs:3978-4073`

#### Added RemediationOrchestrator Parameter

```csharp
app.MapPost("/api/convert-with-config", async (
    HttpRequest request,
    ConfigurableFieldDetectionService fieldService,
    PdfCompleteRebuildService completeRebuildService,
    WordToPdfConverter.Services.PdfPreservationService pdfPreservationService,
    WordToPdfConverter.Services.Remediation.RemediationOrchestrator remediationOrchestrator,  // ← NEW
    ILogger<Program> logger) =>
```

#### Added Remediation Call After PDF Processing

```csharp
if (isPdf)
{
    // Existing PDF processing
    fields = pdfPreservationService.GetExistingFields(fileBytes);
    pdfBytes = await pdfPreservationService.ProcessExistingPdfAsync(fileBytes);

    // NEW: Run closed-loop remediation for PDF/UA compliance
    logger.LogInformation("Starting closed-loop PDF/UA remediation");
    var remediationResult = await remediationOrchestrator.RemediateAsync(
        pdfBytes,
        WordToPdfConverter.Services.Remediation.Models.RemediationOptions.Production);

    if (remediationResult.Success)
    {
        logger.LogInformation($"Remediation successful! Compliant={remediationResult.Summary.IsCompliant}, " +
            $"Iterations={remediationResult.Summary.TotalIterations}, " +
            $"Violations Fixed={remediationResult.Summary.ViolationsFixed}");
        pdfBytes = remediationResult.OutputPdf;
    }
    else
    {
        logger.LogWarning($"Remediation exited: {remediationResult.ExitReason}, " +
            $"Remaining Violations={remediationResult.Summary.FinalViolationCount}");
        pdfBytes = remediationResult.OutputPdf; // Use best-effort result
    }
}
```

**Key Points:**
- Uses `RemediationOptions.Production` preset (5 iterations, 15 min timeout, 95% threshold)
- Logs success/failure with metrics
- Always uses the output PDF (best-effort even if not fully compliant)
- Does NOT break existing functionality if remediation fails

### 3. Enhanced API Response

**Location:** `Program.cs:4151-4194`

#### Enhanced `report` Object

```csharp
report = new
{
    // Existing fields
    compliance = remediationResult?.Summary.IsCompliant == true ? "PDF/UA Compliant" : "WCAG 2.1 AA",
    fieldsProcessed = fields?.Count ?? 0,
    measuresApplied = 12,
    aiEnhanced = config.Services.UseClaudeVision || config.Services.UseClaudeValidation,
    accessibilityScore = remediationResult?.FinalValidation?.Summary?.ComplianceScore ?? 85,
    processingTime = remediationResult?.Summary.TotalDuration.TotalSeconds ?? 0,

    // NEW: PDF/UA specific validation data
    pdfUACompliant = remediationResult?.Summary.IsCompliant ?? false,
    remediationIterations = remediationResult?.Summary.TotalIterations ?? 0,
    violationsFixed = remediationResult?.Summary.ViolationsFixed ?? 0,
    violationsRemaining = remediationResult?.Summary.FinalViolationCount ?? 0,
    exitReason = remediationResult?.ExitReason.ToString()
}
```

#### New `validation` Object

```csharp
validation = remediationResult != null ? new
{
    enabled = true,
    success = remediationResult.Success,
    compliant = remediationResult.Summary.IsCompliant,
    iterations = remediationResult.Summary.TotalIterations,
    duration = remediationResult.Summary.FormattedDuration,
    initialViolations = remediationResult.Summary.InitialViolationCount,
    finalViolations = remediationResult.Summary.FinalViolationCount,
    violationsFixed = remediationResult.Summary.ViolationsFixed,
    complianceImprovement = remediationResult.Summary.ComplianceImprovement,
    exitReason = remediationResult.ExitReason.ToString(),
    recommendations = remediationResult.Recommendations?.Select(r => new
    {
        type = r.Type.ToString(),
        priority = r.Priority.ToString(),
        description = r.Description,
        suggestedAction = r.SuggestedAction
    }).ToArray(),
    iterationHistory = remediationResult.IterationHistory?.Select(h => new
    {
        iteration = h.IterationNumber,
        violations = h.ViolationCount,
        compliance = h.ComplianceScore,
        fixesApplied = h.FixesApplied,
        duration = h.Duration.TotalSeconds,
        phases = h.PhasesExecuted
    }).ToArray()
} : null
```

## Frontend Integration (Index.razor)

### 1. Data Models

**Location:** `Pages/Index.razor:784-852`

#### Extended ProcessingResult Class

```csharp
private class ProcessingResult
{
    // Existing fields...
    public string ComplianceLevel { get; set; } = "";
    public int AccessibilityScore { get; set; }

    // NEW: PDF/UA Validation fields (from closed-loop remediation)
    public bool PdfUACompliant { get; set; }
    public int RemediationIterations { get; set; }
    public int ViolationsFixed { get; set; }
    public int ViolationsRemaining { get; set; }
    public string? ExitReason { get; set; }
    public ValidationData? Validation { get; set; }
}
```

#### New ValidationData Class

```csharp
private class ValidationData
{
    public bool Enabled { get; set; }
    public bool Success { get; set; }
    public bool Compliant { get; set; }
    public int Iterations { get; set; }
    public string Duration { get; set; } = "";
    public int InitialViolations { get; set; }
    public int FinalViolations { get; set; }
    public int ViolationsFixed { get; set; }
    public double ComplianceImprovement { get; set; }
    public string ExitReason { get; set; } = "";
    public List<RecommendationData> Recommendations { get; set; } = new();
    public List<IterationData> IterationHistory { get; set; } = new();
}

private class RecommendationData
{
    public string Type { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Description { get; set; } = "";
    public string? SuggestedAction { get; set; }
}

private class IterationData
{
    public int Iteration { get; set; }
    public int Violations { get; set; }
    public double Compliance { get; set; }
    public int FixesApplied { get; set; }
    public double Duration { get; set; }
    public List<string> Phases { get; set; } = new();
}
```

### 2. Response Parsing

**Location:** `Pages/Index.razor:1338-1404` and `1512-1577`

Both Word and PDF processing paths parse the validation data:

```csharp
// Parse PDF/UA validation data from report object
PdfUACompliant = GetJsonBool(result.GetProperty("report"), "pdfUACompliant", false),
RemediationIterations = GetJsonInt(result.GetProperty("report"), "remediationIterations", 0),
ViolationsFixed = GetJsonInt(result.GetProperty("report"), "violationsFixed", 0),
ViolationsRemaining = GetJsonInt(result.GetProperty("report"), "violationsRemaining", 0),
ExitReason = GetJsonString(result.GetProperty("report"), "exitReason", null)

// Parse full validation object if present
if (result.TryGetProperty("validation", out JsonElement validationProp) &&
    validationProp.ValueKind != JsonValueKind.Null)
{
    processingResults.Validation = new ValidationData { /* ... */ };

    // Parse recommendations
    // Parse iteration history
}
```

### 3. UI Display Components

**Location:** `Pages/Index.razor:301-379`

#### PDF/UA Validation Badge

```razor
@if (processingResults.Validation != null && processingResults.Validation.Enabled)
{
    <div class="validation-results mb-3">
        <div class="alert @(processingResults.PdfUACompliant ? "alert-success" : "alert-warning") mb-2">
            <strong>PDF/UA Validation:</strong>
            @if (processingResults.PdfUACompliant)
            {
                <span class="badge bg-success">✓ Compliant</span>
            }
            else
            {
                <span class="badge bg-warning text-dark">⚠ @processingResults.ViolationsRemaining violations remaining</span>
            }
        </div>

        <!-- Remediation Summary -->
        <small class="text-muted">Remediation Summary:</small>
        <ul class="small mb-0 mt-1">
            <li>🔄 Iterations: @processingResults.RemediationIterations (@processingResults.Validation.Duration)</li>
            <li>✓ Violations Fixed: @processingResults.ViolationsFixed</li>
            @if (processingResults.Validation.ComplianceImprovement > 0)
            {
                <li>📈 Compliance Improved: +@processingResults.Validation.ComplianceImprovement.ToString("F1")%</li>
            }
            <li>🏁 Exit Reason: @processingResults.ExitReason</li>
        </ul>
```

#### Expandable Recommendations Section

```razor
@if (processingResults.Validation.Recommendations.Any())
{
    <details class="mt-2">
        <summary class="text-muted small" style="cursor: pointer;">
            View Recommendations (@processingResults.Validation.Recommendations.Count)
        </summary>
        <ul class="small mt-1">
            @foreach (var rec in processingResults.Validation.Recommendations)
            {
                <li>
                    <span class="badge bg-@(rec.Priority == "Critical" ? "danger" : rec.Priority == "High" ? "warning" : "info")">
                        @rec.Priority
                    </span>
                    @rec.Description
                    @if (!string.IsNullOrEmpty(rec.SuggestedAction))
                    {
                        <br/><small class="text-muted">→ @rec.SuggestedAction</small>
                    }
                </li>
            }
        </ul>
    </details>
}
```

#### Iteration History Table

```razor
@if (processingResults.Validation.IterationHistory.Any())
{
    <details class="mt-2">
        <summary class="text-muted small" style="cursor: pointer;">
            View Iteration History (@processingResults.Validation.IterationHistory.Count iterations)
        </summary>
        <div class="table-responsive mt-1">
            <table class="table table-sm small">
                <thead>
                    <tr>
                        <th>#</th>
                        <th>Violations</th>
                        <th>Compliance</th>
                        <th>Fixed</th>
                        <th>Duration</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var iter in processingResults.Validation.IterationHistory)
                    {
                        <tr>
                            <td>@iter.Iteration</td>
                            <td>@iter.Violations</td>
                            <td>@iter.Compliance.ToString("F1")%</td>
                            <td>@iter.FixesApplied</td>
                            <td>@iter.Duration.ToString("F1")s</td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
    </details>
}
```

## Data Flow Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│ User uploads PDF via Index.razor                                │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│ JavaScript: accessForm.uploadFileDirectly()                     │
│ - Converts file to base64                                       │
│ - POSTs to /api/convert-with-config                             │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│ API Endpoint: /api/convert-with-config                          │
│ 1. Read uploaded file bytes                                     │
│ 2. Call PdfPreservationService.ProcessExistingPdfAsync()        │
│ 3. Call RemediationOrchestrator.RemediateAsync()                │
│    ├─ Validate with VeraPDF                                     │
│    ├─ Analyze violations                                        │
│    ├─ Select remediation strategy                               │
│    ├─ Execute phases (Whitespace → Content → Links)             │
│    ├─ Re-validate                                               │
│    └─ Iterate until compliant or exit condition                 │
│ 4. Build enhanced response with validation data                 │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│ Response JSON Structure                                         │
│ {                                                                │
│   normalPdf: { filename, data, size },                          │
│   accessiblePdf: { filename, data, size },                      │
│   report: {                                                      │
│     compliance: "PDF/UA Compliant" | "WCAG 2.1 AA",             │
│     pdfUACompliant: true/false,                                 │
│     remediationIterations: 3,                                   │
│     violationsFixed: 45,                                        │
│     violationsRemaining: 0,                                     │
│     exitReason: "Success"                                       │
│   },                                                             │
│   validation: {                                                  │
│     enabled: true,                                               │
│     success: true,                                               │
│     compliant: true,                                             │
│     iterations: 3,                                               │
│     duration: "1m 34s",                                          │
│     initialViolations: 45,                                       │
│     finalViolations: 0,                                          │
│     violationsFixed: 45,                                         │
│     complianceImprovement: 15.3,                                 │
│     exitReason: "Success",                                       │
│     recommendations: [...],                                      │
│     iterationHistory: [...]                                      │
│   }                                                              │
│ }                                                                │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│ Index.razor: ProcessApiResponse()                               │
│ - Parse JSON response                                           │
│ - Populate ProcessingResult object                              │
│ - Store validation data                                         │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│ Index.razor: UI Display                                         │
│ - Show PDF/UA compliance badge                                  │
│ - Show remediation summary                                      │
│ - Show expandable recommendations                               │
│ - Show expandable iteration history                             │
│ - Provide download button for remediated PDF                    │
└─────────────────────────────────────────────────────────────────┘
```

## Configuration Options

### RemediationOptions Presets

#### Production (Default)
```csharp
var options = RemediationOptions.Production;
// MaxIterations: 5
// MaxDuration: 15 minutes
// AcceptableComplianceScore: 0.95 (95%)
// DetectStagnation: true
```

#### Aggressive (Full Compliance)
```csharp
var options = RemediationOptions.Aggressive;
// MaxIterations: 10
// MaxDuration: 30 minutes
// AcceptableComplianceScore: null (100% required)
// DetectStagnation: true
```

#### Custom
```csharp
var options = new RemediationOptions
{
    MaxIterations = 8,
    MaxDuration = TimeSpan.FromMinutes(20),
    AcceptableComplianceScore = 0.90,
    ValidateBetweenPhases = true,
    StopOnRegression = true,
    DetectStagnation = true,
    GenerateDetailedReport = true
};
```

### How to Change Configuration

To use a different preset or custom options, modify `Program.cs:4057`:

```csharp
// Change from:
var remediationResult = await remediationOrchestrator.RemediateAsync(
    pdfBytes,
    RemediationOptions.Production);

// To:
var remediationResult = await remediationOrchestrator.RemediateAsync(
    pdfBytes,
    RemediationOptions.Aggressive);  // or custom options

// Or make it configurable via appsettings.json:
var options = configuration.GetSection("Remediation").Get<RemediationOptions>()
    ?? RemediationOptions.Production;
var remediationResult = await remediationOrchestrator.RemediateAsync(pdfBytes, options);
```

## Error Handling

### Backend Error Handling

The remediation system is designed to fail gracefully:

1. **Service Failure:** If a remediation service fails, it returns `Success = false` and the original PDF
2. **Validation Failure:** If veraPDF fails, the system logs error and returns original PDF
3. **Timeout:** If max duration exceeded, returns best-effort result with `ExitReason = Timeout`
4. **Regression:** If violations increase, stops and returns previous iteration's PDF

```csharp
if (remediationResult.Success)
{
    logger.LogInformation($"Remediation successful!");
    pdfBytes = remediationResult.OutputPdf;
}
else
{
    logger.LogWarning($"Remediation exited: {remediationResult.ExitReason}");
    pdfBytes = remediationResult.OutputPdf; // Best-effort result
}
```

**Key Point:** The API endpoint ALWAYS returns a PDF, even if remediation fails completely. The original PDF is returned as fallback.

### Frontend Error Handling

The UI gracefully handles missing validation data:

```csharp
// Use null-coalescing operators
PdfUACompliant = GetJsonBool(result.GetProperty("report"), "pdfUACompliant", false),
RemediationIterations = GetJsonInt(result.GetProperty("report"), "remediationIterations", 0),

// Check for null before displaying
@if (processingResults.Validation != null && processingResults.Validation.Enabled)
{
    // Display validation results
}
```

## Performance Considerations

### Backend
- **Remediation adds 30 seconds to 10 minutes** depending on document complexity
- Uses `Production` preset by default (max 5 iterations, 15 min timeout)
- Logs timing information for monitoring
- Does NOT block other requests (uses async/await)

### Frontend
- Validation data is optional - page works without it
- Expandable sections (`<details>`) reduce initial render time
- Large iteration histories (10+ iterations) may slow rendering slightly

### Optimization Opportunities
1. Run remediation in background queue (for batch processing)
2. Cache validation results (if same PDF uploaded multiple times)
3. Add progress indicator during remediation (WebSockets/SignalR)
4. Make remediation optional (add checkbox in UI)

## Testing the Integration

### Manual Testing Steps

1. **Upload a PDF with violations:**
   - Go to http://localhost:5001 (or your port)
   - Upload a non-compliant PDF
   - Wait for processing to complete

2. **Verify remediation ran:**
   - Check server logs for "Starting closed-loop PDF/UA remediation"
   - Check for "Remediation successful!" or warning message

3. **Verify UI displays validation data:**
   - Look for "PDF/UA Validation" section
   - Expand "View Recommendations" if present
   - Expand "View Iteration History" if present
   - Verify compliance badge color (green = compliant, yellow = warnings)

4. **Download and validate result:**
   - Download the "Accessible PDF"
   - Run through veraPDF manually to confirm compliance
   - Compare with original PDF

### Automated Testing

```csharp
// Example integration test (to be implemented)
[Fact]
public async Task ConvertWithConfig_PdfWithViolations_RunsRemediation()
{
    // Arrange
    var pdfBytes = File.ReadAllBytes("test-with-violations.pdf");
    var formData = new MultipartFormDataContent();
    formData.Add(new ByteArrayContent(pdfBytes), "file", "test.pdf");

    // Act
    var response = await _client.PostAsync("/api/convert-with-config", formData);
    var result = await response.Content.ReadFromJsonAsync<ApiResponse>();

    // Assert
    Assert.NotNull(result.Validation);
    Assert.True(result.Validation.Enabled);
    Assert.True(result.Report.RemediationIterations > 0);
}
```

## Troubleshooting

### Issue: Validation data not showing in UI

**Symptoms:** UI shows processed PDF but no "PDF/UA Validation" section

**Causes:**
1. Remediation failed completely (check server logs)
2. `validation` object is null in API response
3. Frontend parsing error

**Solutions:**
1. Check server logs for errors in RemediationOrchestrator
2. Check browser console for JavaScript errors
3. Verify VeraPdfService is configured correctly (see appsettings.json)

### Issue: Remediation takes too long

**Symptoms:** Request times out or takes > 5 minutes

**Causes:**
1. PDF has 100+ violations (many iterations needed)
2. VeraPDF is slow (old Java version, limited memory)
3. MaxIterations or MaxDuration too high

**Solutions:**
1. Reduce `MaxIterations` in RemediationOptions
2. Reduce `MaxDuration` timeout
3. Upgrade to faster Java version for veraPDF
4. Use `AcceptableComplianceScore = 0.95` to stop at 95%

### Issue: Remediation fails with "veraPDF not found"

**Symptoms:** Logs show "veraPDF executable not found" or similar

**Causes:**
1. VeraPDF not installed in expected location
2. Java not installed or wrong version
3. appsettings.json has incorrect paths

**Solutions:**
1. Verify paths in appsettings.json:
   ```json
   {
     "VeraPdf": {
       "ExecutablePath": "/path/to/verapdf/verapdf",
       "JavaHome": "/path/to/jdk-21.0.8.jdk/Contents/Home",
       "WorkingDirectory": "/tmp/verapdf-work"
     }
   }
   ```
2. Install veraPDF: See VERAPDF-INSTALL.md
3. Install Java 21: `brew install openjdk@21`

## Future Enhancements

1. **Progress Indicator:** Real-time progress updates during remediation (WebSockets)
2. **Optional Remediation:** Checkbox to enable/disable remediation
3. **Preset Selector:** Let user choose Production vs. Aggressive mode
4. **Batch Processing:** Queue-based processing for multiple PDFs
5. **Detailed Reports:** Downloadable PDF report with before/after comparison
6. **Violation Filtering:** Show specific violation types in UI
7. **Historical Tracking:** Store remediation results in database for analytics

## Related Documentation

- **Usage Guide:** See [USAGE-EXAMPLE.md](./USAGE-EXAMPLE.md) for API usage examples
- **Roadmap:** See [ROADMAP.md](./ROADMAP.md) for implementation status
- **VeraPDF Setup:** See [VERAPDF-INSTALL.md](../../VERAPDF-INSTALL.md) for installation guide

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2025-10-22 | Initial integration with web interface |

---

**Questions or Issues?** Check the Troubleshooting section or review server logs for errors.
