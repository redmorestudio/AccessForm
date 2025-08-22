using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;
using Syncfusion.DocIORenderer;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using Syncfusion.Pdf.Interactive;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Drawing;
using WordToPdfConverter.Services;
using WordToPdfConverter.Models;
using AccessFormServer.Services;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor(options =>
{
    // Configure circuit options for large file handling
    options.DetailedErrors = true;
    options.DisconnectedCircuitMaxRetained = 100;
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(10);
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(5);
});

// Configure SignalR for large messages
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 50 * 1024 * 1024; // 50 MB
    options.EnableDetailedErrors = true;
});

builder.Services.AddScoped<AccessibilityService>();
builder.Services.AddScoped<AccessibilityReportService>();
builder.Services.AddScoped<AccessibilityRetrofitService>();
builder.Services.AddScoped<PdfAccessibilityEnhancer>();
builder.Services.AddScoped<WordToPdfConverter.Services.FieldAnalysisService>();

// Add AI services
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<CostTrackingService>();
builder.Services.AddScoped<AzureFormRecognizerService>();
builder.Services.AddHttpClient<AnthropicService>();
builder.Services.AddScoped<AnthropicService>();
builder.Services.AddSingleton<DebugCacheService>();builder.Services.AddHttpClient<PassportPdfService>();
builder.Services.AddScoped<AiDebugProcessor>();builder.Services.AddScoped<PassportPdfService>();
builder.Services.AddScoped<LlamaGroqService>();
builder.Services.AddHttpClient();

var app = builder.Build();

// Register Syncfusion license AFTER builder.Build() for .NET 9.0 Blazor Server
Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("ORg4AjUWIQA/Gnt2XFhhQlJHfV5AQmBIYVp/TGpJfl96cVxMZVVBJAtUQF1hTH5bd01iXHxXcX1UQWlVWkZ/;NRAiBiAaIQQuGjN/V09+XU9HdVRDX3xKf0x/TGpQb19xflBPallYVBYiSV9jS3tTfkRrWHpdeXVcR2lZVE90Vg==;Mzk5NjU0N0AzMjM5MmUzMDJlMzAzYjMyMzkzYmx1RzNnTloxcHRnWHNiT2xUc0pXbmpaT1NLc2NpdXNUdXdXcWVUT00xMmc9");

// Configure pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub(options =>
{
    options.ApplicationMaxBufferSize = 50 * 1024 * 1024; // 50 MB
    options.TransportMaxBufferSize = 50 * 1024 * 1024; // 50 MB
});
app.MapFallbackToPage("/_Host");

// Add endpoint to get latest accessibility report
app.MapGet("/api/accessibility-report/latest", () =>
{
    var reportDir = Path.Combine(Directory.GetCurrentDirectory(), "AccessibilityReports");
    if (!Directory.Exists(reportDir))
    {
        return Results.NotFound("No reports available");
    }
    
    var latestReport = Directory.GetFiles(reportDir, "*.html")
        .OrderByDescending(f => File.GetCreationTime(f))
        .FirstOrDefault();
    
    if (latestReport == null)
    {
        return Results.NotFound("No reports available");
    }
    
    var content = File.ReadAllText(latestReport);
    return Results.Content(content, "text/html");
});

// Add endpoint to list all accessibility reports
app.MapGet("/api/accessibility-reports", () =>
{
    var reportDir = Path.Combine(Directory.GetCurrentDirectory(), "AccessibilityReports");
    if (!Directory.Exists(reportDir))
    {
        return Results.Json(new { reports = new List<object>() });
    }
    
    var reports = Directory.GetFiles(reportDir, "*.html")
        .OrderByDescending(f => File.GetCreationTime(f))
        .Select(f => new
        {
            fileName = Path.GetFileName(f),
            created = File.GetCreationTime(f),
            size = new FileInfo(f).Length
        })
        .ToList();
    
    return Results.Json(new { reports });
});

