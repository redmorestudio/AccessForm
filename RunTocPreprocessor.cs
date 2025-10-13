using System;
using System.IO;
using AccessFormServer.Services;
using Microsoft.Extensions.Logging;

class RunTocPreprocessor
{
    static async Task Main(string[] args)
    {
        // Setup
        var inputPath = args.Length > 0 ? args[0]
            : "TWC Forms/eBily/foster-youth-services-guide-twc.pdf";

        var outputDir = "TOC-Reports";
        Directory.CreateDirectory(outputDir);

        var fileName = Path.GetFileName(inputPath);
        var outputPdfPath = Path.Combine(outputDir, fileName.Replace(".pdf", "-preprocessed.pdf"));
        var reportHtmlPath = Path.Combine(outputDir, fileName.Replace(".pdf", "-manual-fixes.html"));
        var reportTxtPath = Path.Combine(outputDir, fileName.Replace(".pdf", "-manual-fixes.txt"));

        Console.WriteLine("===== TOC PREPROCESSOR & MANUAL FIX GENERATOR =====\n");
        Console.WriteLine($"Input:  {inputPath}");
        Console.WriteLine($"Output: {outputDir}/\n");

        // Create logger
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        var logger = loggerFactory.CreateLogger<TocPreprocessorService>();

        // Create service
        var service = new TocPreprocessorService(logger);

        try
        {
            // Read PDF
            Console.WriteLine("Reading PDF...");
            var pdfBytes = await File.ReadAllBytesAsync(inputPath);

            // Run preprocessor
            Console.WriteLine("Analyzing TOC structure and generating fix instructions...\n");
            var result = await service.PreprocessAndAnalyzeAsync(pdfBytes, fileName);

            if (result.Success)
            {
                // Save preprocessed PDF
                await File.WriteAllBytesAsync(outputPdfPath, result.ProcessedPdf);
                Console.WriteLine($"✅ Preprocessed PDF saved: {outputPdfPath}");

                // Save HTML report
                await File.WriteAllTextAsync(reportHtmlPath, result.HtmlReport);
                Console.WriteLine($"📄 HTML report saved: {reportHtmlPath}");

                // Generate text report for console
                Console.WriteLine("\n" + new string('=', 70));
                Console.WriteLine("ANALYSIS SUMMARY");
                Console.WriteLine(new string('=', 70));

                // Statistics
                var totalIssues = result.PageIssues.Values.Sum(p => p.LinkIssues.Count);
                var autoFixed = result.PageIssues.Values.Sum(p => p.LinkIssues.Count(i => i.AutoFixed));
                var manualRequired = totalIssues - autoFixed;

                Console.WriteLine($"TOC Pages Found: {result.TocPages.Count} (Pages: {string.Join(", ", result.TocPages)})");
                Console.WriteLine($"Total Issues:    {totalIssues}");
                Console.WriteLine($"Auto-Fixed:      {autoFixed}");
                Console.WriteLine($"Manual Required: {manualRequired}");

                // Auto-fixes applied
                if (result.AutoFixesApplied.Any())
                {
                    Console.WriteLine("\n✅ AUTOMATIC FIXES APPLIED:");
                    foreach (var fix in result.AutoFixesApplied)
                    {
                        Console.WriteLine($"  - {fix}");
                    }
                }

                // Manual fixes required
                if (result.ManualFixesRequired.Any())
                {
                    Console.WriteLine("\n" + new string('=', 70));
                    Console.WriteLine($"📋 MANUAL FIXES REQUIRED ({result.ManualFixesRequired.Count} total)");
                    Console.WriteLine(new string('=', 70));

                    var estimatedTime = result.ManualFixesRequired.Count * 30; // seconds
                    Console.WriteLine($"⏱️  Estimated time: {estimatedTime / 60} minutes\n");

                    // Generate text instructions
                    var textReport = GenerateTextReport(result);
                    Console.WriteLine(textReport);

                    // Save text report
                    await File.WriteAllTextAsync(reportTxtPath, textReport);
                    Console.WriteLine($"\n📄 Text instructions saved: {reportTxtPath}");
                }
                else
                {
                    Console.WriteLine("\n🎉 NO MANUAL FIXES REQUIRED! All issues were resolved automatically.");
                }

                // Open HTML report
                Console.WriteLine($"\n📌 TO VIEW INSTRUCTIONS:");
                Console.WriteLine($"   Open: {Path.GetFullPath(reportHtmlPath)}");

                // Platform-specific open command
                if (Environment.OSVersion.Platform == PlatformID.Unix ||
                    Environment.OSVersion.Platform == PlatformID.MacOSX)
                {
                    Console.WriteLine($"\n   Or run: open \"{reportHtmlPath}\"");
                }
                else
                {
                    Console.WriteLine($"\n   Or run: start \"{reportHtmlPath}\"");
                }
            }
            else
            {
                Console.WriteLine($"❌ ERROR: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ FATAL ERROR: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    static string GenerateTextReport(TocPreprocessResult result)
    {
        var report = new System.Text.StringBuilder();

        int fixNumber = 1;
        foreach (var fix in result.ManualFixesRequired)
        {
            report.AppendLine($"\nFIX #{fixNumber}: Page {fix.PageNumber} - {fix.IssueType}");
            report.AppendLine(new string('-', 60));
            report.AppendLine($"Severity: {fix.Severity}");
            report.AppendLine($"Time: {fix.EstimatedTime}");
            report.AppendLine("\nSteps:");

            foreach (var step in fix.StepByStep)
            {
                report.AppendLine($"  {step}");
            }

            fixNumber++;
        }

        return report.ToString();
    }
}