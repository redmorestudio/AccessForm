using System;
using System.IO;
using AccessFormServer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

public class TestEnhancedTocFix
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("===== TESTING ENHANCED TOC LINK FIX =====\n");

        // Setup logging
        var serviceProvider = new ServiceCollection()
            .AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information))
            .BuildServiceProvider();

        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<TocLinkFixServiceEnhanced>();

        // Get the PDF file
        string inputPath = args.Length > 0 ? args[0] :
            "TWC Forms/eBily/foster-youth-services-guide-twc.pdf";

        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"Error: File not found: {inputPath}");
            return;
        }

        Console.WriteLine($"Processing: {inputPath}");
        Console.WriteLine($"File size: {new FileInfo(inputPath).Length / 1024.0:F1} KB\n");

        try
        {
            // Read the PDF
            byte[] pdfBytes = await File.ReadAllBytesAsync(inputPath);

            // Create the enhanced service
            var tocFixService = new TocLinkFixServiceEnhanced(logger);

            // Process the PDF
            Console.WriteLine("Running Enhanced TOC Link Fix...");
            var startTime = DateTime.Now;

            var result = await tocFixService.FixTocLinksAsync(pdfBytes);

            var elapsed = DateTime.Now - startTime;

            // Display results
            Console.WriteLine("\n===== RESULTS =====");
            Console.WriteLine($"Success: {result.Success}");
            Console.WriteLine($"Processing time: {elapsed.TotalSeconds:F2} seconds");
            Console.WriteLine($"Fixed TOC structure elements: {result.FixedTocElements}");
            Console.WriteLine($"Fixed link structures: {result.FixedLinks}");

            if (!result.Success)
            {
                Console.WriteLine($"Error: {result.ErrorMessage}");
            }
            else
            {
                // Save the fixed PDF
                string outputPath = Path.GetFileNameWithoutExtension(inputPath) + "-enhanced-fixed.pdf";
                await File.WriteAllBytesAsync(outputPath, result.FixedPdf);
                Console.WriteLine($"\nFixed PDF saved to: {outputPath}");
                Console.WriteLine($"Output size: {result.FixedPdf.Length / 1024.0:F1} KB");

                // Show improvement summary
                Console.WriteLine("\n===== EXPECTED IMPROVEMENTS =====");
                Console.WriteLine("✓ All link annotations should now be nested in Link elements");
                Console.WriteLine("✓ Each Link should have an OBJR child");
                Console.WriteLine("✓ All Links should have alternative text");
                Console.WriteLine("✓ PAC should show 0 'Link annotation not nested' errors");
                Console.WriteLine("\nPlease run PAC (PDF Accessibility Checker) to verify compliance.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}