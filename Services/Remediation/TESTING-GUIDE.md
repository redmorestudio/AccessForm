# Closed-Loop Remediation - Testing Guide

**Date:** 2025-10-22
**Version:** 1.0
**Status:** Ready for Testing

## Overview

This guide helps you test the closed-loop PDF/UA remediation system with your existing PDFs (both compliant and non-compliant).

## Test Organization

### Directory Structure

```
TestAssets/
├── Remediation/
│   ├── Known-Pass/           ← PDFs that are already PDF/UA compliant
│   │   ├── test-001.pdf
│   │   ├── test-002.pdf
│   │   └── ...
│   ├── Known-Fail/           ← PDFs with known violations
│   │   ├── fail-001.pdf
│   │   ├── fail-002.pdf
│   │   └── ...
│   ├── Results/              ← Test results
│   │   ├── YYYY-MM-DD_HH-MM-SS/
│   │   │   ├── test-001/
│   │   │   │   ├── input.pdf
│   │   │   │   ├── output.pdf
│   │   │   │   ├── initial-validation.json
│   │   │   │   ├── final-validation.json
│   │   │   │   └── remediation-report.json
│   │   │   ├── test-002/
│   │   │   └── summary.json
│   └── run-remediation-tests.sh    ← Test harness script
```

### Setup Test Directories

```bash
# Create test directory structure
mkdir -p TestAssets/Remediation/Known-Pass
mkdir -p TestAssets/Remediation/Known-Fail
mkdir -p TestAssets/Remediation/Results

# Copy your PDFs
cp /path/to/passing/*.pdf TestAssets/Remediation/Known-Pass/
cp /path/to/failing/*.pdf TestAssets/Remediation/Known-Fail/
```

## Testing Approach

### Phase 1: Manual Testing (Start Here)

**Purpose:** Quick validation that the system works

**Steps:**
1. Start the web server
2. Upload one PDF from Known-Fail
3. Observe the results
4. Verify the output

### Phase 2: Automated Testing

**Purpose:** Systematic testing of multiple PDFs

**Steps:**
1. Run test harness script
2. Review results
3. Analyze patterns

### Phase 3: Performance Testing

**Purpose:** Measure speed and resource usage

**Steps:**
1. Test with varying document sizes
2. Measure iteration times
3. Track success rates

## Manual Testing Steps

### Step 1: Start the Server

```bash
cd /Users/sethredmore/Documents/Redmore\ Studio/AccessForm/WordToPdfConverter
ASPNETCORE_URLS="http://localhost:5001" dotnet run --project AccessFormServer.csproj --nologo --verbosity quiet
```

### Step 2: Upload a PDF

1. Open browser: http://localhost:5001
2. Enable "AI Mode" (checkbox in UI)
3. Drag and drop or select a PDF from Known-Fail
4. Wait for processing to complete

### Step 3: Observe Results

Look for these UI elements:

#### Success Indicators
- ✅ **Green badge:** "✓ Compliant"
- **Compliance Level:** "PDF/UA Compliant"
- **Exit Reason:** "Success"
- **Violations Remaining:** 0

#### Partial Success Indicators
- ⚠️ **Yellow badge:** "⚠ X violations remaining"
- **Exit Reason:** "NoProgress" or "MaxIterationsReached" or "ThresholdMet"
- **Violations Fixed:** Should be > 0
- **Recommendations:** May suggest manual intervention

#### Failure Indicators
- ❌ **Red badge** (if system added this)
- **Exit Reason:** "Regression" or "Timeout"
- **Violations Remaining:** > initial violations (regression)

### Step 4: Check Server Logs

Look for these log entries:

```
Starting closed-loop PDF/UA remediation
RemediationOrchestrator: Starting iteration 1
ViolationAnalyzer: Found X violations in Y categories
RemediationExecutor: Executing Phase 1: Whitespace Cleanup
RemediationExecutor: Executing Phase 2: Content Remediation
RemediationOrchestrator: Starting iteration 2
...
Remediation successful! Compliant=True, Iterations=3, Violations Fixed=45
```

### Step 5: Download and Verify

