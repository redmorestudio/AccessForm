using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Pdf;
using WordToPdfConverter.Services.Remediation.Structure;

/// <summary>
/// Test pikepdf integration end-to-end (C# -> Python -> pikepdf -> C#).
/// </summary>
public static class TestPikepdfIntegration
{
    public static async Task Main(string[] args)
    {
        // Setup logging
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger<PikepdfStructureWriterService>();

        // Setup configuration
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PikepdfSettings:PythonPath"] = "python3"
            })
            .Build();

        // Create service
        var service = new PikepdfStructureWriterService(logger, config);

        // Test 1: Check installation
        Console.WriteLine(new string('=', 60));
        Console.WriteLine("TEST 1: Testing pikepdf installation");
        Console.WriteLine(new string('=', 60));

        var installOk = await service.TestInstallationAsync();
        Console.WriteLine($"Installation test: {(installOk ? "✅ PASSED" : "❌ FAILED")}");

        if (!installOk)
        {
            Console.WriteLine("❌ Pikepdf installation test failed. Cannot proceed.");
            Environment.Exit(1);
        }

        // Test 2: Process a simple PDF
        Console.WriteLine();
        Console.WriteLine(new string('=', 60));
        Console.WriteLine("TEST 2: Processing test PDF with structure tree");
        Console.WriteLine(new string('=', 60));

        // Find a test PDF
        var testPdfPath = FindTestPdf();
        if (testPdfPath == null)
        {
            Console.WriteLine("❌ No test PDF found. Skipping processing test.");
            Environment.Exit(1);
        }

        Console.WriteLine($"Using test PDF: {testPdfPath}");

        // Read test PDF
        var pdfBytes = await File.ReadAllBytesAsync(testPdfPath);
        Console.WriteLine($"Read {pdfBytes.Length} bytes from test PDF");

        // Create simple structure tree
        var structureTree = CreateSimpleStructureTree();
        Console.WriteLine($"Created structure tree with {structureTree.Nodes.Count} root nodes");

        try
        {
            // Process with pikepdf
            var outputBytes = await service.RebuildStructureAsync(pdfBytes, structureTree, enableMcid: true);

            Console.WriteLine($"✅ Processing succeeded! Output: {outputBytes.Length} bytes");

            // Save output
            var outputPath = Path.Combine(
                Path.GetDirectoryName(testPdfPath)!,
                Path.GetFileNameWithoutExtension(testPdfPath) + "_pikepdf_test.pdf"
            );

            await File.WriteAllBytesAsync(outputPath, outputBytes);
            Console.WriteLine($"✅ Saved output to: {outputPath}");

            Console.WriteLine();
            Console.WriteLine("🎉 ALL TESTS PASSED!");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Processing failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }

    private static string? FindTestPdf()
    {
        var projectRoot = Path.GetDirectoryName(AppContext.BaseDirectory);
        while (projectRoot != null && !File.Exists(Path.Combine(projectRoot, "AccessFormServer.csproj")))
        {
            projectRoot = Directory.GetParent(projectRoot)?.FullName;
        }

        if (projectRoot == null)
            return null;

        // Try to find any PDF in common test locations
        var testLocations = new[]
        {
            Path.Combine(projectRoot, "TestAssets"),
            Path.Combine(projectRoot, "StateAssets"),
            projectRoot
        };

        foreach (var location in testLocations)
        {
            if (!Directory.Exists(location))
                continue;

            var pdfs = Directory.GetFiles(location, "*.pdf", SearchOption.TopDirectoryOnly)
                .Where(p => !p.Contains("_output") && !p.Contains("_test"))
                .ToArray();

            if (pdfs.Length > 0)
                return pdfs[0];
        }

        return null;
    }

    private static StructureTree CreateSimpleStructureTree()
    {
        // Create a simple two-level structure tree:
        // Document
        //   -> H1 "Test Heading"
        //   -> P "Test paragraph"

        var h1Attrs = new Dictionary<string, string> { ["id"] = "/0/0" };
        var h1 = new StructureNode(
            Role: "H1",
            TextContent: "Test Heading",
            Attributes: h1Attrs,
            Children: null
        );

        var pAttrs = new Dictionary<string, string> { ["id"] = "/0/1" };
        var p = new StructureNode(
            Role: "P",
            TextContent: "Test paragraph",
            Attributes: pAttrs,
            Children: null
        );

        var docAttrs = new Dictionary<string, string>
        {
            ["id"] = "/0",
            ["lang"] = "en-US"
        };
        var document = new StructureNode(
            Role: "Document",
            TextContent: null,
            Attributes: docAttrs,
            Children: new[] { h1, p }
        );

        return new StructureTree(new[] { document });
    }
}
