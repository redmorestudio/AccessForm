using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.Pdf.Tagutils;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using WordToPdfConverter.Models;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Adopts orphaned whitespace text objects into artifact tags
    /// to fix "Text object not tagged" PDF/UA violations
    ///
    /// SAFE APPROACH: Instead of manipulating content streams directly,
    /// we use iText7's structure tree API to wrap orphaned content in Artifact tags.
    /// This is safer and preserves PDF integrity.
    /// </summary>
    public class OrphanedWhitespaceAdoptionService
    {
        private readonly ILogger<OrphanedWhitespaceAdoptionService> _logger;
        private readonly string _pythonScript = "wrap_orphaned_whitespace.py";

        public OrphanedWhitespaceAdoptionService(ILogger<OrphanedWhitespaceAdoptionService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Find and wrap orphaned whitespace in artifact tags using Python/PyMuPDF
        /// </summary>
        public async Task<AdoptionResult> AdoptOrphanedWhitespaceAsync(byte[] pdfBytes)
        {
            try
            {
                _logger.LogInformation("Checking for orphaned whitespace (untagged text objects)...");

                // Save PDF to temp file
                var tempInputPath = Path.Combine(Path.GetTempPath(), $"orphan_fix_input_{Guid.NewGuid()}.pdf");
                await File.WriteAllBytesAsync(tempInputPath, pdfBytes);

                var scriptPath = Path.Combine(Directory.GetCurrentDirectory(), _pythonScript);
                if (!File.Exists(scriptPath))
                {
                    _logger.LogError($"Python script not found at: {scriptPath}");
                    return new AdoptionResult
                    {
                        Success = false,
                        ErrorMessage = $"Python script not found: {scriptPath}"
                    };
                }

                var tempOutputPath = Path.Combine(Path.GetTempPath(), $"orphan_fix_output_{Guid.NewGuid()}.pdf");
                _logger.LogDebug($"Running Python script: {scriptPath}");

                // Run Python script
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "python3",
                        Arguments = $"\"{scriptPath}\" \"{tempInputPath}\" \"{tempOutputPath}\"",
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
                    CleanupTempFile(tempOutputPath);
                    return new AdoptionResult
                    {
                        Success = false,
                        ErrorMessage = "Orphan wrap script timed out"
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
                    CleanupTempFile(tempOutputPath);
                    return new AdoptionResult
                    {
                        Success = false,
                        ErrorMessage = $"Script failed: {error}"
                    };
                }

                _logger.LogInformation($"Script output: {output}");

                // Check if output file was created
                if (!File.Exists(tempOutputPath))
                {
                    // No orphans found, return original
                    _logger.LogInformation("✅ No orphaned whitespace found - PDF is clean");
                    CleanupTempFile(tempInputPath);
                    return new AdoptionResult
                    {
                        Success = true,
                        OrphansFound = 0,
                        OrphansAdopted = 0,
                        FixedPdf = pdfBytes
                    };
                }

                // Read the fixed PDF
                var fixedPdfBytes = await File.ReadAllBytesAsync(tempOutputPath);

                // Parse orphan count from output
                var orphansWrapped = 0;
                var match = System.Text.RegularExpressions.Regex.Match(output, @"Wrapped (\d+) orphaned");
                if (match.Success)
                {
                    orphansWrapped = int.Parse(match.Groups[1].Value);
                }

                _logger.LogInformation($"✅ Wrapped {orphansWrapped} orphaned whitespace elements in /Artifact tags");

                // Cleanup
                CleanupTempFile(tempInputPath);
                CleanupTempFile(tempOutputPath);

                return new AdoptionResult
                {
                    Success = true,
                    OrphansFound = orphansWrapped,
                    OrphansAdopted = orphansWrapped,
                    FixedPdf = fixedPdfBytes
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to adopt orphaned whitespace");
                return new AdoptionResult
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
