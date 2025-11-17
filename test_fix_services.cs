using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Remediation.Fixes;

namespace TestFixServices
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: dotnet run <input.pdf> <output.pdf>");
                return;
            }

            var inputPath = args[0];
            var outputPath = args[1];

            // Set up logging
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Test Empty Form Element Removal Service
            Console.WriteLine("=== Testing EmptyFormElementRemovalService ===");
            var emptyFormLogger = loggerFactory.CreateLogger<EmptyFormElementRemovalService>();
            var emptyFormService = new EmptyFormElementRemovalService(emptyFormLogger);

            var pdfBytes = await File.ReadAllBytesAsync(inputPath);
            var result1 = await emptyFormService.RemediateAsync(pdfBytes);

            if (result1.Success && result1.ChangesMade)
            {
                Console.WriteLine($"✅ EmptyFormElementRemovalService: Fixed {result1.IssuesFixed} issues");
                pdfBytes = result1.OutputPdf;
            }
            else
            {
                Console.WriteLine($"No changes from EmptyFormElementRemovalService");
            }

            // Test Unmarked XObject Content Fix Service
            Console.WriteLine("\\n=== Testing UnmarkedXObjectContentFixService ===");
            var unmarkedXObjectLogger = loggerFactory.CreateLogger<UnmarkedXObjectContentFixService>();
            var unmarkedXObjectService = new UnmarkedXObjectContentFixService(unmarkedXObjectLogger);

            var result2 = await unmarkedXObjectService.RemediateAsync(pdfBytes);

            if (result2.Success && result2.ChangesMade)
            {
                Console.WriteLine($"✅ UnmarkedXObjectContentFixService: Fixed {result2.IssuesFixed} issues");
                pdfBytes = result2.OutputPdf;
            }
            else
            {
                Console.WriteLine($"No changes from UnmarkedXObjectContentFixService");
            }

            // Save output
            await File.WriteAllBytesAsync(outputPath, pdfBytes);
            Console.WriteLine($"\\n📄 Output saved to: {outputPath}");
        }
    }
}
