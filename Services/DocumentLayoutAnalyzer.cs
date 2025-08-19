using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using Syncfusion.Drawing;

namespace AccessFormServer.Services
{
    /// <summary>
    /// Document Layout Analyzer (DLA) implementing intelligent document structure analysis
    /// Based on Section 2.3 specifications for structural tagging and reading order
    /// </summary>
    public class DocumentLayoutAnalyzer
    {
        private readonly ILogger<DocumentLayoutAnalyzer> _logger;
        private readonly AzureFormRecognizerService _formRecognizer;

        // Layout analysis parameters
        private const float COLUMN_THRESHOLD = 50f; // Minimum gap between columns
        private const float LINE_HEIGHT_VARIANCE = 0.2f; // Allowed variance in line height
        private const float HEADING_SIZE_RATIO = 1.2f; // Minimum size ratio for headings

        public DocumentLayoutAnalyzer(
            ILogger<DocumentLayoutAnalyzer> logger,
            AzureFormRecognizerService formRecognizer = null)
        {
            _logger = logger;
            _formRecognizer = formRecognizer;
        }

        /// <summary>
        /// Analyze document layout to identify logical structure and roles
        /// </summary>
        public async Task<DocumentLayout> AnalyzeDocumentLayout(PdfLoadedDocument document)
        {
            _logger.LogInformation("Starting document layout analysis");
            var layout = new DocumentLayout();

            try
            {
                // Process each page
                for (int pageIndex = 0; pageIndex < document.Pages.Count; pageIndex++)
                {
                    var page = document.Pages[pageIndex] as PdfLoadedPage;
                    if (page == null) continue;

                    var pageLayout = await AnalyzePageLayoutAsync(page, pageIndex + 1);
                    layout.Pages.Add(pageLayout);
                    layout.Elements.AddRange(pageLayout.Elements);
                }

                // Detect document-wide patterns
                DetectDocumentStructure(layout);

                // Identify reading zones and columns
                IdentifyReadingZones(layout);

                // Determine logical reading order
                DetermineLogicalReadingOrder(layout);

                // Classify element roles
                ClassifyElementRoles(layout);

                layout.Success = true;
                _logger.LogInformation($"Layout analysis complete: {layout.Elements.Count} elements identified");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during layout analysis");
                layout.Success = false;
                layout.ErrorMessage = ex.Message;
            }

            return layout;
        }

        /// <summary>
        /// Analyze individual page layout
        /// </summary>
        private async Task<PageLayout> AnalyzePageLayoutAsync(PdfLoadedPage page, int pageNumber)
        {
            var pageLayout = new PageLayout
            {
                PageNumber = pageNumber,
                Width = page.Size.Width,
                Height = page.Size.Height
            };

            // Extract text with positioning
            var textElements = ExtractTextElements(page);
            pageLayout.TextElements.AddRange(textElements);

            // Group text into logical blocks
            var textBlocks = GroupTextIntoBlocks(textElements);
            
            foreach (var block in textBlocks)
            {
                var element = new LayoutElement
                {
                    Type = "TextBlock",
                    Content = block.Text,
                    PageNumber = pageNumber,
                    Bounds = block.Bounds,
                    FontSize = block.AverageFontSize,
                    FontWeight = block.IsBold ? "bold" : "normal"
                };

                pageLayout.Elements.Add(element);
            }

            // Detect tables
            var tables = DetectTables(page);
            foreach (var table in tables)
            {
                pageLayout.Elements.Add(new LayoutElement
                {
                    Type = "Table",
                    PageNumber = pageNumber,
                    Bounds = table.Bounds,
                    TableInfo = table
                });
            }

            // Detect images and figures
            var images = DetectImages(page);
            foreach (var image in images)
            {
                pageLayout.Elements.Add(new LayoutElement
                {
                    Type = "Image",
                    PageNumber = pageNumber,
                    Bounds = image.Bounds
                });
            }

            // Form fields are accessed at document level, not page level
            // Skip for now as we handle them separately

            return pageLayout;
        }

