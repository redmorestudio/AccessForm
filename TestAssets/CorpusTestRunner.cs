using System;
using System.Threading.Tasks;

namespace WordToPdfConverter.TestAssets
{
    /// <summary>
    /// Simple console runner for corpus validation
    /// Run with: dotnet run --project AccessFormServer.csproj -- test-corpus
    /// Or compile and run standalone
    /// </summary>
    public class CorpusTestRunner
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("veraPDF Corpus Validation Test Runner");
            Console.WriteLine("=====================================");
            Console.WriteLine();

            // Parse command line options
            string corpusPath = null;
            string veraPdfPath = null;
            string javaHome = null;
            string outputPath = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--corpus" && i + 1 < args.Length)
                    corpusPath = args[i + 1];
                else if (args[i] == "--verapdf" && i + 1 < args.Length)
                    veraPdfPath = args[i + 1];
                else if (args[i] == "--java" && i + 1 < args.Length)
                    javaHome = args[i + 1];
                else if (args[i] == "--output" && i + 1 < args.Length)
                    outputPath = args[i + 1];
            }

            // Show configuration
            if (corpusPath != null)
                Console.WriteLine($"Corpus Path: {corpusPath}");
            if (veraPdfPath != null)
                Console.WriteLine($"veraPDF: {veraPdfPath}");
            if (javaHome != null)
                Console.WriteLine($"Java Home: {javaHome}");
            Console.WriteLine();

            // Create harness
            var harness = new CorpusValidationHarness(corpusPath, veraPdfPath, javaHome);

            try
            {
                // Run tests
                var report = await harness.RunAllTestsAsync();

                // Print results
                harness.PrintReport(report);

                // Save report
                await harness.SaveReportAsync(report, outputPath);

                // Exit code based on success
                if (!string.IsNullOrEmpty(report.ErrorMessage))
                {
                    Console.WriteLine($"FATAL ERROR: {report.ErrorMessage}");
                    Environment.Exit(1);
                }
                else if (report.FailedTests > 0)
                {
                    Console.WriteLine($"TESTS FAILED: {report.FailedTests}/{report.TotalTests}");
                    Environment.Exit(1);
                }
                else
                {
                    Console.WriteLine($"ALL TESTS PASSED: {report.PassedTests}/{report.TotalTests}");
                    Environment.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FATAL ERROR: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// Quick test of a single PDF
        /// </summary>
        public static async Task QuickTest(string pdfPath)
        {
            Console.WriteLine($"Quick Test: {pdfPath}");
            Console.WriteLine();

            var harness = new CorpusValidationHarness();

            // This would need to be adapted to test a single file
            // For now, just show usage
            Console.WriteLine("Usage: dotnet run -- [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  --corpus <path>    Path to veraPDF corpus directory");
            Console.WriteLine("  --verapdf <path>   Path to veraPDF executable");
            Console.WriteLine("  --java <path>      Path to Java home directory");
            Console.WriteLine("  --output <path>    Path to save report");
            Console.WriteLine();
            Console.WriteLine("Example:");
            Console.WriteLine("  dotnet run -- --corpus ./TestAssets/veraPDF-corpus/PDF_UA-1");
        }
    }
}
