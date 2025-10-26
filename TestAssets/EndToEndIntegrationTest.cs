using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.PdfUA;
using WordToPdfConverter.Services;

namespace WordToPdfConverter.TestAssets
{
    /// <summary>
    /// End-to-end integration test demonstrating full validation workflow
    /// Tests: PDF → Validate → Report Violations → (Future: Fix → Re-validate)
    /// </summary>
    public class EndToEndIntegrationTest
    {
        private readonly ILogger<VeraPdfService> _logger;
        private readonly IConfiguration _config;
        private readonly VeraPdfService _veraPdfService;
        private readonly string _testAssetsPath;

        public EndToEndIntegrationTest(
            ILogger<VeraPdfService> logger = null,
            IConfiguration config = null)
        {
            // Setup logging
            _logger = logger ?? CreateDefaultLogger();

            // Get test assets path
            _testAssetsPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "TestAssets");

            // Setup configuration
            _config = config ?? CreateDefaultConfiguration();

            // Create VeraPdfService
            _veraPdfService = new VeraPdfService(_logger, _config);
        }

        /// <summary>
        /// Run complete end-to-end test workflow
        /// </summary>
        public async Task<IntegrationTestReport> RunFullWorkflowAsync(string testPdfPath = null)
        {
            var report = new IntegrationTestReport
            {
                StartTime = DateTime.UtcNow
            };

            Console.WriteLine("╔═══════════════════════════════════════════════════════╗");
            Console.WriteLine("║   PDF/UA Checker - End-to-End Integration Test       ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════╝");
            Console.WriteLine();

            try
            {
                // Step 1: Select test PDF
                Console.WriteLine("Step 1: Select Test PDF");
                Console.WriteLine("───────────────────────");

                testPdfPath ??= SelectTestPdf();

                if (!File.Exists(testPdfPath))
                {
                    throw new FileNotFoundException($"Test PDF not found: {testPdfPath}");
                }

                report.TestPdfPath = testPdfPath;
                Console.WriteLine($"✓ Selected: {Path.GetFileName(testPdfPath)}");
                Console.WriteLine($"  Size: {new FileInfo(testPdfPath).Length / 1024.0:F2} KB");
                Console.WriteLine();

                // Step 2: Initial Validation
                Console.WriteLine("Step 2: Initial PDF/UA Validation");
                Console.WriteLine("──────────────────────────────────");
                Console.WriteLine("Running veraPDF validation...");

                var stopwatch = Stopwatch.StartNew();
                var initialValidation = await _veraPdfService.ValidatePdfAsync(testPdfPath);
                stopwatch.Stop();

                report.InitialValidation = initialValidation;

                Console.WriteLine($"✓ Validation completed in {stopwatch.ElapsedMilliseconds}ms");
                Console.WriteLine();

                // Step 3: Display Results
                Console.WriteLine("Step 3: Validation Results");
                Console.WriteLine("──────────────────────────");
                DisplayValidationResults(initialValidation);
                Console.WriteLine();

                // Step 4: Analyze Violations
                if (!initialValidation.Summary.IsCompliant)
                {
                    Console.WriteLine("Step 4: Violation Analysis");
                    Console.WriteLine("──────────────────────────");
                    AnalyzeViolations(initialValidation);
                    Console.WriteLine();
                }

                // Step 5: Suggest Remediations (Future)
                if (initialValidation.Violations.Any())
                {
                    Console.WriteLine("Step 5: Remediation Suggestions");
                    Console.WriteLine("───────────────────────────────");
                    SuggestRemediations(initialValidation);
                    Console.WriteLine();
                }

                // Step 6: Summary
                Console.WriteLine("Step 6: Test Summary");
                Console.WriteLine("────────────────────");
                report.Success = true;
                report.ErrorMessage = null;
            }
            catch (Exception ex)
            {
                report.Success = false;
                report.ErrorMessage = ex.Message;

                Console.WriteLine();
                Console.WriteLine("✗ TEST FAILED");
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                report.EndTime = DateTime.UtcNow;
            }

            // Print final summary
            PrintFinalSummary(report);

            return report;
        }