        /// <summary>
        /// Extract text elements with positioning information
        /// </summary>
        private List<TextElement> ExtractTextElements(PdfLoadedPage page)
        {
            var textElements = new List<TextElement>();

            try
            {
                // Extract text using Syncfusion's text extraction
                var text = page.ExtractText(true);
                
                // For now, create simplified text elements
                // In production, use detailed text extraction with bounds
                if (!string.IsNullOrEmpty(text))
                {
                    var lines = text.Split('\n');
                    float y = 50;
                    
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            textElements.Add(new TextElement
                            {
                                Text = line.Trim(),
                                X = 50,
                                Y = y,
                                Width = 500,
                                Height = 12,
                                FontSize = 11
                            });
                            y += 15;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract text elements");
            }

            return textElements;
        }

        /// <summary>
        /// Group individual text elements into logical blocks
        /// </summary>
        private List<TextBlock> GroupTextIntoBlocks(List<TextElement> elements)
        {
            var blocks = new List<TextBlock>();
            
            if (!elements.Any())
                return blocks;

            // Sort by Y position, then X
            var sorted = elements.OrderBy(e => e.Y).ThenBy(e => e.X).ToList();

            TextBlock currentBlock = null;
            float lastY = 0;
            float lineThreshold = 5f; // Threshold for same line

            foreach (var element in sorted)
            {
                // Check if this is part of the same line
                if (currentBlock != null && Math.Abs(element.Y - lastY) < lineThreshold)
                {
                    // Same line, add to current block
                    currentBlock.Elements.Add(element);
                    currentBlock.Text += " " + element.Text;
                }
                else
                {
                    // New line/block
                    if (currentBlock != null)
                    {
                        blocks.Add(currentBlock);
                    }

                    currentBlock = new TextBlock
                    {
                        Elements = new List<TextElement> { element },
                        Text = element.Text
                    };
                }

                lastY = element.Y;
            }

            // Add the last block
            if (currentBlock != null)
            {
                blocks.Add(currentBlock);
            }

            // Calculate bounds for each block
            foreach (var block in blocks)
            {
                block.CalculateBounds();
            }

            return blocks;
        }

        /// <summary>
        /// Detect tables in the page
        /// </summary>
        private List<TableStructure> DetectTables(PdfLoadedPage page)
        {
            var tables = new List<TableStructure>();

            // Simplified table detection
            // In production, use more sophisticated algorithms or Azure Form Recognizer
            // For now, return empty list
            
            return tables;
        }

        /// <summary>
        /// Detect images and figures in the page
        /// </summary>
        private List<ImageInfo> DetectImages(PdfLoadedPage page)
        {
            var images = new List<ImageInfo>();

            // Extract images from PDF
            // Syncfusion supports image extraction
            // For now, return empty list
            
            return images;
        }

        /// <summary>
        /// Detect document-wide structural patterns
        /// </summary>
        private void DetectDocumentStructure(DocumentLayout layout)
        {
            // Analyze font sizes to identify heading levels
            var fontSizes = layout.Elements
                .Where(e => e.Type == "TextBlock" && e.FontSize > 0)
                .Select(e => e.FontSize)
                .Distinct()
                .OrderByDescending(s => s)
                .ToList();

            if (fontSizes.Count > 1)
            {
                layout.HeadingFontSizes = new Dictionary<int, float>();
                for (int i = 0; i < Math.Min(3, fontSizes.Count); i++)
                {
                    layout.HeadingFontSizes[i + 1] = fontSizes[i];
                }
            }

            // Detect consistent margins
            var leftMargins = layout.Elements
                .Where(e => e.Type == "TextBlock")
                .Select(e => e.Bounds.X)
                .GroupBy(x => Math.Round(x / 10) * 10) // Group by 10-pixel ranges
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            if (leftMargins != null)
            {
                layout.StandardLeftMargin = (float)leftMargins.Key;
            }
        }

        /// <summary>
        /// Identify reading zones and columns
        /// </summary>
        private void IdentifyReadingZones(DocumentLayout layout)
        {
            foreach (var page in layout.Pages)
            {
                var zones = new List<ReadingZone>();
                var textElements = page.Elements.Where(e => e.Type == "TextBlock").ToList();

                if (!textElements.Any())
                    continue;

                // Group elements by vertical position to detect columns
                var columnGroups = new Dictionary<float, List<LayoutElement>>();

                foreach (var element in textElements)
                {
                    var columnKey = Math.Round(element.Bounds.X / COLUMN_THRESHOLD) * COLUMN_THRESHOLD;
                    
                    if (!columnGroups.ContainsKey((float)columnKey))
                        columnGroups[(float)columnKey] = new List<LayoutElement>();
                    
                    columnGroups[(float)columnKey].Add(element);
                }

                // Create reading zones from column groups
                int zoneIndex = 0;
                foreach (var column in columnGroups.OrderBy(c => c.Key))
                {
                    var zone = new ReadingZone
                    {
                        Index = zoneIndex++,
                        Elements = column.Value,
                        Type = columnGroups.Count > 1 ? "Column" : "Main"
                    };

                    zone.CalculateBounds();
                    zones.Add(zone);
                }

                page.ReadingZones = zones;
            }
        }

        /// <summary>
        /// Determine logical reading order across all elements
        /// </summary>
        private void DetermineLogicalReadingOrder(DocumentLayout layout)
        {
            int globalOrder = 0;

            foreach (var page in layout.Pages.OrderBy(p => p.PageNumber))
            {
                // Process each reading zone in order
                if (page.ReadingZones.Any())
                {
                    foreach (var zone in page.ReadingZones.OrderBy(z => z.Index))
                    {
                        // Order elements within zone top-to-bottom
                        foreach (var element in zone.Elements.OrderBy(e => e.Bounds.Y))
                        {
                            element.ReadingOrder = globalOrder++;
                        }
                    }
                }
                else
                {
                    // No zones, use simple top-to-bottom, left-to-right ordering
                    foreach (var element in page.Elements
                        .OrderBy(e => e.Bounds.Y)
                        .ThenBy(e => e.Bounds.X))
                    {
                        element.ReadingOrder = globalOrder++;
                    }
                }
            }
        }

        /// <summary>
        /// Classify element roles based on visual and contextual cues
        /// </summary>
        private void ClassifyElementRoles(DocumentLayout layout)
        {
            foreach (var element in layout.Elements.Where(e => e.Type == "TextBlock"))
            {
                // Classify based on font size
                if (layout.HeadingFontSizes != null && layout.HeadingFontSizes.Any())
                {
                    foreach (var heading in layout.HeadingFontSizes)
                    {
                        if (Math.Abs(element.FontSize - heading.Value) < 1f)
                        {
                            element.Role = $"heading{heading.Key}";
                            element.Confidence = 0.8f;
                            break;
                        }
                    }
                }

                // If not classified as heading, check other patterns
                if (string.IsNullOrEmpty(element.Role))
                {
                    // Check for list items
                    if (IsListItem(element.Content))
                    {
                        element.Role = "list_item";
                        element.Confidence = 0.7f;
                    }
                    // Check for captions
                    else if (IsCaption(element))
                    {
                        element.Role = "caption";
                        element.Confidence = 0.6f;
                    }
                    // Default to paragraph
                    else
                    {
                        element.Role = "paragraph";
                        element.Confidence = 0.5f;
                    }
                }
            }

            // Classify tables
            foreach (var element in layout.Elements.Where(e => e.Type == "Table"))
            {
                element.Role = "table";
                element.Confidence = 0.9f;
            }

            // Classify images
            foreach (var element in layout.Elements.Where(e => e.Type == "Image"))
            {
                element.Role = "figure";
                element.Confidence = 0.9f;
            }
        }

        private bool IsListItem(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            // Check for common list markers
            var listPatterns = new[]
            {
                @"^\d+\.",      // Numbered list (1., 2., etc.)
                @"^[a-z]\.",    // Lettered list (a., b., etc.)
                @"^[A-Z]\.",    // Lettered list (A., B., etc.)
                @"^[•·▪▫◦‣⁃]", // Bullet characters
                @"^[-*+]",      // Common markdown bullets
                @"^\([a-z]\)",  // (a), (b), etc.
                @"^\([A-Z]\)",  // (A), (B), etc.
                @"^\(\d+\)"     // (1), (2), etc.
            };

            return listPatterns.Any(pattern => 
                System.Text.RegularExpressions.Regex.IsMatch(text.Trim(), pattern));
        }

        private bool IsCaption(LayoutElement element)
        {
            if (element == null || string.IsNullOrEmpty(element.Content))
                return false;

            var content = element.Content.ToLower();
            
            // Check for common caption indicators
            return content.StartsWith("figure") ||
                   content.StartsWith("table") ||
                   content.StartsWith("exhibit") ||
                   content.StartsWith("diagram") ||
                   content.StartsWith("chart");
        }
    }

