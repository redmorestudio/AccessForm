using System;
using System.IO;
using System.Threading.Tasks;
using iText.Kernel.Pdf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.Layout;
using WordToPdfConverter.Models.Logical;
using WordToPdfConverter.Models.Remediation;
using WordToPdfConverter.Services.Layout;
using WordToPdfConverter.Services.Phase6H;
using WordToPdfConverter.Services.Pdf;
using WordToPdfConverter.Services.Remediation.Models;
using WordToPdfConverter.Services.Remediation.Structure;

/// <summary>
/// Phase 6H Integration Test
/// Tests the external MCID rewriter microservice by:
/// 1. Loading a test PDF
/// 2. Building a structure tree
/// 3. Calling ITextPdfStructureWriter (which calls Phase 6H microservice)
/// 4. Verifying BDC/EMC markers in the output
/// </summary>
public class TestPhase6H
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("PHASE 6H INTEGRATION TEST");
        Console.WriteLine("========================================\n");

        // Setup DI
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Configuration
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        // Job context
        services.AddScoped<RemediationJobContext>();

        // Phase 6H services
        services.AddHttpClient<ExternalMcidRewriterService>();
        services.AddScoped<McidRewritePlanBuilder>();
        services.AddScoped<ExternalMcidRewriterService>();

        // PDF services
        services.AddScoped<IPdfStructureWriter, ITextPdfStructureWriter>();
        services.AddScoped<IContentMcidMarker, ItextContentMcidMarker>();
        services.AddScoped<IPageLayoutEngine, SimpleTopDownLayoutEngine>();

        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TestPhase6H>>();
        var writer = scope.ServiceProvider.GetRequiredService<IPdfStructureWriter>();
        var layoutEngine = scope.ServiceProvider.GetRequiredService<IPageLayoutEngine>();
        var jobContext = scope.ServiceProvider.GetRequiredService<RemediationJobContext>();

        // Configure job context with MCID linking enabled
        jobContext.Options = new RemediationOptions
        {
            EnableMcidLinking = true
        };

        // Find test PDF
        var testPdfPath = "./alexandria_FINAL_WORKING.pdf";
        if (!File.Exists(testPdfPath))
        {
            logger.LogError("[TEST] Test PDF not found: {Path}", testPdfPath);
            return;
        }

        logger.LogInformation("[TEST] Loading test PDF: {Path}", testPdfPath);
        var inputBytes = await File.ReadAllBytesAsync(testPdfPath);

        // Build a simple structure tree for testing
        logger.LogInformation("[TEST] Building structure tree...");
        var tree = BuildTestStructureTree();

        // Build layout plan from structure tree
        logger.LogInformation("[TEST] Building layout plan...");
        var layoutPlan = layoutEngine.BuildLayoutPlan(tree);

        // Create context
        var context = new StructureRebuildContext
        {
            LayoutPlan = layoutPlan
        };
        jobContext.StructureContext.LayoutPlan = layoutPlan;

        // Call ITextPdfStructureWriter (which will call Phase 6H)
        logger.LogInformation("[TEST] ========== CALLING STRUCTURE WRITER ==========");
        var outputBytes = writer.Rewrite(inputBytes, tree, context);

        // Save output
        var outputPath = "./alexandria_phase6h_test_output.pdf";
        await File.WriteAllBytesAsync(outputPath, outputBytes);
        logger.LogInformation("[TEST] Saved output to: {Path}", outputPath);

        // Verify BDC/EMC markers
        logger.LogInformation("[TEST] ========== VERIFYING BDC/EMC MARKERS ==========");
        var pdfText = System.Text.Encoding.ASCII.GetString(outputBytes);
        var bdcCount = pdfText.Split(new[] { "BDC" }, StringSplitOptions.None).Length - 1;
        var emcCount = pdfText.Split(new[] { "EMC" }, StringSplitOptions.None).Length - 1;

        logger.LogInformation("[TEST] ========================================");
        logger.LogInformation("[TEST] BDC markers found: {BdcCount}", bdcCount);
        logger.LogInformation("[TEST] EMC markers found: {EmcCount}", emcCount);
        logger.LogInformation("[TEST] ========================================");

        if (bdcCount > 0 && emcCount > 0)
        {
            logger.LogInformation("[TEST] ✅✅✅ SUCCESS! Phase 6H is working - MCID markers PERSIST!");
        }
        else
        {
            logger.LogWarning("[TEST] ⚠️  No BDC/EMC markers found - Phase 6H may not be working");
        }

        Console.WriteLine("\nTest complete. Check the output PDF: {0}", outputPath);
    }

    private static StructureTree BuildTestStructureTree()
    {
        // Build a simple test structure with a few paragraphs
        var paragraph1 = new StructureNode(
            Role: "P",
            TextContent: null,
            Attributes: null,
            Children: null)
        {
            Bounds = new Rect(72, 720, 468, 12),
            McidReferences = new System.Collections.Generic.List<McidReference>
            {
                new McidReference { PageIndex = 0, Mcid = 0 }
            }
        };

        var paragraph2 = new StructureNode(
            Role: "P",
            TextContent: null,
            Attributes: null,
            Children: null)
        {
            Bounds = new Rect(72, 700, 468, 12),
            McidReferences = new System.Collections.Generic.List<McidReference>
            {
                new McidReference { PageIndex = 0, Mcid = 1 }
            }
        };

        var documentRoot = new StructureNode(
            Role: "Document",
            TextContent: null,
            Attributes: null,
            Children: new System.Collections.Generic.List<StructureNode> { paragraph1, paragraph2 });

        var tree = new StructureTree(
            Nodes: new System.Collections.Generic.List<StructureNode> { documentRoot });

        return tree;
    }
}
