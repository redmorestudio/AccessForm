# PDF/UA Remediation System - Current State & Context

**Last Updated**: 2025-10-23
**Purpose**: Critical context for continuing development across sessions

---

## Overview

This system performs **closed-loop PDF/UA remediation** - iteratively fixing PDF accessibility violations through validation feedback cycles. Recently integrated GPT-5 as a fallback for complex issues that specialized services can't handle.

---

## 🔄 Closed-Loop Remediation: How It Works

### Key Principle: REACTIVE, NOT PROACTIVE

The system operates in passes:

1. **Validate** → Run veraPDF to detect violations
2. **Analyze** → Categorize violations by type
3. **Select Services** → Pick appropriate remediation services based on violations found
4. **Apply Fixes** → Run selected services
5. **Validate Again** → Check if violations were fixed
6. **Repeat** → Continue until no more violations or max iterations reached

### Critical Understanding

**Services should NOT run proactively** - they run REACTIVELY when validation detects their target violations.

**Example**:
- ❌ **Wrong**: CircularRoleMappingFixService runs on every pass with Priority=5, IsRequired=true
- ✅ **Right**: CircularRoleMappingFixService only runs when veraPDF reports "circular role mapping" violation

### Service Configuration

```csharp
public int Priority => 100;      // Low priority = reactive/fallback
public bool IsRequired => false;  // Only run when violation detected
```

High priority (5-50) + IsRequired=true = runs every pass (for foundational fixes like whitespace cleanup).

---

## 🤖 GPT-5 Integration

### Current Status: WORKING

After 3 bug fixes, GPT-5 is successfully generating and executing Python remediation scripts.

### Configuration

**File**: `appsettings.json` (gitignored - use `appsettings.template.json`)

```json
{
  "AiServices": {
    "OpenAI": {
      "Enabled": true,
      "ApiKey": "YOUR_KEY_HERE",
      "Model": "gpt-5",
      "BaseUrl": "https://api.openai.com/v1",
      "MaxTokens": 16384,
      "Temperature": 1.0
    }
  }
}
```

**Temperature**: 1.0 (creative, not deterministic)
**Max Tokens**: 16384 (liberal response length)

### Bugs Fixed

1. **Wrong API format**: Used nested object `{type: "input_text", text: prompt}` instead of direct string
2. **Wrong endpoint**: Called `/completions` instead of `/chat/completions`
3. **Wrong parameter**: Used `max_tokens` instead of `max_completion_tokens` for gpt-5

### Prompt Engineering

**Location**: `Services/Remediation/AI/GptRemediationService.cs:BuildPrompt()`

The prompt includes:
- PDF/UA violation details
- Working pikepdf code examples
- Explicit API usage notes

**Critical API Guidance** (Services/Remediation/AI/GptRemediationService.cs:301-338):

Strengthened with explicit error messages and wrong/right examples:
```
⚠️ CRITICAL pikepdf API REQUIREMENTS - FAILURE TO FOLLOW WILL CAUSE SCRIPT TO CRASH:

1. METADATA VALUES MUST BE STRINGS:
   ❌ WRONG: meta['pdfuaid:part'] = 1  (TypeError: Setting pdfuaid:part to 1 with type <class 'int'>)
   ✅ RIGHT: meta['pdfuaid:part'] = '1'  (String value required!)

   ❌ WRONG: want_part = 1; meta['pdfuaid:part'] = want_part
   ✅ RIGHT: want_part = '1'; meta['pdfuaid:part'] = want_part

2. Use PascalCase: pdf.Root (NOT pdf.root)
3. Use register_xml_namespace() NOT register_namespace()
```

**Working Example**: Complete script with inline comments emphasizing string requirements

### Script Execution

**Location**: `Services/Remediation/AI/ScriptExecutor.cs`

- Creates temp directory per session
- Wraps user script in try/catch with sys/os/traceback imports
- Executes with `python3` (120s timeout)
- Keeps failed sessions for debugging
- Cleans up successful ones

**Script Structure**:
```python
import sys, os, traceback
INPUT_PDF = '/tmp/...pdf'
OUTPUT_PDF = '/tmp/...pdf'

try:
    # User's GPT-generated script here
    import pikepdf
    pdf = pikepdf.open(INPUT_PDF)
    # ... fixes ...
    pdf.save(OUTPUT_PDF)
except Exception as e:
    print(f'ERROR: {e}', file=sys.stderr)
    traceback.print_exc(file=sys.stderr)
    sys.exit(1)

if not os.path.exists(OUTPUT_PDF):
    print('ERROR: Output PDF not created', file=sys.stderr)
    sys.exit(1)

sys.exit(0)
```

---

## 💾 Solution Cache

### Purpose

Store successful GPT-generated scripts for reuse, avoiding repeated API calls and faster remediation.

