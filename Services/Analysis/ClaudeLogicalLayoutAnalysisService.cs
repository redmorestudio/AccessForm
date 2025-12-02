using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PDFtoImage;
using SkiaSharp;
using WordToPdfConverter.Models.Logical;

namespace WordToPdfConverter.Services.Analysis;

/// <summary>
/// Real implementation of ILogicalLayoutAnalysisService using Claude Vision API.
/// Renders PDF pages to images and uses Claude's vision capabilities to detect
/// semantic layout structure (headings, paragraphs, tables, figures).
/// </summary>
public sealed class ClaudeLogicalLayoutAnalysisService : ILogicalLayoutAnalysisService
{
    private readonly ILogger<ClaudeLogicalLayoutAnalysisService> _logger;
    private readonly AccessFormServer.Services.AnthropicService _anthropicService;
    private readonly ProcessingProgressService? _progressService;
    private readonly IConfiguration _configuration;

    public ClaudeLogicalLayoutAnalysisService(
        ILogger<ClaudeLogicalLayoutAnalysisService> logger,
        AccessFormServer.Services.AnthropicService anthropicService,
        IConfiguration configuration,
        ProcessingProgressService? progressService = null)
    {
        _logger = logger;
        _anthropicService = anthropicService;
        _configuration = configuration;
        _progressService = progressService;
    }

    public async Task<LogicalDocument> AnalyzeAsync(
        byte[] pdfBytes,
        LogicalLayoutOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("[CLAUDE-LAYOUT] Starting Claude Vision layout analysis");

            // Step 1: Determine page count and limit
            var pageCount = GetPageCount(pdfBytes);
            var pagesToAnalyze = options.MaxPages > 0
                ? Math.Min(pageCount, options.MaxPages)
                : pageCount;

            _logger.LogInformation(
                $"[CLAUDE-LAYOUT] Analyzing {pagesToAnalyze} of {pageCount} pages");

            var pages = new List<LogicalPage>();

            // Step 2: Process each page
            for (int pageNum = 1; pageNum <= pagesToAnalyze; pageNum++)
            {
                _logger.LogInformation($"[CLAUDE-LAYOUT] Analyzing page {pageNum}/{pagesToAnalyze}...");

                try
                {
                    var logicalPage = await AnalyzePageAsync(
                        pdfBytes,
                        pageNum,
                        options,
                        cancellationToken);

                    pages.Add(logicalPage);

                    _logger.LogInformation(
                        $"[CLAUDE-LAYOUT] Page {pageNum} complete: {logicalPage.Blocks.Count} blocks detected " +
                        $"({CountBlocksByType(logicalPage)})");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[CLAUDE-LAYOUT] Failed to analyze page {pageNum}, skipping");

                    // Add empty page on failure to maintain page numbering
                    pages.Add(new LogicalPage(pageNum, new List<LogicalBlock>()));
                }
            }

            var doc = new LogicalDocument(pages);
            _logger.LogInformation(
                $"[CLAUDE-LAYOUT] Analysis complete: {doc.Pages.Count} pages, " +
                $"{doc.Pages.Sum(p => p.Blocks.Count)} total blocks");

            return doc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CLAUDE-LAYOUT] Failed to analyze PDF layout");

