using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.PdfUA;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Service to validate PDFs using veraPDF CLI
    /// </summary>
    public class VeraPdfService
    {
        private readonly ILogger<VeraPdfService> _logger;
        private readonly IConfiguration _config;
        private readonly string _veraPdfPath;
        private readonly string _javaHome;
        private readonly string _workingDir;

        public VeraPdfService(
            ILogger<VeraPdfService> logger,
            IConfiguration config)
        {
            _logger = logger;
            _config = config;

            // Read configuration
            _veraPdfPath = config["VeraPdf:ExecutablePath"] ?? "/usr/local/verapdf/verapdf";
            _javaHome = config["VeraPdf:JavaHome"];
            _workingDir = config["VeraPdf:WorkingDirectory"] ?? "/tmp/verapdf-work";

            // Ensure working directory exists
            Directory.CreateDirectory(_workingDir);
        }

        /// <summary>
        /// Validate a PDF file for PDF/UA compliance
        /// </summary>
        public async Task<ValidationResult> ValidatePdfAsync(
            string pdfPath,
            string profile = "ua1")
        {
            var result = new ValidationResult
            {
                FilePath = pdfPath,
                Status = ValidationStatus.Running
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Verify PDF exists
                if (!File.Exists(pdfPath))
                {
                    throw new FileNotFoundException($"PDF not found: {pdfPath}");
                }

                result.FileSizeBytes = new FileInfo(pdfPath).Length;

                // Verify veraPDF is installed
                if (!File.Exists(_veraPdfPath))
                {
                    throw new InvalidOperationException(
                        $"veraPDF not found at: {_veraPdfPath}. " +
                        $"Please install veraPDF - see VERAPDF-INSTALL.md");
                }

                // Run veraPDF validation
                var jsonOutput = await RunVeraPdfAsync(pdfPath, profile);

                // Store raw output
                result.RawVeraPdfOutput = jsonOutput;

                // DEBUG: Save JSON to file for inspection
                var debugPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"verapdf-debug-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                await System.IO.File.WriteAllTextAsync(debugPath, jsonOutput);
                _logger.LogInformation($"DEBUG: VeraPDF JSON saved to {debugPath}");

                // Parse veraPDF output
                ParseVeraPdfOutput(jsonOutput, result);

                result.Status = ValidationStatus.Completed;
                _logger.LogInformation(
                    "PDF validation completed: {FilePath} - Compliant: {IsCompliant}, Violations: {ViolationCount}",
                    pdfPath, result.Summary.IsCompliant, result.Violations.Count);
            }
            catch (Exception ex)
            {
                result.Status = ValidationStatus.Failed;
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, "PDF validation failed: {FilePath}", pdfPath);
            }
            finally
            {
                stopwatch.Stop();
                result.ProcessingTime = stopwatch.Elapsed;
            }

            return result;
        }

        /// <summary>
        /// Run veraPDF CLI and capture JSON output
        /// </summary>
        private async Task<string> RunVeraPdfAsync(string pdfPath, string profile)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _veraPdfPath,
                Arguments = $"--flavour {profile} --format json \"{pdfPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _workingDir
            };

            // Set JAVA_HOME if configured
            if (!string.IsNullOrEmpty(_javaHome))
            {
                startInfo.EnvironmentVariables["JAVA_HOME"] = _javaHome;
            }

            using var process = new Process { StartInfo = startInfo };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                    outputBuilder.AppendLine(args.Data);
            };

            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                    errorBuilder.AppendLine(args.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait with timeout (2 minutes)
            var completed = await Task.Run(() => process.WaitForExit(120000));

            if (!completed)
            {
                process.Kill();
                throw new TimeoutException("veraPDF validation timed out after 2 minutes");
            }

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            // VeraPDF returns exit code 1 when PDF has violations (by design)
            // Only treat it as an error if we didn't get valid JSON output
            if (process.ExitCode != 0)
            {
                // Check if we got valid JSON despite exit code 1
                if (!string.IsNullOrWhiteSpace(output) && output.TrimStart().StartsWith("{"))
                {
                    // We got JSON output, exit code 1 just means violations were found
                    _logger.LogDebug("veraPDF returned exit code {ExitCode} (expected for PDFs with violations)", process.ExitCode);
                    return output;
                }

                // Real error - no valid output
                _logger.LogError(
                    "veraPDF failed with exit code {ExitCode}\n" +
                    "STDERR: {Error}\n" +
                    "STDOUT: {Output}",
                    process.ExitCode, error, output);

                throw new InvalidOperationException(
                    $"veraPDF failed with exit code {process.ExitCode}. " +
                    $"Error: {(string.IsNullOrEmpty(error) ? "No error output" : error.Trim())}. " +
                    $"Output: {(string.IsNullOrEmpty(output) ? "No output" : output.Substring(0, Math.Min(200, output.Length)))}");
            }

            return output;
        }

        /// <summary>
        /// Parse veraPDF JSON output into ValidationResult
        /// </summary>
        private void ParseVeraPdfOutput(string jsonOutput, ValidationResult result)
        {
            try
            {
                // Log first 500 characters of JSON for debugging
                _logger.LogDebug("VeraPDF JSON output (first 500 chars): {Json}",
                    jsonOutput.Length > 500 ? jsonOutput.Substring(0, 500) : jsonOutput);

                using var document = JsonDocument.Parse(jsonOutput);
                var root = document.RootElement;

                // Navigate JSON structure: report -> jobs -> job -> validationReport
                if (!root.TryGetProperty("report", out var report))
                {
                    throw new InvalidOperationException("Invalid veraPDF output: missing 'report' property");
                }

                if (!report.TryGetProperty("jobs", out var jobs))
                {
                    throw new InvalidOperationException("Invalid veraPDF output: missing 'jobs' property");
                }

                // jobs is an array, get the first element
                if (jobs.ValueKind != JsonValueKind.Array || jobs.GetArrayLength() == 0)
                {
                    throw new InvalidOperationException("Invalid veraPDF output: 'jobs' is not an array or is empty");
                }

                var job = jobs[0];

                if (!job.TryGetProperty("validationResult", out var validationResults))
                {
                    throw new InvalidOperationException("Invalid veraPDF output: missing 'validationResult' property");
                }

                // validationResult is also an array, get the first element
                if (validationResults.ValueKind != JsonValueKind.Array || validationResults.GetArrayLength() == 0)
                {
                    throw new InvalidOperationException("Invalid veraPDF output: 'validationResult' is not an array or is empty");
                }

                var valReport = validationResults[0];

                // Parse summary - use TryGetProperty to handle optional fields
                if (valReport.TryGetProperty("profileName", out var profileName))
                    result.Summary.ProfileName = profileName.GetString() ?? "PDF/UA-1";
                else
                    result.Summary.ProfileName = "PDF/UA-1";

                if (valReport.TryGetProperty("statement", out var statement))
                    result.Summary.Statement = statement.GetString() ?? "";
                else
                    result.Summary.Statement = "";

                if (valReport.TryGetProperty("isCompliant", out var isCompliant))
                    result.Summary.IsCompliant = isCompliant.GetString() == "true";
                else
                    result.Summary.IsCompliant = false;

                // Parse details
                if (valReport.TryGetProperty("details", out var details))
                {
                    if (details.TryGetProperty("passedRules", out var passedRules))
                        result.Summary.PassedChecks = passedRules.GetInt32();

                    if (details.TryGetProperty("failedRules", out var failedRules))
                        result.Summary.FailedChecks = failedRules.GetInt32();

                    result.Summary.TotalChecks = result.Summary.PassedChecks + result.Summary.FailedChecks;

                    // Parse violations (failed rules) - use "ruleSummaries" not "rule"
                    if (details.TryGetProperty("ruleSummaries", out var rules))
                    {
                        if (rules.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var rule in rules.EnumerateArray())
                            {
                                ParseViolation(rule, result);
                            }
                        }
                        else
                        {
                            // Single rule
                            ParseViolation(rules, result);
                        }
                    }
                }
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogError(ex, "Missing expected JSON property in veraPDF output. JSON: {Json}",
                    jsonOutput.Length > 1000 ? jsonOutput.Substring(0, 1000) : jsonOutput);
                throw new InvalidOperationException("Failed to parse veraPDF output - missing expected property", ex);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse veraPDF JSON output");
                throw new InvalidOperationException("Failed to parse veraPDF output", ex);
            }
        }

        /// <summary>
        /// Parse a single violation from veraPDF rule element
        /// </summary>
        private void ParseViolation(JsonElement rule, ValidationResult result)
        {
            // Check status - skip non-failures
            if (!rule.TryGetProperty("status", out var statusProp))
                return;

            var status = statusProp.GetString();
            if (status != "failed")
                return; // Only capture failures

            var violation = new PdfUAViolation
            {
                Clause = rule.TryGetProperty("clause", out var clauseProp) ? clauseProp.GetString() ?? "" : "",
                TestNumber = rule.TryGetProperty("testNumber", out var testNumProp) ? testNumProp.GetInt32() : 0,
                Status = ViolationStatus.Failed,
                Specification = rule.TryGetProperty("specification", out var specProp) ? specProp.GetString() ?? "ISO 14289-1:2014" : "ISO 14289-1:2014"
            };

            violation.RuleId = $"{violation.Clause}-{violation.TestNumber}";

            // Description
            if (rule.TryGetProperty("description", out var desc))
                violation.Description = desc.GetString() ?? "";

            // Location/context from checks array (use "checks" not "check")
            if (rule.TryGetProperty("checks", out var checks))
            {
                JsonElement checkElement;
                if (checks.ValueKind == JsonValueKind.Array)
                {
                    // Get first element if array is not empty
                    var enumerator = checks.EnumerateArray();
                    if (enumerator.Any())
                    {
                        checkElement = enumerator.First();
                    }
                    else
                    {
                        return; // No check elements, skip this violation
                    }
                }
                else
                {
                    checkElement = checks;
                }

                if (checkElement.TryGetProperty("context", out var context))
                {
                    violation.Context = context.GetString() ?? "";
                    ParseLocation(context.GetString() ?? "", violation.Location);
                }

                if (checkElement.TryGetProperty("message", out var message))
                    violation.ErrorMessage = message.GetString() ?? "";
            }

            // Determine severity based on clause
            violation.Severity = DetermineSeverity(violation.Clause);

            result.Violations.Add(violation);
        }

        /// <summary>
        /// Parse location information from context string
        /// </summary>
        private void ParseLocation(string context, ViolationLocation location)
        {
            location.ContextDescription = context;

            // Try to extract page number from context
            // Example context: "root/document[0]/pages[0]/page[2]"
            var pageMatch = System.Text.RegularExpressions.Regex.Match(
                context, @"page\[(\d+)\]");

            if (pageMatch.Success)
            {
                location.PageNumber = int.Parse(pageMatch.Groups[1].Value) + 1; // Convert 0-based to 1-based
            }
        }

        /// <summary>
        /// Determine violation severity based on clause number
        /// </summary>
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

        /// <summary>
        /// Compare two validation results to track improvement
        /// </summary>
        public ValidationComparison CompareValidations(
            ValidationResult before,
            ValidationResult after)
        {
            return new ValidationComparison
            {
                BeforeViolationCount = before.Violations.Count,
                AfterViolationCount = after.Violations.Count,
                ViolationsFixed = before.Violations.Count - after.Violations.Count,
                ComplianceImprovement = after.Summary.ComplianceScore - before.Summary.ComplianceScore,
                IsImproved = after.Violations.Count < before.Violations.Count
            };
        }
    }

    /// <summary>
    /// Comparison of before/after validation results
    /// </summary>
    public class ValidationComparison
    {
        public int BeforeViolationCount { get; set; }
        public int AfterViolationCount { get; set; }
        public int ViolationsFixed { get; set; }
        public double ComplianceImprovement { get; set; }
        public bool IsImproved { get; set; }
    }
}
