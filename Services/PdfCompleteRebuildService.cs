using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Service that performs complete PDF rebuild to eliminate ghost fields and corruption.
    /// Uses Python script with PyMuPDF for clean PDF reconstruction.
    /// </summary>
    public class PdfCompleteRebuildService
    {
        private readonly ILogger<PdfCompleteRebuildService> _logger;
        private readonly string _pythonScriptPath;

        public PdfCompleteRebuildService(ILogger<PdfCompleteRebuildService> logger)
        {
            _logger = logger;
            // Get the script path relative to the application directory
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _pythonScriptPath = Path.Combine(baseDir, "pdf_complete_rebuild.py");
            
            if (!File.Exists(_pythonScriptPath))
            {
                // Try alternative paths
                _pythonScriptPath = Path.Combine(Directory.GetCurrentDirectory(), "pdf_complete_rebuild.py");
                if (!File.Exists(_pythonScriptPath))
                {
                    _logger.LogWarning($"Python script not found at expected locations");
                }
            }
        }

        public class FieldUpdate
        {
            public string OriginalName { get; set; } = "";
            public string NewName { get; set; } = "";
            public string? FieldType { get; set; }
            public string? Tooltip { get; set; }
            public bool? IsRequired { get; set; }
            public float? X { get; set; }
            public float? Y { get; set; }
            public float? Width { get; set; }
            public float? Height { get; set; }
            public int? PageNumber { get; set; }
        }

        public class RebuildResult
        {
            public bool Success { get; set; }
            public byte[]? PdfBytes { get; set; }
            public string? ErrorMessage { get; set; }
            public List<FieldInfo> AddedFields { get; set; } = new();
            public int TotalFields { get; set; }
            public int TagElements { get; set; }
            public string? Message { get; set; }
        }

        public class FieldInfo
        {
            public string Name { get; set; } = "";
            public string Type { get; set; } = "";
            public int Page { get; set; }
        }

        /// <summary>
        /// Completely rebuilds a PDF with clean structure and no ghost fields.
        /// </summary>
        public async Task<RebuildResult> CompletelyRebuildPdfAsync(byte[] pdfBytes, List<FieldUpdate> fieldUpdates)
        {
            try
            {
                _logger.LogInformation($"Starting complete PDF rebuild with {fieldUpdates.Count} field updates");

                // Save input PDF to temp file
                var tempInputPath = Path.GetTempFileName() + ".pdf";
                await File.WriteAllBytesAsync(tempInputPath, pdfBytes);

                // Prepare field updates JSON
                var fieldUpdatesJson = JsonSerializer.Serialize(fieldUpdates, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                // Save JSON to temp file (to avoid command line length issues)
                var tempJsonPath = Path.GetTempFileName() + ".json";
                await File.WriteAllTextAsync(tempJsonPath, fieldUpdatesJson);

                try
                {
                    // Execute Python script
                    var result = await ExecutePythonScriptAsync(tempInputPath, tempJsonPath);
                    
                    if (result.Success && !string.IsNullOrEmpty(result.OutputPath))
                    {
                        // Read the rebuilt PDF
                        var rebuiltPdfBytes = await File.ReadAllBytesAsync(result.OutputPath);
                        
                        // Clean up output file
                        try { File.Delete(result.OutputPath); } catch { }
                        
                        _logger.LogInformation($"PDF rebuild successful: {result.TotalFields} fields, {result.TagElements} tag elements");
                        
                        return new RebuildResult
                        {
                            Success = true,
                            PdfBytes = rebuiltPdfBytes,
                            AddedFields = result.AddedFields,
                            TotalFields = result.TotalFields,
                            TagElements = result.TagElements,
                            Message = result.Message
                        };
                    }
                    else
                    {
                        _logger.LogError($"Python script failed: {result.Error}");
                        return new RebuildResult
                        {
                            Success = false,
                            ErrorMessage = result.Error ?? "Unknown error in Python script"
                        };
                    }
                }
                finally
                {
                    // Clean up temp files
                    try { File.Delete(tempInputPath); } catch { }
                    try { File.Delete(tempJsonPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Complete PDF rebuild failed");
                return new RebuildResult
                {
                    Success = false,
                    ErrorMessage = $"Rebuild failed: {ex.Message}"
                };
            }
        }

        private async Task<PythonResult> ExecutePythonScriptAsync(string pdfPath, string jsonPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"\"{_pythonScriptPath}\" \"{pdfPath}\" \"{jsonPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                var output = await outputTask;
                var error = await errorTask;

                if (!string.IsNullOrWhiteSpace(error))
                {
                    _logger.LogDebug($"Python stderr: {error}");
                }

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    try
                    {
                        var result = JsonSerializer.Deserialize<PythonResult>(output, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (result != null)
                        {
                            return result;
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, $"Failed to parse Python output: {output}");
                    }
                }

                return new PythonResult
                {
                    Success = false,
                    Error = $"Python script failed with exit code {process.ExitCode}: {error}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute Python script");
                return new PythonResult
                {
                    Success = false,
                    Error = $"Failed to execute Python script: {ex.Message}"
                };
            }
        }

        private class PythonResult
        {
            public bool Success { get; set; }
            public string? OutputPath { get; set; }
            public string? Error { get; set; }
            public List<FieldInfo> AddedFields { get; set; } = new();
            public int TotalFields { get; set; }
            public int TagElements { get; set; }
            public string? Message { get; set; }
        }
    }

    /// <summary>
    /// Extension to integrate the complete rebuild service into the existing workflow
    /// </summary>
    public static class PdfCompleteRebuildExtensions
    {
        /// <summary>
        /// Use complete rebuild when ghost fields are detected or corruption is suspected.
        /// </summary>
        public static async Task<byte[]?> TryCompleteRebuildAsync(
            this PdfCompleteRebuildService service,
            byte[] pdfBytes,
            List<PdfCompleteRebuildService.FieldUpdate> updates,
            ILogger logger)
        {
            try
            {
                logger.LogInformation("Attempting complete PDF rebuild to eliminate ghost fields");
                
                var result = await service.CompletelyRebuildPdfAsync(pdfBytes, updates);
                
                if (result.Success && result.PdfBytes != null)
                {
                    logger.LogInformation($"Complete rebuild successful: {result.TotalFields} fields created");
                    return result.PdfBytes;
                }
                else
                {
                    logger.LogError($"Complete rebuild failed: {result.ErrorMessage}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Complete rebuild failed with exception");
                return null;
            }
        }
    }
}