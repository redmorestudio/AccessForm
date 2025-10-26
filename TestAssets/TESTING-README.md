# veraPDF Testing Infrastructure

This directory contains test harness and tools for validating the PDF/UA checker implementation.

## Test Components

### 1. Corpus Validation Harness

**File**: `CorpusValidationHarness.cs`

A comprehensive test harness that validates all PDFs in the veraPDF corpus and compares actual results against expected outcomes (parsed from filenames).

**Features**:
- Iterates through all corpus PDFs
- Runs veraPDF validation on each file
- Parses expected result from filename (e.g., "7.21.6-t03-pass-a.pdf" should pass)
- Compares actual vs expected
- Generates detailed test report
- Saves results to Reports/ directory

**Usage**:
```csharp
var harness = new CorpusValidationHarness();
var report = await harness.RunAllTestsAsync();
harness.PrintReport(report);
await harness.SaveReportAsync(report);
```

### 2. Corpus Test Runner

**File**: `CorpusTestRunner.cs`

Console application to run corpus validation tests.

**Usage**:
```bash
# Default paths (uses TestAssets/veraPDF-corpus, TestAssets/verapdf, etc.)
dotnet run CorpusTestRunner.cs

# Custom paths
dotnet run CorpusTestRunner.cs -- \
  --corpus ./TestAssets/veraPDF-corpus/PDF_UA-1 \
  --verapdf ./TestAssets/verapdf/verapdf \
  --java ./TestAssets/jdk-21.0.8.jdk/Contents/Home \
  --output ./Reports/my-test-report.txt
```

### 3. Shell Script Runner

**File**: `run-corpus-tests.sh`

Convenience script to run corpus validation with default paths.

**Usage**:
```bash
cd TestAssets
./run-corpus-tests.sh
```

## Prerequisites

Before running tests, you must:

1. **Install veraPDF** (see [VERAPDF-INSTALL.md](../VERAPDF-INSTALL.md))
   ```bash
   cd TestAssets/verapdf-greenfield-1.26.2
   ../jdk-21.0.8.jdk/Contents/Home/bin/java -jar verapdf-izpack-installer-1.26.2.jar
   # Install to: TestAssets/verapdf
   ```

2. **Verify installation**:
   ```bash
   TestAssets/verapdf/verapdf --version
   # Should output: veraPDF 1.26.2
   ```

3. **Test with a single PDF**:
   ```bash
   TestAssets/verapdf/verapdf --flavour ua1 --format json \
     "TestAssets/veraPDF-corpus/PDF_UA-1/7.21 Fonts/7.21.6 Character encodings/7.21.6-t03-pass-a.pdf"
   ```

## Test Corpus Structure

The veraPDF corpus is organized by PDF/UA clause:

```
veraPDF-corpus/PDF_UA-1/
├── 6.1 File header/
│   └── 6.1-t01-fail-a.pdf    # Should FAIL validation
├── 6.2 Metadata/
│   ├── 6.2-t01-pass-a.pdf    # Should PASS validation
│   └── 6.2-t02-fail-a.pdf    # Should FAIL validation
├── 7.1 General/
│   └── ...
├── 7.18 Annotations/
│   └── ...
└── 7.21 Fonts/
    ├── 7.21.6 Character encodings/
    │   ├── 7.21.6-t01-pass-a.pdf
    │   ├── 7.21.6-t02-pass-a.pdf
    │   └── 7.21.6-t03-pass-a.pdf
    └── ...
```

**Filename Convention**:
- Format: `{clause}-t{testNum}-{pass|fail}-{variant}.pdf`
- Example: `7.21.6-t03-pass-a.pdf`
  - Clause: 7.21.6 (Character encodings)
  - Test number: 03
  - Expected: PASS
  - Variant: a

## Expected Results

### Success Criteria

The test harness should achieve:
- **100% execution**: All corpus PDFs validate without errors
- **95%+ accuracy**: Actual results match expected results (pass/fail)
- **Performance**: <5s per PDF on average

### Sample Output

```
=== veraPDF Corpus Validation Harness ===
Corpus Path: /Users/.../TestAssets/veraPDF-corpus/PDF_UA-1
veraPDF: /Users/.../TestAssets/verapdf/verapdf

Found 326 test PDFs

[1/326] 6.1 File header/6.1-t01-fail-a.pdf... ✓ PASS
[2/326] 6.2 Metadata/6.2-t01-pass-a.pdf... ✓ PASS
[3/326] 6.2 Metadata/6.2-t02-fail-a.pdf... ✓ PASS
...

=== Test Report ===
Total Tests: 326
Passed: 310 (95.1%)
Failed: 16
Duration: 1234.5s
```

## Integration with VeraPdfService

The corpus harness tests the raw veraPDF CLI. To test the full `VeraPdfService.cs`:

```csharp
// Create service
var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string>
    {
        ["VeraPdf:ExecutablePath"] = "/path/to/verapdf",
        ["VeraPdf:JavaHome"] = "/path/to/java"
    })
    .Build();

var service = new VeraPdfService(logger, config);

// Validate a PDF
var result = await service.ValidatePdfAsync(pdfPath);

// Check results
Assert.Equal(expectedCompliant, result.Summary.IsCompliant);
Assert.Equal(expectedViolationCount, result.Violations.Count);
```

## Next Steps

1. **Unit Tests**: Create xUnit tests for `VeraPdfService` JSON parsing (using mock data)
2. **Integration Tests**: Test full service with real PDFs
3. **CI/CD Integration**: Add corpus validation to build pipeline
4. **Performance Tests**: Measure validation speed across corpus
5. **Regression Tests**: Track validation accuracy over time

## Troubleshooting

### veraPDF Not Found
```
ERROR: veraPDF not found at: /path/to/verapdf
```
**Fix**: Install veraPDF manually - see [VERAPDF-INSTALL.md](../VERAPDF-INSTALL.md)

### Java Not Found
```
ERROR: Java not found at: /path/to/java
```
**Fix**: Verify Java JDK is installed in `TestAssets/jdk-21.0.8.jdk/`

### Corpus Not Found
```
ERROR: Corpus directory not found: /path/to/corpus
```
**Fix**: The corpus should be at `TestAssets/veraPDF-corpus/PDF_UA-1/`. If missing, re-clone:
```bash
cd TestAssets
git clone https://github.com/veraPDF/veraPDF-corpus.git
```

### Validation Timeout
```
TimeoutException: veraPDF timed out after 30 seconds
```
**Fix**: Increase timeout in `CorpusValidationHarness.cs` (line 144):
```csharp
var completed = await Task.Run(() => process.WaitForExit(60000)); // 60s timeout
```

## Test Reports

All test reports are saved to `Reports/corpus-validation-YYYY-MM-DD_HH-mm-ss.txt`

Example report structure:
```
=== veraPDF Corpus Validation Report ===
Generated: 2025-10-18 14:30:00
Corpus: /Users/.../TestAssets/veraPDF-corpus/PDF_UA-1

Total Tests: 326
Passed: 310 (95.1%)
Failed: 16
Duration: 1234.5s

=== Detailed Results ===
PASS | 6.1 File header/6.1-t01-fail-a.pdf | 3.45s
PASS | 6.2 Metadata/6.2-t01-pass-a.pdf | 2.12s
FAIL | 6.2 Metadata/6.2-t03-pass-a.pdf | 2.87s
     Expected: Pass, Actual: FAIL
     Error: Unexpected compliance result
...
```
