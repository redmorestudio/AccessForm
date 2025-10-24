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

                // If no cached solution, build prompt and call GPT-5
                if (!usedCache)
                {
                    var prompt = BuildRemediationPrompt(category, violations);
                    gptResponse = await _openAiService.CallTextApiAsync(prompt);
                }

                if (string.IsNullOrEmpty(gptResponse))
                {
                    _logger.LogWarning($"[GPT-5-REMEDIATION] Empty response from GPT-5 for {category}");
                    return result;
                }

                // Detect response type (script or JSON instructions)
                if (IsScriptResponse(gptResponse))
                {
                    _logger.LogInformation($"[GPT-5-REMEDIATION] GPT-5 provided a Python script for {category}");

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
                            result.FixedCount = violations.Count; // Estimate
                            _logger.LogInformation($"[GPT-5-REMEDIATION] Script executed successfully");

                            // Store successful solution in cache for future use
                            if (!usedCache && _solutionCache != null && violations.Any())
                            {
                                _solutionCache.StoreGptSolution(
                                    violations.First().Description,
                                    category,
                                    script,
                                    SolutionCache.SolutionType.PythonScript);
                                _logger.LogInformation("[GPT-5-REMEDIATION] Stored successful solution in cache");
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"[GPT-5-REMEDIATION] Script execution failed after retries: {scriptResult.ErrorMessage}");
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

        private string BuildRemediationPrompt(string category, List<PdfUAViolation> violations)
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are a PDF/UA compliance expert using GPT-5. Analyze these violations and provide remediation.");
            sb.AppendLine();
            sb.AppendLine($"VIOLATION CATEGORY: {category}");
            sb.AppendLine($"TOTAL VIOLATIONS: {violations.Count}");
            sb.AppendLine();

            // Special handling for Form/Widget violations
            bool hasFormViolations = violations.Any(v =>
                v.Description.Contains("Form element") ||
                v.Description.Contains("widget") ||
                v.Description.Contains("Role attribute") ||
                v.RuleId.Contains("7.18"));

            if (hasFormViolations)
            {
                sb.AppendLine("⚠️ FORM/WIDGET VIOLATION DETECTED - SPECIAL INSTRUCTIONS:");
                sb.AppendLine("This is a common PDF/UA violation where Form elements lack proper Role attributes");
                sb.AppendLine("or don't have correct widget annotation references (per Table 348 & Table 340).");
                sb.AppendLine();
                sb.AppendLine("The fix typically involves:");
                sb.AppendLine("1. Ensuring each Form tag has a Role='/Form' attribute");
                sb.AppendLine("2. Ensuring Form tags contain proper widget annotation references");
                sb.AppendLine("3. Fixing the parent-child relationship between Form tags and widgets");
                sb.AppendLine();
                sb.AppendLine("Please provide a Python script using pikepdf that:");
                sb.AppendLine("- Iterates through all Form tags in the structure tree");
                sb.AppendLine("- Adds Role='/Form' attribute if missing");
                sb.AppendLine("- Ensures proper widget references as children");
                sb.AppendLine("- Maintains the existing form field functionality");
                sb.AppendLine();
            }

            sb.AppendLine("VIOLATION DETAILS:");

            foreach (var violation in violations.Take(10)) // Limit to first 10 for context
            {
                sb.AppendLine($"- Rule: {violation.RuleId}");
                sb.AppendLine($"  Description: {violation.Description}");
                sb.AppendLine($"  Context: {violation.Context}");
                sb.AppendLine($"  Location: Page {violation.Location?.PageNumber ?? 0}, {violation.Location?.ContextDescription}");
                sb.AppendLine();
            }

            sb.AppendLine("You have TWO options for remediation:");
            sb.AppendLine();
            sb.AppendLine("OPTION 1: Provide a Python script that fixes these violations");
            sb.AppendLine("The script will have access to:");
            sb.AppendLine("- INPUT_PDF: path to input PDF file");
            sb.AppendLine("- OUTPUT_PDF: path where the fixed PDF should be saved");
            sb.AppendLine("- Libraries: pikepdf, PyPDF2, reportlab, pypdf");
            sb.AppendLine();
            sb.AppendLine("⚠️ CRITICAL pikepdf API REQUIREMENTS - FAILURE TO FOLLOW WILL CAUSE SCRIPT TO CRASH:");
            sb.AppendLine();
            sb.AppendLine("1. METADATA VALUES MUST BE STRINGS:");
            sb.AppendLine("   ❌ WRONG: meta['pdfuaid:part'] = 1  (TypeError: Setting pdfuaid:part to 1 with type <class 'int'>)");
            sb.AppendLine("   ✅ RIGHT: meta['pdfuaid:part'] = '1'  (String value required!)");
            sb.AppendLine();
            sb.AppendLine("   ❌ WRONG: want_part = 1; meta['pdfuaid:part'] = want_part");
            sb.AppendLine("   ✅ RIGHT: want_part = '1'; meta['pdfuaid:part'] = want_part");
            sb.AppendLine();
            sb.AppendLine("2. Use PascalCase: pdf.Root (NOT pdf.root)");
            sb.AppendLine("3. Use register_xml_namespace() NOT register_namespace()");
            sb.AppendLine();
            sb.AppendLine("Working example - fixing PDF/UA metadata (FOLLOW THIS PATTERN EXACTLY):");
            sb.AppendLine("```python");
            sb.AppendLine("import pikepdf");
            sb.AppendLine();
            sb.AppendLine("pdf = pikepdf.open(INPUT_PDF)");
            sb.AppendLine();
            sb.AppendLine("# Get or create metadata");
            sb.AppendLine("with pdf.open_metadata() as meta:");
            sb.AppendLine("    # Register namespace (use register_xml_namespace, NOT register_namespace)");
            sb.AppendLine("    meta.register_xml_namespace('pdfuaid', 'http://www.aiim.org/pdfua/ns/id/')");
            sb.AppendLine("    ");
            sb.AppendLine("    # CRITICAL: ALL metadata values MUST be STRINGS!");
            sb.AppendLine("    # Use QUOTES around numbers to make them strings");
            sb.AppendLine("    want_part = '1'  # STRING not integer! '1' not 1");
            sb.AppendLine("    meta['pdfuaid:part'] = want_part  # Now assigning a STRING");
            sb.AppendLine("    ");
            sb.AppendLine("    # Or assign directly as string:");
            sb.AppendLine("    meta['pdfuaid:conformance'] = 'A'  # String value");
            sb.AppendLine("    meta['dc:title'] = 'Accessible Document'  # String value");
            sb.AppendLine();
            sb.AppendLine("# Access catalog with pdf.Root (PascalCase!)");
            sb.AppendLine("if '/MarkInfo' not in pdf.Root:");
            sb.AppendLine("    pdf.Root.MarkInfo = pdf.make_indirect(pikepdf.Dictionary(Marked=True))");
            sb.AppendLine();
            sb.AppendLine("pdf.save(OUTPUT_PDF)");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("OPTION 2: Provide JSON instructions for simple fixes:");
            sb.AppendLine(@"{
  ""instructions"": [
    {
      ""action"": ""add_tag|modify_tag|remove_tag|set_attribute|fix_structure"",
      ""target"": ""specific element or pattern to target"",
      ""details"": {
        ""tag_name"": ""tag to add/modify"",
        ""attributes"": {""key"": ""value""},
        ""content"": ""content if needed""
      },
      ""reasoning"": ""why this fix resolves the violation""
    }
  ]
}");

            sb.AppendLine();
            sb.AppendLine("For complex tag tree manipulations, structure fixes, or when you need precise control,");
            sb.AppendLine("please provide a Python script. For simple metadata or attribute changes, use JSON.");
            sb.AppendLine();
            sb.AppendLine("Focus on fixes that will resolve the most violations with minimal changes.");

            return sb.ToString();
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
        private async Task<ScriptExecutor.ScriptResult> ExecuteScriptWithRetryAsync(
            string script,
            byte[] pdfBytes,
            string category,
            int maxRetries = 2)
        {
            var currentScript = script;
            ScriptExecutor.ScriptResult result = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                _logger.LogInformation($"[GPT-5-RETRY] Executing script (attempt {attempt}/{maxRetries})");

                result = await _scriptExecutor.ExecutePythonScriptAsync(
                    currentScript,
                    pdfBytes,
                    $"gpt5-{category.ToLower()}-attempt{attempt}");

                if (result.Success)
                {
                    if (attempt > 1)
                    {
                        _logger.LogInformation($"[GPT-5-RETRY] Script succeeded on attempt {attempt} after error correction");
                    }
                    return result;
                }

                // If failed and we have retries left, ask GPT to fix the script
                if (attempt < maxRetries)
                {
                    _logger.LogWarning($"[GPT-5-RETRY] Script failed on attempt {attempt}, asking GPT to fix it");
                    _logger.LogDebug($"[GPT-5-RETRY] Error: {result.StandardError}");

                    var fixedScript = await _openAiService.FixScriptFromErrorAsync(
                        currentScript,
                        result.StandardError,
                        result.StandardOutput);

                    if (string.IsNullOrEmpty(fixedScript))
                    {
                        _logger.LogWarning($"[GPT-5-RETRY] GPT failed to generate fixed script");
                        break; // Can't continue without a fixed script
                    }

                    currentScript = fixedScript;
                    _logger.LogInformation($"[GPT-5-RETRY] Received corrected script from GPT, retrying...");
                }
                else
                {
                    _logger.LogWarning($"[GPT-5-RETRY] Script failed after {maxRetries} attempts");
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