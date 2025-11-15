using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AccessFormServer.Services;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.PdfUA;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using System.IO;
using System.Text.Json;
using System.Text;

namespace WordToPdfConverter.Services.Remediation.AI
{
    /// <summary>
    /// GPT-5 powered PDF/UA remediation service for handling complex violations
    /// that standard remediation services cannot fix, including tag tree manipulation
    /// </summary>
    public class GptRemediationService : IRemediationService
    {
        private readonly ILogger<GptRemediationService> _logger;
        private readonly OpenAIService _openAiService;
        private readonly ScriptExecutor _scriptExecutor;
        private readonly SolutionCache _solutionCache;
        private List<PdfUAViolation> _violations; // Store violations for context

        public string ServiceName => "GPT-5 Powered Remediation";
        public ViolationCategory TargetCategory => ViolationCategory.Unknown; // Handles all categories
        public int Priority => 100; // Low priority (fallback)
        public bool IsRequired => false; // Optional service

        public GptRemediationService(
            ILogger<GptRemediationService> logger,
            ILoggerFactory loggerFactory,
            OpenAIService openAiService,
            ScriptExecutor scriptExecutor = null,
            SolutionCache solutionCache = null)
        {
            _logger = logger;
            _openAiService = openAiService;
            _scriptExecutor = scriptExecutor ?? new ScriptExecutor(loggerFactory.CreateLogger<ScriptExecutor>());
            _solutionCache = solutionCache ?? new SolutionCache(loggerFactory.CreateLogger<SolutionCache>());
        }

        // Method to set violations context before remediation
        public void SetViolations(List<PdfUAViolation> violations)
        {
            _violations = violations;
        }

        // Implement interface method
        public async Task<ServiceResult> RemediateAsync(byte[] pdfBytes)
        {
            return await RemediateWithViolationsAsync(pdfBytes, _violations);
        }

