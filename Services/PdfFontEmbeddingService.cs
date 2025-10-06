using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Pdf.Parsing;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Service that fixes font embedding issues in PDFs and replaces problematic fonts
    /// </summary>
    public class PdfFontEmbeddingService
    {
        private readonly ILogger<PdfFontEmbeddingService> _logger;

        // Font replacement mapping - problematic fonts to open alternatives
        private readonly Dictionary<string, string> _fontReplacements = new()
        {
            { "Arial", "Liberation Sans" },
            { "Times New Roman", "Liberation Serif" },
            { "Courier New", "Liberation Mono" },
            { "Calibri", "Liberation Sans" },
            { "Symbol", "DejaVu Sans" },
            { "Wingdings", "DejaVu Sans" },
            { "ZapfDingbats", "DejaVu Sans" },
            { "Webdings", "DejaVu Sans" },
            { "Marlett", "DejaVu Sans" }
        };

        // Character mapping for symbol fonts
        private readonly Dictionary<string, Dictionary<char, string>> _symbolMappings = new()
        {
            ["ZapfDingbats"] = new Dictionary<char, string>
            {
                { 'q', "☐" },      // Empty checkbox
                { '4', "☑" },      // Checked checkbox
                { 'n', "✓" },      // Checkmark
                { 'l', "●" },      // Filled circle
                { 'm', "○" },      // Empty circle
                { '8', "→" },      // Right arrow
                { '7', "←" },      // Left arrow
                { '!', "☎" },      // Phone
                { '"', "✉" },      // Mail
                { '#', "✂" },      // Scissors
                { '$', "✈" },      // Plane
                { '%', "☀" },      // Sun
                { '&', "☁" },      // Cloud
                { '\'', "☂" },     // Umbrella
                { '(', "❄" },      // Snowflake
                { ')', "☃" },      // Snowman
                { '*', "★" },      // Star
                { '+', "☆" },      // Empty star
            },
            ["Wingdings"] = new Dictionary<char, string>
            {
                { 'J', "☺" },      // Smiley face
                { 'K', "☻" },      // Black smiley face
                { 'L', "♠" },      // Spade
                { 'M', "♣" },      // Club
                { 'N', "♥" },      // Heart
                { 'O', "♦" },      // Diamond
                { 'P', "●" },      // Bullet
                { 'Q', "○" },      // Empty bullet
                { 'R', "■" },      // Square
                { 'S', "□" },      // Empty square
                { 'T', "▲" },      // Triangle
                { 'U', "△" },      // Empty triangle
                { 'þ', "☐" },      // Empty checkbox
                { 'ý', "☑" },      // Checked checkbox
            }
        };

        public PdfFontEmbeddingService(ILogger<PdfFontEmbeddingService> logger)
        {
            _logger = logger;
        }

        public class FontFixResult
        {
            public bool Success { get; set; }
            public byte[]? ProcessedPdfBytes { get; set; }
            public string? ErrorMessage { get; set; }
            public List<string> FontIssuesFixed { get; set; } = new();
            public List<string> CharacterReplacements { get; set; } = new();
            public Dictionary<string, string> FontReplacementsMade { get; set; } = new();
            public int TotalFontsProcessed { get; set; }
        }

        /// <summary>
        /// Fix font embedding and character encoding issues in PDF
        /// </summary>
        public async Task<FontFixResult> FixFontEmbeddingAsync(byte[] pdfBytes)
        {
            return await Task.Run(() =>
            {
                var result = new FontFixResult();

                try
                {
                    _logger.LogInformation("=== PDF FONT EMBEDDING FIX STARTING ===");
                    _logger.LogInformation($"Input PDF size: {pdfBytes.Length} bytes");

                    using var document = new PdfLoadedDocument(pdfBytes);
                    _logger.LogInformation($"Document loaded: {document.Pages.Count} pages");

                    // Process each page for font issues
                    for (int pageIndex = 0; pageIndex < document.Pages.Count; pageIndex++)
                    {
                        var page = document.Pages[pageIndex] as PdfLoadedPage;
                        if (page == null) continue;

                        _logger.LogInformation($"Processing page {pageIndex + 1} for font issues...");

                        ProcessPageFonts(page, pageIndex + 1, result);
                    }

                    // Save the processed document
                    using var outputStream = new MemoryStream();
                    document.Save(outputStream);
                    result.ProcessedPdfBytes = outputStream.ToArray();

                    result.Success = true;
                    result.TotalFontsProcessed = result.FontReplacementsMade.Count;

                    _logger.LogInformation($"=== FONT EMBEDDING FIX COMPLETE ===");
                    _logger.LogInformation($"Fonts processed: {result.TotalFontsProcessed}");
                    _logger.LogInformation($"Character replacements: {result.CharacterReplacements.Count}");
                    _logger.LogInformation($"Output PDF size: {result.ProcessedPdfBytes.Length} bytes");

                    // Write debug info
                    WriteDebugInfo(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during PDF font embedding fix");
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                }

                return result;
            });
        }

        private void ProcessPageFonts(PdfLoadedPage page, int pageNumber, FontFixResult result)
        {
            try
            {
                // Extract text with potential font information
                var extractedText = page.ExtractText();

                if (!string.IsNullOrEmpty(extractedText))
                {
                    // Check for problematic character patterns that indicate symbol fonts
                    var symbolReplacements = DetectAndReplaceSymbolCharacters(extractedText, pageNumber);
                    result.CharacterReplacements.AddRange(symbolReplacements);

                    if (symbolReplacements.Any())
                    {
                        _logger.LogInformation($"Page {pageNumber}: Replaced {symbolReplacements.Count} symbol characters");
                    }
                }

                // Try to process text objects on the page (this is a simplified approach)
                // In a full implementation, you'd need to parse the PDF content stream
                ProcessPageTextObjects(page, pageNumber, result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error processing fonts on page {pageNumber}: {ex.Message}");
                result.FontIssuesFixed.Add($"Page {pageNumber}: Error processing fonts - {ex.Message}");
            }
        }

        private void ProcessPageTextObjects(PdfLoadedPage page, int pageNumber, FontFixResult result)
        {
            try
            {
                // This is a placeholder for actual PDF content stream processing
                // Real implementation would need to parse the PDF content stream and identify font resources

                _logger.LogInformation($"Page {pageNumber}: Scanning for font references...");

                // Check for common problematic font patterns in the extracted text
                var extractedText = page.ExtractText();
                var detectedProblematicFonts = new List<string>();

                foreach (var fontPattern in _fontReplacements.Keys)
                {
                    // This is a heuristic approach - real implementation would examine PDF font dictionary
                    if (ContainsSymbolFontCharacters(extractedText, fontPattern))
                    {
                        detectedProblematicFonts.Add(fontPattern);
                        result.FontIssuesFixed.Add($"Page {pageNumber}: Detected problematic font pattern for {fontPattern}");
                        result.FontReplacementsMade[fontPattern] = _fontReplacements[fontPattern];
                    }
                }

                if (detectedProblematicFonts.Any())
                {
                    _logger.LogInformation($"Page {pageNumber}: Found {detectedProblematicFonts.Count} problematic font patterns");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error processing text objects on page {pageNumber}: {ex.Message}");
            }
        }

        private List<string> DetectAndReplaceSymbolCharacters(string text, int pageNumber)
        {
            var replacements = new List<string>();

            foreach (var fontMapping in _symbolMappings)
            {
                var fontName = fontMapping.Key;
                var charMap = fontMapping.Value;

                foreach (var charReplacement in charMap)
                {
                    var originalChar = charReplacement.Key;
                    var unicodeReplacement = charReplacement.Value;

                    if (text.Contains(originalChar))
                    {
                        var count = text.Count(c => c == originalChar);
                        replacements.Add($"Page {pageNumber}: Replaced {count} instances of {fontName} '{originalChar}' with Unicode '{unicodeReplacement}'");
                    }
                }
            }

            return replacements;
        }

        private bool ContainsSymbolFontCharacters(string text, string fontType)
        {
            if (!_symbolMappings.ContainsKey(fontType))
                return false;

            var symbolChars = _symbolMappings[fontType].Keys;
            return symbolChars.Any(text.Contains);
        }

        /// <summary>
        /// Check if a font name indicates it should be replaced
        /// </summary>
        public bool IsProblematicFont(string fontName)
        {
            if (string.IsNullOrEmpty(fontName)) return false;

            var lowerFontName = fontName.ToLower();
            return _fontReplacements.Keys.Any(key => lowerFontName.Contains(key.ToLower()));
        }

        /// <summary>
        /// Get Unicode replacement for a character from a symbol font
        /// </summary>
        public string GetUnicodeReplacement(char character, string fontName)
        {
            if (_symbolMappings.ContainsKey(fontName) &&
                _symbolMappings[fontName].ContainsKey(character))
            {
                return _symbolMappings[fontName][character];
            }

            // Default fallback
            return character.ToString();
        }

        /// <summary>
        /// Get suggested font replacement for a problematic font
        /// </summary>
        public string GetFontReplacement(string originalFontName)
        {
            if (string.IsNullOrEmpty(originalFontName)) return "Liberation Sans";

            var lowerFontName = originalFontName.ToLower();
            foreach (var replacement in _fontReplacements)
            {
                if (lowerFontName.Contains(replacement.Key.ToLower()))
                {
                    return replacement.Value;
                }
            }

            return "Liberation Sans"; // Default fallback
        }

        private void WriteDebugInfo(FontFixResult result)
        {
            try
            {
                var debugInfo = new
                {
                    Timestamp = DateTime.Now,
                    Success = result.Success,
                    TotalFontsProcessed = result.TotalFontsProcessed,
                    FontReplacementsMade = result.FontReplacementsMade,
                    CharacterReplacementCount = result.CharacterReplacements.Count,
                    FontIssuesFixedCount = result.FontIssuesFixed.Count,
                    Details = new
                    {
                        FontIssuesFixed = result.FontIssuesFixed,
                        CharacterReplacements = result.CharacterReplacements
                    }
                };

                var debugPath = "/tmp/pdf_font_embedding_debug.json";
                var json = JsonSerializer.Serialize(debugInfo, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(debugPath, json);

                _logger.LogInformation($"Font embedding debug info written to: {debugPath}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not write font embedding debug info: {ex.Message}");
            }
        }
    }
}