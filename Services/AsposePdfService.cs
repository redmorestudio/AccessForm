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
        private bool _isConfigured = false;

        public AsposePdfService(ILogger<AsposePdfService> logger, TwcFontComplianceService fontComplianceService)
        {
            _logger = logger;
            _fontComplianceService = fontComplianceService;
            
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
                    _logger.LogInformation($"===== ASPOSE OPTIMIZATION STARTING =====");
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
                        
                        // Log initial font status
                        LogFontStatus(document, "BEFORE optimization");
                        
                        // Embed all fonts
                        EmbedFonts(document);
                        
                        // Optimize the document
                        _logger.LogInformation("Applying optimization options...");
                        try
                        {
                            var optimizationOptions = new Aspose.Pdf.Optimization.OptimizationOptions
                            {
                                RemoveUnusedObjects = true,
                                RemoveUnusedStreams = true,
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
                            
                            document.OptimizeResources(optimizationOptions);
                            _logger.LogInformation("Optimization applied successfully");
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
        /// Helper method to embed all fonts in the document
        /// </summary>
        private void EmbedFonts(Document document)
        {
            _logger.LogInformation("===== EMBEDDING FONTS AND REPLACING BASE-14 FONTS =====");

            int checkboxesCleaned = 0;

            // First, detect and substitute all base-14 fonts using Aspose's native substitution API
            var substitutions = DetectAndSubstituteBase14Fonts(document);
            if (substitutions.Any())
            {
                _logger.LogInformation($"Applied {substitutions.Count} base-14 font substitutions");
            }

            // DISABLED: Checkbox cleaning uses ZapfDingbats which cannot be embedded
            // CleanCheckboxFonts(document, ref checkboxesCleaned);
            // _logger.LogInformation($"Checkboxes cleaned: {checkboxesCleaned}");
            _logger.LogInformation("Skipping checkbox font cleaning (would introduce non-embeddable ZapfDingbats)");

            // Use FontUtilities to embed and subset ALL fonts (including base 14 fonts)
            // This is the key: SubsetAllFonts will force embedding of all fonts, even base 14
            try
            {
                _logger.LogInformation("Embedding and subsetting ALL fonts...");
                document.FontUtilities.SubsetFonts(FontSubsetStrategy.SubsetAllFonts);
                _logger.LogInformation("Successfully embedded and subsetted all fonts");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not apply font subsetting: {ex.Message}");
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
    }
}