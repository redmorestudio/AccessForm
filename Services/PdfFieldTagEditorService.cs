using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using Syncfusion.Pdf.Interactive;
using Syncfusion.Drawing;
using AccessFormServer.Services;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Service for editing PDF fields and tag tree structure while maintaining PDF/UA compliance.
    /// This service coordinates between Syncfusion for initial field manipulation and Python
    /// scripts for proper tag tree updates.
    /// </summary>
    public class PdfFieldTagEditorService
    {
        private readonly ILogger<PdfFieldTagEditorService> _logger;
        private readonly PassportPdfService _passportPdfService;
        private readonly PdfUAComplianceService _complianceService;
        private readonly string _pythonScript = "pdf_tag_field_editor.py";

        public PdfFieldTagEditorService(
            ILogger<PdfFieldTagEditorService> logger,
            PassportPdfService passportPdfService,
            PdfUAComplianceService complianceService)
        {
            _logger = logger;
            _passportPdfService = passportPdfService;
            _complianceService = complianceService;
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

        public class EditResult
        {
            public bool Success { get; set; }
            public byte[]? PdfBytes { get; set; }
            public string? ErrorMessage { get; set; }
            public List<string> ModifiedFields { get; set; } = new();
            public List<string> ModifiedTags { get; set; } = new();
            public Dictionary<string, object> Metadata { get; set; } = new();
        }

        /// <summary>
        /// Updates PDF fields and tag tree structure comprehensively.
        /// </summary>
        public async Task<EditResult> UpdateFieldsAndTagsAsync(
            byte[] pdfBytes, 
            List<FieldUpdate> fieldUpdates,
            bool ensurePdfUaCompliance = true)
        {
            var result = new EditResult();
            
            try
            {
                _logger.LogInformation($"Starting comprehensive field and tag update for {fieldUpdates.Count} fields");

                // Step 1: Initial field modification using Syncfusion
                var syncfusionResult = await ModifyFieldsWithSyncfusion(pdfBytes, fieldUpdates);
                if (!syncfusionResult.Success)
                {
                    return syncfusionResult;
                }

                // Step 2: Update tag tree structure using Python/PyMuPDF
                var tagUpdateResult = await UpdateTagTreeStructure(
                    syncfusionResult.PdfBytes!, 
                    fieldUpdates);
                
                if (!tagUpdateResult.Success)
                {
                    // Fall back to Syncfusion-only result if tag update fails
                    _logger.LogWarning("Tag tree update failed, using Syncfusion-only result");
                    result = syncfusionResult;
                }
                else
                {
                    result = tagUpdateResult;
                }

                // Step 3: Ensure PDF/UA compliance if requested
                if (ensurePdfUaCompliance && result.Success && result.PdfBytes != null)
                {
                    _logger.LogInformation("Ensuring PDF/UA compliance");
                    
                    try
                    {
                        var complianceOptions = new ComplianceOptions
                        {
                            AutoRemediate = true,
                            ConvertToPdfA = true,
                            PreserveFieldNames = true,
                            Language = "en-US"
                        };

                        var complianceResult = await _complianceService.EnsureComplianceAsync(
                            result.PdfBytes, 
                            complianceOptions);

                        if (complianceResult.Success && complianceResult.OutputPdf != null)
                        {
                            result.PdfBytes = complianceResult.OutputPdf;
                            result.Metadata["pdfUaCompliant"] = true;
                            result.Metadata["conformance"] = "PDF/A-3u";
                            _logger.LogInformation("PDF/UA compliance ensured successfully");
                        }
                        else
                        {
                            _logger.LogWarning($"PDF/UA compliance check failed: {complianceResult.ErrorMessage}");
                            result.Metadata["pdfUaCompliant"] = false;
                            result.Metadata["complianceWarning"] = complianceResult.ErrorMessage;
                        }
                    }
                    catch (Exception compEx)
                    {
                        _logger.LogWarning($"PDF/UA compliance processing failed: {compEx.Message}");
                        result.Metadata["pdfUaCompliant"] = false;
                        result.Metadata["complianceError"] = compEx.Message;
                    }
                }

                result.Metadata["totalFieldsModified"] = result.ModifiedFields.Count;
                result.Metadata["totalTagsModified"] = result.ModifiedTags.Count;
                result.Metadata["timestamp"] = DateTime.UtcNow.ToString("o");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Comprehensive field and tag update failed");
                return new EditResult
                {
                    Success = false,
                    ErrorMessage = $"Field and tag update failed: {ex.Message}"
                };
            }
        }

        private async Task<EditResult> ModifyFieldsWithSyncfusion(
            byte[] pdfBytes, 
            List<FieldUpdate> fieldUpdates)
        {
            try
            {
                using var pdfStream = new MemoryStream(pdfBytes);
                using var pdfDoc = new PdfLoadedDocument(pdfStream);
                
                var modifiedFields = new List<string>();

                // Check if form exists
                if (pdfDoc.Form == null)
                {
                    return new EditResult
                    {
                        Success = false,
                        ErrorMessage = "PDF has no form fields"
                    };
                }

                // Process each field update
                foreach (var update in fieldUpdates)
                {
                    var fieldModified = false;

                    // Find and modify the field
                    foreach (PdfField field in pdfDoc.Form.Fields)
                    {
                        if (FieldNameMatches(field.Name, update.OriginalName, update.FieldType))
                        {
                            // For now, we can only update certain properties with Syncfusion
                            // Field renaming requires recreation which we'll handle differently
                            
                            if (field is PdfLoadedTextBoxField textField)
                            {
                                if (update.Tooltip != null) textField.ToolTip = update.Tooltip;
                                if (update.IsRequired.HasValue) textField.Required = update.IsRequired.Value;
                                fieldModified = true;
                            }
                            else if (field is PdfLoadedCheckBoxField checkField)
                            {
                                if (update.Tooltip != null) checkField.ToolTip = update.Tooltip;
                                if (update.IsRequired.HasValue) checkField.Required = update.IsRequired.Value;
                                fieldModified = true;
                            }
                            else if (field is PdfLoadedComboBoxField comboField)
                            {
                                if (update.Tooltip != null) comboField.ToolTip = update.Tooltip;
                                if (update.IsRequired.HasValue) comboField.Required = update.IsRequired.Value;
                                fieldModified = true;
                            }
                            else if (field is PdfLoadedRadioButtonListField radioField)
                            {
                                if (update.Tooltip != null) radioField.ToolTip = update.Tooltip;
                                if (update.IsRequired.HasValue) radioField.Required = update.IsRequired.Value;
                                fieldModified = true;
                            }

                            if (fieldModified)
                            {
                                modifiedFields.Add(update.OriginalName);
                                _logger.LogDebug($"Modified field properties for: {update.OriginalName}");
                            }
                            
                            break;
                        }
                    }

                    if (!fieldModified)
                    {
                        _logger.LogWarning($"Field not found for modification: {update.OriginalName}");
                    }
                }

                // Save the modified PDF
                using var outputStream = new MemoryStream();
                pdfDoc.Save(outputStream);
                pdfDoc.Close(true);

                return new EditResult
                {
                    Success = true,
                    PdfBytes = outputStream.ToArray(),
                    ModifiedFields = modifiedFields
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Syncfusion field modification failed");
                return new EditResult
                {
                    Success = false,
                    ErrorMessage = $"Syncfusion modification failed: {ex.Message}"
                };
            }
        }

        private async Task<EditResult> UpdateTagTreeStructure(
            byte[] pdfBytes, 
            List<FieldUpdate> fieldUpdates)
        {
            try
            {
                // Save PDF to temp file for Python processing
                var tempInputPath = Path.Combine(Path.GetTempPath(), $"input_{Guid.NewGuid()}.pdf");
                await File.WriteAllBytesAsync(tempInputPath, pdfBytes);

                // Prepare field updates JSON
                var updates = fieldUpdates.Select(u => new
                {
                    originalName = u.OriginalName,
                    newName = u.NewName,
                    fieldType = u.FieldType,
                    tooltip = u.Tooltip
                }).ToList();

                var updatesJson = JsonSerializer.Serialize(updates);

                // Run Python script for tag tree update
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "python3",
                        Arguments = $"{_pythonScript} \"{tempInputPath}\" '{updatesJson}'",
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
                    return new EditResult
                    {
                        Success = false,
                        ErrorMessage = "Tag tree update timed out"
                    };
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();

                CleanupTempFile(tempInputPath);

                if (process.ExitCode != 0)
                {
                    _logger.LogError($"Python script failed: {error}");
                    return new EditResult
                    {
                        Success = false,
                        ErrorMessage = $"Tag tree update failed: {error}"
                    };
                }

                // Parse the result
                var pythonResult = JsonSerializer.Deserialize<JsonElement>(output);
                
                if (!pythonResult.TryGetProperty("success", out var success) || !success.GetBoolean())
                {
                    var errorMsg = pythonResult.TryGetProperty("error", out var err) 
                        ? err.GetString() 
                        : "Unknown error";
                    
                    return new EditResult
                    {
                        Success = false,
                        ErrorMessage = errorMsg
                    };
                }

                // Get the output file path
                if (!pythonResult.TryGetProperty("output_path", out var outputPath))
                {
                    return new EditResult
                    {
                        Success = false,
                        ErrorMessage = "No output path returned from Python script"
                    };
                }

                var outputFile = outputPath.GetString();
                if (string.IsNullOrEmpty(outputFile) || !File.Exists(outputFile))
                {
                    return new EditResult
                    {
                        Success = false,
                        ErrorMessage = "Output file not found"
                    };
                }

                // Read the modified PDF
                var modifiedPdfBytes = await File.ReadAllBytesAsync(outputFile);
                CleanupTempFile(outputFile);

                // Extract modification details
                var modifiedFields = new List<string>();
                var modifiedTags = new List<string>();

                if (pythonResult.TryGetProperty("modified_fields", out var fields))
                {
                    foreach (var field in fields.EnumerateArray())
                    {
                        if (field.TryGetProperty("original", out var original))
                        {
                            modifiedFields.Add(original.GetString() ?? "");
                        }
                    }
                }

                if (pythonResult.TryGetProperty("modified_tags", out var tags))
                {
                    foreach (var tag in tags.EnumerateArray())
                    {
                        if (tag.TryGetProperty("original", out var original))
                        {
                            modifiedTags.Add(original.GetString() ?? "");
                        }
                    }
                }

                _logger.LogInformation($"Successfully updated {modifiedFields.Count} fields and {modifiedTags.Count} tags");

                return new EditResult
                {
                    Success = true,
                    PdfBytes = modifiedPdfBytes,
                    ModifiedFields = modifiedFields,
                    ModifiedTags = modifiedTags
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tag tree update failed");
                return new EditResult
                {
                    Success = false,
                    ErrorMessage = $"Tag tree update failed: {ex.Message}"
                };
            }
        }

        private bool FieldNameMatches(string fieldName, string targetName, string? fieldType)
        {
            // Exact match
            if (fieldName == targetName) return true;
            
            // Match with type suffix
            if (!string.IsNullOrEmpty(fieldType))
            {
                if (fieldName == $"{targetName}[{fieldType}]") return true;
                if (fieldName.StartsWith($"{targetName}[")) return true;
            }
            
            return false;
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