### Implementation

**Location**: `Services/Remediation/AI/SolutionCache.cs`

**Persistence**: `remediation-solution-cache.json` (in project directory)

```csharp
// Loads on startup
LoadCacheFromDisk();

// Saves after each successful GPT solution
StoreGptSolution(violationPattern, category, solution, type);
SaveCacheToDisk();
```

### Current Limitations

**Keying**: Hash of exact violation text → too specific

```csharp
private string GenerateKey(string pattern)
{
    using (var sha = SHA256.Create())
    {
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(pattern.ToLower()));
        return Convert.ToBase64String(hash).Substring(0, 12);
    }
}
```

### Future Enhancement Needed

**Problem**: A script that fixes "Form → Form" won't match "P → P" or "Div → Div"

**Solution**: Extract patterns, not exact text
- Detect "circular role mapping" pattern
- Store one generic solution
- Match similar violations: `.*is mapped in a circular fashion.*`

**Not urgent** - GPT can handle variations for now, just less efficient.

---

## 🔤 Font Embedding Issues

### The Problem

"Font not embedded" errors persist despite extensive embedding logic in `AsposePdfService.cs`.

### What We Know

1. **AsposePdfService** has comprehensive font embedding:
   - `document.EmbedStandardFonts = true`
   - Base-14 font detection and substitution
   - ArialMT → Arial conversion
   - Full embedding (no subsetting to avoid CIDset errors)

2. **But fonts still aren't embedded** in final output

### Theories

**Theory A**: Fonts embedded early, then later services (PassportPDF, FormGraphicsTagging, etc.) add NEW content with unembedded fonts

**Theory B**: Font embedding logic doesn't actually work despite logs saying "✅ Marked X fonts for embedding"

### Evidence

From logs:
```
[Information] Total fonts: 14, Embedded: 14, Not embedded: 0
[Information] ✅ Marked 4 fonts in page resources for embedding
```

But PAC2024 still reports:
```
Font not embedded (1 error)
```

### Where to Investigate

1. **After AsposePdfService**: Check if subsequent services introduce unembedded fonts
2. **PassportPdfService**: Does it add content without embedding fonts?
3. **FormGraphicsTaggingService**: Check font usage
4. **Final validation**: Verify fonts actually embedded vs. just marked for embedding

### Whitespace + Unembedded Font Pattern

From user's screenshot: Whitespace content using unembedded fonts should be **deleted entirely**, not just wrapped or fixed.

**Existing scripts**:
- `detect_tagged_whitespace.py` - finds whitespace content
- `wrap_orphaned_whitespace.py` - wraps in Artifact tags

**Potential fix**: Delete whitespace content that uses unembedded fonts instead of trying to embed them.

---

## 📁 Recent Additions

### 1. CircularRoleMappingFixService

**File**: `Services/Remediation/CircularRoleMappingFixService.cs`

**Purpose**: Remove circular role mappings (e.g., Form → Form, P → P)

**Why This Happens**: Self-inflicted during remediation - documents don't come in with these mappings

**How It Works**:
```csharp
// Find mappings where key == value
foreach (var key in roleMap.KeySet())
{
    var value = roleMap.Get(key);
    if (key.Equals(value))
    {
        toRemove.Add(key);  // Remove Form → Form
    }
}
```

**Configuration**:
- Priority: 100 (low - reactive)
- IsRequired: false (only when violation detected)
- TargetCategory: Structure

### 2. Cache Persistence

Added save/load functionality to SolutionCache:
- Loads `remediation-solution-cache.json` on startup
- Saves after each successful GPT solution
- Survives app restarts

**Common Solutions Pre-loaded**:
- Form/widget role fixes
- Alt text addition for figures

---

## 🚧 Known Issues

### 1. Font Embedding Failures

**Severity**: High
**Impact**: PAC2024 still reports unembedded fonts
**Status**: Under investigation

### 2. Circular Role Mappings

**Severity**: Medium
**Impact**: Self-inflicted during remediation
**Status**: Fix implemented, needs testing
**Solution**: CircularRoleMappingFixService removes them

### 3. Cache Too Specific

**Severity**: Low
**Impact**: Reduced efficiency (GPT called more than necessary)
**Status**: Known limitation, future enhancement
**Workaround**: GPT can handle variations, just slower

---

## 📋 Next Steps

### Immediate

1. ✅ Fix CircularRoleMappingFixService priority (DONE: Priority=100, IsRequired=false)
2. 🔍 **Test closed-loop** with new circular mapping fix
3. 🔍 **Investigate font embedding** - where do unembedded fonts get introduced?

### Short Term

1. **Font debugging**: Add logging after each service to track font embedding status
2. **Early exit feature**: User wants "good enough, give me what you got" button to stop remediation early
3. **Cache patterns**: Consider generalizing cache keys to patterns (not urgent)