    // Supporting classes for layout analysis

    public class DocumentLayout
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public List<PageLayout> Pages { get; set; } = new();
        public List<LayoutElement> Elements { get; set; } = new();
        public Dictionary<int, float> HeadingFontSizes { get; set; }
        public float StandardLeftMargin { get; set; }
    }

    public class PageLayout
    {
        public int PageNumber { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public List<LayoutElement> Elements { get; set; } = new();
        public List<TextElement> TextElements { get; set; } = new();
        public List<ReadingZone> ReadingZones { get; set; } = new();
    }

    public class LayoutElement
    {
        public string Type { get; set; }
        public string Content { get; set; }
        public string Role { get; set; }
        public int PageNumber { get; set; }
        public ElementBounds Bounds { get; set; }
        public float FontSize { get; set; }
        public string FontWeight { get; set; }
        public int ReadingOrder { get; set; }
        public float Confidence { get; set; }
        public TableStructure TableInfo { get; set; }
    }

    public class ElementBounds
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
    }

    public class TextElement
    {
        public string Text { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float FontSize { get; set; }
        public bool IsBold { get; set; }
    }

    public class TextBlock
    {
        public List<TextElement> Elements { get; set; } = new();
        public string Text { get; set; }
        public ElementBounds Bounds { get; set; }
        public float AverageFontSize => Elements.Any() ? Elements.Average(e => e.FontSize) : 0;
        public bool IsBold => Elements.Any() && Elements.All(e => e.IsBold);

