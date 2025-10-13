using System;
using System.IO;
using AccessFormServer.Services;
using Microsoft.Extensions.Logging;

class TestTocFix
{
    static async Task Main()
    {
        // Create logger
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
        });
        var logger = loggerFactory.CreateLogger<TocLinkFixService>();

        // Create service
        var service = new TocLinkFixService(logger);

        // Read original PDF
        var inputPath = "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/TWC Forms/eBily/ORIGINAL-foster-youth-services-guide-twc.pdf";
        var outputPath = "/Users/sethredmore/Documents/Redmore Studio/AccessForm/WordToPdfConverter/foster-youth-fixed-toc.pdf";

        Console.WriteLine($"Reading PDF from: {inputPath}");
        var pdfBytes = await File.ReadAllBytesAsync(inputPath);

        // Process with TocLinkFixService
        Console.WriteLine("Processing with TocLinkFixService...");
        var result = await service.FixTocLinksAsync(pdfBytes);

        if (result.Success)
        {
            Console.WriteLine($"SUCCESS! Fixed {result.FixedTocElements} TOC elements and {result.FixedLinks} links");
            await File.WriteAllBytesAsync(outputPath, result.FixedPdf);
            Console.WriteLine($"Saved to: {outputPath}");
        }
        else
        {
            Console.WriteLine($"ERROR: {result.ErrorMessage}");
        }
    }
}