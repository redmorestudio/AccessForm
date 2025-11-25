using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Pdf;
using WordToPdfConverter.Services.Remediation.Structure;

/// <summary>
/// Test program for PikepdfStructureWriterService C# wrapper.
/// Tests end-to-end integration: C# -> Python orchestrator -> pikepdf -> back to C#
/// </summary>
public class TestPikepdfWrapper
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== PikepdfStructureWriterService Integration Test ===\n");

        // Setup logging
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .SetMinimumLevel(LogLevel.Debug)
                .AddConsole();
        });

        var logger = loggerFactory.CreateLogger<PikepdfStructureWriterService>();

        // Setup configuration
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PikepdfSettings:PythonPath"] = "python3"
            })
            .Build();

        // Create service
        PikepdfStructureWriterService service;
        try
        {
            service = new PikepdfStructureWriterService(logger, configuration);
            Console.WriteLine("✅ Service initialized successfully\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Service initialization failed: {ex.Message}\n");
            return;
        }

        // Test 1: Installation test
        Console.WriteLine("--- Test 1: Installation Test ---");
        var installationOk = await service.TestInstallationAsync();
        Console.WriteLine($"Result: {(installationOk ? "✅ PASSED" : "❌ FAILED")}\n");

        if (!installationOk)
        {
            Console.WriteLine("Installation test failed. Cannot proceed with integration test.");
            return;
        }

        // Test 2: End-to-end integration test
        Console.WriteLine("--- Test 2: End-to-End Integration Test ---");

        try
        {
            // Find test PDF
            var testPdfPath = "test_orthodontia.pdf";
            if (!File.Exists(testPdfPath))
            {
                Console.WriteLine($"❌ Test PDF not found: {testPdfPath}");
                return;
            }

            Console.WriteLine($"Reading test PDF: {testPdfPath}");
            var pdfBytes = await File.ReadAllBytesAsync(testPdfPath);
            Console.WriteLine($"Input size: {pdfBytes.Length:N0} bytes");

            // Create simple structure tree for testing
            var structureTree = new StructureTree(
                Nodes: new List<StructureNode>
                {
                    new StructureNode(
                        Role: "Document",
                        TextContent: null,
                        Attributes: new Dictionary<string, string?>
                        {
                            ["id"] = "/0",
                            ["lang"] = "en-US"
                        },
                        Children: new List<StructureNode>
                        {
                            new StructureNode(
                                Role: "H1",
                                TextContent: null,
                                Attributes: new Dictionary<string, string?> { ["id"] = "/0/0" },
                                Children: null
                            ),
                            new StructureNode(
                                Role: "P",
                                TextContent: null,
                                Attributes: new Dictionary<string, string?> { ["id"] = "/0/1" },
                                Children: null
                            )
                        }
                    )
                }
            );

            Console.WriteLine("Structure tree created: Document -> H1, P");

            // Call service
            Console.WriteLine("\nCalling PikepdfStructureWriterService.RebuildStructureAsync()...");
            var outputBytes = await service.RebuildStructureAsync(pdfBytes, structureTree, enableMcid: true);

            Console.WriteLine($"✅ Success! Output size: {outputBytes.Length:N0} bytes");

            // Write output to file
            var outputPath = "test_pikepdf_wrapper_output.pdf";
            await File.WriteAllBytesAsync(outputPath, outputBytes);
            Console.WriteLine($"Output written to: {outputPath}");

            // Validate output
            if (outputBytes.Length < 1000)
            {
                Console.WriteLine("⚠️  Warning: Output file seems too small");
            }
            else
            {
                Console.WriteLine("✅ Output file size looks reasonable");
            }

            Console.WriteLine("\n=== All Tests PASSED ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test failed: {ex.Message}");
            Console.WriteLine($"Stack trace:\n{ex.StackTrace}");
        }
    }
}