        public void CalculateBounds()
        {
            if (!Elements.Any())
                return;

            Bounds = new ElementBounds
            {
                X = Elements.Min(e => e.X),
                Y = Elements.Min(e => e.Y),
                Width = Elements.Max(e => e.X + e.Width) - Elements.Min(e => e.X),
                Height = Elements.Max(e => e.Y + e.Height) - Elements.Min(e => e.Y)
            };
        }
    }

    public class ReadingZone
    {
        public int Index { get; set; }
        public string Type { get; set; }
        public List<LayoutElement> Elements { get; set; } = new();
        public ElementBounds Bounds { get; set; }

        public void CalculateBounds()
        {
            if (!Elements.Any())
                return;

            Bounds = new ElementBounds
            {
                X = Elements.Min(e => e.Bounds.X),
                Y = Elements.Min(e => e.Bounds.Y),
                Width = Elements.Max(e => e.Bounds.X + e.Bounds.Width) - Elements.Min(e => e.Bounds.X),
                Height = Elements.Max(e => e.Bounds.Y + e.Bounds.Height) - Elements.Min(e => e.Bounds.Y)
            };
        }
    }

    public class TableStructure
    {
        public ElementBounds Bounds { get; set; }
        public int RowCount { get; set; }
        public int ColumnCount { get; set; }
        public List<List<string>> Cells { get; set; } = new();
        public bool HasHeader { get; set; }
    }

    public class ImageInfo
    {
        public ElementBounds Bounds { get; set; }
        public string AltText { get; set; }
        public bool IsDecorative { get; set; }
    }
}