        /// <summary>
        /// Select a test PDF from the corpus or TWC forms
        /// </summary>
        private string SelectTestPdf()
        {
            // Priority: Use a known failing PDF from corpus for better testing
            var candidatePaths = new[]
            {
                // Known non-compliant PDFs
                Path.Combine(_testAssetsPath, "veraPDF-corpus", "PDF_UA-1", "6.1 File header", "6.1-t01-fail-a.pdf"),
                Path.Combine(_testAssetsPath, "veraPDF-corpus", "PDF_UA-1", "6.2 Metadata", "6.2-t02-fail-a.pdf"),
                Path.Combine(_testAssetsPath, "veraPDF-corpus", "PDF_UA-1", "7.18.1 Annotations", "7.18.1-t01-fail-a.pdf"),

                // Known compliant PDFs (for comparison)
                Path.Combine(_testAssetsPath, "veraPDF-corpus", "PDF_UA-1", "7.21 Fonts", "7.21.6 Character encodings", "7.21.6-t03-pass-a.pdf"),

                // Real-world TWC forms (if available)
                Path.Combine(_testAssetsPath, "..", "TWC Forms", "comet", "bet-form-grievance-twc copy.pdf")
            };

            // Return first existing PDF
            foreach (var path in candidatePaths)
            {
                if (File.Exists(path))
                    return path;
            }

            // If none found, look for any PDF in corpus
            var corpusPath = Path.Combine(_testAssetsPath, "veraPDF-corpus", "PDF_UA-1");
            if (Directory.Exists(corpusPath))
            {
                var firstPdf = Directory.GetFiles(corpusPath, "*.pdf", SearchOption.AllDirectories).FirstOrDefault();
                if (firstPdf != null)
                    return firstPdf;
            }

            throw new FileNotFoundException("No test PDFs found. Run corpus download first.");
        }

        /// <summary>
        /// Display validation results in readable format
        /// </summary>
        private void DisplayValidationResults(ValidationResult result)
        {
            Console.WriteLine($"Profile: {result.Summary.ProfileName}");
            Console.WriteLine($"Status: {(result.Summary.IsCompliant ? "✓ COMPLIANT" : "✗ NON-COMPLIANT")}");
            Console.WriteLine($"Compliance Score: {result.Summary.ComplianceScore:F2}%");
            Console.WriteLine();

            Console.WriteLine($"Checks: {result.Summary.TotalChecks} total");
            Console.WriteLine($"  ✓ Passed: {result.Summary.PassedChecks}");
            Console.WriteLine($"  ✗ Failed: {result.Summary.FailedChecks}");
            Console.WriteLine();

            if (result.Violations.Any())
            {
                Console.WriteLine($"Violations: {result.Violations.Count}");

                var bySeverity = result.Violations
                    .GroupBy(v => v.Severity)
                    .OrderByDescending(g => g.Key);

                foreach (var group in bySeverity)
                {
                    var symbol = group.Key switch
                    {
                        ViolationSeverity.Critical => "🔴",
                        ViolationSeverity.Error => "🟠",
                        ViolationSeverity.Warning => "🟡",
                        _ => "ℹ️"
                    };
                    Console.WriteLine($"  {symbol} {group.Key}: {group.Count()}");
                }
            }
        }

        /// <summary>
        /// Analyze violations and provide insights
        /// </summary>
        private void AnalyzeViolations(ValidationResult result)
        {
            var violations = result.Violations;

            // Group by clause
            var byClause = violations
                .GroupBy(v => v.Clause)
                .OrderByDescending(g => g.Count());

            Console.WriteLine("Most Common Issues:");
            foreach (var group in byClause.Take(5))
            {
                var firstViolation = group.First();
                Console.WriteLine($"  • {group.Key}: {group.Count()} violations");
                Console.WriteLine($"    \"{firstViolation.Description}\"");
            }
            Console.WriteLine();

            // Group by page (if location available)
            var withPages = violations.Where(v => v.Location?.PageNumber != null);
            if (withPages.Any())
            {
                var byPage = withPages
                    .GroupBy(v => v.Location.PageNumber)
                    .OrderByDescending(g => g.Count());

                Console.WriteLine("Pages with Most Issues:");
                foreach (var group in byPage.Take(5))
                {
                    Console.WriteLine($"  • Page {group.Key}: {group.Count()} violations");
                }
                Console.WriteLine();
            }

            // Critical violations
            var critical = violations.Where(v => v.Severity == ViolationSeverity.Critical);
            if (critical.Any())
            {
                Console.WriteLine("🔴 Critical Issues (Must Fix):");
                foreach (var v in critical.Take(3))
                {
                    Console.WriteLine($"  • [{v.RuleId}] {v.Description}");
                    if (!string.IsNullOrEmpty(v.ErrorMessage))
                        Console.WriteLine($"    → {v.ErrorMessage}");
                }
                if (critical.Count() > 3)
                    Console.WriteLine($"  ... and {critical.Count() - 3} more");
                Console.WriteLine();
            }
        }