### Long Term

1. **Whitespace font cleanup**: Delete whitespace content with unembedded fonts
2. **Cache pattern matching**: Generalize solutions across similar violations
3. **GPT prompt tuning**: Continue improving based on script success rates

---

## 🔑 Key Files Reference

### Core Remediation
- `RemediationOrchestrator.cs` - Main closed-loop controller
- `RemediationExecutor.cs` - Runs services
- `ViolationAnalyzer.cs` - Categorizes violations
- `RemediationStrategySelector.cs` - Picks services for violations

### GPT Integration
- `GptRemediationService.cs` - Main GPT service (prompt building, script generation)
- `ScriptExecutor.cs` - Executes Python scripts safely
- `SolutionCache.cs` - Caches successful solutions
- `OpenAIService.cs` - API client

### Font Handling
- `AsposePdfService.cs` - Main font embedding logic (EmbedFonts method)
- `PdfFontEmbeddingService.cs` - Symbol font handling
- `TwcFontComplianceService.cs` - Font compliance checks

### Validation
- `VeraPdfService.cs` - PDF/UA validation via veraPDF CLI
- `PdfUAComplianceService.cs` - Compliance checking

---

## 💡 Important Patterns

### Service Priority Guidelines

**1-10**: Critical foundational fixes (always run first)
**20-50**: Standard fixes (run when needed)
**60-90**: Complex fixes
**100+**: Reactive/fallback fixes (only when violation detected)

### IsRequired Guidelines

**true**: Service should run on every pass (foundational)
**false**: Service runs only when its TargetCategory violations detected

### Cache Usage Pattern

```csharp
// 1. Check cache before calling GPT
var cachedSolution = _solutionCache.GetSolution(violationDescription);
if (cachedSolution != null)
{
    // Use cached script
    return await _scriptExecutor.ExecutePythonScriptAsync(
        cachedSolution.Solution, pdfBytes);
}

// 2. Call GPT if no cache hit
var script = await _openAiService.GenerateScript(violations);

// 3. Execute script
var result = await _scriptExecutor.ExecutePythonScriptAsync(script, pdfBytes);

// 4. If successful, cache it
if (result.Success)
{
    _solutionCache.StoreGptSolution(
        violationPattern, category, script, SolutionType.PythonScript);
}
```

---

## 🐛 Debugging Tips

### Check GPT Script Execution

Failed scripts stay in `/tmp/pdf-remediation-scripts/{sessionId}/`:
- `input.pdf` - Input PDF
- `remediate.py` - Generated script
- `execution.log` - stdout/stderr/exit code
- `output.pdf` - Output (if successful)

### Common Script Errors

1. **pdf.root → pdf.Root**: PascalCase required
2. **meta['pdfuaid:part'] = 1 → '1'**: Strings required for metadata
3. **register_namespace → register_xml_namespace**: Wrong method name
4. **Missing sys import**: ScriptExecutor now handles this automatically

### Validation Debugging

```bash
# Run veraPDF manually
/path/to/verapdf/verapdf --flavour ua1 --format json file.pdf

# Check specific rules
/path/to/verapdf/verapdf --flavour ua1 --format json file.pdf | jq '.jobs[].validationResult.details.ruleSummaries'
```

### Font Debugging

Add logging after each service in RemediationExecutor:
```csharp
_logger.LogInformation($"[FONT-DEBUG] After {service.ServiceName}:");
// Extract and log font embedding status
```

---

## 📚 Related Documentation

- `TESTING-GUIDE.md` - How to test remediation
- `PRODUCT-SPEC.md` - Overall system design
- `ROADMAP.md` - Future plans
- `USAGE-EXAMPLE.md` - How to use the system

---

## 🎯 Success Metrics

### What's Working

✅ Closed-loop remediation completes successfully
✅ GPT-5 generates and executes Python scripts
✅ Solution cache persists across restarts
✅ Most structural violations fixed automatically
✅ Whitespace violations handled
✅ Link structure fixed

### What Needs Work

❌ Font embedding not reliable
⚠️ Circular role mappings appear during remediation
⚠️ Cache too specific (not pattern-based)
⚠️ No early exit option

---

## 🔐 Security Notes

**API Keys**:
- `appsettings.json` is gitignored
- Use `appsettings.template.json` as template
- Never commit real keys

**Script Execution**:
- Scripts run in isolated temp directories
- 120-second timeout prevents infinite loops
- Automatic cleanup on success

**Cache File**:
- `remediation-solution-cache.json` is safe to commit (contains no secrets)
- Just Python scripts and metadata

---

**End of Document**

Continue from here when resuming work. Focus on font embedding investigation or testing closed-loop with circular mapping fixes.
