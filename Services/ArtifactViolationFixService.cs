using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Service to fix PDF/UA violations where tagged content is incorrectly placed inside artifacts.
    /// Uses Python/PyMuPDF to unwrap tagged content from artifact markers.
    /// </summary>
    public class ArtifactViolationFixService
    {
        private readonly ILogger<ArtifactViolationFixService> _logger;
        private readonly string _pythonScript = "fix_artifact_violations.py";
        private readonly string _pythonScriptV2 = "fix_artifact_violations_v2.py";
        private readonly string _controlCharScript = "fix_control_characters.py";
        private readonly string _aggressiveWhitespaceScript = "fix_all_untagged_whitespace.py";
        private bool _useV2Script = true; // Default to v2 for better handling

        public ArtifactViolationFixService(ILogger<ArtifactViolationFixService> logger)
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
        /// Fix artifact violations in a PDF by unwrapping tagged content from artifact markers.
        /// </summary>
        public async Task<FixResult> FixArtifactViolationsAsync(byte[] pdfBytes, bool useV2 = true)
        {
            try
            {
                _logger.LogInformation($"Checking for artifact violations using {(useV2 ? "v2" : "v1")} script...");

                // Save PDF to temp file
                var tempInputPath = Path.Combine(Path.GetTempPath(), $"artifact_fix_input_{Guid.NewGuid()}.pdf");
                await File.WriteAllBytesAsync(tempInputPath, pdfBytes);

                var scriptToUse = useV2 ? _pythonScriptV2 : _pythonScript;
                var scriptPath = Path.Combine(Directory.GetCurrentDirectory(), scriptToUse);

                // Fallback to v1 if v2 doesn't exist
                if (!File.Exists(scriptPath) && useV2)
                {
                    _logger.LogWarning($"V2 script not found, falling back to v1");
                    scriptPath = Path.Combine(Directory.GetCurrentDirectory(), _pythonScript);
                    scriptToUse = _pythonScript;
                }

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

                // Run Python script with optional verbose flag for v2
                var scriptArgs = useV2 && scriptToUse == _pythonScriptV2
                    ? $"\"{scriptPath}\" \"{tempInputPath}\" --verbose"
                    : $"\"{scriptPath}\" \"{tempInputPath}\"";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "python3",
                        Arguments = scriptArgs,
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
                        ErrorMessage = "Artifact fix script timed out"
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

                if (violationsFixed == 0)
                {
                    _logger.LogInformation("✅ No violations to fix - PDF is clean");
                    CleanupTempFile(tempInputPath);
                    return new FixResult
                    {
                        Success = true,
                        FixedPdf = pdfBytes, // Return original
                        ViolationsFound = violationsFound,
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

                _logger.LogInformation($"✅ Fixed {violationsFixed} artifact violations (found {violationsFound} total)");

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
                _logger.LogError(ex, "Failed to fix artifact violations");
                return new FixResult
                {
                    Success = false,
                    ErrorMessage = $"Exception: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Fix control character violations (ETX, untagged spaces) in a PDF.
        /// This is a more aggressive fix for persistent untagged text issues.
        /// </summary>
        public async Task<FixResult> FixControlCharactersAsync(byte[] pdfBytes)
        {
            try
            {
                _logger.LogInformation("Fixing control character violations (ETX, spaces)...");

                // Save PDF to temp file
                var tempInputPath = Path.Combine(Path.GetTempPath(), $"control_fix_input_{Guid.NewGuid()}.pdf");
                await File.WriteAllBytesAsync(tempInputPath, pdfBytes);

                var scriptPath = Path.Combine(Directory.GetCurrentDirectory(), _controlCharScript);

                if (!File.Exists(scriptPath))
                {
                    _logger.LogWarning($"Control character fix script not found at: {scriptPath}");
                    CleanupTempFile(tempInputPath);
                    // Return original PDF if script not found
                    return new FixResult
                    {
                        Success = true,
                        FixedPdf = pdfBytes,
                        ViolationsFound = 0,
                        ViolationsFixed = 0
                    };
                }

                _logger.LogDebug($"Running control character fix script: {scriptPath}");

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
                        ErrorMessage = "Control character fix script timed out"
                    };
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();

                if (!string.IsNullOrWhiteSpace(error))
                {
                    _logger.LogDebug($"Script stderr: {error}");
                }

                if (process.ExitCode != 0)
                {
                    _logger.LogWarning($"Control character fix script failed: {output}");
                    CleanupTempFile(tempInputPath);
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = $"Script failed: {output}"
                    };
                }

                // Parse JSON output
                var result = JsonSerializer.Deserialize<JsonElement>(output);

                if (!result.TryGetProperty("success", out var success) || !success.GetBoolean())
                {
                    CleanupTempFile(tempInputPath);
                    var errorMsg = result.TryGetProperty("error", out var err) ? err.GetString() : "Unknown error";
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = errorMsg
                    };
                }

                // Get counts
                var etxRemoved = result.TryGetProperty("etx_removed", out var etx) ? etx.GetInt32() : 0;
                var spacesFixed = result.TryGetProperty("spaces_fixed", out var spaces) ? spaces.GetInt32() : 0;
                var btBlocksFixed = result.TryGetProperty("bt_blocks_fixed", out var bt) ? bt.GetInt32() : 0;
                var totalOperations = etxRemoved + spacesFixed + btBlocksFixed;

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

                _logger.LogInformation($"✅ Fixed control characters: {etxRemoved} ETX, {spacesFixed} spaces, {btBlocksFixed} BT blocks");

                // Cleanup
                CleanupTempFile(tempInputPath);
                CleanupTempFile(outputFile);

                return new FixResult
                {
                    Success = true,
                    FixedPdf = fixedPdfBytes,
                    ViolationsFound = totalOperations,
                    ViolationsFixed = totalOperations
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fix control characters");
                return new FixResult
                {
                    Success = false,
                    ErrorMessage = $"Exception: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Fix ALL untagged whitespace aggressively for PAC compliance.
        /// </summary>
        public async Task<FixResult> FixAggressiveWhitespaceAsync(byte[] pdfBytes)
        {
            try
            {
                _logger.LogInformation("Running aggressive whitespace fix for PAC compliance...");

                // Save PDF to temp file
                var tempInputPath = Path.Combine(Path.GetTempPath(), $"aggressive_fix_input_{Guid.NewGuid()}.pdf");
                await File.WriteAllBytesAsync(tempInputPath, pdfBytes);

                var scriptPath = Path.Combine(Directory.GetCurrentDirectory(), _aggressiveWhitespaceScript);

                if (!File.Exists(scriptPath))
                {
                    _logger.LogWarning($"Aggressive whitespace fix script not found at: {scriptPath}");
                    CleanupTempFile(tempInputPath);
                    // Return original PDF if script not found
                    return new FixResult
                    {
                        Success = true,
                        FixedPdf = pdfBytes,
                        ViolationsFound = 0,
                        ViolationsFixed = 0
                    };
                }

                _logger.LogDebug($"Running aggressive whitespace fix script: {scriptPath}");

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
                        ErrorMessage = "Aggressive whitespace fix script timed out"
                    };
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();

                if (!string.IsNullOrWhiteSpace(error))
                {
                    _logger.LogDebug($"Script stderr: {error}");
                }

                if (process.ExitCode != 0)
                {
                    _logger.LogWarning($"Aggressive whitespace fix script failed: {output}");
                    CleanupTempFile(tempInputPath);
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = $"Script failed: {output}"
                    };
                }

                // Parse JSON output
                var result = JsonSerializer.Deserialize<JsonElement>(output);

                if (!result.TryGetProperty("success", out var success) || !success.GetBoolean())
                {
                    CleanupTempFile(tempInputPath);
                    var errorMsg = result.TryGetProperty("error", out var err) ? err.GetString() : "Unknown error";
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = errorMsg
                    };
                }

                // Get counts
                var spacesRemoved = result.TryGetProperty("spaces_removed", out var spaces) ? spaces.GetInt32() : 0;
                var controlRemoved = result.TryGetProperty("control_removed", out var control) ? control.GetInt32() : 0;
                var blocksRemoved = result.TryGetProperty("blocks_removed", out var blocks) ? blocks.GetInt32() : 0;
                var totalFixes = result.TryGetProperty("total_fixes", out var total) ? total.GetInt32() : 0;

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

                _logger.LogInformation($"✅ Aggressive fix: {spacesRemoved} spaces, {controlRemoved} control chars, {blocksRemoved} blocks");

                // Cleanup
                CleanupTempFile(tempInputPath);
                CleanupTempFile(outputFile);

                return new FixResult
                {
                    Success = true,
                    FixedPdf = fixedPdfBytes,
                    ViolationsFound = totalFixes,
                    ViolationsFixed = totalFixes
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run aggressive whitespace fix");
                return new FixResult
                {
                    Success = false,
                    ErrorMessage = $"Exception: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Fix text operations that appear after EMC markers or in problematic locations.
        /// This targets the specific pattern where text appears outside proper tagging.
        /// </summary>
        public async Task<FixResult> FixTextAfterEmcAsync(byte[] pdfBytes)
        {
            try
            {
                _logger.LogInformation("Fixing text after EMC and artifact violations...");

                // Save PDF to temp file
                var tempInputPath = Path.Combine(Path.GetTempPath(), $"emc_fix_input_{Guid.NewGuid()}.pdf");
                await File.WriteAllBytesAsync(tempInputPath, pdfBytes);

                var scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "fix_text_after_emc.py");

                if (!File.Exists(scriptPath))
                {
                    _logger.LogWarning($"EMC fix script not found at: {scriptPath}");
                    CleanupTempFile(tempInputPath);
                    // Return original PDF if script not found
                    return new FixResult
                    {
                        Success = true,
                        FixedPdf = pdfBytes,
                        ViolationsFound = 0,
                        ViolationsFixed = 0
                    };
                }

                _logger.LogDebug($"Running EMC fix script: {scriptPath}");

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
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogDebug($"EMC fix stderr: {error}");
                }

                // Parse the JSON output
                JsonDocument result;
                try
                {
                    result = JsonDocument.Parse(output);
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogError($"Failed to parse EMC fix output: {jsonEx.Message}\nOutput: {output}");
                    CleanupTempFile(tempInputPath);
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = "Failed to parse script output"
                    };
                }

                // Check success
                if (!result.RootElement.TryGetProperty("success", out var success) || !success.GetBoolean())
                {
                    var errorMsg = result.RootElement.TryGetProperty("error", out var err) ? err.GetString() : "Unknown error";
                    CleanupTempFile(tempInputPath);
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = errorMsg
                    };
                }

                // Get metrics
                var afterEmcFixed = result.RootElement.TryGetProperty("after_emc_fixed", out var afterProp) ? afterProp.GetInt32() : 0;
                var artifactFixed = result.RootElement.TryGetProperty("artifact_fixed", out var artifactProp) ? artifactProp.GetInt32() : 0;
                var totalFixed = afterEmcFixed + artifactFixed;

                // Get output path
                if (!result.RootElement.TryGetProperty("output_path", out var outputPath))
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

                _logger.LogInformation($"✅ EMC fix: {afterEmcFixed} after-EMC, {artifactFixed} artifact violations fixed");

                // Cleanup
                CleanupTempFile(tempInputPath);
                CleanupTempFile(outputFile);

                return new FixResult
                {
                    Success = true,
                    FixedPdf = fixedPdfBytes,
                    ViolationsFound = totalFixed,
                    ViolationsFixed = totalFixed
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run EMC fix");
                return new FixResult
                {
                    Success = false,
                    ErrorMessage = $"Exception: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Fix violations introduced by PassportPDF processing.
        /// PassportPDF uses \r (carriage return) as line separators and creates specific patterns.
        /// </summary>
        public async Task<FixResult> FixPassportPdfViolationsAsync(byte[] pdfBytes)
        {
            try
            {
                _logger.LogInformation("Fixing PassportPDF-specific violations...");

                // Create temp file for input
                var tempInputPath = Path.GetTempFileName();
                await File.WriteAllBytesAsync(tempInputPath, pdfBytes);

                // Run the PassportPDF violations fix script
                var scriptPath = Path.Combine(AppContext.BaseDirectory, "fix_passportpdf_violations.py");

                if (!File.Exists(scriptPath))
                {
                    CleanupTempFile(tempInputPath);
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = "PassportPDF fix script not found"
                    };
                }

                var processInfo = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"\"{scriptPath}\" \"{tempInputPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    CleanupTempFile(tempInputPath);
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = "Failed to start PassportPDF fix script"
                    };
                }

                // Wait for completion with timeout
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                }
                catch (TimeoutException)
                {
                    process.Kill();
                    CleanupTempFile(tempInputPath);
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = "PassportPDF fix script timed out"
                    };
                }

                if (!process.HasExited)
                {
                    process.Kill();
                    CleanupTempFile(tempInputPath);
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = "PassportPDF fix script timed out"
                    };
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();

                if (!string.IsNullOrWhiteSpace(error))
                {
                    _logger.LogWarning($"PassportPDF script stderr: {error}");
                }

                if (process.ExitCode != 0)
                {
                    _logger.LogWarning($"PassportPDF fix script failed with exit code {process.ExitCode}");
                    _logger.LogWarning($"Output: {output}");
                    _logger.LogWarning($"Error: {error}");
                    CleanupTempFile(tempInputPath);
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = $"Script failed: {output}"
                    };
                }

                // Parse JSON output
                JsonElement result;
                try
                {
                    result = JsonSerializer.Deserialize<JsonElement>(output);
                }
                catch (Exception parseEx)
                {
                    _logger.LogError($"Failed to parse PassportPDF script output as JSON: {parseEx.Message}");
                    _logger.LogError($"Raw output: {output}");
                    CleanupTempFile(tempInputPath);
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = $"Failed to parse script output: {parseEx.Message}"
                    };
                }

                if (!result.TryGetProperty("success", out var success) || !success.GetBoolean())
                {
                    CleanupTempFile(tempInputPath);
                    var errorMsg = result.TryGetProperty("error", out var err) ? err.GetString() : "Unknown error";
                    return new FixResult
                    {
                        Success = false,
                        ErrorMessage = errorMsg
                    };
                }

                // Get the output path from the result
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

                // Get counts
                var etxFixed = result.TryGetProperty("etx_fixed", out var etx) ? etx.GetInt32() : 0;
                var transparencyFixed = result.TryGetProperty("transparency_fixed", out var trans) ? trans.GetInt32() : 0;
                var totalFixed = result.TryGetProperty("total_fixed", out var total) ? total.GetInt32() : 0;

                _logger.LogInformation($"✅ PassportPDF fix: {etxFixed} ETX, {transparencyFixed} transparency blocks (total: {totalFixed})");

                // Cleanup
                CleanupTempFile(tempInputPath);
                CleanupTempFile(outputFile);

                return new FixResult
                {
                    Success = true,
                    FixedPdf = fixedPdfBytes,
                    ViolationsFound = totalFixed,
                    ViolationsFixed = totalFixed
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run PassportPDF fix");
                return new FixResult
                {
                    Success = false,
                    ErrorMessage = $"Exception: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Run ALL fixes in sequence for maximum PAC compliance.
        /// </summary>
        public async Task<FixResult> FixAllViolationsAsync(byte[] pdfBytes)
        {
            var totalFound = 0;
            var totalFixed = 0;
            var currentPdf = pdfBytes;

            // 1. First run artifact fix
            _logger.LogInformation("[1/4] Running artifact violation fix...");
            var artifactResult = await FixArtifactViolationsAsync(currentPdf, true);
            if (artifactResult.Success && artifactResult.FixedPdf != null)
            {
                currentPdf = artifactResult.FixedPdf;
                totalFound += artifactResult.ViolationsFound;
                totalFixed += artifactResult.ViolationsFixed;
            }

            // 2. Then run control character fix
            _logger.LogInformation("[2/4] Running control character fix...");
            var controlResult = await FixControlCharactersAsync(currentPdf);
            if (controlResult.Success && controlResult.FixedPdf != null)
            {
                currentPdf = controlResult.FixedPdf;
                totalFound += controlResult.ViolationsFound;
                totalFixed += controlResult.ViolationsFixed;
            }

            // 3. Run aggressive whitespace fix for any remaining issues
            _logger.LogInformation("[3/4] Running aggressive whitespace fix for PAC compliance...");
            var aggressiveResult = await FixAggressiveWhitespaceAsync(currentPdf);
            if (aggressiveResult.Success && aggressiveResult.FixedPdf != null)
            {
                currentPdf = aggressiveResult.FixedPdf;
                totalFound += aggressiveResult.ViolationsFound;
                totalFixed += aggressiveResult.ViolationsFixed;
            }

            // 4. Run EMC fix for text outside proper tagging
            _logger.LogInformation("[4/5] Running EMC/Artifact fix for text outside proper tagging...");
            var emcResult = await FixTextAfterEmcAsync(currentPdf);
            if (emcResult.Success && emcResult.FixedPdf != null)
            {
                currentPdf = emcResult.FixedPdf;
                totalFound += emcResult.ViolationsFound;
                totalFixed += emcResult.ViolationsFixed;
            }

            // 5. Run PassportPDF-specific fix (only if PassportPDF has already run)
            // Note: This is primarily for post-PassportPDF cleanup
            _logger.LogInformation("[5/5] Attempting PassportPDF-specific fixes...");
            try
            {
                var passportResult = await FixPassportPdfViolationsAsync(currentPdf);
                if (passportResult.Success && passportResult.FixedPdf != null)
                {
                    currentPdf = passportResult.FixedPdf;
                    totalFound += passportResult.ViolationsFound;
                    totalFixed += passportResult.ViolationsFixed;
                    _logger.LogInformation($"PassportPDF fix succeeded: {passportResult.ViolationsFixed} violations fixed");
                }
                else
                {
                    _logger.LogDebug("PassportPDF fix did not find violations or failed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"PassportPDF fix skipped: {ex.Message}");
            }

            _logger.LogInformation($"✅ All fixes complete: {totalFixed} violations fixed");

            // Return combined results
            return new FixResult
            {
                Success = true,
                FixedPdf = currentPdf,
                ViolationsFound = totalFound,
                ViolationsFixed = totalFixed
            };
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