        /// <summary>
        /// Suggest remediation services for violations
        /// </summary>
        private void SuggestRemediations(ValidationResult result)
        {
            Console.WriteLine("Recommended Remediation Services:");

            // Group violations by remediation service
            var serviceMap = new Dictionary<string, List<PdfUAViolation>>();

            foreach (var violation in result.Violations)
            {
                var service = MapViolationToService(violation);
                if (!serviceMap.ContainsKey(service))
                    serviceMap[service] = new List<PdfUAViolation>();
                serviceMap[service].Add(violation);
            }

            foreach (var kvp in serviceMap.OrderByDescending(x => x.Value.Count))
            {
                Console.WriteLine($"  • {kvp.Key} ({kvp.Value.Count} issues)");

                var clauses = kvp.Value
                    .Select(v => v.Clause)
                    .Distinct()
                    .Take(3);

                Console.WriteLine($"    Clauses: {string.Join(", ", clauses)}");
            }

            Console.WriteLine();
            Console.WriteLine("Note: Remediation services are not yet implemented.");
            Console.WriteLine("This demonstrates the future closed-loop workflow:");
            Console.WriteLine("  1. Validate → 2. Identify issues → 3. Apply fixes → 4. Re-validate");
        }

        /// <summary>
        /// Map violation to appropriate remediation service
        /// </summary>
        private string MapViolationToService(PdfUAViolation violation)
        {
            var clause = violation.Clause;

            // Based on SPECIFICATION.md error-to-fixer mapping
            if (clause.StartsWith("6.2"))
                return "MetadataFixerService";

            if (clause.StartsWith("7.1"))
                return "LanguageFixerService";

            if (clause.StartsWith("7.18.1"))
                return "TabOrderFixerService";

            if (clause.StartsWith("7.18"))
                return "AnnotationFixerService";

            if (clause.StartsWith("7.21"))
                return "FontFixerService";

            if (clause.StartsWith("7.3"))
                return "StructureTreeFixerService";

            return "GenericRemediationService";
        }

        /// <summary>
        /// Print final test summary
        /// </summary>
        private void PrintFinalSummary(IntegrationTestReport report)
        {
            Console.WriteLine("═══════════════════════════════════════════════════════");
            Console.WriteLine();

            if (report.Success)
            {
                Console.WriteLine("✓ Integration Test Completed Successfully");
            }
            else
            {
                Console.WriteLine("✗ Integration Test Failed");
                Console.WriteLine($"Error: {report.ErrorMessage}");
            }

            Console.WriteLine();
            Console.WriteLine($"Duration: {(report.EndTime - report.StartTime).TotalSeconds:F2}s");

            if (report.InitialValidation != null)
            {
                Console.WriteLine($"PDF Status: {(report.InitialValidation.Summary.IsCompliant ? "Compliant" : "Non-Compliant")}");
                Console.WriteLine($"Violations: {report.InitialValidation.Violations.Count}");
                Console.WriteLine($"Compliance: {report.InitialValidation.Summary.ComplianceScore:F1}%");
            }

            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════════════");
        }

        /// <summary>
        /// Create default logger for testing
        /// </summary>
        private ILogger<VeraPdfService> CreateDefaultLogger()
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            return loggerFactory.CreateLogger<VeraPdfService>();
        }

        /// <summary>
        /// Create default configuration for testing
        /// </summary>
        private IConfiguration CreateDefaultConfiguration()
        {
            var configData = new Dictionary<string, string>
            {
                ["VeraPdf:ExecutablePath"] = Path.Combine(_testAssetsPath, "verapdf", "verapdf"),
                ["VeraPdf:JavaHome"] = Path.Combine(_testAssetsPath, "jdk-21.0.8.jdk", "Contents", "Home"),
                ["VeraPdf:WorkingDirectory"] = "/tmp/verapdf-work"
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();
        }
    }

    #region Data Models

    public class IntegrationTestReport
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string TestPdfPath { get; set; }
        public ValidationResult InitialValidation { get; set; }
        public ValidationResult AfterRemediation { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }

    #endregion

    /// <summary>
    /// Simple console entry point
    /// </summary>
    public class IntegrationTestRunner
    {
        public static async Task Main(string[] args)
        {
            var testPdfPath = args.Length > 0 ? args[0] : null;

            var test = new EndToEndIntegrationTest();
            var report = await test.RunFullWorkflowAsync(testPdfPath);

            Environment.Exit(report.Success ? 0 : 1);
        }
    }
}