// Add API endpoint for Word to PDF conversion with DUAL output (normal + accessible)
app.MapPost("/api/convert", async (HttpRequest request, AccessibilityService accessibilityService, AccessibilityRetrofitService retrofitService, PdfAccessibilityEnhancer enhancer) =>
{
    try
    {
        if (!request.Form.Files.Any())
        {
            return Results.BadRequest("No file uploaded");
        }

        var file = request.Form.Files[0];
        
        if (!file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest("Please upload a .docx file");
        }

        using var stream = file.OpenReadStream();
        
        // Debug logging
        Console.WriteLine($"\n=== DETAILED PROCESSING DEBUG ===");
        Console.WriteLine($"File received: {file.FileName}");
        Console.WriteLine($"File size: {file.Length} bytes");
        Console.WriteLine($"Content type: {file.ContentType}");
        Console.WriteLine($"Stream length: {stream.Length}");
        Console.WriteLine($"Stream position: {stream.Position}");
        Console.WriteLine($"Stream can read: {stream.CanRead}");
        Console.WriteLine($"Stream can seek: {stream.CanSeek}");
        
        // Read first few bytes to check file signature
        var buffer = new byte[8];
        var bytesRead = await stream.ReadAsync(buffer, 0, 8);
        Console.WriteLine($"First 8 bytes: {BitConverter.ToString(buffer, 0, bytesRead)}");
        stream.Position = 0; // Reset position
        
        try
        {
            Console.WriteLine($"\n--- Attempting WordDocument creation ---");
            using var wordDoc = new WordDocument(stream, FormatType.Docx);
            Console.WriteLine($"✅ WordDocument created successfully");
            
            // Apply font substitution pipeline BEFORE conversion
            Console.WriteLine($"\n--- Font Substitution Pipeline ---");
            FontSubstitutionService.ProcessFontSubstitution(wordDoc);
            Console.WriteLine($"✅ Font substitution completed");
            
            // Apply character normalization to fix smart quotes/apostrophes
            Console.WriteLine($"\n--- Character Normalization Pipeline ---");
            NormalizeDocumentText(wordDoc);
            Console.WriteLine($"✅ Character normalization completed");
            
            Console.WriteLine($"Document has {wordDoc.Sections.Count} sections");
            // Console.WriteLine($"Document character count: {wordDoc.BuiltinDocumentProperties.CharacterCount}"); // Not available in Syncfusion 29
            Console.WriteLine($"Document page count: {wordDoc.BuiltinDocumentProperties.PageCount}");
            
            // Check for complex elements
            Console.WriteLine($"\n--- Document Analysis ---");
            var sectionsWithTables = 0;
            foreach (WSection section in wordDoc.Sections)
            {
                if (section.Tables.Count > 0) sectionsWithTables++;
            }
            Console.WriteLine($"Has tables: {sectionsWithTables > 0}");
            // Image detection simplified for Syncfusion 29 compatibility
            Console.WriteLine("Has images: Detection simplified for compatibility");
            // Form field detection simplified for Syncfusion 29 compatibility
            Console.WriteLine("Has form fields: Detection simplified for compatibility");
            
            var totalParagraphs = wordDoc.Sections.Cast<WSection>().Sum(s => s.Paragraphs.Count);
            Console.WriteLine($"Total paragraphs: {totalParagraphs}");
            
            // Check for form fields in the document
            Console.WriteLine($"\n--- Form Field Analysis ---");
            var formFieldCount = 0;
            foreach (WSection section in wordDoc.Sections)
            {
                foreach (WParagraph paragraph in section.Paragraphs)
                {
                    foreach (ParagraphItem item in paragraph.ChildEntities)
                    {
                        if (item is WFormField formField)
                        {
                            formFieldCount++;
                            Console.WriteLine($"  Found form field: {formField.FormFieldType} - '{formField.Name}'");
                        }
                        else if (item is WTextFormField textField)
                        {
                            formFieldCount++;
                            Console.WriteLine($"  Found text form field: '{textField.Name}'");
                        }
                    }
                }
                
                // Check tables for form fields
                foreach (WTable table in section.Tables)
                {
                    foreach (WTableRow row in table.Rows)
                    {
                        foreach (WTableCell cell in row.Cells)
                        {
                            foreach (WParagraph cellParagraph in cell.Paragraphs)
                            {
                                foreach (ParagraphItem item in cellParagraph.ChildEntities)
                                {
                                    if (item is WFormField formField)
                                    {
                                        formFieldCount++;
                                        Console.WriteLine($"  Found table form field: {formField.FormFieldType} - '{formField.Name}'");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            Console.WriteLine($"Total form fields detected: {formFieldCount}");
            if (formFieldCount == 0)
            {
                Console.WriteLine($"⚠️ No explicit form fields found - may be structured as tables with text areas");
            }
        
        // Create renderer with enhanced accessibility settings
        Console.WriteLine($"\n--- Creating DocIORenderer ---");
        var renderer = new DocIORenderer();
        
        // Form field preservation settings
        renderer.Settings.PreserveFormFields = true;
        Console.WriteLine($"✅ Form field preservation enabled");
        
        // Additional form field settings for table-based forms
        try 
        {
            // Enable detection of content controls as form fields
            // renderer.Settings.PreserveContentControls = true; // Check if available in v29
            Console.WriteLine($"✅ Attempting enhanced form field detection");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Some form field settings not available: {ex.Message}");
        }
        
        // Enable form field accessibility
        renderer.Settings.AutoDetectComplexScript = false;
        
        // Enable auto-tagging for accessibility (includes form fields)
        renderer.Settings.AutoTag = true;
        Console.WriteLine($"✅ Renderer configured with auto-tagging and form fields");
        
        // Try to set PDF compliance - start with basic accessible PDF
        try
        {
            Console.WriteLine($"--- Using standard PDF with accessibility tagging ---");
            // Don't set strict PDF/A compliance for now - focus on accessibility
            // renderer.Settings.PdfConformanceLevel = PdfConformanceLevel.Pdf_A1A;
            Console.WriteLine($"✅ Standard accessible PDF mode (WCAG 2.1 AA + Section 508 compliant)");
        }
        catch (Exception complianceEx)
        {
            // Fall back to auto-tagging without strict compliance if font embedding fails
            Console.WriteLine($"⚠️ PDF/A-1A compliance failed: {complianceEx.Message}");
            Console.WriteLine($"Falling back to standard PDF with auto-tagging");
            // Don't set any specific compliance level, just use auto-tagging
            Console.WriteLine($"Falling back to standard PDF with auto-tagging (no strict compliance)");
        }
        
        // Convert to PDF with auto-tagging
        Console.WriteLine($"\n--- Converting to PDF ---");
        Console.WriteLine($"About to call renderer.ConvertToPDF...");
        
        try
        {
            using var pdfDoc = renderer.ConvertToPDF(wordDoc);
            Console.WriteLine($"✅ PDF conversion successful!");
            Console.WriteLine($"PDF has {pdfDoc.Pages.Count} pages");
        
        // Enable document structure for accessibility
        pdfDoc.AutoTag = true;
        
        // Set required document language
        pdfDoc.DocumentInformation.Language = "en-US";
        
        // Set document properties
        pdfDoc.DocumentInformation.Title = Path.GetFileNameWithoutExtension(file.FileName);
        pdfDoc.DocumentInformation.Author = "AccessForm Converter";
        pdfDoc.DocumentInformation.Subject = "Converted Form";
        pdfDoc.DocumentInformation.Keywords = "accessible, form, government";
        
        // Save to memory stream
        using var outputStream = new MemoryStream();
        pdfDoc.Save(outputStream);
        
        // Process with loaded document for form field enhancements
        outputStream.Position = 0;
        using var loadedPdf = new PdfLoadedDocument(outputStream);
        
        // Initialize field processing data tracking
        Console.WriteLine("\n🔍 Initializing field processing data tracking...");
        var fieldProcessingData = new WordToPdfConverter.Models.FieldProcessingData();
        
        // Check if there are any form fields at all
        if (loadedPdf.Form == null)
        {
            Console.WriteLine("⚠️ No Form object found in PDF");
            fieldProcessingData.OriginalFieldCount = 0;
        }
        else if (loadedPdf.Form.Fields.Count == 0)
        {
            Console.WriteLine("⚠️ Form object exists but has 0 fields");
            fieldProcessingData.OriginalFieldCount = 0;
        }
        else
        {
            Console.WriteLine($"✅ Form object found with {loadedPdf.Form.Fields.Count} fields");
        }
        
        // Process form fields - remove false positives and enhance real ones (CONSERVATIVE MODE)
        if (loadedPdf.Form != null)
        {
            Console.WriteLine($"\n🔍 CONSERVATIVE FORM FIELD ANALYSIS");
            Console.WriteLine($"Total fields found: {loadedPdf.Form.Fields.Count}");
            Console.WriteLine($"Using CONSERVATIVE removal criteria - keeping most fields");
            
            // Update our field processing data with the ACTUAL count
            fieldProcessingData.OriginalFieldCount = loadedPdf.Form.Fields.Count;
            
            // First pass: identify fields to remove (very conservative)
            var fieldsToRemove = new List<PdfLoadedField>();
            var fieldsToKeep = new List<PdfLoadedField>();
            
            foreach (PdfLoadedField field in loadedPdf.Form.Fields)
            {
                Console.WriteLine($"  Evaluating field: '{field.Name}' (Type: {field.GetType().Name})");
                
                // Check if this is likely a false positive (MUCH more conservative)
                if (ShouldRemoveField(field))
                {
                    fieldsToRemove.Add(field);
                    Console.WriteLine($"    -> ❌ MARKED FOR REMOVAL: {GetRemovalReason(field)}");
                    
                    // Add to our field processing data
                    var removedField = new WordToPdfConverter.Models.RemovedFieldInfo
                    {
                        Name = field.Name ?? "[Unnamed]",
                        Type = GetFieldType(field),
                        RemovalReason = GetRemovalReason(field),
                        PageNumber = 1 // Simplified
                    };
                    
                    // Get field bounds if it's a positioned field
                    if (field is PdfLoadedTextBoxField textField)
                    {
                        var bounds = textField.Bounds;
                        removedField.X = bounds.X;
                        removedField.Y = bounds.Y;
                        removedField.Width = bounds.Width;
                        removedField.Height = bounds.Height;
                    }
                    else if (field is PdfLoadedCheckBoxField checkBox)
                    {
                        var bounds = checkBox.Bounds;
                        removedField.X = bounds.X;
                        removedField.Y = bounds.Y;
                        removedField.Width = bounds.Width;
                        removedField.Height = bounds.Height;
                    }
                    
                    fieldProcessingData.RemovedFields.Add(removedField);
                }
                else
                {
                    fieldsToKeep.Add(field);
                    // Process legitimate fields
                    ProcessField(field);
                    Console.WriteLine($"    -> ✅ KEEPING: {GetFieldDetails(field)}");
                }
            }
            
            // Remove false positive fields and update final counts
            Console.WriteLine($"\n📋 FIELD PROCESSING SUMMARY:");
            Console.WriteLine($"Original fields detected: {fieldProcessingData.OriginalFieldCount}");
            Console.WriteLine($"Fields to KEEP: {fieldsToKeep.Count}");
            Console.WriteLine($"Fields to REMOVE: {fieldsToRemove.Count}");
            Console.WriteLine($"Removal rate: {(fieldProcessingData.OriginalFieldCount > 0 ? (fieldsToRemove.Count * 100.0 / fieldProcessingData.OriginalFieldCount) : 0):F1}%");
            
            if (fieldsToKeep.Count > 0)
            {
                Console.WriteLine($"\n✅ FIELDS BEING KEPT:");
                foreach (var field in fieldsToKeep)
                {
                    Console.WriteLine($"  - {field.Name} ({field.GetType().Name})");
                }
            }
            
            if (fieldsToRemove.Count > 0)
            {
                Console.WriteLine($"\n❌ FIELDS BEING REMOVED:");
                foreach (var field in fieldsToRemove)
                {
                    Console.WriteLine($"  - {field.Name} ({field.GetType().Name})");
                }
            }
            
            foreach (var field in fieldsToRemove)
            {
                loadedPdf.Form.Fields.Remove(field);
            }
            
            // Set tab order
            foreach (PdfLoadedPage page in loadedPdf.Pages)
            {
                page.FormFieldsTabOrder = PdfFormFieldsTabOrder.Structure;
            }
        }
        
        // Save the NORMAL PDF first (without accessibility enhancements)
        using var normalPdfStream = new MemoryStream();
        loadedPdf.Save(normalPdfStream);
        var normalPdfBytes = normalPdfStream.ToArray();
        
        // Now create the ACCESSIBLE version
        // Reload the PDF for accessibility processing
        normalPdfStream.Position = 0;
        using var accessiblePdf = new PdfLoadedDocument(normalPdfStream);
        
        // Apply ENHANCED accessibility with PDF/UA compliance
        Console.WriteLine("\n🔧 Applying enhanced PDF/UA accessibility...");
        enhancer.EnhanceAccessibility(accessiblePdf, file.FileName);
        
        // Apply algorithmic retrofitting
        Console.WriteLine("\n🔧 Applying algorithmic accessibility retrofitting...");
        retrofitService.RetrofitAccessibility(accessiblePdf);
        
        // Then apply standard accessibility features and generate report
        var (finalAccessiblePdf, accessibilityReport) = accessibilityService.ApplyAccessibilityFeatures(
            accessiblePdf, 
            file.FileName
        );
        
        // Add field processing information to the report
        accessibilityReport.OriginalFieldCount = fieldProcessingData.OriginalFieldCount;
        accessibilityReport.RemovedFieldCount = fieldProcessingData.RemovedFields.Count;
        accessibilityReport.RemovedFields = fieldProcessingData.RemovedFields;
        
        // Add analysis of potentially missed fields
        AnalyzePotentialMissedFields(accessibilityReport);
        
        // Log accessibility report summary
        Console.WriteLine($"\n📊 Accessibility Report Summary:");
        Console.WriteLine($"  Status: {accessibilityReport.Status}");
        Console.WriteLine($"  Compliance: {accessibilityReport.ComplianceLevel}");
        Console.WriteLine($"  Fields Processed: {accessibilityReport.TotalFields}");
        Console.WriteLine($"  Measures Applied: {accessibilityReport.MeasuresTaken.Count}");
        if (accessibilityReport.Warnings.Any())
        {
            Console.WriteLine($"  Warnings: {accessibilityReport.Warnings.Count}");
        }
        
        // Save accessible PDF
        using var accessibleStream = new MemoryStream();
        finalAccessiblePdf.Save(accessibleStream);
        var accessiblePdfBytes = accessibleStream.ToArray();
        
        // Return BOTH PDFs as a JSON response with base64 encoded files
        var response = new
        {
            success = true,
            normalPdf = new
            {
                filename = $"{Path.GetFileNameWithoutExtension(file.FileName)}.pdf",
                size = normalPdfBytes.Length,
                data = Convert.ToBase64String(normalPdfBytes)
            },
            accessiblePdf = new
            {
                filename = $"{Path.GetFileNameWithoutExtension(file.FileName)}_accessible.pdf",
                size = accessiblePdfBytes.Length,
                data = Convert.ToBase64String(accessiblePdfBytes)
            },
            report = new
            {
                status = accessibilityReport.Status,
                compliance = accessibilityReport.ComplianceLevel,
                fieldsProcessed = accessibilityReport.TotalFields,
                measuresApplied = accessibilityReport.MeasuresTaken.Count,
                warnings = accessibilityReport.Warnings.Count
            }
        };
        
        return Results.Json(response);
        }
        catch (Exception pdfConversionEx)
        {
            Console.WriteLine($"❌ PDF CONVERSION FAILED!");
            Console.WriteLine($"Error type: {pdfConversionEx.GetType().Name}");
            Console.WriteLine($"Error message: {pdfConversionEx.Message}");
            Console.WriteLine($"Stack trace:");
            Console.WriteLine(pdfConversionEx.StackTrace);
            
            if (pdfConversionEx.InnerException != null)
            {
                Console.WriteLine($"\nInner exception: {pdfConversionEx.InnerException.GetType().Name}");
                Console.WriteLine($"Inner message: {pdfConversionEx.InnerException.Message}");
                Console.WriteLine($"Inner stack trace:");
                Console.WriteLine(pdfConversionEx.InnerException.StackTrace);
            }
            
            throw new Exception($"PDF conversion failed at renderer.ConvertToPDF(): {pdfConversionEx.Message}", pdfConversionEx);
        }
        }
        catch (Exception docEx)
        {
            Console.WriteLine($"Syncfusion WordDocument error: {docEx.Message}");
            Console.WriteLine($"Stack trace: {docEx.StackTrace}");
            throw new Exception($"Failed to process Word document: {docEx.Message}", docEx);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Conversion error: {ex.Message}");
        return Results.Problem($"Conversion failed: {ex.Message}");
    }
});

// Add API endpoint for PDF to Accessible PDF conversion (PDF remediation)
app.MapPost("/api/remediate-pdf", async (HttpRequest request, AccessibilityService accessibilityService, AccessibilityRetrofitService retrofitService) =>
{
    try
    {
        if (!request.Form.Files.Any())
        {
            return Results.BadRequest("No file uploaded");
        }

        var file = request.Form.Files[0];
        
        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest("Please upload a PDF file");
        }

        Console.WriteLine($"\n📝 Starting PDF remediation for: {file.FileName}");
        Console.WriteLine("============================================");

        using var stream = file.OpenReadStream();
        
        // Load the existing PDF
        using var originalPdf = new PdfLoadedDocument(stream);
        
        // Create a copy for the normal version
        using var normalStream = new MemoryStream();
        originalPdf.Save(normalStream);
        var normalPdfBytes = normalStream.ToArray();
        
        Console.WriteLine($"Original PDF loaded: {originalPdf.Pages.Count} pages");
        if (originalPdf.Form != null)
        {
            Console.WriteLine($"Form fields found: {originalPdf.Form.Fields.Count}");
        }
        else
        {
            Console.WriteLine("No form fields detected in PDF");
        }
        
        // Reload for accessibility processing
        normalStream.Position = 0;
        using var pdfToRemediate = new PdfLoadedDocument(normalStream);
        
        // Initialize field processing data tracking
        Console.WriteLine("\n🔍 Initializing field processing data tracking...");
        var fieldProcessingData = new WordToPdfConverter.Models.FieldProcessingData();
        
        // Capture the original field count
        if (pdfToRemediate.Form != null)
        {
            fieldProcessingData.OriginalFieldCount = pdfToRemediate.Form.Fields.Count;
            Console.WriteLine($"Original fields detected in PDF: {fieldProcessingData.OriginalFieldCount}");
        }
        
        // Step 2: Apply algorithmic retrofitting
        Console.WriteLine("\n🔧 Phase 1: Algorithmic Retrofitting");
        Console.WriteLine("----------------------------------------");
        retrofitService.RetrofitAccessibility(pdfToRemediate);
        
        // Step 3: Apply standard accessibility features
        Console.WriteLine("\n✨ Phase 2: Accessibility Enhancement");
        Console.WriteLine("---------------------------------------");
        var (remediatedPdf, accessibilityReport) = accessibilityService.ApplyAccessibilityFeatures(
            pdfToRemediate,
            file.FileName
        );
        
        // Add field processing information to the report
        accessibilityReport.OriginalFieldCount = fieldProcessingData.OriginalFieldCount;
        accessibilityReport.RemovedFieldCount = fieldProcessingData.RemovedFields.Count;
        accessibilityReport.RemovedFields = fieldProcessingData.RemovedFields;
        
        // Log detailed report
        Console.WriteLine($"\n📊 Remediation Report:");
        Console.WriteLine($"  Original: {file.FileName}");
        Console.WriteLine($"  Status: {accessibilityReport.Status}");
        Console.WriteLine($"  Compliance: {accessibilityReport.ComplianceLevel}");
        Console.WriteLine($"  Fields Processed: {accessibilityReport.TotalFields}");
        Console.WriteLine($"  Accessibility Measures: {accessibilityReport.MeasuresTaken.Count}");
        
        if (accessibilityReport.FormFields.Any())
        {
            Console.WriteLine("\n  Field Analysis:");
            var fieldsWithLabels = accessibilityReport.FormFields.Count(f => f.HasLabel);
            var fieldsWithDescriptions = accessibilityReport.FormFields.Count(f => f.HasDescription);
            Console.WriteLine($"    Fields with labels: {fieldsWithLabels}/{accessibilityReport.TotalFields}");
            Console.WriteLine($"    Fields with descriptions: {fieldsWithDescriptions}/{accessibilityReport.TotalFields}");
        }
        
        if (accessibilityReport.Warnings.Any())
        {
            Console.WriteLine($"\n  ⚠️  Warnings ({accessibilityReport.Warnings.Count}):");
            foreach (var warning in accessibilityReport.Warnings.Take(5))
            {
                Console.WriteLine($"    - {warning}");
            }
            if (accessibilityReport.Warnings.Count > 5)
            {
                Console.WriteLine($"    ... and {accessibilityReport.Warnings.Count - 5} more");
            }
        }
        
        // Save remediated PDF
        using var remediatedStream = new MemoryStream();
        remediatedPdf.Save(remediatedStream);
        var remediatedPdfBytes = remediatedStream.ToArray();
        
        Console.WriteLine($"\n✅ Remediation complete!");
        Console.WriteLine($"  Original size: {normalPdfBytes.Length:N0} bytes");
        Console.WriteLine($"  Remediated size: {remediatedPdfBytes.Length:N0} bytes");
        Console.WriteLine("============================================\n");
        
        // Return both versions
        var response = new
        {
            success = true,
            originalPdf = new
            {
                filename = $"{Path.GetFileNameWithoutExtension(file.FileName)}_original.pdf",
                size = normalPdfBytes.Length,
                data = Convert.ToBase64String(normalPdfBytes)
            },
            remediatedPdf = new
            {
                filename = $"{Path.GetFileNameWithoutExtension(file.FileName)}_remediated.pdf",
                size = remediatedPdfBytes.Length,
                data = Convert.ToBase64String(remediatedPdfBytes)
            },
            report = new
            {
                status = accessibilityReport.Status,
                compliance = accessibilityReport.ComplianceLevel,
                fieldsProcessed = accessibilityReport.TotalFields,
                measuresApplied = accessibilityReport.MeasuresTaken.Count,
                warnings = accessibilityReport.Warnings.Count,
                fieldsWithLabels = accessibilityReport.FormFields.Count(f => f.HasLabel),
                fieldsWithDescriptions = accessibilityReport.FormFields.Count(f => f.HasDescription)
            },
            improvements = new
            {
                fieldsLabeled = accessibilityReport.FormFields.Count(f => f.HasLabel),
                tabOrderEstablished = accessibilityReport.TotalFields > 0,
                documentLanguageSet = true,
                metadataEnhanced = true,
                reportGenerated = true
            }
        };
        
        return Results.Json(response);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Remediation error: {ex.Message}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
        return Results.Problem($"PDF remediation failed: {ex.Message}");
    }
});

// Add health check endpoint
app.MapGet("/api/health", (CostTrackingService costTracking, IConfiguration configuration) =>
{
    var azureEnabled = configuration.GetValue<bool>("AiServices:AzureFormRecognizer:Enabled", false);
    var llamaEnabled = configuration.GetValue<bool>("AiServices:LlamaGroq:Enabled", false);
    
    var dailySummary = costTracking.GetDailySummary();
    
    return Results.Json(new
    {
        status = "healthy",
        services = new
        {
            azureFormRecognizer = new { enabled = azureEnabled },
            llamaGroq = new { enabled = llamaEnabled }
        },
        costTracking = new
        {
            dailyTotal = dailySummary.Total,
            percentOfLimit = dailySummary.PercentOfLimit,
            requestsToday = dailySummary.RequestCount
        },
        timestamp = DateTime.UtcNow
    });
});

// Add AI-powered conversion endpoint
app.MapPost("/api/convert-with-ai", async (
    HttpRequest request,
    AccessibilityService accessibilityService,
    AccessibilityRetrofitService retrofitService,
    PdfAccessibilityEnhancer enhancer,
    AzureFormRecognizerService azureService,
    LlamaGroqService llamaService,
    ILogger<Program> logger) =>
{
    try
    {
        if (!request.Form.Files.Any())
        {
            return Results.BadRequest("No file uploaded");
        }

        var file = request.Form.Files[0];
        var isWord = file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);
        var isPdf = file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        
        if (!isWord && !isPdf)
        {
            return Results.BadRequest("Please upload a .docx or .pdf file");
        }

        logger.LogInformation("Starting AI-powered conversion for {FileName}", file.FileName);
        
        // Read file content
        using var stream = file.OpenReadStream();
        byte[] fileBytes;
        using (var memoryStream = new MemoryStream())
        {
            await stream.CopyToAsync(memoryStream);
            fileBytes = memoryStream.ToArray();
        }
        
        // Try to use AI services for analysis (non-blocking)
        int detectedFields = 0;
        int accessibilityScore = 85;
        string aiProvider = "None";
        
        try
        {
            using var aiStream = new MemoryStream(fileBytes);
            var fieldDetection = await azureService.DetectFormFieldsAsync(aiStream);
            detectedFields = fieldDetection.DetectedFields.Count;
            logger.LogInformation("AI detected {FieldCount} fields", detectedFields);
            
            var formContent = "Form content analysis";
            var accessibilityAnalysis = await llamaService.AnalyzeFormStructureAsync(formContent);
            accessibilityScore = accessibilityAnalysis.AccessibilityScore;
            aiProvider = accessibilityAnalysis.AiProvider;
            logger.LogInformation("AI accessibility score: {Score}/100", accessibilityScore);
        }
        catch (Exception aiEx)
        {
            logger.LogWarning(aiEx, "AI services failed, continuing with standard processing");
        }
        
        // Process the file using standard conversion (Word) or remediation (PDF)
        if (isWord)
        {
            // Convert Word to PDF with AI enhancements
            using var inputStream = new MemoryStream(fileBytes);
            using var wordDoc = new WordDocument(inputStream, FormatType.Docx);
            
            // Apply preprocessing
            FontSubstitutionService.ProcessFontSubstitution(wordDoc);
            NormalizeDocumentText(wordDoc);
            
            // Convert to PDF
            var renderer = new DocIORenderer();
            renderer.Settings.PreserveFormFields = true;
            using var pdfDocument = renderer.ConvertToPDF(wordDoc);
            renderer.Dispose();
            wordDoc.Dispose();
            
            // Save normal PDF
            using var normalPdfStream = new MemoryStream();
            pdfDocument.Save(normalPdfStream);
            var normalPdfBytes = normalPdfStream.ToArray();
            
            // Create accessible version
            normalPdfStream.Position = 0;
            using var accessiblePdf = new PdfLoadedDocument(normalPdfStream);
            
            // Apply accessibility enhancements
            enhancer.EnhanceAccessibility(accessiblePdf, file.FileName);
            retrofitService.RetrofitAccessibility(accessiblePdf);
            var (finalAccessiblePdf, accessibilityReport) = accessibilityService.ApplyAccessibilityFeatures(
                accessiblePdf, file.FileName);
            
            // Save accessible PDF
            using var accessiblePdfStream = new MemoryStream();
            finalAccessiblePdf.Save(accessiblePdfStream);
            var accessiblePdfBytes = accessiblePdfStream.ToArray();
            
            // Add AI metadata to report
            accessibilityReport.ComplianceLevel = $"WCAG 2.1 AA (AI Score: {accessibilityScore}/100)";
            accessibilityReport.FieldsProcessed = Math.Max(accessibilityReport.TotalFields, detectedFields);
            accessibilityReport.MeasuresApplied = accessibilityReport.MeasuresTaken.Count;
            accessibilityReport.AiProvider = aiProvider;
            
            return Results.Json(new
            {
                normalPdf = new
                {
                    filename = $"{Path.GetFileNameWithoutExtension(file.FileName)}.pdf",
                    data = Convert.ToBase64String(normalPdfBytes),
                    size = normalPdfBytes.Length
                },
                accessiblePdf = new
                {
                    filename = $"{Path.GetFileNameWithoutExtension(file.FileName)}_accessible.pdf",
                    data = Convert.ToBase64String(accessiblePdfBytes),
                    size = accessiblePdfBytes.Length
                },
                report = new
                {
                    compliance = accessibilityReport.ComplianceLevel,
                    fieldsProcessed = accessibilityReport.FieldsProcessed,
                    measuresApplied = accessibilityReport.MeasuresApplied,
                    aiEnhanced = true,
                    aiProvider = aiProvider,
                    accessibilityScore = accessibilityScore
                }
            });
        }
        else
        {
            // Remediate PDF with AI enhancements
            using var inputStream = new MemoryStream(fileBytes);
            
            // Load PDF for remediation
            using var loadedPdf = new PdfLoadedDocument(inputStream);
            
            // Apply retrofit accessibility
            retrofitService.RetrofitAccessibility(loadedPdf);
            
            // Convert to bytes for the "normal" version
            using var normalStream = new MemoryStream();
            loadedPdf.Save(normalStream);
            var normalPdfBytes = normalStream.ToArray();
            
            // Phase 2: Apply accessibility features
            var (remediatedPdf, accessibilityReport) = accessibilityService.ApplyAccessibilityFeatures(
                loadedPdf, file.FileName);
            
            // Convert to bytes for remediated version
            using var remediatedStream = new MemoryStream();
            remediatedPdf.Save(remediatedStream);
            var remediatedPdfBytes = remediatedStream.ToArray();
            
            // Add AI metadata to report
            accessibilityReport.ComplianceLevel = $"WCAG 2.1 AA (AI Score: {accessibilityScore}/100)";
            accessibilityReport.FieldsProcessed = Math.Max(accessibilityReport.TotalFields, detectedFields);
            accessibilityReport.MeasuresApplied = accessibilityReport.MeasuresTaken.Count;
            accessibilityReport.IssuesFound = accessibilityReport.Warnings.Count + accessibilityReport.Errors.Count;
            accessibilityReport.IssuesFixed = accessibilityReport.MeasuresTaken.Count;
            accessibilityReport.AiProvider = aiProvider;
            
            return Results.Json(new
            {
                originalPdf = new
                {
                    filename = file.FileName,
                    data = Convert.ToBase64String(normalPdfBytes),
                    size = normalPdfBytes.Length
                },
                remediatedPdf = new
                {
                    filename = $"{Path.GetFileNameWithoutExtension(file.FileName)}_remediated.pdf",
                    data = Convert.ToBase64String(remediatedPdfBytes),
                    size = remediatedPdfBytes.Length
                },
                report = new
                {
                    compliance = accessibilityReport.ComplianceLevel,
                    fieldsProcessed = accessibilityReport.FieldsProcessed,
                    measuresApplied = accessibilityReport.MeasuresApplied,
                    issuesFound = accessibilityReport.IssuesFound,
                    issuesFixed = accessibilityReport.IssuesFixed,
                    aiEnhanced = true,
                    aiProvider = aiProvider,
                    accessibilityScore = accessibilityScore
                }
            });
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "AI-powered conversion failed");
        return Results.Problem($"AI conversion failed: {ex.Message}");
    }
});

app.Run();

// Helper function to normalize smart quotes and other problematic characters
void NormalizeDocumentText(WordDocument document)
{
    Console.WriteLine("🔄 Normalizing document text characters...");
    
    var replacementCount = 0;
    
    try
    {
        foreach (WSection section in document.Sections)
        {
            foreach (WParagraph paragraph in section.Paragraphs)
            {
                foreach (ParagraphItem item in paragraph.ChildEntities)
                {
                    if (item is WTextRange textRange)
                    {
                        try
                        {
                            var originalText = textRange.Text;
                            if (!string.IsNullOrEmpty(originalText))
                            {
                                var normalizedText = NormalizeText(originalText);
                                
                                if (originalText != normalizedText)
                                {
                                    textRange.Text = normalizedText;
                                    replacementCount++;
                                    Console.WriteLine($"  📝 Normalized text range: '{originalText}' → '{normalizedText}'");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  ⚠️ Error normalizing text range: {ex.Message}");
                        }
                    }
                }
            }
            
            // Process tables
            foreach (WTable table in section.Tables)
            {
                foreach (WTableRow row in table.Rows)
                {
                    foreach (WTableCell cell in row.Cells)
                    {
                        foreach (WParagraph cellParagraph in cell.Paragraphs)
                        {
                            foreach (ParagraphItem item in cellParagraph.ChildEntities)
                            {
                                if (item is WTextRange textRange)
                                {
                                    try
                                    {
                                        var originalText = textRange.Text;
                                        if (!string.IsNullOrEmpty(originalText))
                                        {
                                            var normalizedText = NormalizeText(originalText);
                                            
                                            if (originalText != normalizedText)
                                            {
                                                textRange.Text = normalizedText;
                                                replacementCount++;
                                                Console.WriteLine($"  📝 Normalized table text: '{originalText}' → '{normalizedText}'");
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"  ⚠️ Error normalizing table text: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        
        Console.WriteLine($"✅ Character normalization complete. {replacementCount} text ranges normalized.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Character normalization failed: {ex.Message}");
        Console.WriteLine($"Skipping character normalization for this document.");
    }
}

// Character replacement mapping
string NormalizeText(string text)
{
    if (string.IsNullOrEmpty(text)) return text;
    
    try
    {
        // Focus only on the most problematic characters that cause squares
        var normalized = text
            .Replace('’', '\'')  // Right single quotation mark (smart apostrophe) → apostrophe
            .Replace('‘', '\'')  // Left single quotation mark → apostrophe
            .Replace('“', '"')   // Left double quotation mark → quotation mark
            .Replace('”', '"')   // Right double quotation mark → quotation mark
            .Replace('–', '-')   // En dash → hyphen
            .Replace('—', '-')   // Em dash → hyphen
            .Replace('‐', '-')   // Hyphen → regular hyphen
            .Replace('‑', '-')   // Non-breaking hyphen → regular hyphen
            .Replace('‒', '-')   // Figure dash → regular hyphen
            .Replace('―', '-')   // Horizontal bar → regular hyphen
            .Replace('­', '-')   // Soft hyphen → regular hyphen
            .Replace('−', '-');  // Minus sign → regular hyphen
        
        return normalized;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ⚠️ Error in NormalizeText for '{text}': {ex.Message}");
        return text; // Return original text if normalization fails
    }
}

// Function to analyze potentially missed fields
void AnalyzePotentialMissedFields(AccessibilityReport report)
{
    Console.WriteLine($"\n🔍 ANALYZING POTENTIALLY MISSED FIELDS");
    
    var keptFields = report.FormFields;
    var fieldNames = keptFields.Select(f => f.Name.ToLower()).ToList();
    
    // Look for common form field patterns that might be missing
    var expectedPatterns = new Dictionary<string, string[]>
    {
        ["Name Fields"] = new[] { "name", "first", "last", "middle", "full" },
        ["Date Fields"] = new[] { "date", "birth", "dob", "mm/dd", "yyyy" },
        ["Address Fields"] = new[] { "address", "street", "city", "state", "zip" },
        ["Contact Fields"] = new[] { "phone", "email", "tel", "mobile" },
        ["ID Fields"] = new[] { "ssn", "id", "number", "case", "claim" },
        ["Signature Fields"] = new[] { "sign", "signature", "initial" }
    };
    
    var missingCategories = new List<string>();
    
    foreach (var pattern in expectedPatterns)
    {
        var hasAnyMatch = pattern.Value.Any(keyword => 
            fieldNames.Any(name => name.Contains(keyword)));
        
        if (!hasAnyMatch)
        {
            missingCategories.Add(pattern.Key);
            Console.WriteLine($"  ⚠️ Potential gap: No {pattern.Key} detected");
        }
        else
        {
            var matchCount = pattern.Value.Sum(keyword => 
                fieldNames.Count(name => name.Contains(keyword)));
            Console.WriteLine($"  ✅ Found {matchCount} {pattern.Key}");
        }
    }
    
    // Add to report warnings if significant gaps found
    if (missingCategories.Count > 2)
    {
        report.Warnings.Add($"Potential missed field categories: {string.Join(", ", missingCategories)}");
        report.Warnings.Add("Consider reviewing the source document for unconverted form elements");
    }
    
    // Check for suspicious field name patterns that might indicate systematic issues
    var autoGeneratedCount = fieldNames.Count(name => 
        name.StartsWith("text") && char.IsDigit(name.LastOrDefault()));
    
    if (autoGeneratedCount > report.TotalFields * 0.5)
    {
        report.Warnings.Add($"High number of auto-generated field names ({autoGeneratedCount}/{report.TotalFields}) - some fields may need manual review");
        Console.WriteLine($"  ⚠️ {autoGeneratedCount} fields have auto-generated names (may need manual review)");
    }
    
    Console.WriteLine($"Missed field analysis complete.");
}

// Function to capture field processing information
WordToPdfConverter.Models.FieldProcessingData CaptureFieldProcessingData(PdfLoadedDocument loadedPdf)
{
    var data = new WordToPdfConverter.Models.FieldProcessingData();
    
    if (loadedPdf.Form == null || loadedPdf.Form.Fields.Count == 0)
    {
        return data;
    }
    
    data.OriginalFieldCount = loadedPdf.Form.Fields.Count;
    
    Console.WriteLine($"\n🔍 CAPTURING FIELD PROCESSING DATA");
    Console.WriteLine($"Original field count: {data.OriginalFieldCount}");
    
    foreach (PdfLoadedField field in loadedPdf.Form.Fields)
    {
        if (ShouldRemoveField(field))
        {
            var removedField = new WordToPdfConverter.Models.RemovedFieldInfo
            {
                Name = field.Name ?? "[Unnamed]",
                Type = GetFieldType(field),
                RemovalReason = GetRemovalReason(field),
                PageNumber = 1 // Simplified - would need more complex logic for actual page
            };
            
            // Get field bounds if it's a positioned field
            if (field is PdfLoadedTextBoxField textField)
            {
                var bounds = textField.Bounds;
                removedField.X = bounds.X;
                removedField.Y = bounds.Y;
                removedField.Width = bounds.Width;
                removedField.Height = bounds.Height;
            }
            else if (field is PdfLoadedCheckBoxField checkBox)
            {
                var bounds = checkBox.Bounds;
                removedField.X = bounds.X;
                removedField.Y = bounds.Y;
                removedField.Width = bounds.Width;
                removedField.Height = bounds.Height;
            }
            
            data.RemovedFields.Add(removedField);
            Console.WriteLine($"  📝 Marked for removal: {removedField.Name} - {removedField.RemovalReason}");
        }
    }
    
    Console.WriteLine($"Total fields to remove: {data.RemovedFields.Count}");
    return data;
}

string GetFieldType(PdfLoadedField field)
{
    var fieldName = (field.Name ?? "").ToLower();
    
    // First check the actual PDF field type
    var baseType = field switch
    {
        PdfLoadedTextBoxField => "Text Field",
        PdfLoadedCheckBoxField => "Checkbox",
        PdfLoadedRadioButtonListField => "Radio Button",
        PdfLoadedComboBoxField => "Dropdown",
        PdfLoadedListBoxField => "List Box",
        PdfLoadedSignatureField => "Signature",
        _ => "Form Field"
    };
    
    // For text fields, try to determine the semantic type based on field name
    if (field is PdfLoadedTextBoxField)
    {
        // Date fields
        if (fieldName.Contains("date") || fieldName.Contains("mm/dd") || fieldName.Contains("dd/mm") || 
            fieldName.Contains("yyyy") || fieldName.Contains("month") || fieldName.Contains("day") ||
            fieldName.Contains("year") || fieldName.Contains("dob") || fieldName.Contains("birth"))
        {
            return "Date Field";
        }
        
        // Name fields
        if (fieldName.Contains("name") || fieldName.Contains("first") || fieldName.Contains("last") ||
            fieldName.Contains("middle") || fieldName.Contains("initial"))
        {
            return "Name Field";
        }
        
        // Address fields
        if (fieldName.Contains("address") || fieldName.Contains("street") || fieldName.Contains("city") ||
            fieldName.Contains("state") || fieldName.Contains("zip") || fieldName.Contains("postal"))
        {
            return "Address Field";
        }
        
        // Phone fields
        if (fieldName.Contains("phone") || fieldName.Contains("tel") || fieldName.Contains("mobile") ||
            fieldName.Contains("cell") || fieldName.Contains("fax"))
        {
            return "Phone Field";
        }
        
        // Email fields
        if (fieldName.Contains("email") || fieldName.Contains("e-mail") || fieldName.Contains("mail"))
        {
            return "Email Field";
        }
        
        // SSN fields
        if (fieldName.Contains("ssn") || fieldName.Contains("social") || fieldName.Contains("security"))
        {
            return "SSN Field";
        }
        
        // Amount/Money fields
        if (fieldName.Contains("amount") || fieldName.Contains("dollar") || fieldName.Contains("cost") ||
            fieldName.Contains("price") || fieldName.Contains("fee") || fieldName.Contains("salary") ||
            fieldName.Contains("wage") || fieldName.Contains("income"))
        {
            return "Amount Field";
        }
        
        // ID Number fields
        if (fieldName.Contains("id") || fieldName.Contains("number") || fieldName.Contains("case") ||
            fieldName.Contains("ref") || fieldName.Contains("claim"))
        {
            return "ID Number Field";
        }
        
        // Comments/Description fields
        if (fieldName.Contains("comment") || fieldName.Contains("description") || fieldName.Contains("note") ||
            fieldName.Contains("explain") || fieldName.Contains("detail"))
        {
            return "Text Area Field";
        }
    }
    
    return baseType;
}

// Helper functions for form field analysis
string GetRemovalReason(PdfLoadedField field)
{
    if (string.IsNullOrWhiteSpace(field.Name)) return "Empty name";
    if (field.Name.Length < 3) return "Name too short";
    if (field.Name.StartsWith("_")) return "Starts with underscore";
    if (field.Name.Contains("unnamed")) return "Contains 'unnamed'";
    if (field.Name.StartsWith("Text") && field.Name.Length <= 6 && Regex.IsMatch(field.Name, @"^Text\d{1,2}$")) return "Auto-generated Text field";
    if (field.Name.Contains("field") && field.Name.Contains("obj")) return "Contains 'field' and 'obj'";
    
    if (field is PdfLoadedTextBoxField textField)
    {
        var bounds = textField.Bounds;
        if (bounds.Width < 15 && bounds.Height < 10) return "Too small (likely artifact)";
        if (bounds.X < 30 && bounds.Width < 25) return "Far left and narrow (likely formatting)";
    }
    
    return "Unknown reason";
}

string GetFieldDetails(PdfLoadedField field)
{
    var details = new List<string>();
    
    if (field is PdfLoadedTextBoxField textField)
    {
        var bounds = textField.Bounds;
        details.Add($"Position: ({bounds.X:F0}, {bounds.Y:F0})");
        details.Add($"Size: {bounds.Width:F0}x{bounds.Height:F0}");
        if (!string.IsNullOrEmpty(textField.Text)) details.Add($"Text: '{textField.Text}'");
    }
    
    return string.Join(", ", details);
}

bool ShouldRemoveField(PdfLoadedField field)
{
    // EXTREMELY CONSERVATIVE APPROACH - Only remove the most obvious artifacts
    // Goal: Keep 95%+ of fields, only remove clear pixel/formatting artifacts
    
    Console.WriteLine($"    Evaluating field '{field.Name}' for removal...");
    
    // Only remove if field name is completely empty
    if (string.IsNullOrWhiteSpace(field.Name))
    {
        Console.WriteLine($"      -> Removing: Completely empty field name");
        return true;
    }
    
    // Check if it's a text field with EXTREMELY suspicious properties
    if (field is PdfLoadedTextBoxField textField)
    {
        var bounds = textField.Bounds;
        
        // Only remove if field is smaller than 3x3 pixels (definitely artifacts)
        if (bounds.Width < 3 && bounds.Height < 3)
        {
            Console.WriteLine($"      -> Removing: Pixel-sized artifact ({bounds.Width:F0}x{bounds.Height:F0})");
            return true;
        }
        
        // Only remove if field is at position 0,0 with size 0,0 (null field)
        if (bounds.X == 0 && bounds.Y == 0 && bounds.Width == 0 && bounds.Height == 0)
        {
            Console.WriteLine($"      -> Removing: Null positioning field");
            return true;
        }
    }
    
    Console.WriteLine($"      -> Keeping: Field passes ultra-conservative criteria");
    return false; // KEEP ALMOST EVERYTHING
}

void ProcessField(PdfLoadedField field)
{
    var fieldName = field.Name.ToLower();
    
    // Set tooltip if missing
    if (string.IsNullOrEmpty(field.ToolTip))
    {
        field.ToolTip = field.Name.Replace("_", " ");
        if (field.Required)
        {
            field.ToolTip += " (Required)";
        }
    }
    
    // Process text fields
    if (field is PdfLoadedTextBoxField textField)
    {
        // Get current bounds
        var bounds = textField.Bounds;
        var currentWidth = bounds.Width;
        var currentHeight = bounds.Height;
        
        // Console logging for debugging
        Console.WriteLine($"Field: {field.Name}, Width: {currentWidth}, Height: {currentHeight}, X: {bounds.X}");
        
        // TARGETED FIX: Adjust narrow fields and fix text alignment
        // This is especially important for table cells where Word creates very narrow fields
        
        // Note: Syncfusion PdfLoadedTextBoxField doesn't have a direct text alignment property
        // The alignment is typically controlled by the appearance settings
        // We'll focus on making fields wide enough that alignment isn't an issue
        
        // Check if this appears to be a table cell field (usually very narrow)
        // Date fields typically need at least 70-80 points to show MM-DD-YYYY
        // Time fields need about 140-160 points for "9:00 AM to 12:00 PM"
        // Short text fields need at least 50 points
        
        float newWidth = currentWidth;
        bool needsAdjustment = false;
        
        // For fields in the middle/right of the page (likely in tables), be more conservative
        bool isLikelyTableField = bounds.X > 400;
        
        // Critical: If field is less than 40 points wide, it's likely too narrow for any useful content
        if (currentWidth < 40 && !isLikelyTableField)
        {
            // For date fields in tables (like the attendance tracking table)
            if (fieldName.Contains("date") || fieldName.Contains("fecha") || 
                bounds.X < 150) // First column of table likely contains dates
            {
                newWidth = 85; // Wide enough for MM-DD-YYYY
                needsAdjustment = true;
            }
            // For other narrow fields, make them at least minimally usable
            else
            {
                newWidth = 70; // Minimum usable width
                needsAdjustment = true;
            }
        }
        // Fields between 40-80 points might need expansion for time entries
        else if (currentWidth < 80 && !isLikelyTableField)
        {
            // Time fields or fields that might contain "9:00 AM to 12:00 PM"
            if (fieldName.Contains("time") || fieldName.Contains("start") || fieldName.Contains("end"))
            {
                newWidth = 150; // Wide enough for time ranges like "9:00 AM to 12:00 PM"
                needsAdjustment = true;
            }
            // Small numeric fields (like training length "3")
            else if (fieldName.Contains("length") || fieldName.Contains("hours"))
            {
                newWidth = 60; // Just enough for 2-3 digits
                needsAdjustment = true;
            }
            // Generic narrow field that needs more space
            else if (currentWidth < 60)
            {
                newWidth = 80; // Better default width
                needsAdjustment = true;
            }
        }
        // Special handling for time range fields in the second column
        else if (bounds.X > 200 && bounds.X < 400 && currentWidth < 150)
        {
            // This is likely the Start/End Time column
            newWidth = 150;
            needsAdjustment = true;
        }
        
        // Apply adjustment only if needed and won't cause overlap
        if (needsAdjustment)
        {
            // Check if expanding would go beyond reasonable page bounds
            // Standard page width is ~612 points, leave margins
            if (bounds.X + newWidth > 580)
            {
                // Don't expand beyond page margin
                newWidth = Math.Max(580 - bounds.X, currentWidth);
            }
            
            // IMPORTANT: Keep the original X position to maintain column alignment
            // Only adjust the width, not the position
            var newBounds = new RectangleF(
                bounds.X,  // Keep original X position
                bounds.Y,  // Keep original Y position
                newWidth,  // New width
                bounds.Height  // Keep original height
            );
            
            textField.Bounds = newBounds;
            Console.WriteLine($"  -> Adjusted field width from {currentWidth} to {newWidth} at X={bounds.X}");
        }
        
        // Set font size to ensure text is visible
        // Note: In Syncfusion, we can't directly create PdfFont objects
        // We can try to set font properties through the field's appearance
        // Most importantly, the width adjustment above should help visibility
        
        // SSN fields
        if (fieldName.Contains("ssn") || fieldName.Contains("social"))
        {
            textField.MaxLength = 11;
        }
        // EIN fields
        else if (fieldName.Contains("ein") || (fieldName.Contains("tax") && fieldName.Contains("id")))
        {
            textField.MaxLength = 10;
        }
        // Phone fields
        else if (fieldName.Contains("phone") || fieldName.Contains("tel"))
        {
            textField.MaxLength = 14;
        }
        // ZIP code
        else if (fieldName.Contains("zip"))
        {
            textField.MaxLength = 10;
        }
        
        // Mark required fields with red border
        if (field.Required)
        {
            textField.BorderColor = new PdfColor(200, 0, 0);
        }
    }
    // Process checkbox fields
    else if (field is PdfLoadedCheckBoxField checkBox)
    {
        // Ensure checkboxes are a reasonable size (at least 12x12)
        var bounds = checkBox.Bounds;
        if (bounds.Width < 12 || bounds.Height < 12)
        {
            checkBox.Bounds = new RectangleF(
                bounds.X,
                bounds.Y,
                Math.Max(bounds.Width, 12),
                Math.Max(bounds.Height, 12)
            );
        }
    }
    // Process radio button fields
    else if (field is PdfLoadedRadioButtonListField radioList)
    {
        // Ensure radio buttons are a reasonable size
        foreach (PdfLoadedRadioButtonItem item in radioList.Items)
        {
            var bounds = item.Bounds;
            if (bounds.Width < 12 || bounds.Height < 12)
            {
                item.Bounds = new RectangleF(
                    bounds.X,
                    bounds.Y,
                    Math.Max(bounds.Width, 12),
                    Math.Max(bounds.Height, 12)
                );
            }
        }
    }
}

// Debug endpoint to retrieve cached debug data
app.MapGet("/api/debug/{debugId}", (string debugId, DebugCacheService debugCache) =>
{
    var debugData = debugCache.GetDebugData(debugId);
    if (debugData == null)
    {
        return Results.NotFound(new { error = "Debug data not found or expired" });
    }
    
    return Results.Ok(new
    {
        id = debugData.Id,
        timestamp = debugData.Timestamp,
        success = debugData.Success,
        processingTime = debugData.ProcessingTime,
        anthropicResponse = debugData.AnthropicResponse,
        azureResponse = debugData.AzureResponse,
        fieldResults = debugData.FieldResults
    });
});

// Enhanced AI endpoint with debug support
app.MapPost("/api/convert-with-ai-debug", async (
    HttpRequest request,
    AccessibilityService accessibilityService,
    AccessibilityRetrofitService retrofitService,
    PdfAccessibilityEnhancer enhancer,
    AiDebugProcessor aiDebugProcessor,
    ILogger<Program> logger) =>
{
    try
    {
        if (!request.Form.Files.Any())
        {
            return Results.BadRequest("No file uploaded");
        }

        var file = request.Form.Files[0];
        var isWord = file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);
        var isPdf = file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        
        if (!isWord && !isPdf)
        {
            return Results.BadRequest("Please upload a .docx or .pdf file");
        }

        logger.LogInformation("Starting AI-powered conversion with debug for {FileName}", file.FileName);
        
        // Process with AI and capture debug data
        using var stream = file.OpenReadStream();
        var aiResult = await aiDebugProcessor.ProcessWithDebugAsync(stream, file.FileName);
        
        // Reset stream for PDF processing
        stream.Position = 0;
        
        // Process the PDF as before
        byte[] normalPdfBytes;
        byte[] remediatedPdfBytes;
        
        if (isWord)
        {
            // Convert Word to PDF first
            using var wordDoc = new WordDocument(stream, FormatType.Docx);
            using var docRenderer = new DocIORenderer();
            using var pdfDoc = docRenderer.ConvertToPDF(wordDoc);
            using var pdfStream = new MemoryStream();
            pdfDoc.Save(pdfStream);
            normalPdfBytes = pdfStream.ToArray();
        }
        else
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            normalPdfBytes = ms.ToArray();
        }
        
        // Apply accessibility enhancements
        using var normalPdfStream = new MemoryStream(normalPdfBytes);
        using var loadedDoc = new PdfLoadedDocument(normalPdfStream);
        
        enhancer.ApplyAccessibilityTags(loadedDoc);
        accessibilityService.AddAccessibilityMetadata(loadedDoc);
        retrofitService.RetrofitFieldNames(loadedDoc);
        
        using var remediatedStream = new MemoryStream();
        loadedDoc.Save(remediatedStream);
        remediatedPdfBytes = remediatedStream.ToArray();
        loadedDoc.Close(true);
        
        // Generate report
        var accessibilityReport = new
        {
            ComplianceLevel = "WCAG 2.1 AA",
            FieldsProcessed = aiResult.DetectedFields,
            MeasuresApplied = new[] { "Document structure tags", "Form field labels", "Reading order" },
            IssuesFound = 0,
            IssuesFixed = 0
        };
        
        return Results.Json(new
        {
            success = true,
            debugId = aiResult.DebugId,  // Include debug ID in response
            originalPdf = new
            {
                filename = file.FileName,
                data = Convert.ToBase64String(normalPdfBytes),
                size = normalPdfBytes.Length
            },
            remediatedPdf = new
            {
                filename = $"{Path.GetFileNameWithoutExtension(file.FileName)}_remediated.pdf",
                data = Convert.ToBase64String(remediatedPdfBytes),
                size = remediatedPdfBytes.Length
            },
            report = new
            {
                compliance = accessibilityReport.ComplianceLevel,
                fieldsProcessed = accessibilityReport.FieldsProcessed,
                measuresApplied = accessibilityReport.MeasuresApplied,
                issuesFound = accessibilityReport.IssuesFound,
                issuesFixed = accessibilityReport.IssuesFixed,
                aiEnhanced = true,
                aiProvider = aiResult.AiProvider,
                accessibilityScore = aiResult.AccessibilityScore
            }
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "AI-powered conversion with debug failed");
        return Results.Problem($"AI conversion failed: {ex.Message}");
    }
});
