using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Service to fix PDF/UA violations where whitespace-only content is tagged.
    /// Uses Python/PyMuPDF to remove tagging from whitespace elements.
    /// </summary>
    public class TaggedWhitespaceFixService
    {
        private readonly ILogger<TaggedWhitespaceFixService> _logger;
        private readonly string _pythonScript = "fix_tagged_whitespace.py";

        public TaggedWhitespaceFixService(ILogger<TaggedWhitespaceFixService> logger)
        {
            _logger = logger;
        }

        public class FixResult
        {
            public bool Success { get; set; }
            public byte[]? FixedPdf { get; set; }
            public int ViolationsFound { get; set; }
            public int ViolationsFixed { get; set; }
            public string? ErrorMessage { get; set; }
        }

        /// <summary>
        /// Fix tagged whitespace violations in a PDF by removing tagging from whitespace-only elements.
        /// </summary>
        public async Task<FixResult> FixTaggedWhitespaceAsync(byte[] pdfBytes)
        {
            try
            {
                _logger.LogInformation("Checking for tagged whitespace violations...");

                // Save PDF to temp file
                var tempInputPath = Path.Combine(Path.GetTempPath(), $"whitespace_fix_input_{Guid.NewGuid()}.pdf");
                await File.WriteAllBytesAsync(tempInputPath, pdfBytes);

                var scriptPath = Path.Combine(Directory.GetCurrentDirectory(), _pythonScript);
                if (!File.Exists(scriptPath))
                {
                    _logger.LogError($"Python script not found at: {scriptPath}");
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = $"Python script not found: {scriptPath}"
                    };
                }

                _logger.LogDebug($"Running Python script: {scriptPath}");

                // Run Python script
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "python3",
                        Arguments = $"\"{scriptPath}\" \"{tempInputPath}\"",
                        WorkingDirectory = Directory.GetCurrentDirectory(),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                if (!process.WaitForExit(30000)) // 30 second timeout
                {
                    process.Kill();
                    CleanupTempFile(tempInputPath);
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = "Whitespace fix script timed out"
                    };
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();

                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogWarning($"Python script stderr: {error}");
                }

                if (process.ExitCode != 0)
                {
                    _logger.LogError($"Python script failed with exit code {process.ExitCode}");
                    _logger.LogError($"Output: {output}");
                    CleanupTempFile(tempInputPath);
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = $"Script failed: {error}"
                    };
                }

                // Parse result JSON
                var result = JsonSerializer.Deserialize<JsonElement>(output);

                if (!result.TryGetProperty("success", out var success) || !success.GetBoolean())
                {
                    var errorMsg = result.TryGetProperty("error", out var err) ? err.GetString() : "Unknown error";
                    CleanupTempFile(tempInputPath);
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = errorMsg
                    };
                }

                // Get violations count
                var violationsFound = result.TryGetProperty("violations_found", out var vf) ? vf.GetInt32() : 0;
                var violationsFixed = result.TryGetProperty("violations_fixed", out var vx) ? vx.GetInt32() : 0;

                if (violationsFound == 0)
                {
                    _logger.LogInformation("✅ No tagged whitespace violations found - PDF is clean");
                    CleanupTempFile(tempInputPath);
                    return new FixResult
                    {
                        Success = true,
                        FixedPdf = pdfBytes, // Return original
                        ViolationsFound = 0,
                        ViolationsFixed = 0
                    };
                }

                // Get output path
                if (!result.TryGetProperty("output_path", out var outputPath))
                {
                    CleanupTempFile(tempInputPath);
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = "No output path in script result"
                    };
                }

                var outputFile = outputPath.GetString();
                if (string.IsNullOrEmpty(outputFile) || !File.Exists(outputFile))
                {
                    CleanupTempFile(tempInputPath);
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = "Output file not found"
                    };
                }

                // Read the fixed PDF
                var fixedPdfBytes = await File.ReadAllBytesAsync(outputFile);

                _logger.LogInformation($"✅ Removed tagging from {violationsFixed} whitespace elements (found {violationsFound} total)");

                // Cleanup
                CleanupTempFile(tempInputPath);
                CleanupTempFile(outputFile);

                return new FixResult
                {
                    Success = true,
                    FixedPdf = fixedPdfBytes,
                    ViolationsFound = violationsFound,
                    ViolationsFixed = violationsFixed
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fix tagged whitespace violations");
                return new FixResult
                {
                    Success = false,
                    ErrorMessage = $"Exception: {ex.Message}"
                };
            }
        }

        private void CleanupTempFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to cleanup temp file {path}: {ex.Message}");
            }
        }
    }
}
