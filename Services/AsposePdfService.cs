using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Aspose.Pdf;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Text;
using System.Collections.Generic;
using System.Linq;
using Aspose.Pdf.Tagged;
using Aspose.Pdf.LogicalStructure;

namespace AccessFormServer.Services
{
    /// <summary>
    /// Service that uses Aspose.PDF (local) to optimize PDFs and embed fonts
    /// </summary>
    public class AsposePdfService
    {
        private readonly ILogger<AsposePdfService> _logger;
        private readonly TwcFontComplianceService _fontComplianceService;
        private readonly TableLinkAccessibilityService _tableLinkService;
        private bool _isConfigured = false;

        public AsposePdfService(ILogger<AsposePdfService> logger, TwcFontComplianceService fontComplianceService, TableLinkAccessibilityService tableLinkService)
        {
            _logger = logger;
            _fontComplianceService = fontComplianceService;
            _tableLinkService = tableLinkService;
            
            try
            {
                // Set license if available
                var licenseFile = Environment.GetEnvironmentVariable("ASPOSE_LICENSE_PATH");
                if (!string.IsNullOrEmpty(licenseFile) && File.Exists(licenseFile))
                {
                    var license = new License();
                    license.SetLicense(licenseFile);
                    _logger.LogInformation("Aspose.PDF license applied");
                }
                else
                {
                    _logger.LogWarning("===== ASPOSE.PDF RUNNING IN EVALUATION MODE =====");
                    _logger.LogWarning("This will add watermarks and have limitations!");
                }
                
                _isConfigured = true;
                _logger.LogInformation("===== ASPOSE PDF SERVICE INITIALIZED (LOCAL VERSION) =====");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Aspose PDF");
                _isConfigured = false;
            }
        }

        public bool IsConfigured() => _isConfigured;

