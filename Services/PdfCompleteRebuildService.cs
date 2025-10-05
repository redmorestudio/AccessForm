using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using iText.Kernel.Pdf;
using iText.Kernel.Font;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Pdf.Annot;

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
        // private readonly AccessFormServer.Services.PdfFormStructureService? _formStructureService;
        // private readonly AccessFormServer.Services.PythonFormStructureFixService? _pythonFormFixService;

        public PdfCompleteRebuildService(
            ILogger<PdfCompleteRebuildService> logger, 
            AccessFormServer.Services.PassportPdfService? passportPdfService = null,
            AccessFormServer.Services.AdobeAutotagService? adobeAutotagService = null,
            AccessFormServer.Services.AsposePdfService? asposePdfService = null)
            // AccessFormServer.Services.PdfFormStructureService? formStructureService = null,
            // AccessFormServer.Services.PythonFormStructureFixService? pythonFormFixService = null
        {
            _logger = logger;
            _passportPdfService = passportPdfService;
            _adobeAutotagService = adobeAutotagService;
            _asposePdfService = asposePdfService;
            // _formStructureService = formStructureService;
            // _pythonFormFixService = pythonFormFixService;
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
        public class ServiceOptions
        {
            public bool UseAsposeAutotag { get; set; } = false;
            public bool UseAsposeFontEmbed { get; set; } = true;
            public bool UsePassportPdf { get; set; } = false;
        }
        
        public async Task<RebuildResult> CompletelyRebuildPdfAsync(byte[] pdfBytes, List<FieldUpdate> fieldUpdates, ServiceOptions? options = null)
        {
            // Use default options if not provided
            options ??= new ServiceOptions();
            
            return await CompletelyRebuildPdfAsyncInternal(pdfBytes, fieldUpdates, options);
        }
        
        private async Task<RebuildResult> CompletelyRebuildPdfAsyncInternal(byte[] pdfBytes, List<FieldUpdate> fieldUpdates, ServiceOptions options)
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
                        
                        // Step 1: Remove ZapfDingbats from checkboxes
                        try
                        {
                            _logger.LogInformation("===== STEP 1: REMOVING ZAPFDINGBATS WITH ITEXT =====");
                            rebuiltPdfBytes = RemoveZapfDingbatsFromCheckboxes(rebuiltPdfBytes);
                            _logger.LogInformation("===== ZAPFDINGBATS REMOVAL COMPLETE =====");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to remove ZapfDingbats with iText");
                        }
                        
                        // Step 2: Use Aspose for font embedding and optimization (if enabled)
                        if (options.UseAsposeFontEmbed && _asposePdfService != null && _asposePdfService.IsConfigured())
                        {
                            try
                            {
                                _logger.LogInformation("===== STEP 2: ASPOSE FONT EMBEDDING =====");
                                _logger.LogInformation($"Sending {rebuiltPdfBytes.Length} bytes to Aspose...");
                                var beforeAspose = rebuiltPdfBytes.Length;
                                
                                rebuiltPdfBytes = await _asposePdfService.OptimizePdfAsync(rebuiltPdfBytes);
                                
                                _logger.LogInformation($"===== ASPOSE PROCESSING COMPLETE =====");
                                _logger.LogInformation($"Received {rebuiltPdfBytes.Length} bytes from Aspose (was {beforeAspose})");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "===== ASPOSE PROCESSING FAILED =====");
                            }
                        }
                        else if (options.UseAsposeFontEmbed)
                        {
                            _logger.LogWarning("Aspose font embedding requested but service not available");
                        }
                        else
                        {
                            _logger.LogInformation("Skipping Aspose font embedding (disabled by user)");
                        }

                        // Step 2B: Use PassportPDF as fallback for font embedding if enabled
                        if (options.UsePassportPdf && _passportPdfService != null)
                        {
                            try
                            {
                                _logger.LogInformation("===== STEP 2B: PASSPORTPDF FONT EMBEDDING (FALLBACK) =====");
                                _logger.LogInformation($"Sending {rebuiltPdfBytes.Length} bytes to PassportPDF for PDF/A conversion...");
                                var beforePassport = rebuiltPdfBytes.Length;

                                // PassportPDF's PDF/A-2u conversion will embed all fonts
                                rebuiltPdfBytes = await _passportPdfService.ConvertToPdfAAsync(rebuiltPdfBytes, "rebuilt.pdf");

                                _logger.LogInformation($"===== PASSPORTPDF PROCESSING COMPLETE =====");
                                _logger.LogInformation($"Received {rebuiltPdfBytes.Length} bytes from PassportPDF (was {beforePassport})");
                                _logger.LogInformation("✅ PassportPDF PDF/A conversion ensures 100% font embedding");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "PassportPDF processing failed - fonts may not be fully embedded");
                            }
                        }

                        // Step 3: ALWAYS run autotag LAST for proper accessibility tagging
                        // Use Aspose autotag if enabled
                        bool autotagSuccessful = false;

                        _logger.LogInformation($"===== CHECKING AUTOTAG SERVICES =====");
                        _logger.LogInformation($"Aspose enabled: {options.UseAsposeAutotag}");

                        // Try Aspose auto-tagging if enabled
                        if (options.UseAsposeAutotag && _asposePdfService != null && _asposePdfService.IsConfigured())
                        {
                            try
                            {
                                _logger.LogInformation("===== STEP 3B: ASPOSE AUTOTAG FALLBACK =====");
                                _logger.LogInformation($"Using Aspose.PDF auto-tagging as fallback...");
                                var beforeAspose = rebuiltPdfBytes.Length;
                                
                                rebuiltPdfBytes = await _asposePdfService.AutoTagPdfAsync(rebuiltPdfBytes);
                                
                                _logger.LogInformation($"===== ASPOSE AUTOTAG COMPLETE =====");
                                _logger.LogInformation($"Received {rebuiltPdfBytes.Length} bytes from Aspose (was {beforeAspose})");
                                autotagSuccessful = true;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Aspose autotag also failed");
                            }
                        }
                        
                        if (!autotagSuccessful)
                        {
                            _logger.LogError("===== NO AUTOTAG SERVICE AVAILABLE - MISSING ACCESSIBILITY TAGS =====");
                            _logger.LogError("Both Adobe and Aspose autotag services failed or unavailable!");
                        }
                        
                        // STEP 4: Post-process to ensure form widgets are in Form structure elements
                        _logger.LogInformation("===== STEP 4: ENSURING FORM WIDGETS ARE IN FORM STRUCTURE ELEMENTS =====");
                        rebuiltPdfBytes = EnsureFormWidgetsInFormStructure(rebuiltPdfBytes);
                        
                        // STEP 5: Restore TWC logo alt-text
                        _logger.LogInformation("===== STEP 5: RESTORING TWC LOGO ALT-TEXT =====");
                        rebuiltPdfBytes = RestoreTwcLogoAltText(rebuiltPdfBytes);
                        
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
                
                // Exit code 0 means success, regardless of stdout content
                if (process.ExitCode == 0)
                {
                    // Try to parse JSON output if available
                    if (!string.IsNullOrWhiteSpace(output))
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
                                _logger.LogWarning("Deserialized result was null, but exit code was 0 - treating as success");
                            }
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogWarning(ex, $"Failed to parse Python output as JSON, but exit code was 0 - treating as success. Output: {output}");
                        }
                    }

                    // Exit code 0 = success even if JSON parsing failed
                    return new PythonResult
                    {
                        Success = true,
                        Error = null
                    };
                }
                else
                {
                    _logger.LogError($"Python script failed with exit code {process.ExitCode}. Error: {error}");
                    return new PythonResult
                    {
                        Success = false,
                        Error = $"Python script failed with exit code {process.ExitCode}: {error}"
                    };
                }
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

        private byte[] EnsureFormWidgetsInFormStructure(byte[] pdfBytes)
        {
            try
            {
                // First try the Python service if available (more reliable)
                if (false) // _pythonFormFixService != null && _pythonFormFixService.IsAvailable)
                {
                    _logger.LogInformation("Using PythonFormStructureFixService to ensure Form elements");
                    // var fixedBytes = _pythonFormFixService.FixFormStructureAsync(pdfBytes).GetAwaiter().GetResult();
                    var fixedBytes = pdfBytes; // Service not available
                    
                    // Check if the Python service actually made changes
                    if (fixedBytes.Length != pdfBytes.Length)
                    {
                        _logger.LogInformation("Python service successfully modified Form structure");
                        return fixedBytes;
                    }
                    else
                    {
                        _logger.LogInformation("Python service completed but no changes were needed");
                    }
                }
                
                // Fall back to iText-based service if Python not available or didn't make changes
                if (false) // _formStructureService != null)
                {
                    _logger.LogInformation("Using PdfFormStructureService (iText) to ensure Form elements");
                    // return _formStructureService.EnsureFormStructure(pdfBytes);
                    return pdfBytes; // Service not available
                }
                else
                {
                    _logger.LogWarning("No Form structure service available - Form structure elements may be missing");
                    return pdfBytes;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure form widgets are in Form structure elements");
                return pdfBytes;
            }
        }
        
        private byte[] RestoreTwcLogoAltText(byte[] pdfBytes)
        {
            try
            {
                if (false) // _formStructureService != null)
                {
                    _logger.LogInformation("Using PdfFormStructureService to add image alt-text");
                    // This is synchronous for now, but could be made async
                    // var task = _formStructureService.AddImageAltText(pdfBytes);
                    // task.Wait();
                    // return task.Result;
                    return pdfBytes; // Service not available
                }
                else
                {
                    _logger.LogWarning("PdfFormStructureService not available - image alt-text may be missing");
                    return pdfBytes;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore TWC logo alt-text");
                return pdfBytes;
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
        
        private byte[] RemoveZapfDingbatsFromCheckboxes(byte[] pdfBytes)
        {
            try
            {
                using (var inputStream = new MemoryStream(pdfBytes))
                using (var outputStream = new MemoryStream())
                {
                    using (var reader = new PdfReader(inputStream))
                    using (var writer = new PdfWriter(outputStream))
                    using (var document = new PdfDocument(reader, writer))
                    {
                        // Get the form
                        var form = PdfAcroForm.GetAcroForm(document, false);
                        if (form != null)
                        {
                            var fields = form.GetAllFormFields();
                            _logger.LogInformation($"iText: Found {fields.Count} form fields");
                            
                            foreach (var fieldEntry in fields)
                            {
                                var field = fieldEntry.Value;
                                if (field is PdfButtonFormField buttonField)
                                {
                                    // Check if it's a checkbox (not radio button)
                                    if (!buttonField.IsRadio())
                                    {
                                        _logger.LogInformation($"iText: Processing checkbox: {fieldEntry.Key}");
                                        
                                        // AGGRESSIVE APPROACH: Remove all appearance data
                                        try
                                        {
                                            // Get the widgets (visual representations)
                                            var widgets = field.GetWidgets();
                                            foreach (var widget in widgets)
                                            {
                                                try
                                                {
                                                    var widgetDict = widget.GetPdfObject();
                                                    
                                                    // Remove the appearance dictionary completely
                                                    if (widgetDict.ContainsKey(PdfName.AP))
                                                    {
                                                        _logger.LogInformation($"iText: Removing AP from checkbox widget");
                                                        widgetDict.Remove(PdfName.AP);
                                                    }
                                                    
                                                    // Remove any font references in DA
                                                    if (widgetDict.ContainsKey(PdfName.DA))
                                                    {
                                                        var daValue = widgetDict.GetAsString(PdfName.DA);
                                                        _logger.LogInformation($"iText: Found DA: {daValue}");
                                                        widgetDict.Remove(PdfName.DA);
                                                        // Set a simple DA without ZapfDingbats
                                                        widgetDict.Put(PdfName.DA, new PdfString("0 g"));
                                                    }
                                                    
                                                    // Remove MK (appearance characteristics) if it references fonts
                                                    if (widgetDict.ContainsKey(PdfName.MK))
                                                    {
                                                        _logger.LogInformation($"iText: Removing MK from checkbox widget");
                                                        widgetDict.Remove(PdfName.MK);
                                                    }
                                                    
                                                    // Set border style to simple
                                                    widget.SetBorderStyle(PdfAnnotation.STYLE_SOLID);
                                                }
                                                catch (Exception ex)
                                                {
                                                    _logger.LogWarning($"iText: Could not process widget: {ex.Message}");
                                                }
                                            }
                                            
                                            // Don't regenerate - let PDF viewer handle it
                                            _logger.LogInformation($"iText: Cleared all appearance data for checkbox: {fieldEntry.Key}");
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogWarning($"iText: Could not process checkbox {fieldEntry.Key}: {ex.Message}");
                                        }
                                    }
                                }
                            }
                        }
                        
                        document.Close();
                    }
                    
                    return outputStream.ToArray();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "iText: Failed to remove ZapfDingbats");
                return pdfBytes; // Return original if we fail
            }
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