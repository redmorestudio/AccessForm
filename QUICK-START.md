# PDF/UA Checker - Quick Start

**Status**: ✅ Core implementation complete, ready for veraPDF installation

---

## 🚦 One-Time Setup (5 minutes)

### 1. Install veraPDF

```bash
cd TestAssets/verapdf-greenfield-1.26.2
../jdk-21.0.8.jdk/Contents/Home/bin/java -jar verapdf-izpack-installer-1.26.2.jar
```

Install to: `TestAssets/verapdf`

### 2. Verify Installation

```bash
TestAssets/verapdf/verapdf --version
# Should output: veraPDF 1.26.2
```

### 3. Test with Sample PDF

```bash
TestAssets/verapdf/verapdf --flavour ua1 --format json \
  "TestAssets/veraPDF-corpus/PDF_UA-1/7.21 Fonts/7.21.6 Character encodings/7.21.6-t03-pass-a.pdf"
```

---

## 💻 Usage in Code

### Basic Validation

```csharp
// Setup (once)
builder.Services.AddSingleton<VeraPdfService>();

// Use
var service = serviceProvider.GetRequiredService<VeraPdfService>();
var result = await service.ValidatePdfAsync("/path/to/document.pdf");

// Check results
Console.WriteLine($"Compliant: {result.Summary.IsCompliant}");
Console.WriteLine($"Score: {result.Summary.ComplianceScore:F2}%");
Console.WriteLine($"Violations: {result.Violations.Count}");
```

### Display Violations

```csharp
foreach (var violation in result.Violations)
{
    var severity = violation.Severity switch
    {
        ViolationSeverity.Critical => "🔴",
        ViolationSeverity.Error => "🟠",
        ViolationSeverity.Warning => "🟡",
        _ => "ℹ️"
    };

    Console.WriteLine($"{severity} [{violation.RuleId}] {violation.Description}");

    if (violation.Location?.PageNumber != null)
        Console.WriteLine($"   Page {violation.Location.PageNumber}");
}
```

### Before/After Comparison

```csharp
var before = await service.ValidatePdfAsync("original.pdf");
// ... apply fixes ...
var after = await service.ValidatePdfAsync("fixed.pdf");

var comparison = service.CompareValidations(before, after);
Console.WriteLine($"Fixed: {comparison.ViolationsFixed} violations");
Console.WriteLine($"Improved: {comparison.ComplianceImprovement:F2}%");
```

---

## 🧪 Run Tests

### Corpus Validation (all 326 PDFs)

```bash
cd TestAssets
./run-corpus-tests.sh
```

Expected: 95%+ success rate (310+ passing)

### Integration Test (single PDF workflow)

```bash
cd TestAssets
./run-integration-test.sh
```

Or with custom PDF:

```bash
./run-integration-test.sh "/path/to/test.pdf"
```

---

## ⚙️ Configuration

Add to `appsettings.json`:

```json
{
  "VeraPdf": {
    "ExecutablePath": "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/TestAssets/verapdf/verapdf",
    "JavaHome": "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/TestAssets/jdk-21.0.8.jdk/Contents/Home",
    "WorkingDirectory": "/tmp/verapdf-work"
  }
}
```

---

## 📊 What You Get

| Item | Description |
|------|-------------|
| **ValidationResult** | Complete validation report with violations |
| **Compliance Score** | 0-100% score (passed checks / total checks) |
| **Violations List** | Each violation with: RuleId, Severity, Description, Location |
| **Processing Time** | How long validation took |
| **Raw Output** | Original veraPDF JSON for debugging |

---

## 🎯 Common Tasks

### Task: Validate a PDF

```csharp
var result = await veraPdfService.ValidatePdfAsync(pdfPath);
```

### Task: Check if compliant

```csharp
if (result.Summary.IsCompliant)
{
    // PDF is PDF/UA compliant
}
```

### Task: Get critical issues

```csharp
var critical = result.Violations
    .Where(v => v.Severity == ViolationSeverity.Critical)
    .ToList();
```

### Task: Group violations by page

```csharp
var byPage = result.Violations
    .Where(v => v.Location?.PageNumber != null)
    .GroupBy(v => v.Location.PageNumber)
    .OrderBy(g => g.Key);

foreach (var page in byPage)
{
    Console.WriteLine($"Page {page.Key}: {page.Count()} issues");
}
```

### Task: Track improvement

```csharp
// Before
var before = await veraPdfService.ValidatePdfAsync("original.pdf");
Console.WriteLine($"Before: {before.Violations.Count} violations");

// Apply fixes (using your existing services)
await pdfEnhancer.ApplyComplianceFixes("original.pdf", "fixed.pdf");

// After
var after = await veraPdfService.ValidatePdfAsync("fixed.pdf");
Console.WriteLine($"After: {after.Violations.Count} violations");

// Compare
var comparison = veraPdfService.CompareValidations(before, after);
Console.WriteLine($"Fixed: {comparison.ViolationsFixed} issues");
```

---

## 🔍 Troubleshooting

### veraPDF not found

```bash
# Check if installed
ls -la TestAssets/verapdf/verapdf

# If missing, install it
cd TestAssets/verapdf-greenfield-1.26.2
../jdk-21.0.8.jdk/Contents/Home/bin/java -jar verapdf-izpack-installer-1.26.2.jar
```

### Java not found

```bash
# Check Java
TestAssets/jdk-21.0.8.jdk/Contents/Home/bin/java -version

# Should output: openjdk version "21.0.8"
```

### Validation times out

Increase timeout in `VeraPdfService.cs:145`:

```csharp
var completed = await Task.Run(() => process.WaitForExit(300000)); // 5 minutes
```

### Test corpus missing

```bash
cd TestAssets
git clone https://github.com/veraPDF/veraPDF-corpus.git
```

---

## 📚 Documentation

- **Installation**: `VERAPDF-INSTALL.md`
- **Testing**: `TestAssets/TESTING-README.md`
- **Build Summary**: `BUILD-SUMMARY.md`
- **Full Spec**: `SPECIFICATION.md`
- **Implementation Plan**: `IMPLEMENTATION-PLAN.md`

---

## 🎯 Next Steps

1. ✅ **Install veraPDF** (manual, 5 minutes)
2. 📋 **Register service** in Program.cs
3. 📋 **Add configuration** to appsettings.json
4. 📋 **Test validation** with a real PDF
5. 📋 **Integrate into pipeline** (call after PDF generation)
6. 📋 **Build UI** to display violations
7. 📋 **Implement remediation** orchestrator

---

**Ready to go!** Just install veraPDF and you're good. 🚀
