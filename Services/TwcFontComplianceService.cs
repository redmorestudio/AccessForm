using Aspose.Pdf;
using Aspose.Pdf.Text;
using System.Text.RegularExpressions;

namespace AccessFormServer.Services;

/// <summary>
/// Service for handling TWC-specific font embedding compliance issues.
/// This centralizes all special-case font handling for Texas Workforce Commission documents
/// to ensure WCAG/Section 508 compliance.
/// </summary>
public class TwcFontComplianceService
{
    private readonly ILogger<TwcFontComplianceService> _logger;

    // TWC-specific text patterns that commonly fail font embedding
    private static readonly string[] TwcHeaderPatterns = new[]
    {
        "Texas Workforce Commission",
        "Vocational Rehabilitation Services",
        "Texas Workforce Commission Vocational Rehabilitation Services"
    };

    private static readonly Regex TwcFormNumberPattern = new Regex(@"VR\d{4}\s*\(\d{1,2}/\d{2,4}\)", RegexOptions.Compiled);

    public TwcFontComplianceService(ILogger<TwcFontComplianceService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Comprehensive font compliance check and correction
    /// </summary>
    public FontComplianceReport EnsureFontCompliance(Document document)
    {
        _logger.LogInformation("===== TWC FONT COMPLIANCE CHECK =====");

        var report = new FontComplianceReport();

        // Step 1: Detect TWC patterns and flag potential issues
        DetectTwcPatterns(document, report);

        // Step 2: Process all text fragments for font embedding
        ProcessAllTextFragments(document, report);

        // Step 3: Validate all fonts are now embedded
        ValidateAllFontsEmbedded(document, report);

        _logger.LogInformation($"===== COMPLIANCE REPORT =====");
        _logger.LogInformation($"TWC patterns detected: {report.TwcPatternsDetected}");
        _logger.LogInformation($"Text fragments processed: {report.TextFragmentsProcessed}");
        _logger.LogInformation($"Fonts embedded: {report.FontsEmbedded}");
        _logger.LogInformation($"Fonts replaced: {report.FontsReplaced}");
        _logger.LogInformation($"Fonts failed: {report.FontsFailed}");
        _logger.LogInformation($"Compliance status: {(report.IsCompliant ? "PASS" : "FAIL")}");

        if (report.Warnings.Any())
        {
            _logger.LogWarning($"Warnings ({report.Warnings.Count}):");
            foreach (var warning in report.Warnings)
            {
                _logger.LogWarning($"  - {warning}");
            }
        }

        return report;
    }

    /// <summary>
    /// Detect TWC-specific text patterns
    /// </summary>
    private void DetectTwcPatterns(Document document, FontComplianceReport report)
    {
        foreach (Page page in document.Pages)
        {
            var absorber = new TextFragmentAbsorber();
            page.Accept(absorber);

            foreach (TextFragment fragment in absorber.TextFragments)
            {
                var text = fragment.Text;

                // Check for TWC header patterns
                foreach (var pattern in TwcHeaderPatterns)
                {
                    if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        report.TwcPatternsDetected++;
                        _logger.LogInformation($"TWC header pattern detected: '{text}' on page {page.Number}");
                        report.TwcPatternLocations.Add(new PatternLocation
                        {
                            PageNumber = page.Number,
                            Text = text,
                            PatternType = "Header"
                        });
                    }
                }

                // Check for form number patterns (VR####)
                if (TwcFormNumberPattern.IsMatch(text))
                {
                    report.TwcPatternsDetected++;
                    _logger.LogInformation($"TWC form number detected: '{text}' on page {page.Number}");
                    report.TwcPatternLocations.Add(new PatternLocation
                    {
                        PageNumber = page.Number,
                        Text = text,
                        PatternType = "FormNumber"
                    });
                }
            }
        }
    }

