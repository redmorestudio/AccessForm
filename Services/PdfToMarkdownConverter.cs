using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Service to convert PDF to Markdown using Marker library for enhanced table analysis
    /// </summary>
    public class PdfToMarkdownConverter
    {
        private readonly ILogger<PdfToMarkdownConverter> _logger;
        private readonly string _markerWrapperPath;

        public PdfToMarkdownConverter(ILogger<PdfToMarkdownConverter> logger)
        {
            _logger = logger;
            // Get the path to marker_wrapper.py relative to the current directory
            _markerWrapperPath = Path.Combine(Directory.GetCurrentDirectory(), "marker_wrapper.py");

            if (!File.Exists(_markerWrapperPath))
            {
                _logger.LogWarning($"Marker wrapper not found at {_markerWrapperPath}");
            }
        }

        /// <summary>
        /// Convert PDF bytes to markdown using Marker library
        /// </summary>
        public string ConvertToMarkdown(byte[] pdfBytes)
        {
            try
            {
                return ConvertToMarkdownAsync(pdfBytes).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting PDF to markdown synchronously");
                return string.Empty;
            }
        }

        /// <summary>
        /// Convert PDF bytes to markdown using Marker library (async)
        /// </summary>
        public async Task<string> ConvertToMarkdownAsync(byte[] pdfBytes)
        {
            if (!File.Exists(_markerWrapperPath))
            {
                _logger.LogWarning("Marker wrapper script not found, skipping markdown conversion");
                return string.Empty;
            }

            string tempPdfPath = null;
            try
            {
                // Create temporary PDF file
                tempPdfPath = Path.Combine(Path.GetTempPath(), $"temp_pdf_{Guid.NewGuid()}.pdf");
                await File.WriteAllBytesAsync(tempPdfPath, pdfBytes);

                _logger.LogInformation($"Converting PDF to markdown using Marker: {Path.GetFileName(tempPdfPath)}");

                // Run marker_wrapper.py
                var result = await RunMarkerWrapperAsync(tempPdfPath);

                if (result.Success && !string.IsNullOrEmpty(result.Markdown))
                {
                    _logger.LogInformation($"Successfully converted PDF to markdown: {result.Markdown.Length} characters");
                    return result.Markdown;
                }
                else
                {
                    _logger.LogWarning($"Marker conversion failed: {result.Error}");
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting PDF to markdown");
                return string.Empty;
            }
            finally
            {
                // Clean up temporary file
                if (tempPdfPath != null && File.Exists(tempPdfPath))
                {
                    try
                    {
                        File.Delete(tempPdfPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Could not delete temporary PDF file: {tempPdfPath}");
                    }
                }
            }
        }

        /// <summary>
        /// Run the marker_wrapper.py script
        /// </summary>
        private async Task<MarkerResult> RunMarkerWrapperAsync(string pdfPath)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"\"{_markerWrapperPath}\" \"{pdfPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = processStartInfo };

                var outputTask = Task.Run(async () =>
                {
                    using var reader = process.StandardOutput;
                    return await reader.ReadToEndAsync();
                });

                var errorTask = Task.Run(async () =>
                {
                    using var reader = process.StandardError;
                    return await reader.ReadToEndAsync();
                });

                process.Start();

                // Wait for process to complete with timeout (5 minutes)
                var processTask = Task.Run(() => process.WaitForExit(300000)); // 5 minute timeout

                await processTask;

                if (!process.HasExited)
                {
                    _logger.LogWarning("Marker process timed out, killing process");
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not kill timed out process");
                    }
                    return new MarkerResult { Success = false, Error = "Process timed out" };
                }

                var output = await outputTask;
                var error = await errorTask;

                if (process.ExitCode != 0)
                {
                    _logger.LogError($"Marker process failed with exit code {process.ExitCode}. Error: {error}");
                    return new MarkerResult { Success = false, Error = $"Exit code {process.ExitCode}: {error}" };
                }

                if (string.IsNullOrEmpty(output))
                {
                    _logger.LogWarning("Marker process returned empty output");
                    return new MarkerResult { Success = false, Error = "Empty output from marker process" };
                }

                // Parse JSON output from marker_wrapper.py
                try
                {
                    var result = JsonSerializer.Deserialize<MarkerResult>(output, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return result ?? new MarkerResult { Success = false, Error = "Failed to deserialize marker output" };
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, $"Failed to parse marker output as JSON: {output}");
                    return new MarkerResult { Success = false, Error = $"JSON parse error: {ex.Message}" };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running marker wrapper script");
                return new MarkerResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Check if Marker is available and working
        /// </summary>
        public async Task<bool> IsMarkerAvailableAsync()
        {
            if (!File.Exists(_markerWrapperPath))
            {
                return false;
            }

            try
            {
                // Test with a minimal command to see if Python and script are working
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = processStartInfo };
                process.Start();
                await Task.Run(() => process.WaitForExit(5000)); // 5 second timeout

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Python3 not available or marker script missing");
                return false;
            }
        }
    }

    /// <summary>
    /// Result from marker_wrapper.py script
    /// </summary>
    public class MarkerResult
    {
        public bool Success { get; set; }
        public string Markdown { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public object Metadata { get; set; }
    }
}