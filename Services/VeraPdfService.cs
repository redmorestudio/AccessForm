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

            if (process.ExitCode != 0)
            {
                var error = errorBuilder.ToString();
                throw new InvalidOperationException(
                    $"veraPDF failed with exit code {process.ExitCode}: {error}");
            }

            return outputBuilder.ToString();
        }

        /// <summary>
        /// Parse veraPDF JSON output into ValidationResult
        /// </summary>
        private void ParseVeraPdfOutput(string jsonOutput, ValidationResult result)
        {
            try
            {
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

                if (!jobs.TryGetProperty("job", out var job))
                {
                    throw new InvalidOperationException("Invalid veraPDF output: missing 'job' property");
                }

                if (!job.TryGetProperty("validationReport", out var valReport))
                {
                    throw new InvalidOperationException("Invalid veraPDF output: missing 'validationReport' property");
                }

                // Parse summary
                result.Summary.ProfileName = valReport.GetProperty("profileName").GetString() ?? "PDF/UA-1";
                result.Summary.Statement = valReport.GetProperty("statement").GetString() ?? "";
                result.Summary.IsCompliant = valReport.GetProperty("isCompliant").GetString() == "true";

                // Parse details
                if (valReport.TryGetProperty("details", out var details))
                {
                    if (details.TryGetProperty("passedRules", out var passedRules))
                        result.Summary.PassedChecks = passedRules.GetInt32();

                    if (details.TryGetProperty("failedRules", out var failedRules))
                        result.Summary.FailedChecks = failedRules.GetInt32();

                    result.Summary.TotalChecks = result.Summary.PassedChecks + result.Summary.FailedChecks;

                    // Parse violations (failed rules)
                    if (details.TryGetProperty("rule", out var rules))
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
            var status = rule.GetProperty("status").GetString();
            if (status != "failed")
                return; // Only capture failures

            var violation = new PdfUAViolation
            {
                Clause = rule.GetProperty("clause").GetString() ?? "",
                TestNumber = rule.GetProperty("testNumber").GetInt32(),
                Status = ViolationStatus.Failed,
                Specification = rule.GetProperty("specification").GetString() ?? "ISO 14289-1:2014"
            };

            violation.RuleId = $"{violation.Clause}-{violation.TestNumber}";

            // Description
            if (rule.TryGetProperty("description", out var desc))
                violation.Description = desc.GetString() ?? "";

            // Location/context from check element
            if (rule.TryGetProperty("check", out var checks))
            {
                var checkElement = checks.ValueKind == JsonValueKind.Array
                    ? checks.EnumerateArray().GetEnumerator().Current
                    : checks;

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