1. Click "Download Accessible PDF"
2. Validate manually with veraPDF:
   ```bash
   TestAssets/verapdf/verapdf --format json --flavour ua1 output.pdf
   ```
3. Check compliance status in JSON output

## Test Cases

### Test Case 1: Already Compliant PDF (Known-Pass)

**Expected Behavior:**
- Iteration 1: Validates and finds 0 violations
- Exit Reason: Success
- Iterations: 1
- Violations Fixed: 0
- Duration: < 30 seconds

**What This Tests:**
- System recognizes compliant PDFs
- Doesn't break already-good PDFs
- Fast processing for simple cases

### Test Case 2: Simple Non-Compliant PDF (Few Violations)

**Example:** PDF with 5-10 violations (whitespace, artifacts)

**Expected Behavior:**
- Iterations: 1-3
- Exit Reason: Success or ThresholdMet
- Violations Fixed: Most or all
- Duration: 30 seconds - 2 minutes

**What This Tests:**
- Phases 1-2 (Whitespace, Content) work
- Progress tracking works
- Exit conditions work correctly

### Test Case 3: Moderate Non-Compliant PDF (10-50 Violations)

**Example:** PDF with structure, content, and link issues

**Expected Behavior:**
- Iterations: 2-5
- Exit Reason: Success, ThresholdMet, or NoProgress
- Violations Fixed: Majority
- Duration: 2-5 minutes

**What This Tests:**
- Multiple phases execute
- Iteration loop works correctly
- Stagnation detection works

### Test Case 4: Complex Non-Compliant PDF (50+ Violations)

**Example:** PDF with many structural issues, missing metadata

**Expected Behavior:**
- Iterations: 5+ (may hit MaxIterations)
- Exit Reason: MaxIterationsReached or NoProgress
- Violations Fixed: Some (not all)
- Duration: 5-15 minutes
- Recommendations: Should suggest manual intervention

**What This Tests:**
- System handles difficult cases gracefully
- Timeout protection works
- Recommendations system works
- Returns best-effort result

### Test Case 5: PDF with Only Metadata Issues (Phase 7 Not Yet Implemented)

**Example:** PDF with all content correct but missing PDF/UA metadata

**Expected Behavior:**
- Iterations: 1-2
- Exit Reason: NoProgress (Phase 7 not implemented yet)
- Violations Fixed: 0
- Violations Remaining: Metadata violations
- Recommendations: "Requires manual intervention or new tooling"

**What This Tests:**
- System recognizes Category 3 (New Function Needed) violations
- Graceful handling of unimplemented phases

## Automated Test Harness

### Create Test Script

```bash
#!/bin/bash
# TestAssets/Remediation/run-remediation-tests.sh

TIMESTAMP=$(date +"%Y-%m-%d_%H-%M-%S")
RESULTS_DIR="TestAssets/Remediation/Results/$TIMESTAMP"
mkdir -p "$RESULTS_DIR"

echo "=== Remediation Testing Started at $TIMESTAMP ==="
echo "Results directory: $RESULTS_DIR"
echo ""

# Test counters
TOTAL=0
SUCCESS=0
PARTIAL=0
FAILED=0

# Function to test a single PDF
test_pdf() {
    local input_pdf="$1"
    local test_name=$(basename "$input_pdf" .pdf)
    local test_dir="$RESULTS_DIR/$test_name"

    mkdir -p "$test_dir"
    cp "$input_pdf" "$test_dir/input.pdf"

    echo "Testing: $test_name"

    # TODO: Call API endpoint with curl
    # For now, manual test through UI

    TOTAL=$((TOTAL + 1))
}

# Test Known-Pass PDFs
echo "=== Testing Known-Pass PDFs ==="
for pdf in TestAssets/Remediation/Known-Pass/*.pdf; do
    [ -e "$pdf" ] || continue
    test_pdf "$pdf"
done

echo ""
echo "=== Testing Known-Fail PDFs ==="
for pdf in TestAssets/Remediation/Known-Fail/*.pdf; do
    [ -e "$pdf" ] || continue
    test_pdf "$pdf"
done

echo ""
echo "=== Test Summary ==="
echo "Total: $TOTAL"
echo "Success: $SUCCESS"
echo "Partial: $PARTIAL"
echo "Failed: $FAILED"
```

