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
        private readonly AccessFormServer.Services.PassportPdfService? _passportPdfService;
        private readonly AccessFormServer.Services.AdobeAutotagService? _adobeAutotagService;
        private readonly AccessFormServer.Services.AsposePdfService? _asposePdfService;

        public PdfCompleteRebuildService(
            ILogger<PdfCompleteRebuildService> logger, 
            AccessFormServer.Services.PassportPdfService? passportPdfService = null,
            AccessFormServer.Services.AdobeAutotagService? adobeAutotagService = null,
            AccessFormServer.Services.AsposePdfService? asposePdfService = null)
        {
            _logger = logger;
            _passportPdfService = passportPdfService;
            _adobeAutotagService = adobeAutotagService;
            _asposePdfService = asposePdfService;
            // Get the script path relative to the application directory
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _pythonScriptPath = Path.Combine(baseDir, "pdf_complete_rebuild.py");
            
            _logger.LogInformation($"Looking for Python script at: {_pythonScriptPath}");
            
            if (!File.Exists(_pythonScriptPath))
            {
                // Try alternative paths
                _pythonScriptPath = Path.Combine(Directory.GetCurrentDirectory(), "pdf_complete_rebuild.py");
                _logger.LogInformation($"Trying alternative path: {_pythonScriptPath}");
                
                if (!File.Exists(_pythonScriptPath))
                {
                    _logger.LogError($"Python script not found at any expected location!");
                    _logger.LogError($"BaseDirectory: {baseDir}");
                    _logger.LogError($"CurrentDirectory: {Directory.GetCurrentDirectory()}");
                }
                else
                {
                    _logger.LogInformation($"Found Python script at: {_pythonScriptPath}");
                }
            }
            else
            {
                _logger.LogInformation($"Found Python script at: {_pythonScriptPath}");
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
                        
                        // Use Aspose for font embedding and optimization (local version, no RestSharp conflict)
                        if (_asposePdfService != null && _asposePdfService.IsConfigured())
                        {
                            try
                            {
                                _logger.LogInformation("Applying Aspose font embedding and optimization...");
                                rebuiltPdfBytes = await _asposePdfService.OptimizePdfAsync(rebuiltPdfBytes);
                                _logger.LogInformation("Aspose optimization applied successfully");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to apply Aspose processing, trying Adobe as fallback");
                                
                                // Fallback to Adobe if Aspose fails
                                if (_adobeAutotagService != null && _adobeAutotagService.IsConfigured())
                                {
                                    try
                                    {
                                        _logger.LogInformation("Applying Adobe autotag as fallback...");
                                        rebuiltPdfBytes = await _adobeAutotagService.AutotagPdfAsync(rebuiltPdfBytes, generateReport: false);
                                        _logger.LogInformation("Adobe autotag applied successfully");
                                    }
                                    catch (Exception fallbackEx)
                                    {
                                        _logger.LogWarning(fallbackEx, "Adobe fallback also failed, continuing with basic PDF");
                                    }
                                }
                            }
                        }
                        else if (_adobeAutotagService != null && _adobeAutotagService.IsConfigured())
                        {
                            try
                            {
                                _logger.LogInformation("Applying Adobe autotag for accessibility...");
                                rebuiltPdfBytes = await _adobeAutotagService.AutotagPdfAsync(rebuiltPdfBytes, generateReport: false);
                                _logger.LogInformation("Adobe autotag applied successfully");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to apply Adobe processing, continuing with basic PDF");
                            }
                        }
                        else
                        {
                            _logger.LogInformation("No PDF optimization service configured");
                        }
                        
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
                _logger.LogInformation($"Executing Python script:");
                _logger.LogInformation($"  Script: {_pythonScriptPath}");
                _logger.LogInformation($"  PDF: {pdfPath}");
                _logger.LogInformation($"  JSON: {jsonPath}");
                
                // Verify files exist
                if (!File.Exists(_pythonScriptPath))
                {
                    _logger.LogError($"Python script not found: {_pythonScriptPath}");
                    return new PythonResult
                    {
                        Success = false,
                        Error = $"Python script not found: {_pythonScriptPath}"
                    };
                }
                
                if (!File.Exists(pdfPath))
                {
                    _logger.LogError($"PDF file not found: {pdfPath}");
                    return new PythonResult
                    {
                        Success = false,
                        Error = $"PDF file not found: {pdfPath}"
                    };
                }
                
                if (!File.Exists(jsonPath))
                {
                    _logger.LogError($"JSON file not found: {jsonPath}");
                    return new PythonResult
                    {
                        Success = false,
                        Error = $"JSON file not found: {jsonPath}"
                    };
                }
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"\"{_pythonScriptPath}\" \"{pdfPath}\" \"{jsonPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                _logger.LogInformation($"Executing command: python3 {startInfo.Arguments}");

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                var output = await outputTask;
                var error = await errorTask;

                // Always log what we got back for debugging
                _logger.LogInformation($"Python exit code: {process.ExitCode}");
                _logger.LogInformation($"Python stdout length: {output?.Length ?? 0}");
                _logger.LogInformation($"Python stdout: {output}");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    _logger.LogWarning($"Python stderr: {error}");
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
                        else
                        {
                            _logger.LogError("Deserialized result was null");
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, $"Failed to parse Python output as JSON. Output: {output}");
                    }
                }
                else if (process.ExitCode != 0)
                {
                    _logger.LogError($"Python script failed with exit code {process.ExitCode}. Error: {error}");
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