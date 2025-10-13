using System;
using System.IO;
using AccessFormServer.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Test program for the enhanced TOC link fix service
/// </summary>
class TestTocFixEnhanced
{
    static async Task Main(string[] args)
    {
        // Configure paths
        var inputPath = args.Length > 0
            ? args[0]
            : "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/TWC Forms/eBily/foster-youth-services-guide-twc.pdf";

        var outputPath = args.Length > 1
            ? args[1]
            : "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/TWC Forms/eBily/foster-youth-ENHANCED-fixed.pdf";

        Console.WriteLine("===== ENHANCED TOC LINK FIX TEST =====\n");
        Console.WriteLine($"Input:  {inputPath}");
        Console.WriteLine($"Output: {outputPath}\n");

        // Create logger
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        var logger = loggerFactory.CreateLogger<TocLinkFixServiceEnhanced>();

        // Create service
        var service = new TocLinkFixServiceEnhanced(logger);

        try
        {
            // Read original PDF
            Console.WriteLine("Reading PDF...");
            var pdfBytes = await File.ReadAllBytesAsync(inputPath);
            Console.WriteLine($"PDF size: {pdfBytes.Length:N0} bytes\n");

            // Process with enhanced service
            Console.WriteLine("Processing with Enhanced TocLinkFixService...\n");
            var result = await service.FixTocLinksAsync(pdfBytes);

            if (result.Success)
            {
                Console.WriteLine("\n✅ SUCCESS! Remediation complete:");
                Console.WriteLine($"   - Fixed TOC elements: {result.FixedTocElements}");
                Console.WriteLine($"   - Fixed link structures: {result.FixedLinks}");
                Console.WriteLine($"   - Output size: {result.FixedPdf.Length:N0} bytes");

                // Save fixed PDF
                await File.WriteAllBytesAsync(outputPath, result.FixedPdf);
                Console.WriteLine($"\n📄 Saved to: {outputPath}");

                // Provide PAC testing instructions
                Console.WriteLine("\n📋 Next Steps:");
                Console.WriteLine("1. Open the output PDF in PAC (PDF Accessibility Checker)");
                Console.WriteLine("2. Run a full accessibility check");
                Console.WriteLine("3. Check that 'Link annotation is not nested inside a link structure element' errors are resolved");
                Console.WriteLine("4. Check that all TOC links have proper alternative text");
            }
            else
            {
                Console.WriteLine($"\n❌ ERROR: {result.ErrorMessage}");

                // Still save the original if processing failed
                await File.WriteAllBytesAsync(outputPath.Replace(".pdf", "-failed.pdf"), pdfBytes);
                Console.WriteLine("Original PDF saved with -failed suffix");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ FATAL ERROR: {ex.Message}");
            Console.WriteLine($"Stack trace:\n{ex.StackTrace}");
        }
    }
}