            // Return minimal document on catastrophic failure
            return new LogicalDocument(new List<LogicalPage>
            {
                new LogicalPage(1, new List<LogicalBlock>
                {
                    new ParagraphBlock(new Rect(0, 0, 100, 20),
                        $"Layout analysis failed: {ex.Message}")
                })
            });
        }
    }

    private async Task<LogicalPage> AnalyzePageAsync(
        byte[] pdfBytes,
        int pageNumber,
        LogicalLayoutOptions options,
        CancellationToken cancellationToken)
    {
        // Step 1: Render page to image
        var imageBase64 = await RenderPageToImageAsync(pdfBytes, pageNumber);

        // Step 2: Build prompt for Claude
        var prompt = BuildAnalysisPrompt(options);

        // Step 3: Call Claude Vision API
        var responseJson = await CallClaudeVisionAsync(imageBase64, prompt, cancellationToken);

        // Step 4: Parse response into LogicalBlocks
        var blocks = ParseClaudeResponse(responseJson, pageNumber);

        return new LogicalPage(pageNumber, blocks);
    }

    private async Task<string> RenderPageToImageAsync(byte[] pdfBytes, int pageNumber)
    {
        try
        {
            // Render page to SKBitmap using PDFtoImage (150 DPI for good quality/performance balance)
            var renderOptions = new PDFtoImage.RenderOptions
            {
                Dpi = 150,
                BackgroundColor = SKColors.White
            };

            using var bitmap = PDFtoImage.Conversion.ToImage(
                pdfBytes,
                pageNumber - 1, // PDFtoImage uses 0-based indexing
                null, // password
                renderOptions);

            // Convert to PNG and base64 encode
            using var imageStream = new MemoryStream();
            bitmap.Encode(imageStream, SKEncodedImageFormat.Png, 90);
            var imageBytes = imageStream.ToArray();

            return await Task.FromResult(Convert.ToBase64String(imageBytes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[CLAUDE-LAYOUT] Failed to render page {pageNumber} to image");
            throw;
        }
    }

    private string BuildAnalysisPrompt(LogicalLayoutOptions options)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine("Analyze this PDF page image and identify all semantic content blocks.");
        prompt.AppendLine("Extract the visual layout structure and classify each element.");
        prompt.AppendLine();
        prompt.AppendLine("For each block, provide:");
        prompt.AppendLine("- type: 'heading', 'paragraph', 'table', or 'figure'");
        prompt.AppendLine("- bounds: approximate {x, y, width, height} in PDF points (72 DPI)");
        prompt.AppendLine();
        prompt.AppendLine("For headings:");
        prompt.AppendLine("- level: 1-6 (1=largest/most important, 6=smallest)");
        prompt.AppendLine("- text: exact text content");
        prompt.AppendLine();
        prompt.AppendLine("For paragraphs:");
        prompt.AppendLine("- text: exact text content (merge multi-line if same paragraph)");
        prompt.AppendLine();

        if (options.IncludeTables)
        {
            prompt.AppendLine("For tables:");
            prompt.AppendLine("- rows: array of rows, each with cells array");
            prompt.AppendLine("- cell format: {rowIndex, columnIndex, isHeader, text}");
            prompt.AppendLine("- First row is usually headers (isHeader: true)");
            prompt.AppendLine();
        }

        if (options.IncludeFigures)
        {
            prompt.AppendLine("For figures:");
            prompt.AppendLine("- altTextSuggestion: brief description of the image");
            prompt.AppendLine("- isLikelyDecorative: true if purely decorative (borders, backgrounds, etc.)");
            prompt.AppendLine();
        }

        prompt.AppendLine("Return ONLY valid JSON in this exact format (no markdown, no explanation):");
        prompt.AppendLine("{");
        prompt.AppendLine("  \"blocks\": [");
        prompt.AppendLine("    {\"type\": \"heading\", \"level\": 1, \"text\": \"...\", \"bounds\": {\"x\": 50, \"y\": 50, \"width\": 400, \"height\": 40}},");
        prompt.AppendLine("    {\"type\": \"paragraph\", \"text\": \"...\", \"bounds\": {\"x\": 50, \"y\": 100, \"width\": 500, \"height\": 60}},");

        if (options.IncludeTables)
        {
            prompt.AppendLine("    {\"type\": \"table\", \"rows\": [{\"cells\": [{\"rowIndex\": 0, \"columnIndex\": 0, \"isHeader\": true, \"text\": \"...\"}]}], \"bounds\": {...}},");
        }

        if (options.IncludeFigures)
        {
            prompt.AppendLine("    {\"type\": \"figure\", \"altTextSuggestion\": \"...\", \"isLikelyDecorative\": false, \"bounds\": {...}}");
        }

        prompt.AppendLine("  ]");
        prompt.AppendLine("}");

        return prompt.ToString();
    }

    private async Task<string> CallClaudeVisionAsync(
        string imageBase64,
        string prompt,
        CancellationToken cancellationToken)
    {
        try
        {
            // Use the existing AnthropicService's HTTP client
            // We'll make a direct API call with vision content
            var request = new
            {
                model = "claude-sonnet-4-20250514", // Latest Sonnet 4.5
                max_tokens = 4000,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "image",
                                source = new
                                {
                                    type = "base64",
                                    media_type = "image/png",
                                    data = imageBase64
                                }
                            },
                            new
                            {
                                type = "text",
                                text = prompt
                            }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(request);
            var httpContent = new System.Net.Http.StringContent(
                json,
                Encoding.UTF8,
                "application/json");

            // Access the underlying HttpClient from AnthropicService
            // Since we can't access it directly, we'll use reflection or create our own
            // For now, let's use a simple approach with the injected service

            // Actually, let's just build our own HTTP call here since we need vision-specific formatting
            var httpClient = new System.Net.Http.HttpClient
            {
                BaseAddress = new Uri("https://api.anthropic.com/")
            };
            httpClient.DefaultRequestHeaders.Add("x-api-key", GetApiKey());
            httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            httpClient.Timeout = TimeSpan.FromMinutes(2);

            var response = await httpClient.PostAsync("v1/messages", httpContent, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new Exception($"Claude API error: {response.StatusCode} - {error}");
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            // Parse response to extract text content
            var responseDoc = JsonDocument.Parse(responseContent);
            if (responseDoc.RootElement.TryGetProperty("content", out var contentArray) &&
                contentArray.GetArrayLength() > 0)
            {
                var firstContent = contentArray[0];
                if (firstContent.TryGetProperty("text", out var textElement))
                {
                    return textElement.GetString() ?? "{}";
                }
            }

            throw new Exception("Could not extract text from Claude response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CLAUDE-LAYOUT] Failed to call Claude Vision API");
            throw;
        }
    }

    private string GetApiKey()
    {
        // Get API key from configuration (same as AnthropicService)
        var apiKey = _configuration["ApiKeys:Anthropic"];
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new Exception("Anthropic API key not found in configuration (ApiKeys:Anthropic)");
        }
        return apiKey;
    }

    private IReadOnlyList<LogicalBlock> ParseClaudeResponse(string responseJson, int pageNumber)
    {
        var blocks = new List<LogicalBlock>();

        try
        {
            // Claude may return JSON with markdown code fences, strip them
            var cleanJson = responseJson.Trim();
            if (cleanJson.StartsWith("```json"))
            {
                cleanJson = cleanJson.Substring(7);
            }
            if (cleanJson.StartsWith("```"))
            {
                cleanJson = cleanJson.Substring(3);
            }
            if (cleanJson.EndsWith("```"))
            {
                cleanJson = cleanJson.Substring(0, cleanJson.Length - 3);
            }
            cleanJson = cleanJson.Trim();

            var doc = JsonDocument.Parse(cleanJson);

            if (!doc.RootElement.TryGetProperty("blocks", out var blocksArray))
            {
                _logger.LogWarning("[CLAUDE-LAYOUT] Response missing 'blocks' array");
                return blocks;
            }

            foreach (var blockElement in blocksArray.EnumerateArray())
            {
                try
                {
                    var block = ParseBlock(blockElement);
                    if (block != null)
                    {
                        blocks.Add(block);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[CLAUDE-LAYOUT] Failed to parse block, skipping");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CLAUDE-LAYOUT] Failed to parse Claude response JSON");
        }

        return blocks;
    }

    private LogicalBlock? ParseBlock(JsonElement blockElement)
    {
        if (!blockElement.TryGetProperty("type", out var typeElement))
            return null;

        var type = typeElement.GetString()?.ToLowerInvariant();
        var bounds = ParseBounds(blockElement);

        return type switch
        {
            "heading" => ParseHeading(blockElement, bounds),
            "paragraph" => ParseParagraph(blockElement, bounds),
            "table" => ParseTable(blockElement, bounds),
            "figure" => ParseFigure(blockElement, bounds),
            _ => null
        };
    }

    private Rect ParseBounds(JsonElement blockElement)
    {
        // DPI scaling: Claude sees image at 150 DPI, returns pixel coordinates
        // We need to convert to PDF points (72 DPI)
        const double SCALE_FACTOR = 72.0 / 150.0; // = 0.48

        // Standard Letter page height in PDF points (72 DPI)
        const double PAGE_HEIGHT_PDF = 792.0;

        if (blockElement.TryGetProperty("bounds", out var boundsElement))
        {
            // Get coordinates (likely 150 DPI image pixels from Claude)
            var xRaw = boundsElement.TryGetProperty("x", out var xElem) ? xElem.GetDouble() : 0;
            var yRaw = boundsElement.TryGetProperty("y", out var yElem) ? yElem.GetDouble() : 0;
            var widthRaw = boundsElement.TryGetProperty("width", out var wElem) ? wElem.GetDouble() : 100;
            var heightRaw = boundsElement.TryGetProperty("height", out var hElem) ? hElem.GetDouble() : 20;

            // CRITICAL FIX #1: Scale from 150 DPI image pixels to 72 DPI PDF points
            var x = xRaw * SCALE_FACTOR;
            var yTopLeft = yRaw * SCALE_FACTOR;
            var width = widthRaw * SCALE_FACTOR;
            var height = heightRaw * SCALE_FACTOR;

            // CRITICAL FIX #2: Convert from top-left origin to PDF bottom-left origin
            // Formula: y_pdf = pageHeight - y_topLeft - height
            var yPdf = PAGE_HEIGHT_PDF - yTopLeft - height;

            return new Rect(x, yPdf, width, height);
        }

        return new Rect(0, 0, 100, 20); // Default bounds
    }

    private HeadingBlock? ParseHeading(JsonElement blockElement, Rect bounds)
    {
        var level = blockElement.TryGetProperty("level", out var levelElem)
            ? levelElem.GetInt32()
            : 1;
        var text = blockElement.TryGetProperty("text", out var textElem)
            ? textElem.GetString() ?? ""
            : "";

        // Clamp level to 1-6
        level = Math.Max(1, Math.Min(6, level));

        return new HeadingBlock(bounds, level, text);
    }

    private ParagraphBlock? ParseParagraph(JsonElement blockElement, Rect bounds)
    {
        var text = blockElement.TryGetProperty("text", out var textElem)
            ? textElem.GetString() ?? ""
            : "";

        return new ParagraphBlock(bounds, text);
    }

    private TableBlock? ParseTable(JsonElement blockElement, Rect bounds)
    {
        if (!blockElement.TryGetProperty("rows", out var rowsElement))
            return null;

        var rows = new List<TableRow>();

        foreach (var rowElement in rowsElement.EnumerateArray())
        {
            if (rowElement.TryGetProperty("cells", out var cellsElement))
            {
                var cells = new List<TableCell>();

                foreach (var cellElement in cellsElement.EnumerateArray())
                {
                    var rowIndex = cellElement.TryGetProperty("rowIndex", out var rElem)
                        ? rElem.GetInt32()
                        : 0;
                    var colIndex = cellElement.TryGetProperty("columnIndex", out var cElem)
                        ? cElem.GetInt32()
                        : 0;
                    var isHeader = cellElement.TryGetProperty("isHeader", out var hElem)
                        && hElem.GetBoolean();
                    var text = cellElement.TryGetProperty("text", out var tElem)
                        ? tElem.GetString() ?? ""
                        : "";

                    cells.Add(new TableCell(rowIndex, colIndex, isHeader, text));
                }

                rows.Add(new TableRow(cells));
            }
        }

        return rows.Count > 0 ? new TableBlock(bounds, rows) : null;
    }

    private FigureBlock? ParseFigure(JsonElement blockElement, Rect bounds)
    {
        var altText = blockElement.TryGetProperty("altTextSuggestion", out var altElem)
            ? altElem.GetString()
            : null;
        var isDecorative = blockElement.TryGetProperty("isLikelyDecorative", out var decElem)
            && decElem.GetBoolean();

        // Extract page index from context (default to 0 if not available)
        var pageIndex = blockElement.TryGetProperty("pageIndex", out var pageIdxElem)
            ? pageIdxElem.GetInt32()
            : 0;

        // Generate a source image ID (can be enhanced later)
        var sourceImageId = blockElement.TryGetProperty("sourceImageId", out var srcIdElem)
            ? srcIdElem.GetString()
            : null;

        return new FigureBlock(bounds, pageIndex, altText, isDecorative, sourceImageId);
    }

    private int GetPageCount(byte[] pdfBytes)
    {
        try
        {
            return PDFtoImage.Conversion.GetPageCount(pdfBytes);
        }
        catch
        {
            return 1; // Default to 1 page on error
        }
    }

    private string CountBlocksByType(LogicalPage page)
    {
        var headings = page.Blocks.OfType<HeadingBlock>().Count();
        var paragraphs = page.Blocks.OfType<ParagraphBlock>().Count();
        var tables = page.Blocks.OfType<TableBlock>().Count();
        var figures = page.Blocks.OfType<FigureBlock>().Count();

        var parts = new List<string>();
        if (headings > 0) parts.Add($"{headings} heading(s)");
        if (paragraphs > 0) parts.Add($"{paragraphs} paragraph(s)");
        if (tables > 0) parts.Add($"{tables} table(s)");
        if (figures > 0) parts.Add($"{figures} figure(s)");

        return parts.Count > 0 ? string.Join(", ", parts) : "no blocks";
    }
}
