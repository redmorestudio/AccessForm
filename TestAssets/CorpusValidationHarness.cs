using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WordToPdfConverter.TestAssets
{
    /// <summary>
    /// Test harness to validate all PDFs in the veraPDF corpus
    /// </summary>
    public class CorpusValidationHarness
    {
        private readonly string _corpusPath;
        private readonly string _veraPdfPath;
        private readonly string _javaHome;
        private readonly List<CorpusTestResult> _results = new();

        public CorpusValidationHarness(
            string corpusPath = null,
            string veraPdfPath = null,
            string javaHome = null)
        {
            _corpusPath = corpusPath ??
                Path.Combine(Directory.GetCurrentDirectory(), "veraPDF-corpus", "PDF_UA-1");
            _veraPdfPath = veraPdfPath ??
                "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/TestAssets/verapdf/verapdf";
            _javaHome = javaHome ??
                "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/TestAssets/jdk-21.0.8.jdk/Contents/Home";
        }

        /// <summary>
        /// Run validation tests on all corpus PDFs
        /// </summary>
        public async Task<CorpusTestReport> RunAllTestsAsync()
        {
            var report = new CorpusTestReport
            {
                StartTime = DateTime.UtcNow,
                CorpusPath = _corpusPath
            };

            Console.WriteLine($"=== veraPDF Corpus Validation Harness ===");
            Console.WriteLine($"Corpus Path: {_corpusPath}");
            Console.WriteLine($"veraPDF: {_veraPdfPath}");
            Console.WriteLine();

            // Verify prerequisites
            if (!Directory.Exists(_corpusPath))
            {
                report.ErrorMessage = $"Corpus directory not found: {_corpusPath}";
                Console.WriteLine($"ERROR: {report.ErrorMessage}");
                return report;
            }

            if (!File.Exists(_veraPdfPath))
            {
                report.ErrorMessage = $"veraPDF not found: {_veraPdfPath}\nRun manual installation - see VERAPDF-INSTALL.md";
                Console.WriteLine($"ERROR: {report.ErrorMessage}");
                return report;
            }

            // Find all PDF files in corpus
            var pdfFiles = Directory.GetFiles(_corpusPath, "*.pdf", SearchOption.AllDirectories)
                .OrderBy(f => f)
                .ToList();

            Console.WriteLine($"Found {pdfFiles.Count} test PDFs");
            Console.WriteLine();

            // Run validation on each PDF
            int current = 0;
            foreach (var pdfPath in pdfFiles)
            {
                current++;
                var relativePath = Path.GetRelativePath(_corpusPath, pdfPath);
                Console.Write($"[{current}/{pdfFiles.Count}] {relativePath}... ");

                var result = await ValidatePdfAsync(pdfPath);
                _results.Add(result);

                // Display result
                var statusSymbol = result.Passed ? "✓" : "✗";
                var color = result.Passed ? "PASS" : "FAIL";
                Console.WriteLine($"{statusSymbol} {color}");

                if (!result.Passed && !string.IsNullOrEmpty(result.ErrorMessage))
                {
                    Console.WriteLine($"   Error: {result.ErrorMessage}");
                }
            }

            // Generate summary
            report.EndTime = DateTime.UtcNow;
            report.Results = _results;
            report.TotalTests = _results.Count;
            report.PassedTests = _results.Count(r => r.Passed);
            report.FailedTests = _results.Count(r => !r.Passed);
            report.SuccessRate = report.TotalTests > 0
                ? (double)report.PassedTests / report.TotalTests * 100.0
                : 0.0;

            return report;
        }

        /// <summary>
        /// Validate a single PDF from the corpus
        /// </summary>
        private async Task<CorpusTestResult> ValidatePdfAsync(string pdfPath)
        {
            var result = new CorpusTestResult
            {
                FilePath = pdfPath,
                FileName = Path.GetFileName(pdfPath),
                RelativePath = Path.GetRelativePath(_corpusPath, pdfPath)
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Parse expected result from filename
                // Format: "7.21.6-t03-pass-a.pdf" or "6.1-t01-fail-a.pdf"
                result.ExpectedResult = ParseExpectedResult(result.FileName);

                // Run veraPDF validation
                var jsonOutput = await RunVeraPdfAsync(pdfPath);
                result.RawOutput = jsonOutput;

                // Parse actual result
                result.ActualCompliant = ParseComplianceFromJson(jsonOutput);

                // Compare expected vs actual
                result.Passed = ValidateTestResult(result);
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.ErrorMessage = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                result.ExecutionTime = stopwatch.Elapsed;
            }

            return result;
        }

        /// <summary>
        /// Run veraPDF on a single PDF
        /// </summary>
        private async Task<string> RunVeraPdfAsync(string pdfPath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _veraPdfPath,
                Arguments = $"--flavour ua1 --format json \"{pdfPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.EnvironmentVariables["JAVA_HOME"] = _javaHome;

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

            var completed = await Task.Run(() => process.WaitForExit(30000));

            if (!completed)
            {
                process.Kill();
                throw new TimeoutException("veraPDF timed out after 30 seconds");
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
        /// Parse expected result from corpus filename
        /// </summary>
        private ExpectedResult ParseExpectedResult(string filename)
        {
            // Corpus naming: "{clause}-t{testNum}-{pass|fail}-{variant}.pdf"
            // Example: "7.21.6-t03-pass-a.pdf" should pass
            // Example: "6.1-t01-fail-a.pdf" should fail

            if (filename.Contains("-pass-", StringComparison.OrdinalIgnoreCase))
                return ExpectedResult.Pass;

            if (filename.Contains("-fail-", StringComparison.OrdinalIgnoreCase))
                return ExpectedResult.Fail;

            return ExpectedResult.Unknown;
        }

        /// <summary>
        /// Parse compliance status from veraPDF JSON output
        /// </summary>
        private bool ParseComplianceFromJson(string jsonOutput)
        {
            // Quick JSON parsing - look for "isCompliant":"true"
            // This is a simplified check; VeraPdfService does full parsing
            var match = Regex.Match(jsonOutput, @"""isCompliant""\s*:\s*""(true|false)""");
            if (match.Success)
            {
                return match.Groups[1].Value == "true";
            }

            throw new InvalidOperationException("Could not parse compliance status from veraPDF output");
        }

        /// <summary>
        /// Validate test result: does actual match expected?
        /// </summary>
        private bool ValidateTestResult(CorpusTestResult result)
        {
            switch (result.ExpectedResult)
            {
                case ExpectedResult.Pass:
                    return result.ActualCompliant == true;

                case ExpectedResult.Fail:
                    return result.ActualCompliant == false;

                case ExpectedResult.Unknown:
                    // Unknown expectation - just check it ran without error
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Print test report to console
        /// </summary>
        public void PrintReport(CorpusTestReport report)
        {
            Console.WriteLine();
            Console.WriteLine("=== Test Report ===");
            Console.WriteLine($"Total Tests: {report.TotalTests}");
            Console.WriteLine($"Passed: {report.PassedTests} ({report.SuccessRate:F1}%)");
            Console.WriteLine($"Failed: {report.FailedTests}");
            Console.WriteLine($"Duration: {(report.EndTime - report.StartTime).TotalSeconds:F1}s");

            if (report.FailedTests > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Failed Tests:");
                foreach (var failed in report.Results.Where(r => !r.Passed))
                {
                    Console.WriteLine($"  ✗ {failed.RelativePath}");
                    Console.WriteLine($"    Expected: {failed.ExpectedResult}");
                    Console.WriteLine($"    Actual: {(failed.ActualCompliant == true ? "PASS" : "FAIL")}");
                    if (!string.IsNullOrEmpty(failed.ErrorMessage))
                        Console.WriteLine($"    Error: {failed.ErrorMessage}");
                }
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Save report to file
        /// </summary>
        public async Task SaveReportAsync(CorpusTestReport report, string outputPath = null)
        {
            outputPath ??= Path.Combine(
                Directory.GetCurrentDirectory(),
                "Reports",
                $"corpus-validation-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            var sb = new StringBuilder();
            sb.AppendLine("=== veraPDF Corpus Validation Report ===");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Corpus: {report.CorpusPath}");
            sb.AppendLine();
            sb.AppendLine($"Total Tests: {report.TotalTests}");
            sb.AppendLine($"Passed: {report.PassedTests} ({report.SuccessRate:F1}%)");
            sb.AppendLine($"Failed: {report.FailedTests}");
            sb.AppendLine($"Duration: {(report.EndTime - report.StartTime).TotalSeconds:F1}s");
            sb.AppendLine();

            sb.AppendLine("=== Detailed Results ===");
            foreach (var result in report.Results)
            {
                var status = result.Passed ? "PASS" : "FAIL";
                sb.AppendLine($"{status} | {result.RelativePath} | {result.ExecutionTime.TotalSeconds:F2}s");
                if (!result.Passed)
                {
                    sb.AppendLine($"     Expected: {result.ExpectedResult}, Actual: {(result.ActualCompliant == true ? "PASS" : "FAIL")}");
                    if (!string.IsNullOrEmpty(result.ErrorMessage))
                        sb.AppendLine($"     Error: {result.ErrorMessage}");
                }
            }

            await File.WriteAllTextAsync(outputPath, sb.ToString());
            Console.WriteLine($"Report saved: {outputPath}");
        }
    }

    #region Data Models

    public class CorpusTestReport
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string CorpusPath { get; set; }
        public List<CorpusTestResult> Results { get; set; } = new();
        public int TotalTests { get; set; }
        public int PassedTests { get; set; }
        public int FailedTests { get; set; }
        public double SuccessRate { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class CorpusTestResult
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string RelativePath { get; set; }
        public ExpectedResult ExpectedResult { get; set; }
        public bool? ActualCompliant { get; set; }
        public bool Passed { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public string RawOutput { get; set; }
        public string ErrorMessage { get; set; }
    }

    public enum ExpectedResult
    {
        Unknown,
        Pass,
        Fail
    }

    #endregion
}
