using System;
using System.IO;
using Microsoft.Extensions.Logging;
using AccessFormServer.Services;

class DiagnoseTocDeep
{
    static async Task Main()
    {
        var pdfPath = "TWC Forms/eBily/foster-youth-services-guide-twc.pdf";

        Console.WriteLine("===== DEEP TOC STRUCTURE DIAGNOSTIC =====\n");

        // Create logger
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger<TocDeepDiagnosticService>();
        var service = new TocDeepDiagnosticService(logger);

        // Read PDF
        var pdfBytes = await File.ReadAllBytesAsync(pdfPath);

        // Run diagnostic
        var report = await service.AnalyzeTocStructureAsync(pdfBytes);

        Console.WriteLine(report);

        // Save report
        var reportPath = "toc-deep-diagnostic-report.txt";
        await File.WriteAllTextAsync(reportPath, report);
        Console.WriteLine($"\nReport saved to: {reportPath}");
    }
}