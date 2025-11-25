using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WordToPdfConverter.Services.Remediation.Structure;

namespace WordToPdfConverter.Services.Pdf;

/// <summary>
/// C# wrapper for pikepdf Python orchestrator.
/// Replaces ITextPdfStructureWriter with open-source pikepdf solution.
/// </summary>
public class PikepdfStructureWriterService
{
    private readonly ILogger<PikepdfStructureWriterService> _logger;
    private readonly string _pythonPath;
    private readonly string _orchestratorPath;

    public PikepdfStructureWriterService(
        ILogger<PikepdfStructureWriterService> logger,
        IConfiguration configuration)
    {
        _logger = logger;

        // Get Python path from config or use default
        _pythonPath = configuration["PikepdfSettings:PythonPath"] ?? "python3";

        // Get orchestrator path
        var projectRoot = Path.GetDirectoryName(AppContext.BaseDirectory);
        while (projectRoot != null && !File.Exists(Path.Combine(projectRoot, "AccessFormServer.csproj")))
        {
            projectRoot = Directory.GetParent(projectRoot)?.FullName;
        }

        if (projectRoot == null)
            throw new InvalidOperationException("Could not find project root");

        _orchestratorPath = Path.Combine(projectRoot, "Services", "Pdf", "Pikepdf", "orchestrator.py");

        if (!File.Exists(_orchestratorPath))
            throw new FileNotFoundException($"Pikepdf orchestrator not found at: {_orchestratorPath}");

        _logger.LogInformation("[PIKEPDF-WRAPPER] Initialized with orchestrator: {Path}", _orchestratorPath);
    }

    /// <summary>
    /// Rebuild PDF structure tree with MCID marking using pikepdf.
    /// </summary>
    /// <param name="pdfBytes">Input PDF bytes</param>
    /// <param name="structureTree">Structure tree model</param>
    /// <param name="enableMcid">Enable MCID marking (default true)</param>
    /// <returns>Modified PDF bytes with structure tree and MCIDs</returns>
    public async Task<byte[]> RebuildStructureAsync(
        byte[] pdfBytes,
        StructureTree structureTree,
        bool enableMcid = true)
    {
        _logger.LogInformation("[PIKEPDF-WRAPPER] Starting structure rebuild (MCID: {EnableMcid})", enableMcid);

        // Create temp directory for processing
        var tempDir = Path.Combine(Path.GetTempPath(), $"pikepdf_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write input PDF to temp file
            var inputPath = Path.Combine(tempDir, "input.pdf");
            await File.WriteAllBytesAsync(inputPath, pdfBytes);

            // Write structure tree JSON to temp file
            var structureJsonPath = Path.Combine(tempDir, "structure.json");
            var structureJson = JsonSerializer.Serialize(structureTree, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
            await File.WriteAllTextAsync(structureJsonPath, structureJson);

            // Set output path
            var outputPath = Path.Combine(tempDir, "output.pdf");

            // Call Python orchestrator
            var result = await CallPikepdfOrchestratorAsync(inputPath, outputPath, structureJsonPath);

            if (!result.Success)
            {
                throw new Exception($"Pikepdf orchestrator failed: {result.Error}");
            }

            _logger.LogInformation("[PIKEPDF-WRAPPER] Structure rebuild complete: {Elements} elements, {McrKids} MCR kids, {Markers} markers",
                result.ElementsCreated, result.McrKidsCreated, result.BdcEmcPairs);

            // Read output PDF
            if (!File.Exists(outputPath))
            {
                throw new FileNotFoundException($"Pikepdf did not create output file: {outputPath}");
            }

            var outputBytes = await File.ReadAllBytesAsync(outputPath);

            _logger.LogInformation("[PIKEPDF-WRAPPER] Returning {Size} bytes", outputBytes.Length);

            return outputBytes;
        }
        finally
        {
            // Clean up temp directory
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[PIKEPDF-WRAPPER] Failed to clean up temp directory: {Error}", ex.Message);
            }
        }
    }

    private async Task<PikepdfResult> CallPikepdfOrchestratorAsync(
        string inputPath,
        string outputPath,
        string structureJsonPath)
    {
        _logger.LogInformation("[PIKEPDF-WRAPPER] Calling orchestrator: {Python} {Script}",
            _pythonPath, _orchestratorPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = _pythonPath,
            Arguments = $"\"{_orchestratorPath}\" \"{inputPath}\" \"{outputPath}\" \"{structureJsonPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        using var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                outputBuilder.AppendLine(e.Data);
                _logger.LogDebug("[PIKEPDF-STDOUT] {Line}", e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                errorBuilder.AppendLine(e.Data);
                _logger.LogWarning("[PIKEPDF-STDERR] {Line}", e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        var exitCode = process.ExitCode;
        var stdoutOutput = outputBuilder.ToString();
        var stderrOutput = errorBuilder.ToString();

        _logger.LogInformation("[PIKEPDF-WRAPPER] Process exited with code {ExitCode}", exitCode);

        if (exitCode != 0)
        {
            return new PikepdfResult
            {
                Success = false,
                Error = $"Orchestrator exited with code {exitCode}\nStderr: {stderrOutput}"
            };
        }

        // Parse JSON result from stdout
        try
        {
            var lastLine = stdoutOutput.Trim().Split('\n').Last();
            var result = JsonSerializer.Deserialize<PikepdfResult>(lastLine, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (result == null)
            {
                throw new JsonException("Deserialized result is null");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError("[PIKEPDF-WRAPPER] Failed to parse orchestrator output: {Error}", ex.Message);
            return new PikepdfResult
            {
                Success = false,
                Error = $"Failed to parse orchestrator output: {ex.Message}\nOutput: {stdoutOutput}"
            };
        }
    }

    /// <summary>
    /// Test pikepdf installation and module imports.
    /// </summary>
    public async Task<bool> TestInstallationAsync()
    {
        _logger.LogInformation("[PIKEPDF-WRAPPER] Testing installation");

        var testScriptPath = Path.Combine(Path.GetDirectoryName(_orchestratorPath)!, "test_modules.py");

        if (!File.Exists(testScriptPath))
        {
            _logger.LogError("[PIKEPDF-WRAPPER] Test script not found: {Path}", testScriptPath);
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _pythonPath,
            Arguments = $"\"{testScriptPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            _logger.LogError("[PIKEPDF-WRAPPER] Failed to start test process");
            return false;
        }

        await process.WaitForExitAsync();

        var success = process.ExitCode == 0;
        _logger.LogInformation("[PIKEPDF-WRAPPER] Installation test {Result}", success ? "PASSED" : "FAILED");

        return success;
    }
}

/// <summary>
/// Result from pikepdf orchestrator.
/// Matches JSON output from orchestrator.py.
/// </summary>
public class PikepdfResult
{
    public bool Success { get; set; }
    public int ElementsCreated { get; set; }
    public int McrKidsCreated { get; set; }
    public int BdcEmcPairs { get; set; }
    public string? Error { get; set; }
}