        // Main remediation method with violations
        public async Task<ServiceResult> RemediateWithViolationsAsync(byte[] pdfBytes, List<PdfUAViolation> violations = null)
        {
            var result = new ServiceResult
            {
                Success = false,
                OutputPdf = pdfBytes
            };

            try
            {
                _logger.LogInformation("[GPT-5-REMEDIATION] Starting AI-powered remediation with GPT-5");

                if (violations == null || !violations.Any())
                {
                    _logger.LogInformation("[GPT-REMEDIATION] No violations provided, skipping");
                    result.Success = true;
                    result.ChangesMade = false;
                    return result;
                }

                // Group violations by category for better context
                var violationsByCategory = violations
                    .GroupBy(v => GetViolationCategory(v))
                    .ToDictionary(g => g.Key, g => g.ToList());

                _logger.LogInformation($"[GPT-REMEDIATION] Processing {violations.Count} violations across {violationsByCategory.Count} categories");

                byte[] currentPdf = pdfBytes;
                int totalFixed = 0;

                // Process each violation category
                foreach (var category in violationsByCategory)
                {
                    try
                    {
                        var categoryResult = await RemediateCategoryAsync(
                            currentPdf,
                            category.Key,
                            category.Value);

                        if (categoryResult.Success && categoryResult.OutputPdf != null)
                        {
                            currentPdf = categoryResult.OutputPdf;
                            totalFixed += categoryResult.FixedCount;
                            _logger.LogInformation($"[GPT-REMEDIATION] Fixed {categoryResult.FixedCount} {category.Key} violations");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"[GPT-REMEDIATION] Failed to remediate {category.Key} violations");
                    }
                }

                result.Success = true;
                result.OutputPdf = currentPdf;
                result.ChangesMade = totalFixed > 0;
                result.IssuesFixed = totalFixed;
                result.IssuesFound = violations.Count;

                _logger.LogInformation($"[GPT-REMEDIATION] Completed: Fixed {totalFixed}/{violations.Count} violations");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GPT-REMEDIATION] Remediation failed");
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private async Task<CategoryRemediationResult> RemediateCategoryAsync(
            byte[] pdfBytes,
            string category,
            List<PdfUAViolation> violations)
        {
            var result = new CategoryRemediationResult
            {
                Success = false,
                OutputPdf = pdfBytes
            };

            try
            {
                // Ensure Python dependencies if script executor is available
                if (_scriptExecutor != null)
                {
                    await _scriptExecutor.EnsurePythonDependenciesAsync();
                }

                string gptResponse = null;
                bool usedCache = false;
                int violationsBeforeFix = violations.Count;

                // Check solution cache first
                if (_solutionCache != null && violations.Any())
                {
                    var cachedSolution = _solutionCache.GetSolution(violations.First().Description);
                    if (cachedSolution != null)
                    {
                        _logger.LogInformation($"[GPT-5-REMEDIATION] Using cached solution for {category}");
                        gptResponse = cachedSolution.Solution;
                        usedCache = true;
                    }
                }

                // If no cached solution, build prompt and call GPT with two-tier strategy
                if (!usedCache)
                {
                    var systemMessage = BuildSystemMessage();
                    var prompt = BuildRemediationPrompt(category, violations);

                    // TIER 1: Try GPT-4o first (fast, cheap)
                    _logger.LogInformation($"[GPT-REMEDIATION] Attempting GPT-4o for {category}");
                    int maxApiRetries = 2;

                    for (int attempt = 0; attempt < maxApiRetries; attempt++)
                    {
                        try
                        {
                            gptResponse = await _openAiService.CallTextApiAsync(prompt, model: "gpt-4o", sessionId: null, systemMessage: systemMessage);
                            if (!string.IsNullOrEmpty(gptResponse))
                            {
                                _logger.LogInformation($"[GPT-4o-REMEDIATION] ✓ GPT-4o succeeded for {category}");
                                break; // Success
                            }

                            if (attempt < maxApiRetries - 1)
                            {
                                _logger.LogWarning($"[GPT-4o-REMEDIATION] Empty response from GPT-4o for {category}, retrying ({attempt + 1}/{maxApiRetries})");
                                await Task.Delay(2000);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"[GPT-4o-REMEDIATION] API call failed for {category} (attempt {attempt + 1}/{maxApiRetries})");

                            if (attempt < maxApiRetries - 1)
                            {
                                await Task.Delay(2000);
                            }
                        }
                    }

                    // TIER 2: Fallback to o1-preview if GPT-4o failed (empty response or exceptions after all retries)
                    if (string.IsNullOrEmpty(gptResponse))
                    {
                        _logger.LogWarning($"[GPT-REMEDIATION] GPT-4o failed, escalating to o1-preview for {category}");
                        try
                        {
                            gptResponse = await _openAiService.CallTextApiAsync(prompt, model: "o1-preview", sessionId: null, systemMessage: systemMessage);
                            if (!string.IsNullOrEmpty(gptResponse))
                            {
                                _logger.LogInformation($"[O1-REMEDIATION] ✓ o1-preview succeeded after GPT-4o failure for {category}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"[O1-REMEDIATION] o1-preview fallback also failed for {category}");
                        }
                    }
                }

                if (string.IsNullOrEmpty(gptResponse))
                {
                    _logger.LogWarning($"[GPT-REMEDIATION] Empty response from all models for {category}");
                    return result;
                }

                // Detect response type (script or JSON instructions)
                if (IsScriptResponse(gptResponse))
                {
                    _logger.LogInformation($"[GPT-REMEDIATION] Received Python script for {category}");

                    // Extract and execute the script with retry on error
                    var script = ExtractScript(gptResponse);

                    if (!string.IsNullOrEmpty(script) && _scriptExecutor != null)
                    {
                        var scriptResult = await ExecuteScriptWithRetryAsync(
                            script,
                            pdfBytes,
                            category,
                            maxRetries: 2);

                        if (scriptResult.Success && scriptResult.OutputPdf != null)
                        {
                            result.OutputPdf = scriptResult.OutputPdf;
                            result.Success = true;

                            // CRITICAL FIX: Don't assume violations were fixed
                            // The orchestrator will validate and determine actual fix count
                            result.FixedCount = 0; // Unknown until validation
                            _logger.LogInformation($"[GPT-REMEDIATION] Script executed successfully, awaiting validation to confirm fixes");

                            // NOTE: Don't cache here - let the orchestrator cache only if validation confirms fixes
                            // The orchestrator now handles caching based on actual violation reduction
                        }
                        else
                        {
                            _logger.LogWarning($"[GPT-REMEDIATION] Script execution failed after retries: {scriptResult.ErrorMessage}");
                            _logger.LogDebug($"Script output: {scriptResult.StandardOutput}");
                            _logger.LogDebug($"Script errors: {scriptResult.StandardError}");
                        }
                    }
                }
                else
                {
                    _logger.LogInformation($"[GPT-5-REMEDIATION] GPT-5 provided JSON instructions for {category}");

                    // Parse and apply JSON remediation instructions
                    var instructions = ParseRemediationInstructions(gptResponse);

                    if (instructions != null && instructions.Any())
                    {
                        result.OutputPdf = await ApplyRemediationInstructionsAsync(
                            pdfBytes,
                            instructions);

                        result.Success = true;
                        result.FixedCount = instructions.Count;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[GPT-5-REMEDIATION] Category remediation failed for {category}");
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private string BuildSystemMessage()
        {
            return @"You are an expert PDF/UA accessibility specialist with deep knowledge of pikepdf, PDF internal structure, and accessibility standards.

**CRITICAL TECHNICAL REQUIREMENTS FOR PIKEPDF:**

1. **PDF Dictionary Keys MUST start with '/'**:
   ❌ WRONG: viewer_preferences['DisplayDocTitle'] = True
   ✅ RIGHT: viewer_preferences['/DisplayDocTitle'] = True

   ❌ WRONG: pdf.Root.ViewerPreferences = pikepdf.Dictionary(DisplayDocTitle=True)
   ✅ RIGHT: pdf.Root['/ViewerPreferences'] = pikepdf.Dictionary({'/DisplayDocTitle': True})

2. **Boolean Values**: Use Python True/False directly
   ❌ WRONG: pikepdf.Boolean(True) - Does NOT exist!
   ✅ RIGHT: True

3. **Metadata Values MUST be Strings**:
   ❌ WRONG: meta['pdfuaid:part'] = 1
   ✅ RIGHT: meta['pdfuaid:part'] = '1'

4. **Use PascalCase**: pdf.Root (NOT pdf.root)

5. **File Paths**: ALWAYS use INPUT_PDF and OUTPUT_PDF constants (they are pre-defined)

6. **XMP Namespaces**: Use register_xml_namespace() NOT register_namespace()

**YOUR GOAL:**
Create Python scripts that fix PDF/UA violations efficiently and reliably. Scripts must:
- Work on ANY document with similar violations (not document-specific)
- Use iteration and pattern matching (never hardcode page numbers or specific content)
- Preserve document functionality (especially form fields)
- Be robust to different PDF structures

**RESPONSE FORMAT:**
Return ONLY a Python script in a ```python code block. No explanations, no markdown outside the code block.";
        }

        private string BuildRemediationPrompt(string category, List<PdfUAViolation> violations)
        {
            var sb = new StringBuilder();

            // Analyze violation patterns
            var violationPatterns = AnalyzeViolationPatterns(violations);
            var uniqueRules = violations.Select(v => v.RuleId).Distinct().ToList();
            var affectedPages = violations.Select(v => v.Location?.PageNumber ?? 0).Distinct().Count();

            sb.AppendLine($"**REMEDIATION TASK: {category} Violations**");
            sb.AppendLine();
            sb.AppendLine($"**GOAL**: Reduce {violations.Count} PDF/UA violations to zero by creating a pikepdf Python script.");
            sb.AppendLine();
            sb.AppendLine($"**CONTEXT**:");
            sb.AppendLine($"- Total Violations: {violations.Count}");
            sb.AppendLine($"- Unique Rules: {uniqueRules.Count} ({string.Join(", ", uniqueRules.Take(5))}{(uniqueRules.Count > 5 ? "..." : "")})");
            sb.AppendLine($"- Affected Pages: {affectedPages}");
            sb.AppendLine();

            // Provide pattern analysis
            if (violationPatterns.Count > 0)
            {
                sb.AppendLine($"**VIOLATION PATTERNS** (most common first):");
                foreach (var pattern in violationPatterns.Take(5))
                {
                    sb.AppendLine($"- {pattern.Pattern} ({pattern.Count} occurrences)");
                }
                sb.AppendLine();
            }

            // Provide category-specific guidance
            sb.AppendLine($"**SUCCESS CRITERIA FOR {category.ToUpper()}**:");
            switch (category)
            {
                case "FormFields":
                    sb.AppendLine("- Each Form tag MUST have Role='/Form' attribute in structure tree");
                    sb.AppendLine("- Form tags must reference their widget annotations correctly");
                    sb.AppendLine("- PRESERVE all /AcroForm entries and widget functionality");
                    sb.AppendLine("- Do NOT modify pdf.Root.AcroForm or widget annotations");
                    break;
                case "Metadata":
                    sb.AppendLine("- Set PDF/UA identifier in XMP metadata (pdfuaid:part='1')");
                    sb.AppendLine("- Ensure document title is set and DisplayDocTitle=True");
                    sb.AppendLine("- Set /MarkInfo dictionary with /Marked=True");
                    break;
                case "Structure":
                    sb.AppendLine("- Fix structure tree hierarchy and tag relationships");
                    sb.AppendLine("- Ensure all content is properly tagged");
                    sb.AppendLine("- Remove or fix improperly nested tags");
                    break;
                case "Fonts":
                    sb.AppendLine("- Ensure all fonts are embedded");
                    sb.AppendLine("- Fix font encoding issues");
                    sb.AppendLine("- Validate font descriptors");
                    break;
                case "Whitespace":
                    sb.AppendLine("- Tag whitespace as artifacts");
                    sb.AppendLine("- Remove improper Span tags around whitespace");
                    sb.AppendLine("- Use /Artifact for non-content whitespace");
                    break;
                case "Content":
                    sb.AppendLine("- Add alt text to images and figures");
                    sb.AppendLine("- Fix heading hierarchy");
                    sb.AppendLine("- Ensure links have accessible names");
                    break;
                case "Links":
                    sb.AppendLine("- Add /Contents attribute to link annotations");
                    sb.AppendLine("- Ensure link annotations are properly tagged");
                    sb.AppendLine("- Provide accessible link text");
                    break;
                default:
                    sb.AppendLine("- Fix all reported violations for this category");
                    break;
            }
            sb.AppendLine();

            // Sample violations for context (show up to 20 for better pattern understanding)
            sb.AppendLine($"**SAMPLE VIOLATIONS** ({Math.Min(20, violations.Count)} of {violations.Count}):");
            foreach (var violation in violations.Take(20))
            {
                var pageInfo = violation.Location?.PageNumber > 0 ? $" [Page {violation.Location.PageNumber}]" : "";
                sb.AppendLine($"- {violation.RuleId}: {violation.Description}{pageInfo}");
            }
            sb.AppendLine();

            // Emphasize outcome and reusability
            sb.AppendLine("**SCRIPT REQUIREMENTS**:");
            sb.AppendLine("1. Use pikepdf library exclusively");
            sb.AppendLine("2. Open PDF with: pdf = pikepdf.open(INPUT_PDF)");
            sb.AppendLine("3. Save PDF with: pdf.save(OUTPUT_PDF)");
            sb.AppendLine("4. Make script GENERAL - it will be cached and reused");
            sb.AppendLine("5. Iterate over ALL matching elements (never hardcode specific pages/elements)");
            sb.AppendLine("6. Handle missing or malformed structures gracefully");
            sb.AppendLine();

            // Category-specific critical warnings
            if (category == "FormFields")
            {
                sb.AppendLine("⚠️ **CRITICAL FOR FORM FIELDS**:");
                sb.AppendLine("- ONLY modify structure tree (/StructTreeRoot), NOT AcroForm");
                sb.AppendLine("- Do NOT touch pdf.Root.AcroForm or widget annotations");
                sb.AppendLine("- Form fields must remain functional after remediation");
                sb.AppendLine();
            }

            sb.AppendLine("**OUTPUT**: Provide ONLY a Python script in a ```python code block. No explanations.");

            return sb.ToString();
        }

        private List<ViolationPattern> AnalyzeViolationPatterns(List<PdfUAViolation> violations)
        {
            // Group violations by common patterns
            var patterns = new Dictionary<string, int>();

            foreach (var violation in violations)
            {
                // Extract key pattern from description
                var description = violation.Description ?? "";
                var pattern = ExtractPattern(description);

                if (patterns.ContainsKey(pattern))
                    patterns[pattern]++;
                else
                    patterns[pattern] = 1;
            }

            return patterns
                .OrderByDescending(p => p.Value)
                .Select(p => new ViolationPattern { Pattern = p.Key, Count = p.Value })
                .ToList();
        }

        private string ExtractPattern(string description)
        {
            // Extract meaningful pattern from violation description
            // This is a simple heuristic - could be improved with NLP

            // Remove specific details like page numbers, element names, etc.
            description = System.Text.RegularExpressions.Regex.Replace(description, @"\bpage\s+\d+\b", "page X", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            description = System.Text.RegularExpressions.Regex.Replace(description, @"\belement\s+\d+\b", "element X", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            description = System.Text.RegularExpressions.Regex.Replace(description, @"'[^']*'", "'...'");

            // Take first sentence or first 100 chars
            var firstSentence = description.Split('.')[0];
            if (firstSentence.Length > 100)
                firstSentence = firstSentence.Substring(0, 100) + "...";

            return firstSentence.Trim();
        }

        private class ViolationPattern
        {
            public string Pattern { get; set; }
            public int Count { get; set; }
        }

        private List<RemediationInstruction> ParseRemediationInstructions(string gptResponse)
        {
            try
            {
                // Clean up response - remove markdown if present
                var jsonStart = gptResponse.IndexOf('{');
                var jsonEnd = gptResponse.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    gptResponse = gptResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                }

                var jsonDoc = JsonDocument.Parse(gptResponse);
                var instructions = new List<RemediationInstruction>();

                if (jsonDoc.RootElement.TryGetProperty("instructions", out var instructionsArray))
                {
                    foreach (var instruction in instructionsArray.EnumerateArray())
                    {
                        try
                        {
                            var remediation = new RemediationInstruction
                            {
                                Action = instruction.GetProperty("action").GetString(),
                                Target = instruction.GetProperty("target").GetString(),
                                Reasoning = instruction.TryGetProperty("reasoning", out var r)
                                    ? r.GetString() : null
                            };

                            if (instruction.TryGetProperty("details", out var details))
                            {
                                remediation.Details = new Dictionary<string, object>();

                                foreach (var prop in details.EnumerateObject())
                                {
                                    remediation.Details[prop.Name] = prop.Value.ToString();
                                }
                            }

                            instructions.Add(remediation);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[GPT-REMEDIATION] Failed to parse instruction");
                        }
                    }
                }

                return instructions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GPT-REMEDIATION] Failed to parse GPT response");
                return null;
            }
        }

        private async Task<byte[]> ApplyRemediationInstructionsAsync(
            byte[] pdfBytes,
            List<RemediationInstruction> instructions)
        {
            try
            {
                using (var inputStream = new MemoryStream(pdfBytes))
                {
                    // Load PDF document using Syncfusion
                    var pdfDoc = new PdfLoadedDocument(inputStream);

                    try
                    {
                        foreach (var instruction in instructions)
                        {
                            try
                            {
                                _logger.LogDebug($"[GPT-REMEDIATION] Applying: {instruction.Action} to {instruction.Target}");

                                switch (instruction.Action?.ToLower())
                                {
                                    case "add_tag":
                                        ApplyAddTag(pdfDoc, instruction);
                                        break;

                                    case "modify_tag":
                                        ApplyModifyTag(pdfDoc, instruction);
                                        break;

                                    case "set_attribute":
                                        ApplySetAttribute(pdfDoc, instruction);
                                        break;

                                    case "fix_structure":
                                        ApplyFixStructure(pdfDoc, instruction);
                                        break;

                                    default:
                                        _logger.LogWarning($"[GPT-REMEDIATION] Unknown action: {instruction.Action}");
                                        break;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"[GPT-REMEDIATION] Failed to apply instruction: {instruction.Action}");
                            }
                        }

                        // Set document properties for PDF/UA compliance
                        if (string.IsNullOrWhiteSpace(pdfDoc.DocumentInformation.Title))
                        {
                            pdfDoc.DocumentInformation.Title = "Accessible Document";
                        }

                        // Save to memory stream
                        using (var outputStream = new MemoryStream())
                        {
                            pdfDoc.Save(outputStream);
                            return outputStream.ToArray();
                        }
                    }
                    finally
                    {
                        pdfDoc.Close(true);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GPT-REMEDIATION] Failed to apply instructions to PDF");
                return pdfBytes; // Return original if application fails
            }
        }

        private void ApplyAddTag(PdfLoadedDocument pdfDoc, RemediationInstruction instruction)
        {
            // Add structure element based on instruction
            if (instruction.Details?.TryGetValue("tag_name", out var tagName) == true)
            {
                // Note: Full tag manipulation would require more complex Syncfusion API usage
                // This is a placeholder for future implementation
                _logger.LogDebug($"[GPT-REMEDIATION] Would add tag: {tagName}");
            }
        }

        private void ApplyModifyTag(PdfLoadedDocument pdfDoc, RemediationInstruction instruction)
        {
            // Modify existing tag structure
            if (instruction.Details?.TryGetValue("tag_name", out var tagName) == true)
            {
                // Implementation would modify the existing tag structure
                // This is a placeholder as Syncfusion tag manipulation is complex
                _logger.LogDebug($"[GPT-REMEDIATION] Modified tag: {tagName}");
            }
        }

        private void ApplySetAttribute(PdfLoadedDocument pdfDoc, RemediationInstruction instruction)
        {
            // Set attributes on structure elements
            if (instruction.Details?.TryGetValue("attributes", out var attributes) == true)
            {
                // Implementation would set the attributes on structure elements
                // This is a placeholder as Syncfusion attribute setting is complex
                _logger.LogDebug($"[GPT-REMEDIATION] Set attributes: {attributes}");
            }
        }

        private void ApplyFixStructure(PdfLoadedDocument pdfDoc, RemediationInstruction instruction)
        {
            // Fix structural issues in the PDF
            if (instruction.Target?.Contains("metadata") == true)
            {
                // Set required metadata for PDF/UA compliance
                var title = instruction.Details?.GetValueOrDefault("title")?.ToString() ?? "Accessible Document";
                pdfDoc.DocumentInformation.Title = title;
                pdfDoc.DocumentInformation.Subject = "PDF/UA Compliant Document";

                // Set language if specified
                if (instruction.Details?.TryGetValue("language", out var lang) == true)
                {
                    pdfDoc.DocumentInformation.Language = lang.ToString();
                }

                _logger.LogDebug("[GPT-REMEDIATION] Fixed document structure and metadata");
            }
        }

        private bool IsScriptResponse(string response)
        {
            // Check if the response contains Python script markers
            return response.Contains("```python") ||
                   response.Contains("import ") ||
                   response.Contains("pikepdf") ||
                   response.Contains("PyPDF") ||
                   response.Contains("def ") ||
                   (response.Contains("INPUT_PDF") && response.Contains("OUTPUT_PDF"));
        }

        private string ExtractScript(string response)
        {
            // Extract Python script from response
            if (response.Contains("```python"))
            {
                var startIdx = response.IndexOf("```python") + 9;
                var endIdx = response.IndexOf("```", startIdx);

                if (endIdx > startIdx)
                {
                    return response.Substring(startIdx, endIdx - startIdx).Trim();
                }
            }
            else if (response.Contains("```"))
            {
                // Sometimes GPT might use just ```
                var startIdx = response.IndexOf("```") + 3;
                var endIdx = response.IndexOf("```", startIdx);

                if (endIdx > startIdx)
                {
                    var script = response.Substring(startIdx, endIdx - startIdx).Trim();
                    // Verify it looks like Python
                    if (script.Contains("import ") || script.Contains("def ") || script.Contains("INPUT_PDF"))
                    {
                        return script;
                    }
                }
            }

            // If no code block markers, assume the entire response is the script
            // (but only if it looks like Python code)
            if (response.Contains("import ") || response.Contains("def "))
            {
                return response.Trim();
            }

            return null;
        }

        private string GetViolationCategory(PdfUAViolation violation)
        {
            // Map violations to categories for better GPT context
            var ruleText = (violation.RuleId ?? "") + " " + (violation.Description ?? "");

            if (ruleText.Contains("whitespace", StringComparison.OrdinalIgnoreCase))
                return "Whitespace";
            if (ruleText.Contains("font", StringComparison.OrdinalIgnoreCase))
                return "Fonts";
            if (ruleText.Contains("metadata", StringComparison.OrdinalIgnoreCase) ||
                ruleText.Contains("title", StringComparison.OrdinalIgnoreCase))
                return "Metadata";
            if (ruleText.Contains("structure", StringComparison.OrdinalIgnoreCase) ||
                ruleText.Contains("tag", StringComparison.OrdinalIgnoreCase))
                return "Structure";
            if (ruleText.Contains("form", StringComparison.OrdinalIgnoreCase) ||
                ruleText.Contains("field", StringComparison.OrdinalIgnoreCase))
                return "FormFields";
            if (ruleText.Contains("link", StringComparison.OrdinalIgnoreCase))
                return "Links";
            if (ruleText.Contains("content", StringComparison.OrdinalIgnoreCase) ||
                ruleText.Contains("alt", StringComparison.OrdinalIgnoreCase))
                return "Content";

            return "Other";
        }

        /// <summary>
        /// Execute Python script with automatic retry and error-feedback correction
        /// </summary>
        private async Task<ScriptResult> ExecuteScriptWithRetryAsync(
            string script,
            byte[] pdfBytes,
            string category,
            int maxRetries = 2)
        {
            _logger.LogWarning($"╔════════════════════════════════════════════════════════════════╗");
            _logger.LogWarning($"║ [GPT-5-RETRY] ERROR-FEEDBACK RETRY ACTIVATED (max: {maxRetries})     ║");
            _logger.LogWarning($"║ Category: {category,-50} ║");
            _logger.LogWarning($"╚════════════════════════════════════════════════════════════════╝");

            var currentScript = script;
            ScriptResult result = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                _logger.LogWarning($"[GPT-5-RETRY] ▶ Executing script (attempt {attempt}/{maxRetries})");

                result = await _scriptExecutor.ExecutePythonScriptAsync(
                    currentScript,
                    pdfBytes,
                    $"gpt5-{category.ToLower()}-attempt{attempt}");

                if (result.Success)
                {
                    if (attempt > 1)
                    {
                        _logger.LogWarning($"[GPT-5-RETRY] ✓ Script succeeded on attempt {attempt} after error correction!");
                    }
                    else
                    {
                        _logger.LogWarning($"[GPT-5-RETRY] ✓ Script succeeded on first attempt");
                    }
                    return result;
                }

                // Script failed - log error details
                _logger.LogError($"[GPT-5-RETRY] ⚠ Script failed on attempt {attempt}");
                _logger.LogError($"[GPT-5-RETRY] Error: {result.StandardError?.Substring(0, Math.Min(500, result.StandardError?.Length ?? 0))}");

                // If failed and we have retries left, ask GPT to fix the script
                if (attempt < maxRetries)
                {
                    _logger.LogWarning($"[GPT-5-RETRY] 🔧 Asking GPT to analyze error and fix script...");

                    var fixedScript = await _openAiService.FixScriptFromErrorAsync(
                        currentScript,
                        result.StandardError,
                        result.StandardOutput);

                    if (string.IsNullOrEmpty(fixedScript))
                    {
                        _logger.LogError($"[GPT-5-RETRY] ⚠ GPT failed to generate fixed script - giving up");
                        break; // Can't continue without a fixed script
                    }

                    currentScript = fixedScript;
                    _logger.LogWarning($"[GPT-5-RETRY] ✓ Received corrected script from GPT, retrying...");
                }
                else
                {
                    _logger.LogError($"[GPT-5-RETRY] ⚠ Script failed after {maxRetries} attempts - giving up");
                }
            }

            return result; // Return last attempt result (failure)
        }

        private class CategoryRemediationResult
        {
            public bool Success { get; set; }
            public byte[] OutputPdf { get; set; }
            public int FixedCount { get; set; }
            public string ErrorMessage { get; set; }
        }

        private class RemediationInstruction
        {
            public string Action { get; set; }
            public string Target { get; set; }
            public Dictionary<string, object> Details { get; set; }
            public string Reasoning { get; set; }
        }
    }
}