Make it executable:
```bash
chmod +x TestAssets/Remediation/run-remediation-tests.sh
```

### API Testing with curl

Test the endpoint directly:

```bash
# Test a single PDF
curl -X POST http://localhost:5001/api/convert-with-config \
  -F "file=@TestAssets/Remediation/Known-Fail/test-001.pdf" \
  -F "useSyncfusion=false" \
  -F "useClaudeVision=false" \
  -F "useGoogle=false" \
  -F "useClaudeValidation=false" \
  -F "useAsposeFontEmbed=true" \
  -o result.json

# Check validation data
cat result.json | jq '.validation'
cat result.json | jq '.report.pdfUACompliant'
cat result.json | jq '.report.remediationIterations'
cat result.json | jq '.report.violationsFixed'
```

## What to Look For

### Success Indicators

1. **Compliance Achieved**
   - `pdfUACompliant: true`
   - `exitReason: "Success"`
   - `violationsRemaining: 0`

2. **Progress Made**
   - `violationsFixed > 0`
   - Violations decreased each iteration
   - Compliance score increased

3. **Reasonable Performance**
   - Simple: < 2 minutes
   - Moderate: 2-5 minutes
   - Complex: 5-15 minutes

### Warning Signs

1. **Stagnation**
   - `exitReason: "NoProgress"`
   - Same violation count for 2+ iterations
   - **Action:** Review which violations remain

2. **Regression**
   - `exitReason: "Regression"`
   - Violations increased
   - **Action:** Check which phase caused increase

3. **Timeout**
   - `exitReason: "Timeout"`
   - Took > 15 minutes (Production preset)
   - **Action:** Check document complexity, increase timeout

### Red Flags

1. **System Errors**
   - Log shows exceptions
   - `success: false` but no exit reason
   - **Action:** Check server logs, file bug report

2. **No Changes**
   - `violationsFixed: 0`
   - `exitReason: "MaxIterationsReached"`
   - No phases executed
   - **Action:** Check if services are registered

3. **PDF Corruption**
   - Output PDF won't open
   - Validation fails on output
   - **Action:** Check which service caused corruption

## Interpreting Results

### Exit Reason Breakdown

| Exit Reason | Meaning | Action |
|-------------|---------|--------|
| **Success** | PDF is now compliant | ✅ Great! Download and use |
| **ThresholdMet** | Reached 95% compliance | ✅ Review remaining violations, may be acceptable |
| **NoProgress** | Stagnated, can't improve further | ⚠️ Review recommendations, may need new service |
| **MaxIterationsReached** | Hit iteration limit | ⚠️ Complex document, review progress made |
| **Regression** | Violations increased | ❌ Services conflicting, need investigation |
| **Timeout** | Took too long | ⚠️ Increase timeout or simplify document |

### Iteration Patterns

**Healthy Pattern:**
```
Iteration 1: 45 violations → Execute phases → 30 violations (15 fixed)
Iteration 2: 30 violations → Execute phases → 15 violations (15 fixed)
Iteration 3: 15 violations → Execute phases → 0 violations (15 fixed)
Result: Success
```

**Stagnation Pattern:**
```
Iteration 1: 20 violations → Execute phases → 12 violations (8 fixed)
Iteration 2: 12 violations → Execute phases → 12 violations (0 fixed)
Iteration 3: 12 violations → Execute phases → 12 violations (0 fixed)
Result: NoProgress
```

**Regression Pattern:**
```
Iteration 1: 15 violations → Execute phases → 8 violations (7 fixed)
Iteration 2: 8 violations → Execute phases → 12 violations (-4 fixed!)
Result: Regression
```

## Common Issues & Solutions

### Issue: "VeraPDF not found"

**Symptoms:**
- Log shows "veraPDF executable not found"
- Validation fails immediately

**Solution:**
```bash
# Check veraPDF installation
ls -la TestAssets/verapdf/verapdf
./TestAssets/verapdf/verapdf --version

# If missing, reinstall (see VERAPDF-INSTALL.md)
```

### Issue: "No violations fixed"

**Symptoms:**
- All iterations show 0 fixes
- Exit reason: NoProgress or MaxIterationsReached