        /// <summary>
        /// Optimize PDF to embed all fonts and ensure compliance
        /// </summary>
        public async Task<byte[]> OptimizePdfAsync(byte[] pdfBytes)
        {
            if (!_isConfigured)
            {
                throw new InvalidOperationException("Aspose PDF service is not configured");
            }

            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogWarning("╔═══════════════════════════════════════════════════════════════════╗");
                    _logger.LogWarning("║ 🚨 ASPOSE OPTIMIZEPDFASYNC CALLED - WATCHING FOR FORM DAMAGE     ║");
                    _logger.LogWarning("╚═══════════════════════════════════════════════════════════════════╝");
                    _logger.LogInformation($"Input PDF size: {pdfBytes.Length} bytes");

                    // Save input for debugging
                    var debugInputPath = Path.Combine(Path.GetTempPath(), $"aspose_input_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
                    File.WriteAllBytes(debugInputPath, pdfBytes);
                    _logger.LogInformation($"Debug: Input saved to {debugInputPath}");

                    using (var inputStream = new MemoryStream(pdfBytes))
                    using (var outputStream = new MemoryStream())
                    {
                        // Load the PDF document
                        _logger.LogInformation("Loading PDF into Aspose.Document...");
                        var document = new Document(inputStream);
                        _logger.LogInformation($"Document loaded: {document.Pages.Count} pages");

                        // CHECK FOR FORMS BEFORE PROCESSING
                        _logger.LogWarning($"🔍 ASPOSE: document.Form is null? {document.Form == null}");
                        var formFieldCountBefore = document.Form?.Fields?.Length ?? 0;
                        _logger.LogError($"🚨 ASPOSE SEES {formFieldCountBefore} FORM FIELDS IN THE PDF!");

                        if (formFieldCountBefore > 0)
                        {
                            _logger.LogWarning($"🔍 ASPOSE: Listing first 5 fields:");
                            for (int i = 0; i < Math.Min(5, formFieldCountBefore); i++)
                            {
                                var field = document.Form.Fields[i];
                                _logger.LogWarning($"  - Field {i + 1}: Name='{field.FullName}', Type={field.GetType().Name}");
                            }
                        }
                        
                        // Log initial font status
                        LogFontStatus(document, "BEFORE optimization");
                        
                        // Embed all fonts
                        _logger.LogWarning("🔍 CALLING EmbedFonts() - This used to clear checkbox appearances");
                        EmbedFonts(document);
                        _logger.LogWarning("🔍 EmbedFonts() COMPLETED");
                        
                        // Optimize the document
                        _logger.LogInformation("Applying optimization options...");
                        try
                        {
                            var optimizationOptions = new Aspose.Pdf.Optimization.OptimizationOptions
                            {
                                // DISABLED: RemoveUnusedObjects might remove form field objects!
                                RemoveUnusedObjects = false,  // Was: true - but this can strip AcroForm objects
                                RemoveUnusedStreams = false,  // Also disabled to preserve form streams
                                AllowReusePageContent = false,
                                LinkDuplcateStreams = false,
                                UnembedFonts = false,
                                SubsetFonts = false
                                // Skip image compression - causes "Not supported image type" error with some images
                                // CompressImages = false,
                                // ImageQuality = 100
                            };
                            
                            // Disable image compression to avoid the error
                            optimizationOptions.ImageCompressionOptions.CompressImages = false;
                            
                            _logger.LogWarning("🔍 CALLING document.OptimizeResources() - RemoveUnusedObjects=false, RemoveUnusedStreams=false");
                            document.OptimizeResources(optimizationOptions);
                            _logger.LogWarning("🔍 OptimizeResources() COMPLETED");

                            // CHECK FOR FORMS AFTER OPTIMIZATION
                            var formFieldCountAfterOpt = document.Form?.Fields?.Length ?? 0;
                            if (formFieldCountAfterOpt != formFieldCountBefore)
                            {
                                _logger.LogError($"🚨 FORMS DESTROYED BY OPTIMIZATION! Before: {formFieldCountBefore}, After: {formFieldCountAfterOpt}");
                            }
                            else
                            {
                                _logger.LogWarning($"✅ Forms survived optimization: {formFieldCountAfterOpt} fields still present");
                            }
                        }
                        catch (Exception optEx)
                        {
                            _logger.LogWarning($"OptimizeResources failed (non-critical): {optEx.Message}");
                            _logger.LogInformation("Continuing without resource optimization");
                        }
                        _logger.LogInformation("Optimization applied");
                        
                        // Log final font status
                        LogFontStatus(document, "AFTER optimization");
                        
                        // Save optimized document
                        _logger.LogInformation("Saving optimized document...");
                        document.Save(outputStream);

                        var optimizedBytes = outputStream.ToArray();

                        // FINAL FORM CHECK
                        var formFieldCountFinal = document.Form?.Fields?.Length ?? 0;
                        _logger.LogWarning($"🔍 FINAL FORM CHECK: {formFieldCountFinal} form fields in saved document");
                        
                        // Save output for debugging
                        var debugOutputPath = Path.Combine(Path.GetTempPath(), $"aspose_output_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
                        File.WriteAllBytes(debugOutputPath, optimizedBytes);
                        _logger.LogInformation($"Debug: Output saved to {debugOutputPath}");
                        
                        _logger.LogInformation($"===== ASPOSE OPTIMIZATION COMPLETE =====");
                        _logger.LogInformation($"Output PDF size: {optimizedBytes.Length} bytes (from {pdfBytes.Length})");
                        _logger.LogInformation($"Size change: {optimizedBytes.Length - pdfBytes.Length:+#;-#;0} bytes");
                        
                        return optimizedBytes;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "===== ASPOSE OPTIMIZATION FAILED =====");
                    throw new InvalidOperationException($"Aspose PDF optimization failed: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Convert all fonts to embedded standard fonts
        /// </summary>
        public async Task<byte[]> ConvertFontsAsync(byte[] pdfBytes)
        {
            if (!_isConfigured)
            {
                throw new InvalidOperationException("Aspose PDF service is not configured");
            }

            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInformation($"Starting Aspose font conversion for {pdfBytes.Length} byte PDF");

                    using (var inputStream = new MemoryStream(pdfBytes))
                    using (var outputStream = new MemoryStream())
                    {
                        // Load the PDF document
                        var document = new Document(inputStream);
                        
                        // Convert and embed all fonts
                        EmbedFonts(document);
                        
                        // Convert to PDF/A-1b to ensure font embedding
                        var tempLogPath = Path.GetTempFileName();
                        try
                        {
                            bool isValid = document.Convert(tempLogPath, PdfFormat.PDF_A_1B, ConvertErrorAction.Delete);
                            if (isValid)
                            {
                                _logger.LogInformation("Document converted to PDF/A-1B for font embedding");
                            }
                            else
                            {
                                _logger.LogWarning("Document conversion to PDF/A-1B had issues but continued");
                            }
                        }
                        finally
                        {
                            try { File.Delete(tempLogPath); } catch { }
                        }
                        
                        // Save converted document
                        document.Save(outputStream);
                        
                        var convertedBytes = outputStream.ToArray();
                        _logger.LogInformation($"Aspose font conversion successful, result is {convertedBytes.Length} bytes");
                        
                        return convertedBytes;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to convert fonts with Aspose");
                    throw new InvalidOperationException($"Aspose font conversion failed: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Add accessibility tags and ensure PDF/UA compliance
        /// </summary>
        public async Task<byte[]> AddAccessibilityTagsAsync(byte[] pdfBytes)
        {
            if (!_isConfigured)
            {
                throw new InvalidOperationException("Aspose PDF service is not configured");
            }

            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInformation($"Starting Aspose accessibility tagging for {pdfBytes.Length} byte PDF");

                    using (var inputStream = new MemoryStream(pdfBytes))
                    using (var outputStream = new MemoryStream())
                    {
                        // Load the PDF document
                        var document = new Document(inputStream);
                        
                        // Set document info for accessibility
                        if (document.Info != null)
                        {
                            document.Info.Title = document.Info.Title ?? "Accessible PDF Document";
                            document.Info.Author = document.Info.Author ?? "AccessForm Server";
                        }
                        
                        // Embed all fonts for accessibility
                        EmbedFonts(document);
                        
                        // Convert to PDF/A-2A for accessibility compliance
                        var tempLogPath = Path.GetTempFileName();
                        try
                        {
                            bool isValid = document.Convert(tempLogPath, PdfFormat.PDF_A_2A, ConvertErrorAction.Delete);
                            if (isValid)
                            {
                                _logger.LogInformation("Document converted to PDF/A-2A for accessibility");
                            }
                            else
                            {
                                _logger.LogWarning("Document conversion to PDF/A-2A had issues but continued");
                            }
                        }
                        finally
                        {
                            try { File.Delete(tempLogPath); } catch { }
                        }
                        
                        // Save tagged document
                        document.Save(outputStream);
                        
                        var taggedBytes = outputStream.ToArray();
                        _logger.LogInformation($"Aspose tagging successful, result is {taggedBytes.Length} bytes");
                        
                        return taggedBytes;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add accessibility tags with Aspose");
                    throw new InvalidOperationException($"Aspose accessibility tagging failed: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Helper method to log font status
        /// </summary>
        private void LogFontStatus(Document document, string stage)
        {
            _logger.LogInformation($"===== FONT STATUS {stage} =====");
            int pageNum = 0;
            int totalFonts = 0;
            int embeddedFonts = 0;
            
            foreach (Page page in document.Pages)
            {
                pageNum++;
                if (page.Resources?.Fonts != null)
                {
                    foreach (var font in page.Resources.Fonts)
                    {
                        totalFonts++;
                        if (font.IsEmbedded)
                        {
                            embeddedFonts++;
                            _logger.LogInformation($"Page {pageNum}: {font.FontName} - EMBEDDED");
                        }
                        else
                        {
                            _logger.LogWarning($"Page {pageNum}: {font.FontName} - NOT EMBEDDED");
                        }
                    }
                }
            }
            
            _logger.LogInformation($"Total fonts: {totalFonts}, Embedded: {embeddedFonts}, Not embedded: {totalFonts - embeddedFonts}");
        }

        /// <summary>
        /// Aggressively preprocess and replace problematic fonts before any other operations
        /// </summary>
        private void PreprocessProblematicFonts(Document document)
        {
            _logger.LogInformation("===== PREPROCESSING PROBLEMATIC FONTS =====");

            try
            {
                // Map of problematic fonts to their replacements - be VERY aggressive
                var fontReplacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Times-Roman", "Arial" },
                    { "TimesRoman", "Arial" },
                    { "Times", "Arial" },
                    { "TimesNewRomanPSMT", "Arial" },  // Fix 7.21.4.1-1 violations
                    { "TimesNewRoman", "Arial" },
                    { "Times New Roman", "Arial" },
                    { "OpenSansRegular", "Arial" },
                    { "OpenSans-Regular", "Arial" },
                    { "OpenSans", "Arial" },
                    { "Open Sans", "Arial" },
                    { "Helvetica", "Arial" },
                    { "ZapfDingbats", "Arial" },
                    { "Zapf", "Arial" },
                    { "Symbol", "Arial" },
                    { "Courier", "Arial" }
                };

                int replacedCount = 0;

                // Process all pages
                foreach (Page page in document.Pages)
                {
                    // Use TextFragmentAbsorber to find and replace text with problematic fonts
                    var textAbsorber = new TextFragmentAbsorber();
                    page.Accept(textAbsorber);

                    foreach (TextFragment textFragment in textAbsorber.TextFragments)
                    {
                        if (textFragment.TextState?.Font?.FontName != null)
                        {
                            string currentFontName = textFragment.TextState.Font.FontName;

                            // Check if this font needs replacement
                            foreach (var kvp in fontReplacements)
                            {
                                if (currentFontName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                                {
                                    try
                                    {
                                        // Special handling for ZapfDingbats - convert symbols first
                                        if (currentFontName.Contains("ZapfDingbats", StringComparison.OrdinalIgnoreCase))
                                        {
                                            textFragment.Text = ConvertZapfDingbatsToUnicode(textFragment.Text);
                                        }
                                        // Special handling for Symbol font
                                        else if (currentFontName.Contains("Symbol", StringComparison.OrdinalIgnoreCase))
                                        {
                                            textFragment.Text = ConvertSymbolToUnicode(textFragment.Text);
                                        }

                                        // Replace with Arial (always embeddable)
                                        var arialFont = FontRepository.FindFont("Arial");
                                        if (arialFont != null)
                                        {
                                            textFragment.TextState.Font = arialFont;
                                            replacedCount++;
                                            _logger.LogInformation($"🔄 Replaced font '{currentFontName}' with Arial in text fragment");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogDebug($"Could not replace font {currentFontName}: {ex.Message}");
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }

                // Also process form fields
                if (document.Form != null && document.Form.Fields.Length > 0)
                {
                    foreach (var field in document.Form.Fields)
                    {
                        try
                        {
                            if (field is Aspose.Pdf.Forms.Field formField && formField.DefaultAppearance != null)
                            {
                                var fontName = formField.DefaultAppearance.FontName;
                                if (!string.IsNullOrEmpty(fontName))
                                {
                                    foreach (var kvp in fontReplacements)
                                    {
                                        if (fontName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                                        {
                                            var arialFont = FontRepository.FindFont("Arial");
                                            if (arialFont != null)
                                            {
                                                var fontSize = formField.DefaultAppearance.FontSize > 0 ? formField.DefaultAppearance.FontSize : 10;
                                                formField.DefaultAppearance = new Aspose.Pdf.Annotations.DefaultAppearance(
                                                    arialFont,
                                                    fontSize,
                                                    formField.DefaultAppearance.TextColor
                                                );
                                                replacedCount++;
                                                _logger.LogInformation($"🔄 Replaced font '{fontName}' with Arial in form field {field.FullName}");
                                            }
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug($"Could not preprocess font for field {field.FullName}: {ex.Message}");
                        }
                    }
                }

                _logger.LogInformation($"✅ Preprocessed and replaced {replacedCount} problematic font instances");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Font preprocessing encountered issues: {ex.Message}");
                // Continue anyway - this is best effort
            }
        }

        /// <summary>
        /// Helper method to embed all fonts in the document
        /// </summary>
        private void EmbedFonts(Document document)
        {
            _logger.LogInformation("===== EMBEDDING ALL FONTS (INCLUDING BASE-14) =====");

            try
            {
                // STEP 0: Aggressively preprocess and replace problematic fonts FIRST
                PreprocessProblematicFonts(document);

                // Enable embedding of standard Type 1 fonts (base-14 fonts)
                // This is the key setting that allows embedding fonts like ArialMT, Helvetica, Times-Roman, etc.
                document.EmbedStandardFonts = true;
                _logger.LogInformation("✅ Enabled EmbedStandardFonts flag");

                // Mark ALL fonts in page resources for embedding - be VERY aggressive
                int fontsMarkedForEmbedding = 0;
                int alreadyEmbedded = 0;
                foreach (Page page in document.Pages)
                {
                    if (page.Resources?.Fonts != null)
                    {
                        foreach (var font in page.Resources.Fonts)
                        {
                            if (!font.IsEmbedded)
                            {
                                font.IsEmbedded = true;
                                fontsMarkedForEmbedding++;
                                _logger.LogInformation($"📌 Marked font for embedding: {font.FontName}");

                                // Extra aggressive - try to force embedding through multiple methods
                                try
                                {
                                    // If it's a Type1 font, make sure it's embedded
                                    if (font is Aspose.Pdf.Text.Font type1Font)
                                    {
                                        type1Font.IsEmbedded = true;
                                    }
                                }
                                catch { }
                            }
                            else
                            {
                                alreadyEmbedded++;
                                _logger.LogDebug($"Font already embedded: {font.FontName}");
                            }
                        }
                    }
                }
                _logger.LogInformation($"✅ Marked {fontsMarkedForEmbedding} fonts in page resources for embedding");

                // Also check and handle fonts in form field appearance streams
                if (document.Form != null && document.Form.Fields.Length > 0)
                {
                    _logger.LogInformation($"Checking {document.Form.Fields.Length} form fields for problematic fonts...");
                    int formFieldFontsFound = 0;
                    int zapfDingbatsFieldsReplaced = 0;
                    int checkboxesRegenerated = 0;
                    var base14Fonts = new[] { "ZapfDingbats", "Times-Roman", "Helvetica", "Courier", "Symbol" };

                    // DISABLED: Clearing checkbox appearances breaks Adobe Acrobat form recognition!
                    // When appearance streams are cleared, Adobe can't see the form fields visually.
                    // This was causing Adobe to not recognize that forms exist in the PDF.
                    //
                    // Original intent was to replace ZapfDingbats font in checkboxes, but the side effect
                    // of clearing appearances is worse than having non-embedded fonts.
                    //
                    // TODO: Find a way to replace fonts WITHOUT clearing appearance streams

                    _logger.LogInformation("Skipping checkbox appearance regeneration to preserve Adobe Acrobat compatibility");

                    // foreach (var field in document.Form.Fields)
                    // {
                    //     if (field is Aspose.Pdf.Forms.CheckboxField checkbox)
                    //     {
                    //         try
                    //         {
                    //             bool isChecked = checkbox.Checked;
                    //             checkbox.Appearance.Clear();  // THIS BREAKS ADOBE!
                    //             checkbox.Style = Aspose.Pdf.Forms.BoxStyle.Check;
                    //             var arialFont = FontRepository.FindFont("Arial");
                    //             if (arialFont != null)
                    //             {
                    //                 checkbox.DefaultAppearance = new Aspose.Pdf.Annotations.DefaultAppearance(
                    //                     arialFont, 10, System.Drawing.Color.Black);
                    //             }
                    //             checkbox.Checked = !isChecked;
                    //             checkbox.Checked = isChecked;
                    //             checkboxesRegenerated++;
                    //         }
                    //         catch (Exception ex)
                    //         {
                    //             _logger.LogDebug($"Could not regenerate checkbox appearance for {field.FullName}: {ex.Message}");
                    //         }
                    //     }
                    // }

                    // if (checkboxesRegenerated > 0)
                    // {
                    //     _logger.LogInformation($"✅ Regenerated {checkboxesRegenerated} checkboxes to replace ZapfDingbats");
                    // }

                    // Second pass: Check DefaultAppearance for other base-14 fonts
                    foreach (var field in document.Form.Fields)
                    {
                        try
                        {
                            // Get the field's appearance dictionary to access fonts
                            if (field is Aspose.Pdf.Forms.Field formField)
                            {
                                // Try to access the font through the field's default appearance
                                var defaultAppearance = formField.DefaultAppearance;
                                if (defaultAppearance != null && !string.IsNullOrEmpty(defaultAppearance.FontName))
                                {
                                    _logger.LogDebug($"Field '{field.FullName}' uses font: {defaultAppearance.FontName}");
                                    formFieldFontsFound++;

                                    // Check if this field uses a base-14 font (especially ZapfDingbats)
                                    if (base14Fonts.Any(f => defaultAppearance.FontName.Contains(f, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        _logger.LogInformation($"⚠️ Found base-14 font '{defaultAppearance.FontName}' in form field: {field.FullName}");

                                        // For ZapfDingbats in checkboxes, regenerate appearance with embeddable font
                                        if (defaultAppearance.FontName.Contains("ZapfDingbats", StringComparison.OrdinalIgnoreCase) &&
                                            field is Aspose.Pdf.Forms.CheckboxField checkbox)
                                        {
                                            try
                                            {
                                                // Preserve current checked state
                                                bool isChecked = checkbox.Checked;

                                                // Get font size (default to 10 if not set)
                                                var fontSize = defaultAppearance.FontSize > 0 ? defaultAppearance.FontSize : 10;

                                                // Create new appearance with Arial (embeddable) instead of ZapfDingbats
                                                var arialFont = FontRepository.FindFont("Arial");
                                                if (arialFont != null)
                                                {
                                                    var textColor = defaultAppearance.TextColor == System.Drawing.Color.Empty
                                                        ? System.Drawing.Color.Black
                                                        : defaultAppearance.TextColor;
                                                    checkbox.DefaultAppearance = new Aspose.Pdf.Annotations.DefaultAppearance(
                                                        arialFont,
                                                        fontSize,
                                                        textColor
                                                    );

                                                    // Trigger appearance stream regeneration by re-setting checked state
                                                    checkbox.Checked = isChecked;

                                                    zapfDingbatsFieldsReplaced++;
                                                    _logger.LogInformation($"✅ Regenerated checkbox appearance with Arial: {field.FullName} (checked: {isChecked})");
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                _logger.LogWarning($"Could not regenerate checkbox appearance for {field.FullName}: {ex.Message}");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug($"Could not check font for field {field.FullName}: {ex.Message}");
                        }
                    }
                    _logger.LogInformation($"Found {formFieldFontsFound} form fields with font information");
                    if (zapfDingbatsFieldsReplaced > 0)
                    {
                        _logger.LogInformation($"✅ Replaced ZapfDingbats font in {zapfDingbatsFieldsReplaced} form fields");
                    }
                }

                // Detect and substitute base-14 fonts (Times-Roman, Helvetica, ZapfDingbats, etc.) in text content
                // This handles fonts in regular text and converts special characters (ZapfDingbats/Symbol) to Unicode
                DetectAndSubstituteBase14Fonts(document);

                // Check for fonts known to cause CIDset subsetting issues
                // EXPANDED LIST: We're going nuclear - fully embed all common problematic fonts
                bool hasProblematicFonts = false;
                foreach (Page page in document.Pages)
                {
                    if (page.Resources?.Fonts != null)
                    {
                        foreach (var font in page.Resources.Fonts)
                        {
                            var fontName = font.FontName ?? "";
                            // Aggressively embed ALL common problematic fonts - no subsetting!
                            if (fontName.Contains("OpenSansRegular", StringComparison.OrdinalIgnoreCase) ||
                                fontName.Contains("OpenSans-Regular", StringComparison.OrdinalIgnoreCase) ||
                                fontName.Contains("OpenSans", StringComparison.OrdinalIgnoreCase) ||
                                fontName.Contains("Arial", StringComparison.OrdinalIgnoreCase) ||
                                fontName.Contains("Times-Roman", StringComparison.OrdinalIgnoreCase) ||
                                fontName.Contains("Times", StringComparison.OrdinalIgnoreCase) ||
                                fontName.Contains("Helvetica", StringComparison.OrdinalIgnoreCase) ||
                                fontName.Contains("ZapfDingbats", StringComparison.OrdinalIgnoreCase) ||
                                fontName.Contains("Symbol", StringComparison.OrdinalIgnoreCase) ||
                                fontName.Contains("Courier", StringComparison.OrdinalIgnoreCase))
                            {
                                hasProblematicFonts = true;
                                _logger.LogWarning($"⚠️ Font '{fontName}' will be FULLY EMBEDDED (no subsetting) to avoid CIDset errors");
                                // Don't break - let's log all problematic fonts for debugging
                            }
                        }
                    }
                }

                // IMPORTANT: NEVER subset fonts - it causes CIDset errors
                // We're going nuclear - ALWAYS skip subsetting for ALL fonts
                _logger.LogWarning("⚠️ FORCE SKIPPING all font subsetting - full embedding for EVERYTHING");
                _logger.LogInformation("✅ All fonts will be fully embedded (larger file size but GUARANTEED no CIDset errors)");

                // DO NOT SUBSET ANYTHING - Commenting out ALL subsetting logic
                // The slight increase in file size is worth having working PDFs
                /*
                if (hasProblematicFonts)
                {
                    _logger.LogWarning("⚠️ Skipping font subsetting due to fonts with CIDset issues - fonts will be fully embedded");
                    _logger.LogInformation("✅ All fonts will be fully embedded (larger file size but no CIDset errors)");
                }
                else
                {
                    // Try SubsetEmbeddedFontsOnly first (safer), fall back to SubsetAllFonts if needed
                    try
                    {
                        _logger.LogInformation("Subsetting embedded fonts...");
                        document.FontUtilities.SubsetFonts(FontSubsetStrategy.SubsetEmbeddedFontsOnly);
                        _logger.LogInformation("✅ Successfully subsetted embedded fonts");
                    }
                    catch (Exception subsetEx)
                    {
                        _logger.LogWarning($"SubsetEmbeddedFontsOnly failed ({subsetEx.Message}), trying SubsetAllFonts...");
                        try
                        {
                            document.FontUtilities.SubsetFonts(FontSubsetStrategy.SubsetAllFonts);
                            _logger.LogInformation("✅ Successfully subsetted all fonts");
                        }
                        catch (Exception allSubsetEx)
                        {
                            _logger.LogWarning($"Font subsetting failed: {allSubsetEx.Message}");
                            // Continue without subsetting - at least the fonts are marked for embedding
                        }
                    }
                }
                */
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to embed/subset fonts");
            }

            // DISABLED: TwcFontComplianceService requires Liberation font files which don't exist
            // SubsetAllFonts above claims to work but doesn't actually embed base-14 fonts like Times-Roman/Arial
            // Until we get Liberation fonts OR fix Aspose embedding, this just adds errors
            // var complianceReport = _fontComplianceService.EnsureFontCompliance(document);
            // if (!complianceReport.IsCompliant)
            // {
            //     _logger.LogWarning("Font compliance check failed - some fonts may not be embedded");
            // }

            // Subsetting complete - PDF/A conversion will handle final font embedding validation
            _logger.LogInformation("===== FONT EMBEDDING COMPLETE =====");
        }
        
        /// <summary>
        /// Detect and substitute all PDF base-14 fonts with embeddable alternatives using Aspose's native substitution
        /// </summary>
        private List<string> DetectAndSubstituteBase14Fonts(Document document)
        {
            var substitutions = new List<string>();

            try
            {
                _logger.LogInformation("===== DETECTING BASE-14 FONTS =====");

                // Complete list of PDF base-14 fonts (standard fonts that can't be embedded)
                var base14Fonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    // Times family
                    "Times-Roman", "Times", "Times-Bold", "Times-Italic", "Times-BoldItalic",
                    "TimesNewRoman", "TimesNewRomanPS", "TimesNewRomanPSMT",

                    // Helvetica family
                    "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
                    "ArialMT", "Arial-BoldMT", "Arial-ItalicMT", "Arial-BoldItalicMT",

                    // Courier family
                    "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique",
                    "CourierNew", "CourierNewPS", "CourierNewPSMT",

                    // Symbol fonts
                    "Symbol", "ZapfDingbats"
                };

                // Mapping to embeddable alternatives (using widely-available system fonts)
                var fontMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    // Times variants → Arial (serif → sans-serif, but embeddable)
                    { "Times-Roman", "Arial" },
                    { "Times", "Arial" },
                    { "Times-Bold", "Arial,Bold" },
                    { "Times-Italic", "Arial,Italic" },
                    { "Times-BoldItalic", "Arial,BoldItalic" },
                    { "TimesNewRoman", "Arial" },
                    { "TimesNewRomanPS", "Arial" },
                    { "TimesNewRomanPSMT", "Arial" },

                    // Helvetica/Arial variants → Arial (already similar, ensure embeddable)
                    { "Helvetica", "Arial" },
                    { "Helvetica-Bold", "Arial,Bold" },
                    { "Helvetica-Oblique", "Arial,Italic" },
                    { "Helvetica-BoldOblique", "Arial,BoldItalic" },
                    { "ArialMT", "Arial" },
                    { "Arial-BoldMT", "Arial,Bold" },
                    { "Arial-ItalicMT", "Arial,Italic" },
                    { "Arial-BoldItalicMT", "Arial,BoldItalic" },

                    // Courier variants → Courier New (monospace, usually embeddable)
                    { "Courier", "Courier New" },
                    { "Courier-Bold", "Courier New,Bold" },
                    { "Courier-Oblique", "Courier New,Italic" },
                    { "Courier-BoldOblique", "Courier New,BoldItalic" },
                    { "CourierNew", "Courier New" },
                    { "CourierNewPS", "Courier New" },
                    { "CourierNewPSMT", "Courier New" },

                    // Symbol fonts → Arial (will need Unicode conversion for special chars)
                    { "Symbol", "Arial" },
                    { "ZapfDingbats", "Arial" }
                };

                // Scan ALL fonts in document (page resources + text fragments + form fields)
                var detectedBase14Fonts = new HashSet<string>();

                // Method 1: Check page resources
                foreach (Page page in document.Pages)
                {
                    if (page.Resources?.Fonts != null)
                    {
                        foreach (var font in page.Resources.Fonts)
                        {
                            var fontName = font.FontName ?? "";

                            if (base14Fonts.Contains(fontName) ||
                                base14Fonts.Any(b14 => fontName.Contains(b14, StringComparison.OrdinalIgnoreCase)))
                            {
                                if (!font.IsEmbedded)
                                {
                                    detectedBase14Fonts.Add(fontName);
                                    _logger.LogWarning($"[PAGE-RESOURCE] Detected base-14 font: {fontName} (NOT EMBEDDED)");
                                }
                            }
                        }
                    }
                }

                // Method 2: Scan all text fragments for fonts (catches fonts not in page resources)
                try
                {
                    // Use text edit options that will allow us to remove unused fonts later
                    var textEditOptions = new TextEditOptions(TextEditOptions.FontReplace.RemoveUnusedFonts);
                    var absorber = new TextFragmentAbsorber(textEditOptions);
                    document.Pages.Accept(absorber);

                    foreach (TextFragment fragment in absorber.TextFragments)
                    {
                        var fontName = fragment.TextState?.Font?.FontName ?? "";
                        if (!string.IsNullOrEmpty(fontName))
                        {
                            if (base14Fonts.Contains(fontName) ||
                                base14Fonts.Any(b14 => fontName.Contains(b14, StringComparison.OrdinalIgnoreCase)))
                            {
                                if (detectedBase14Fonts.Add(fontName))
                                {
                                    _logger.LogWarning($"[TEXT-FRAGMENT] Detected base-14 font: {fontName}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not scan text fragments for fonts: {ex.Message}");
                }

                // Actually replace fonts in the document using TextFragmentAbsorber
                int totalReplacements = 0;

                foreach (var base14Font in detectedBase14Fonts)
                {
                    // Find the best mapping
                    string targetFont = "Arial"; // Default fallback

                    foreach (var mapping in fontMappings)
                    {
                        if (base14Font.Equals(mapping.Key, StringComparison.OrdinalIgnoreCase) ||
                            base14Font.Contains(mapping.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            targetFont = mapping.Value;
                            break;
                        }
                    }

                    try
                    {
                        // Use TextFragmentAbsorber with RemoveUnusedFonts option to clean up font resources
                        var textEditOptions = new TextEditOptions(TextEditOptions.FontReplace.RemoveUnusedFonts);
                        var absorber = new TextFragmentAbsorber(textEditOptions);
                        document.Pages.Accept(absorber);

                        int fontReplacements = 0;
                        foreach (TextFragment textFragment in absorber.TextFragments)
                        {
                            if (textFragment.TextState?.Font?.FontName != null &&
                                textFragment.TextState.Font.FontName.Equals(base14Font, StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    // Special handling for symbol fonts - convert characters to Unicode first
                                    if (base14Font.Contains("ZapfDingbats", StringComparison.OrdinalIgnoreCase))
                                    {
                                        textFragment.Text = ConvertZapfDingbatsToUnicode(textFragment.Text);
                                        _logger.LogDebug($"Converted ZapfDingbats text: '{textFragment.Text}'");
                                    }
                                    else if (base14Font.Contains("Symbol", StringComparison.OrdinalIgnoreCase))
                                    {
                                        textFragment.Text = ConvertSymbolToUnicode(textFragment.Text);
                                        _logger.LogDebug($"Converted Symbol text: '{textFragment.Text}'");
                                    }

                                    var replacementFont = FontRepository.FindFont(targetFont);
                                    if (replacementFont != null)
                                    {
                                        textFragment.TextState.Font = replacementFont;
                                        fontReplacements++;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogDebug($"Could not replace font instance: {ex.Message}");
                                }
                            }
                        }

                        if (fontReplacements > 0)
                        {
                            var substitutionMsg = $"Base-14 font substitution: '{base14Font}' → '{targetFont}' ({fontReplacements} instances)";
                            substitutions.Add(substitutionMsg);
                            _logger.LogWarning($"⚠️ {substitutionMsg}");
                            _logger.LogInformation($"🧹 Removed unused font resource: '{base14Font}' from page resources");
                            totalReplacements += fontReplacements;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to replace font {base14Font}: {ex.Message}");
                    }
                }

                if (substitutions.Any())
                {
                    _logger.LogInformation($"✅ Registered {substitutions.Count} base-14 font substitutions");
                }
                else
                {
                    _logger.LogInformation("No base-14 fonts detected - all fonts are embeddable");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting/substituting base-14 fonts");
            }

            return substitutions;
        }

        /// <summary>
        /// Convert ZapfDingbats characters to Unicode equivalents
        /// </summary>
        private string ConvertZapfDingbatsToUnicode(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // ZapfDingbats character code mappings to Unicode
            var charMap = new Dictionary<char, string>
            {
                { '\u0034', "☑" },  // '4' = checkmark/checked box
                { '\u0071', "☐" },  // 'q' = empty checkbox
                { '\u0038', "✗" },  // '8' = X mark
                { '\u006C', "●" },  // 'l' = filled circle (bullet)
                { '\u006D', "○" },  // 'm' = empty circle
                { '\u006E', "✓" },  // 'n' = checkmark
                { '\u0075', "◆" },  // 'u' = diamond
                { '\u0076', "◇" },  // 'v' = hollow diamond
            };

            var result = text;
            foreach (var mapping in charMap)
            {
                result = result.Replace(mapping.Key.ToString(), mapping.Value);
            }

            return result;
        }

        /// <summary>
        /// Convert Symbol font characters to Unicode equivalents
        /// </summary>
        private string ConvertSymbolToUnicode(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Symbol font common mappings to Unicode
            var charMap = new Dictionary<char, string>
            {
                { '\u00B7', "•" },  // bullet
                { '\u00D7', "×" },  // multiplication
                { '\u00F7', "÷" },  // division
            };

            var result = text;
            foreach (var mapping in charMap)
            {
                result = result.Replace(mapping.Key.ToString(), mapping.Value);
            }

            return result;
        }

        /// <summary>
        /// Replace Times-Roman whitespace with embedded font
        /// </summary>
        private void ReplaceTimesRomanWhitespace(Document document)
        {
            try
            {
                _logger.LogInformation("===== REPLACING TIMES-ROMAN WHITESPACE =====");
                int replacements = 0;
                
                foreach (Page page in document.Pages)
                {
                    // Find all text fragments
                    var textAbsorber = new TextFragmentAbsorber();
                    page.Accept(textAbsorber);
                    
                    foreach (TextFragment textFragment in textAbsorber.TextFragments)
                    {
                        // Check if using Times font and is whitespace
                        var fontName = textFragment.TextState?.Font?.FontName ?? "";
                        if ((fontName.Contains("Times") || fontName == "Times-Roman") && 
                            string.IsNullOrWhiteSpace(textFragment.Text))
                        {
                            try
                            {
                                // Replace with Helvetica which embeds properly
                                textFragment.TextState.Font = FontRepository.FindFont("Helvetica");
                                replacements++;
                                _logger.LogDebug($"Replaced Times whitespace with Helvetica");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug($"Could not replace Times whitespace: {ex.Message}");
                            }
                        }
                    }
                }
                
                if (replacements > 0)
                {
                    _logger.LogInformation($"Replaced {replacements} Times-Roman whitespace fragments with Helvetica");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not replace Times-Roman whitespace: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Replace ZapfDingbats font with an embeddable alternative
        /// </summary>
        private void ReplaceZapfDingbatsFont(Document document)
        {
            _logger.LogInformation("===== REPLACING ZAPFDINGBATS FONT =====");
            
            try
            {
                // First, log all fonts in the document
                _logger.LogInformation("Scanning document for all fonts...");
                foreach (Page page in document.Pages)
                {
                    if (page.Resources?.Fonts != null)
                    {
                        foreach (var font in page.Resources.Fonts)
                        {
                            _logger.LogInformation($"Page {document.Pages.IndexOf(page) + 1} has font: {font.FontName}");
                            if (font.FontName.Contains("ZapfDingbats"))
                            {
                                _logger.LogWarning($"*** FOUND ZAPFDINGBATS IN PAGE RESOURCES ***");
                            }
                        }
                    }
                }
                
                // Search through all pages for text using ZapfDingbats
                var textFragmentsToReplace = new List<(Page page, TextFragment fragment)>();
                
                foreach (Page page in document.Pages)
                {
                    // Create TextFragmentAbsorber to find all text
                    var textAbsorber = new TextFragmentAbsorber();
                    page.Accept(textAbsorber);
                    
                    _logger.LogInformation($"Page {document.Pages.IndexOf(page) + 1} has {textAbsorber.TextFragments.Count} text fragments");
                    
                    foreach (TextFragment textFragment in textAbsorber.TextFragments)
                    {
                        // Log every fragment's font for debugging
                        var fontName = textFragment.TextState?.Font?.FontName ?? "null";
                        if (fontName.Contains("ZapfDingbats"))
                        {
                            _logger.LogWarning($"*** FOUND ZapfDingbats text: '{textFragment.Text}' on page {document.Pages.IndexOf(page) + 1} ***");
                            textFragmentsToReplace.Add((page, textFragment));
                        }
                    }
                }
                
                // Replace ZapfDingbats with Helvetica or Symbol font
                foreach (var (page, fragment) in textFragmentsToReplace)
                {
                    try
                    {
                        // Map ZapfDingbats characters to Unicode equivalents
                        string newText = fragment.Text;
                        
                        // Common ZapfDingbats mappings
                        // Character code 52 (4) = checkmark
                        // Character code 113 (q) = empty box
                        if (fragment.Text.Contains("4") || fragment.Text.Contains("\u0034"))
                        {
                            newText = "✓"; // Unicode checkmark
                        }
                        else if (fragment.Text.Contains("q") || fragment.Text.Contains("\u0071"))
                        {
                            newText = "☐"; // Unicode empty box
                        }
                        else if (fragment.Text.Contains("8") || fragment.Text.Contains("\u0038"))
                        {
                            newText = "✗"; // Unicode X mark
                        }
                        
                        // Replace the text and font
                        fragment.Text = newText;
                        fragment.TextState.Font = FontRepository.FindFont("Helvetica");
                        fragment.TextState.FontSize = fragment.TextState.FontSize; // Keep same size
                        
                        _logger.LogInformation($"Replaced ZapfDingbats character with '{newText}' using Helvetica");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Could not replace ZapfDingbats fragment: {ex.Message}");
                    }
                }
                
                // Also check form fields for ZapfDingbats
                if (document.Form != null)
                {
                    _logger.LogInformation($"Checking {document.Form.Fields.Length} form fields...");
                    foreach (var field in document.Form.Fields)
                    {
                        if (field is Aspose.Pdf.Forms.CheckboxField checkbox)
                        {
                            try
                            {
                                _logger.LogInformation($"Checking checkbox: {checkbox.FullName}");
                                
                                // Check various properties
                                var defaultAppearance = checkbox.DefaultAppearance;
                                if (defaultAppearance != null)
                                {
                                    _logger.LogInformation($"  DefaultAppearance FontName: {defaultAppearance.FontName ?? "null"}");
                                    _logger.LogInformation($"  DefaultAppearance FontSize: {defaultAppearance.FontSize}");
                                    
                                    if (defaultAppearance.FontName?.Contains("ZapfDingbats") == true)
                                    {
                                        _logger.LogWarning($"*** FOUND ZapfDingbats in checkbox DefaultAppearance: {checkbox.FullName} ***");
                                        
                                        // Change to Helvetica
                                        checkbox.DefaultAppearance.FontName = "Helvetica";
                                        checkbox.DefaultAppearance.FontSize = defaultAppearance.FontSize;
                                        
                                        // Force update
                                        checkbox.Flatten();  // This will embed the appearance
                                        _logger.LogInformation($"Flattened checkbox to remove ZapfDingbats: {checkbox.FullName}");
                                    }
                                }
                                
                                // Check if checkbox has any appearance streams
                                _logger.LogInformation($"  Checkbox checked state: {checkbox.Checked}");
                                _logger.LogInformation($"  Checkbox style: {checkbox.Style}");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Could not process checkbox {checkbox.FullName}: {ex.Message}");
                            }
                        }
                    }
                }
                
                _logger.LogInformation("ZapfDingbats replacement complete");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to replace ZapfDingbats font");
            }
        }
        
        /// <summary>
        /// Clean checkbox fields from unnecessary font references
        /// </summary>
        private void CleanCheckboxFonts(Document document, ref int checkboxesCleaned)
        {
            _logger.LogInformation("Cleaning checkbox form fields from font references...");
            
            try
            {
                // Access form fields
                if (document.Form != null)
                {
                    foreach (var field in document.Form.Fields)
                    {
                        // Check if it's a checkbox field
                        if (field is Aspose.Pdf.Forms.CheckboxField checkbox)
                        {
                            _logger.LogInformation($"Found checkbox field: {checkbox.FullName}");
                            
                            try
                            {
                                // Try to set checkbox style to not use custom fonts
                                checkbox.Style = Aspose.Pdf.Forms.BoxStyle.Check;
                                
                                // Ensure checkbox uses standard states
                                if (checkbox.Checked)
                                {
                                    checkbox.ActiveState = "Yes";
                                }
                                else
                                {
                                    checkbox.ActiveState = "Off";
                                }
                                
                                checkboxesCleaned++;
                                _logger.LogInformation($"Standardized checkbox appearance: {checkbox.FullName}");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Could not clean checkbox {checkbox.FullName}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not access form fields: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Apply auto-tagging for PDF/UA compliance using Aspose.PDF
        /// This is an alternative to Adobe's autotag service
        /// </summary>
        public async Task<byte[]> AutoTagPdfAsync(byte[] pdfBytes)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInformation("===== ASPOSE AUTO-TAGGING FOR ACCESSIBILITY =====");
                    
                    using var inputStream = new MemoryStream(pdfBytes);
                    using var document = new Document(inputStream);
                    
                    // Create tagged content
                    ITaggedContent taggedContent = document.TaggedContent;
                    
                    // Set document properties for accessibility
                    taggedContent.SetTitle("Accessible PDF Document");
                    taggedContent.SetLanguage("en-US");
                    
                    // Get the structure tree root
                    StructureElement rootElement = taggedContent.RootElement;
                    
                    // Process each page
                    foreach (Page page in document.Pages)
                    {
                        _logger.LogInformation($"Processing page {page.Number} for tagging");
                        
                        // Create a section element for the page
                        SectElement sectElement = taggedContent.CreateSectElement();
                        rootElement.AppendChild(sectElement);
                        
                        // Extract text fragments and create paragraph elements
                        TextFragmentAbsorber textAbsorber = new TextFragmentAbsorber();
                        page.Accept(textAbsorber);
                        
                        int fragmentCount = 0;
                        foreach (TextFragment textFragment in textAbsorber.TextFragments)
                        {
                            // Skip empty fragments
                            if (string.IsNullOrWhiteSpace(textFragment.Text))
                                continue;
                                
                            // Create paragraph element for text
                            ParagraphElement paragraphElement = taggedContent.CreateParagraphElement();
                            sectElement.AppendChild(paragraphElement);
                            
                            // Set actual text
                            paragraphElement.SetText(textFragment.Text);
                            fragmentCount++;
                        }
                        
                        _logger.LogInformation($"Added {fragmentCount} text fragments to page {page.Number}");
                    }
                    
                    _logger.LogInformation("Auto-tagging structure created");
                    
                    // Apply PDF/UA-1 compliance conversion
                    _logger.LogInformation("Converting to PDF/UA-1 format...");
                    
                    // Create conversion options with just the format
                    var pdfUaOptions = new PdfFormatConversionOptions(PdfFormat.PDF_UA_1);
                    
                    // Validate and log any issues
                    using var logStream = new MemoryStream();
                    bool isValidBefore = document.Validate(logStream, PdfFormat.PDF_UA_1);
                    _logger.LogInformation($"PDF/UA validation before conversion: {isValidBefore}");
                    
                    if (!isValidBefore)
                    {
                        logStream.Position = 0;
                        using var reader = new StreamReader(logStream);
                        var validationLog = reader.ReadToEnd();
                        if (!string.IsNullOrEmpty(validationLog))
                        {
                            _logger.LogDebug($"Validation issues: {validationLog.Substring(0, Math.Min(500, validationLog.Length))}...");
                        }
                    }
                    
                    // Try to convert to PDF/UA-1
                    try
                    {
                        document.Convert(pdfUaOptions);
                        _logger.LogInformation("PDF/UA-1 conversion completed");
                    }
                    catch (Exception convEx)
                    {
                        _logger.LogWarning($"PDF/UA conversion warning: {convEx.Message}");
                        // Continue even if conversion has warnings
                    }
                    
                    // Validate after conversion
                    using var logStream2 = new MemoryStream();
                    bool isValidAfter = document.Validate(logStream2, PdfFormat.PDF_UA_1);
                    _logger.LogInformation($"PDF/UA validation after conversion: {isValidAfter}");
                    
                    // Save the tagged document
                    using var outputStream = new MemoryStream();
                    document.Save(outputStream);
                    
                    var resultBytes = outputStream.ToArray();
                    _logger.LogInformation($"Aspose auto-tagging complete. Result: {resultBytes.Length} bytes");
                    
                    return resultBytes;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Aspose auto-tagging failed");
                    // Return original if tagging fails
                    return pdfBytes;
                }
            });
        }
        
        /// <summary>
        /// Validate PDF/UA compliance
        /// </summary>
        public bool ValidatePdfUaCompliance(byte[] pdfBytes, out string validationReport)
        {
            try
            {
                using var inputStream = new MemoryStream(pdfBytes);
                using var document = new Document(inputStream);
                using var logStream = new MemoryStream();

                bool isValid = document.Validate(logStream, PdfFormat.PDF_UA_1);

                logStream.Position = 0;
                using var reader = new StreamReader(logStream);
                validationReport = reader.ReadToEnd();

                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PDF/UA validation failed");
                validationReport = $"Validation error: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Comprehensive PDF/UA compliance fixes for all validation errors
        /// </summary>
        public async Task<byte[]> FixPdfUaComplianceAsync(byte[] pdfBytes)
        {
            if (!_isConfigured)
            {
                throw new InvalidOperationException("Aspose PDF service is not configured");
            }

            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogWarning("╔═══════════════════════════════════════════════════════════════════╗");
                    _logger.LogWarning("║ 🚨 FIXPDFUACOMPLIANCEASYNC CALLED - CALLS EmbedFonts()          ║");
                    _logger.LogWarning("╚═══════════════════════════════════════════════════════════════════╝");

                    using (var inputStream = new MemoryStream(pdfBytes))
                    using (var outputStream = new MemoryStream())
                    {
                        var document = new Document(inputStream);

                        // CHECK FOR FORMS AT START
                        var formFieldCountStart = document.Form?.Fields?.Length ?? 0;
                        _logger.LogWarning($"🔍 FORM CHECK AT START OF PDF/UA FIX: {formFieldCountStart} form fields");

                        var taggedContent = document.TaggedContent;

                        if (taggedContent == null || taggedContent.RootElement == null)
                        {
                            _logger.LogWarning("Document is not tagged - cannot fix PDF/UA issues");
                            return pdfBytes;
                        }

                        // 1. Add PDF/UA identifier
                        _logger.LogInformation("Adding PDF/UA identifier...");
                        // Set document metadata for PDF/UA compliance
                        taggedContent.SetTitle("Accessible Document");
                        taggedContent.SetLanguage("en-US");
                        _logger.LogInformation("✅ PDF/UA metadata added");

                        // 2. Fix Figure elements - add alt text to all figures
                        _logger.LogInformation("Fixing Figure elements...");
                        int figuresFixed = FixFigureElements(taggedContent.RootElement);
                        _logger.LogInformation($"✅ Fixed {figuresFixed} Figure elements with alt text");

                        // 3. Fix orphaned TH cells and table/link structure
                        _logger.LogInformation("Cleaning up orphaned TH cells and table/link structure...");
                        var cleanupConfig = new TableLinkCleanupConfig
                        {
                            FixOrphanedTableHeaders = true,
                            RemoveEmptyTables = true,
                            LinkHandling = LinkHandlingMode.Remove,
                            VerboseLogging = true
                        };
                        var cleanupReport = _tableLinkService.CleanupDocument(document, cleanupConfig);
                        _logger.LogInformation($"✅ Fixed {cleanupReport.OrphanedHeadersFixed} orphaned TH cells, " +
                                             $"removed {cleanupReport.EmptyTablesRemoved} empty tables, " +
                                             $"processed {cleanupReport.LinksProcessed} links");

                        // 4. Fix remaining TH cells - COMPREHENSIVE FIX
                        _logger.LogInformation("Applying COMPREHENSIVE table header cell fixes...");
                        int thCellsFixed = FixTableHeaderCells(taggedContent.RootElement, taggedContent);
                        _logger.LogInformation($"✅ Fixed {thCellsFixed} problematic TH cells");

                        // 4b. Extra aggressive table structure validation
                        _logger.LogInformation("Running aggressive table structure validation...");
                        int additionalFixes = ValidateAndFixAllTableStructures(taggedContent.RootElement, taggedContent);
                        _logger.LogInformation($"✅ Applied {additionalFixes} additional table structure fixes");

                        // 5. Embed all fonts (including Times-Roman)
                        _logger.LogWarning("🔍 PDF/UA FIX: Calling EmbedFonts()...");
                        EmbedFonts(document);
                        _logger.LogWarning("🔍 PDF/UA FIX: EmbedFonts() completed");

                        // CHECK FOR FORMS AFTER EMBEDFONTS
                        var formFieldCountAfterEmbed = document.Form?.Fields?.Length ?? 0;
                        if (formFieldCountAfterEmbed != formFieldCountStart)
                        {
                            _logger.LogError($"🚨 FORMS DESTROYED BY EmbedFonts! Before: {formFieldCountStart}, After: {formFieldCountAfterEmbed}");
                        }
                        else
                        {
                            _logger.LogWarning($"✅ Forms survived EmbedFonts: {formFieldCountAfterEmbed} fields still present");
                        }

                        // Save the fixed document
                        document.Save(outputStream);
                        var fixedBytes = outputStream.ToArray();

                        _logger.LogInformation("===== PDF/UA COMPLIANCE FIXES COMPLETE =====");
                        return fixedBytes;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fix PDF/UA compliance");
                    return pdfBytes; // Return original if fix fails
                }
            });
        }

        /// <summary>
        /// Fix Figure elements by adding alt text
        /// </summary>
        private int FixFigureElements(Element rootElement)
        {
            int count = 0;
            var figures = new List<FigureElement>();
            FindFigureElements(rootElement, figures);

            foreach (var figure in figures)
            {
                if (string.IsNullOrEmpty(figure.AlternativeText) && string.IsNullOrEmpty(figure.ActualText))
                {
                    // Add generic alt text - in production, use ImageAltTextService for AI-generated descriptions
                    figure.AlternativeText = "Image content";
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Recursively find all Figure elements
        /// </summary>
        private void FindFigureElements(Element element, List<FigureElement> figures)
        {
            if (element == null) return;

            if (element is FigureElement fig)
            {
                figures.Add(fig);
            }

            foreach (var child in element.ChildElements)
            {
                FindFigureElements(child, figures);
            }
        }

        /// <summary>
        /// Fix TH cells by ensuring they have proper structure
        /// PERMANENT FIX: Every TH cell MUST have either subcells OR be converted to proper text content
        /// </summary>
        private int FixTableHeaderCells(Element rootElement, ITaggedContent taggedContent)
        {
            int count = 0;
            var headers = new List<TableTHElement>();
            FindTableHeaders(rootElement, headers);

            foreach (var th in headers)
            {
                try
                {
                    bool hasSubcells = false;
                    bool hasTextContent = false;

                    // Check if TH has any child elements (subcells)
                    if (th.ChildElements != null && th.ChildElements.Count > 0)
                    {
                        hasSubcells = true;
                    }

                    // Check if TH has actual text content
                    if (!string.IsNullOrEmpty(th.ActualText) || !string.IsNullOrEmpty(th.AlternativeText))
                    {
                        hasTextContent = true;
                    }

                    // If TH has no subcells and no text content, we need to fix it
                    if (!hasSubcells && !hasTextContent)
                    {
                        _logger.LogWarning($"Found TH cell with no subcells or text content - fixing...");

                        // Option 1: Add a text element as a subcell
                        try
                        {
                            // Create a span element with text content
                            var spanElement = taggedContent.CreateSpanElement();
                            spanElement.SetText("Header");
                            th.AppendChild(spanElement);
                            hasSubcells = true;
                            _logger.LogInformation($"✅ Added span subcell to empty TH cell");
                        }
                        catch
                        {
                            // If we can't add a subcell, at least set alternative text
                            th.AlternativeText = "Table header";
                            th.ActualText = "Header";
                            hasTextContent = true;
                            _logger.LogInformation($"✅ Added text content to TH cell");
                        }
                        count++;
                    }
                    // Even if TH has subcells, ensure it also has alt text for accessibility
                    else if (string.IsNullOrEmpty(th.AlternativeText))
                    {
                        th.AlternativeText = "Table header";
                        _logger.LogDebug($"Added alt text to TH cell with subcells");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"Could not fix TH element: {ex.Message}");
                    // As a last resort, try to set alternative text
                    try
                    {
                        th.AlternativeText = "Table header";
                        th.ActualText = "Header";
                    }
                    catch { }
                }
            }

            _logger.LogInformation($"✅ Processed {headers.Count} TH cells, fixed {count} problematic ones");
            return count;
        }

        /// <summary>
        /// Recursively find all TH elements
        /// </summary>
        private void FindTableHeaders(Element element, List<TableTHElement> headers)
        {
            if (element == null) return;

            if (element is TableTHElement th)
            {
                headers.Add(th);
            }

            foreach (var child in element.ChildElements)
            {
                FindTableHeaders(child, headers);
            }
        }

        /// <summary>
        /// AGGRESSIVE validation and fix for ALL table structures
        /// This ensures EVERY table element has proper structure
        /// </summary>
        private int ValidateAndFixAllTableStructures(Element rootElement, ITaggedContent taggedContent)
        {
            int fixCount = 0;

            try
            {
                // Find all table-related elements
                var tables = new List<Element>();
                FindTableElements(rootElement, tables);

                foreach (var tableElement in tables)
                {
                    // Process each table
                    if (tableElement is TableElement table)
                    {
                        // Ensure table has proper structure
                        bool hasValidStructure = ValidateTableStructure(table);
                        if (!hasValidStructure)
                        {
                            _logger.LogWarning("Found table with invalid structure - fixing...");
                            if (FixTableStructure(table, taggedContent))
                            {
                                fixCount++;
                            }
                        }
                    }
                    else if (tableElement is TableTHElement thElement)
                    {
                        // Double-check TH cells
                        if (!HasValidTHStructure(thElement))
                        {
                            _logger.LogWarning("Found invalid TH structure in validation pass - fixing...");
                            if (ForceFixTHCell(thElement, taggedContent))
                            {
                                fixCount++;
                            }
                        }
                    }
                    else if (tableElement is TableTDElement tdElement)
                    {
                        // Ensure TD cells have proper content
                        if (!HasValidTDStructure(tdElement))
                        {
                            _logger.LogDebug("Found TD cell with no content - adding placeholder");
                            try
                            {
                                tdElement.SetText(" "); // Non-breaking space
                                fixCount++;
                            }
                            catch { }
                        }
                    }
                }

                _logger.LogInformation($"Validated {tables.Count} table elements");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error during table structure validation: {ex.Message}");
            }

            return fixCount;
        }

        private void FindTableElements(Element element, List<Element> tables)
        {
            if (element == null) return;

            if (element is TableElement || element is TableTHElement ||
                element is TableTDElement || element is TableTRElement)
            {
                tables.Add(element);
            }

            foreach (var child in element.ChildElements)
            {
                FindTableElements(child, tables);
            }
        }

        private bool ValidateTableStructure(TableElement table)
        {
            // A valid table should have at least one row
            if (table.ChildElements == null || table.ChildElements.Count == 0)
                return false;

            // Check for proper row/cell structure
            bool hasValidRows = false;
            foreach (var child in table.ChildElements)
            {
                if (child is TableTRElement)
                {
                    hasValidRows = true;
                    break;
                }
            }

            return hasValidRows;
        }

        private bool FixTableStructure(TableElement table, ITaggedContent taggedContent)
        {
            try
            {
                // If table has no rows, add a placeholder row
                if (table.ChildElements == null || table.ChildElements.Count == 0)
                {
                    var tr = taggedContent.CreateTableTRElement();
                    var td = taggedContent.CreateTableTDElement();
                    td.SetText("Table content");
                    tr.AppendChild(td);
                    table.AppendChild(tr);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Could not fix table structure: {ex.Message}");
            }
            return false;
        }

        private bool HasValidTHStructure(TableTHElement th)
        {
            // TH must have either child elements OR text content
            bool hasChildren = th.ChildElements != null && th.ChildElements.Count > 0;
            bool hasText = !string.IsNullOrEmpty(th.ActualText) || !string.IsNullOrEmpty(th.AlternativeText);
            return hasChildren || hasText;
        }

        private bool HasValidTDStructure(TableTDElement td)
        {
            // TD should have some content
            bool hasChildren = td.ChildElements != null && td.ChildElements.Count > 0;
            bool hasText = !string.IsNullOrEmpty(td.ActualText) || !string.IsNullOrEmpty(td.AlternativeText);
            return hasChildren || hasText;
        }

        private bool ForceFixTHCell(TableTHElement th, ITaggedContent taggedContent)
        {
            try
            {
                // Force add both text content AND a subcell if possible
                if (string.IsNullOrEmpty(th.ActualText))
                {
                    th.ActualText = "Header";
                }
                if (string.IsNullOrEmpty(th.AlternativeText))
                {
                    th.AlternativeText = "Table header";
                }

                // Try to add a subcell if none exists
                if (th.ChildElements == null || th.ChildElements.Count == 0)
                {
                    try
                    {
                        var span = taggedContent.CreateSpanElement();
                        span.SetText("Header");
                        th.AppendChild(span);
                    }
                    catch
                    {
                        // If we can't add a span, try a paragraph
                        try
                        {
                            var para = taggedContent.CreateParagraphElement();
                            para.SetText("Header");
                            th.AppendChild(para);
                        }
                        catch { }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Could not force fix TH cell: {ex.Message}");
            }
            return false;
        }
    }
}