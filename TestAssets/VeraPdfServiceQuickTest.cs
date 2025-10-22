using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services;

namespace TestAssets
{
    /// <summary>
    /// Quick standalone test for VeraPdfService
    /// Run this to verify the service works end-to-end
    /// </summary>
    public class VeraPdfServiceQuickTest
    {
        public static async Task RunTest()
        {
            // Setup configuration
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            // Setup logger
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });
            var logger = loggerFactory.CreateLogger<VeraPdfService>();

            // Create service
            var service = new VeraPdfService(logger, config);

            // Test with a corpus file
            var testPdf = Path.Combine(
                Directory.GetCurrentDirectory(),
                "TestAssets/veraPDF-corpus/PDF_UA-1/7.21 Fonts/7.21.8 Use of .notdef glyph/7.21.8-t01-fail-a.pdf"
            );

            Console.WriteLine($"Testing VeraPdfService with: {testPdf}");
            Console.WriteLine($"File exists: {File.Exists(testPdf)}");
            Console.WriteLine();

            // Validate
            var result = await service.ValidatePdfAsync(testPdf);

            // Print results
            Console.WriteLine("=== VALIDATION RESULTS ===");
            Console.WriteLine($"Status: {result.Status}");
            Console.WriteLine($"Compliant: {result.Summary.IsCompliant}");
            Console.WriteLine($"Profile: {result.Summary.ProfileName}");
            Console.WriteLine($"Statement: {result.Summary.Statement}");
            Console.WriteLine($"Total Checks: {result.Summary.TotalChecks}");
            Console.WriteLine($"Passed: {result.Summary.PassedChecks}");
            Console.WriteLine($"Failed: {result.Summary.FailedChecks}");
            Console.WriteLine($"Compliance Score: {result.Summary.ComplianceScore:F1}%");
            Console.WriteLine($"Processing Time: {result.ProcessingTime.TotalMilliseconds}ms");
            Console.WriteLine();

            if (result.Violations.Count > 0)
            {
                Console.WriteLine($"=== VIOLATIONS ({result.Violations.Count}) ===");
                foreach (var violation in result.Violations)
                {
                    Console.WriteLine($"  [{violation.Severity}] {violation.RuleId}");
                    Console.WriteLine($"    Clause: {violation.Clause}");
                    Console.WriteLine($"    Description: {violation.Description}");
                    if (violation.Location?.PageNumber.HasValue == true)
                        Console.WriteLine($"    Page: {violation.Location.PageNumber}");
                    Console.WriteLine($"    Message: {violation.ErrorMessage}");
                    Console.WriteLine();
                }
            }

            Console.WriteLine("✅ VeraPdfService test completed successfully!");
        }
    }
}