**Possible Causes:**
1. Services not registered in DI
2. PDF has only unimplemented phase violations (e.g., metadata)
3. Service adapters failing silently

**Solution:**
```bash
# Check logs for service execution
grep "Executing Phase" logs.txt

# Verify services are registered
grep "AddScoped.*ServiceAdapter" Program.cs
```

### Issue: "System is too slow"

**Symptoms:**
- Each iteration takes > 5 minutes
- Timeout frequently

**Solution:**
```bash
# Use Production preset (faster)
var options = RemediationOptions.Production;  // 5 iter, 15 min

# Or reduce iterations
var options = new RemediationOptions {
    MaxIterations = 3,
    MaxDuration = TimeSpan.FromMinutes(10)
};
```

### Issue: "Output PDF is corrupted"

**Symptoms:**
- PDF won't open in Adobe Reader
- VeraPDF validation fails to parse PDF

**Solution:**
1. Check which phase last executed (iteration history)
2. Test that service independently
3. File bug report with input PDF

## Performance Benchmarking

### Collect Metrics

For each test, record:

```json
{
  "testName": "test-001",
  "inputSize": "1.2 MB",
  "initialViolations": 45,
  "finalViolations": 0,
  "violationsFixed": 45,
  "iterations": 3,
  "duration": "94 seconds",
  "exitReason": "Success",
  "phasesExecuted": [
    "Whitespace Cleanup",
    "Content Remediation",
    "Link Structure Fixes"
  ],
  "averageTimePerIteration": "31.3 seconds",
  "successRate": 100
}
```

### Target Metrics (Production Preset)

| Metric | Target | Acceptable | Poor |
|--------|--------|------------|------|
| Success Rate | > 80% | 60-80% | < 60% |
| Avg Iterations | 2-4 | 4-5 | > 5 |
| Avg Duration (Simple) | < 1 min | 1-2 min | > 2 min |
| Avg Duration (Moderate) | < 3 min | 3-5 min | > 5 min |
| Avg Duration (Complex) | < 10 min | 10-15 min | > 15 min |

## Reporting Bugs

When filing issues, include:

1. **Input PDF** (if shareable) or description
2. **Expected behavior**
3. **Actual behavior**
4. **Server logs** (full remediation session)
5. **Remediation result JSON**
6. **System info:**
   - .NET version: `dotnet --version`
   - VeraPDF version: `./TestAssets/verapdf/verapdf --version`
   - Java version: `java -version`

## Next Steps After Testing

### Phase 1: Initial Validation
- [ ] Test 5 Known-Pass PDFs (should stay compliant)
- [ ] Test 10 Known-Fail PDFs (should show improvement)
- [ ] Review results and identify patterns

### Phase 2: Analysis
- [ ] Categorize remaining violations
- [ ] Identify which phases are missing
- [ ] Prioritize next phase to implement

### Phase 3: Implementation
- [ ] Implement Phase 7 (Metadata) - CRITICAL
- [ ] Implement Phase 6 (Fonts) - HIGH
- [ ] Implement Phase 4 (Form Fields) - MEDIUM
- [ ] Implement Phase 3 (Structure) - MEDIUM

### Phase 4: Iteration
- [ ] Re-test with new phases
- [ ] Measure success rate improvement
- [ ] Optimize slow phases

## Success Criteria

### Minimal Success (Phase 1)
- [ ] System runs without crashing
- [ ] At least 1 PDF achieves compliance
- [ ] Exit conditions work correctly
- [ ] Graceful degradation for failures

### Good Success
- [ ] 50%+ of Known-Fail PDFs show improvement
- [ ] No regression cases
- [ ] Average duration < 5 minutes
- [ ] Recommendations are helpful

### Excellent Success
- [ ] 80%+ of Known-Fail PDFs achieve compliance
- [ ] Average duration < 3 minutes
- [ ] Clear patterns identified for remaining failures
- [ ] Ready to implement missing phases

---

**Ready to Start Testing?**

1. Organize your PDFs into Known-Pass and Known-Fail
2. Start with manual testing (1-2 PDFs)
3. Review results and server logs
4. Run broader tests if initial tests look good
5. Report findings!