    /// <summary>
    /// Process all text fragments to ensure fonts are embedded or replaced
    /// </summary>
    private void ProcessAllTextFragments(Document document, FontComplianceReport report)
    {
        foreach (Page page in document.Pages)
        {
            var absorber = new TextFragmentAbsorber();
            page.Accept(absorber);

            foreach (TextFragment fragment in absorber.TextFragments)
            {
                report.TextFragmentsProcessed++;

                if (fragment.TextState?.Font == null)
                {
                    continue;
                }

                var font = fragment.TextState.Font;
                var fontName = font.FontName;

                // Skip if already embedded
                if (font.IsEmbedded)
                {
                    continue;
                }

                // Check if this is a base 14 font or a problematic font (OpenSans has CIDset issues)
                // Base 14 fonts: Times-Roman, Times-Bold, Times-Italic, Times-BoldItalic,
                // Helvetica, Helvetica-Bold, Helvetica-Oblique, Helvetica-BoldOblique,
                // Courier, Courier-Bold, Courier-Oblique, Courier-BoldOblique, Symbol, ZapfDingbats
                bool isBase14 = IsBase14Font(fontName);
                bool isProblematicFont = IsProblematicFont(fontName);

                if (isBase14 || isProblematicFont)
                {
                    // Base 14 fonts cannot be embedded, problematic fonts have subsetting issues - replace with an embeddable font
                    ReplaceWithEmbeddableFont(fragment, fontName, page.Number, report);
                }
                else
                {
                    // Try to embed the font
                    bool embeddingSuccess = TryEmbedFont(font, fontName, fragment.Text, page.Number, report);

                    if (!embeddingSuccess)
                    {
                        // Embedding failed - replace with embeddable font
                        ReplaceWithEmbeddableFont(fragment, fontName, page.Number, report);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Check if a font is one of the PDF base 14 fonts that cannot be embedded
    /// </summary>
    private bool IsBase14Font(string fontName)
    {
        var base14Fonts = new[]
        {
            "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
            "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
            "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique",
            "Symbol", "ZapfDingbats",
            "Arial", "ArialMT", "Arial-BoldMT", "Arial-ItalicMT", "Arial-BoldItalicMT"
        };

        foreach (var base14Font in base14Fonts)
        {
            if (fontName.Contains(base14Font, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if a font has known subsetting/embedding issues (e.g., OpenSans has CIDset problems)
    /// </summary>
    private bool IsProblematicFont(string fontName)
    {
        var problematicFonts = new[]
        {
            "OpenSans", "OpenSansRegular", "OpenSans-Regular",
            "OpenSansBold", "OpenSans-Bold",
            "OpenSansItalic", "OpenSans-Italic",
            "OpenSansBoldItalic", "OpenSans-BoldItalic"
        };

        foreach (var problematicFont in problematicFonts)
        {
            if (fontName.Contains(problematicFont, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempt to embed a font
    /// </summary>
    private bool TryEmbedFont(Aspose.Pdf.Text.Font font, string fontName, string text, int pageNumber, FontComplianceReport report)
    {
        try
        {
            // Special handling for Times fonts - try hard to embed them first
            if (fontName.Contains("Times", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation($"Attempting to embed Times font '{fontName}' on page {pageNumber}");
                font.IsEmbedded = true;

                // Verify it actually embedded
                if (font.IsEmbedded)
                {
                    report.FontsEmbedded++;
                    _logger.LogInformation($"Successfully embedded Times font '{fontName}'");
                    return true;
                }
                else
                {
                    _logger.LogWarning($"Times font '{fontName}' did not embed despite no error");
                    return false;
                }
            }

            // Try to embed other fonts
            font.IsEmbedded = true;

            if (font.IsEmbedded)
            {
                report.FontsEmbedded++;
                _logger.LogInformation($"Successfully embedded font '{fontName}' on page {pageNumber}");
                return true;
            }
            else
            {
                _logger.LogWarning($"Font '{fontName}' did not embed on page {pageNumber}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to embed font '{fontName}' on page {pageNumber}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Replace text fragment font with an embeddable font
    /// </summary>
    private void ReplaceWithEmbeddableFont(TextFragment fragment, string originalFontName, int pageNumber, FontComplianceReport report)
    {
        try
        {
            var originalSize = fragment.TextState.FontSize;
            var originalColor = fragment.TextState.ForegroundColor;
            var originalIsBold = originalFontName.Contains("Bold", StringComparison.OrdinalIgnoreCase);
            var originalIsItalic = originalFontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                                    originalFontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase);

            // Choose an appropriate embeddable font based on the original style
            // Use Liberation Sans (TrueType, embeddable) instead of Arial (base-14, non-embeddable)
            string replacementFontName = "LiberationSans";
            FontStyles fontStyle = FontStyles.Regular;

            if (originalIsBold && originalIsItalic)
            {
                fontStyle = FontStyles.Bold | FontStyles.Italic;
            }
            else if (originalIsBold)
            {
                fontStyle = FontStyles.Bold;
            }
            else if (originalIsItalic)
            {
                fontStyle = FontStyles.Italic;
            }

            // Find and apply the font
            var newFont = FontRepository.FindFont(replacementFontName, fontStyle);

            // Force embedding for the new font
            if (!newFont.IsEmbedded)
            {
                newFont.IsEmbedded = true;
            }

            fragment.TextState.Font = newFont;
            fragment.TextState.FontSize = originalSize;
            fragment.TextState.ForegroundColor = originalColor;

            report.FontsReplaced++;
            var textPreview = fragment.Text.Length > 50 ? fragment.Text.Substring(0, 50) + "..." : fragment.Text;
            report.Warnings.Add($"Page {pageNumber}: Replaced '{originalFontName}' with embedded '{replacementFontName}' for text: '{textPreview}'");
            _logger.LogInformation($"Replaced '{originalFontName}' with embedded '{replacementFontName}' on page {pageNumber}");
        }
        catch (Exception ex)
        {
            report.FontsFailed++;
            report.Warnings.Add($"Page {pageNumber}: FAILED to replace '{originalFontName}' - {ex.Message}");
            _logger.LogError($"Failed to replace font '{originalFontName}' on page {pageNumber}: {ex.Message}");
        }
    }

    /// <summary>
    /// Final validation that all fonts are embedded
    /// </summary>
    private void ValidateAllFontsEmbedded(Document document, FontComplianceReport report)
    {
        var unembeddedFonts = new HashSet<string>();

        foreach (Page page in document.Pages)
        {
            if (page.Resources?.Fonts != null)
            {
                foreach (var font in page.Resources.Fonts)
                {
                    if (!font.IsEmbedded && !font.FontName.Contains("ZapfDingbats"))
                    {
                        unembeddedFonts.Add($"{font.FontName} (Page {page.Number})");
                    }
                }
            }
        }

        if (unembeddedFonts.Any())
        {
            report.IsCompliant = false;
            report.Warnings.Add($"COMPLIANCE FAILURE: {unembeddedFonts.Count} unembedded fonts remain:");
            foreach (var font in unembeddedFonts)
            {
                report.Warnings.Add($"  - {font}");
                _logger.LogError($"Unembedded font: {font}");
            }
        }
        else
        {
            report.IsCompliant = true;
            _logger.LogInformation("All fonts are embedded - COMPLIANCE PASS");
        }
    }
}

/// <summary>
/// Report of font compliance status
/// </summary>
public class FontComplianceReport
{
    public bool IsCompliant { get; set; } = false;
    public int TwcPatternsDetected { get; set; } = 0;
    public int TextFragmentsProcessed { get; set; } = 0;
    public int FontsEmbedded { get; set; } = 0;
    public int FontsReplaced { get; set; } = 0;
    public int FontsFailed { get; set; } = 0;
    public List<string> Warnings { get; set; } = new List<string>();
    public List<PatternLocation> TwcPatternLocations { get; set; } = new List<PatternLocation>();
}

public class PatternLocation
{
    public int PageNumber { get; set; }
    public string Text { get; set; } = "";
    public string PatternType { get; set; } = "";
}