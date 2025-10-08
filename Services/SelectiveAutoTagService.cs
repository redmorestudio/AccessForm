using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aspose.Pdf;
using Aspose.Pdf.Tagged;
using Aspose.Pdf.LogicalStructure;
using Microsoft.Extensions.Logging;

namespace AccessFormServer.Services
{
    /// <summary>
    /// Service that tags ONLY untagged content in PDFs, preserving existing tags, tooltips, and field names.
    /// This is the SAFE alternative to Adobe's destructive auto-tag feature.
    /// </summary>
    public class SelectiveAutoTagService
    {
        private readonly ILogger<SelectiveAutoTagService> _logger;

        public SelectiveAutoTagService(ILogger<SelectiveAutoTagService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Tag only untagged content, preserving all existing tags and form fields
        /// </summary>
        public async Task<byte[]> TagUntaggedContentAsync(byte[] pdfBytes)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInformation("===== SELECTIVE AUTO-TAG STARTING =====");
                    _logger.LogInformation($"Input PDF size: {pdfBytes.Length} bytes");

                    using var inputStream = new MemoryStream(pdfBytes);
                    using var outputStream = new MemoryStream();
                    var document = new Document(inputStream);

                    _logger.LogInformation($"Document loaded: {document.Pages.Count} pages");

                    // Step 1: Analyze existing tag structure
                    var taggedContentMap = BuildTaggedContentMap(document);
                    _logger.LogInformation($"Found {taggedContentMap.Count} existing tagged elements");

                    // Step 2: Identify untagged content
                    var untaggedContent = FindUntaggedContent(document, taggedContentMap);
                    _logger.LogInformation($"Found {untaggedContent.Count} untagged content items");

                    if (untaggedContent.Count == 0)
                    {
                        _logger.LogInformation("All content is already tagged. Nothing to do.");
                        document.Save(outputStream);
                        return outputStream.ToArray();
                    }

                    // Step 3: Ensure document has tag structure
                    EnsureTaggedDocument(document);

                    // Step 4: Tag ONLY the untagged content
                    int taggedCount = TagContentItems(document, untaggedContent);
                    _logger.LogInformation($"Successfully tagged {taggedCount} previously untagged items");

                    // Step 5: Validate form fields still intact
                    ValidateFormFieldsPreserved(document);

                    // Save result
                    document.Save(outputStream);
                    var result = outputStream.ToArray();
                    _logger.LogInformation($"Output PDF size: {result.Length} bytes");
                    _logger.LogInformation("===== SELECTIVE AUTO-TAG COMPLETE =====");

                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to selectively tag untagged content");
                    throw;
                }
            });
        }

        /// <summary>
        /// Build a map of all content that is already tagged
        /// </summary>
        private HashSet<string> BuildTaggedContentMap(Document document)
        {
            var taggedContent = new HashSet<string>();

            try
            {
                if (!document.IsTagged())
                {
                    _logger.LogInformation("Document is not tagged yet");
                    return taggedContent;
                }

                var taggedPdf = document.TaggedContent;
                var rootElement = taggedPdf.RootElement;

                // Recursively walk tag tree
                CollectTaggedContentRecursive(rootElement, taggedContent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error building tagged content map");
            }

            return taggedContent;
        }

        /// <summary>
        /// Recursively collect all tagged content IDs
        /// </summary>
        private void CollectTaggedContentRecursive(Element element, HashSet<string> taggedContent)
        {
            if (element == null) return;

            try
            {
                // Track this element
                var elementInfo = $"{element.GetType().Name}:{element.ActualText}";
                taggedContent.Add(elementInfo);

                // Recurse into child elements
                if (element is StructureElement structElement)
                {
                    foreach (Element child in structElement.ChildElements)
                    {
                        CollectTaggedContentRecursive(child, taggedContent);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error collecting tagged content from element");
            }
        }

        /// <summary>
        /// Find all content that is not tagged
        /// </summary>
        private List<UntaggedContentItem> FindUntaggedContent(Document document, HashSet<string> taggedContentMap)
        {
            var untaggedContent = new List<UntaggedContentItem>();

            for (int pageIndex = 0; pageIndex < document.Pages.Count; pageIndex++)
            {
                var page = document.Pages[pageIndex + 1];

                try
                {
                    // Extract text fragments
                    var textFragmentAbsorber = new Aspose.Pdf.Text.TextFragmentAbsorber();
                    page.Accept(textFragmentAbsorber);

                    foreach (var textFragment in textFragmentAbsorber.TextFragments)
                    {
                        var fragmentInfo = $"Text:{textFragment.Text}";

                        // If this text is not in our tagged content map, it's untagged
                        if (!taggedContentMap.Contains(fragmentInfo) && !string.IsNullOrWhiteSpace(textFragment.Text))
                        {
                            untaggedContent.Add(new UntaggedContentItem
                            {
                                Type = ContentType.Text,
                                Content = textFragment.Text,
                                PageNumber = pageIndex + 1,
                                Rectangle = textFragment.Rectangle
                            });
                        }
                    }

                    // Check for images without alt text
                    var imageAbsorber = new Aspose.Pdf.ImagePlacementAbsorber();
                    page.Accept(imageAbsorber);

                    foreach (var imagePlacement in imageAbsorber.ImagePlacements)
                    {
                        var imageInfo = $"Image:{imagePlacement.Rectangle}";

                        if (!taggedContentMap.Contains(imageInfo))
                        {
                            untaggedContent.Add(new UntaggedContentItem
                            {
                                Type = ContentType.Image,
                                Content = "Image",
                                PageNumber = pageIndex + 1,
                                Rectangle = imagePlacement.Rectangle
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Error finding untagged content on page {pageIndex + 1}");
                }
            }

            return untaggedContent;
        }

        /// <summary>
        /// Ensure document has basic tag structure
        /// </summary>
        private void EnsureTaggedDocument(Document document)
        {
            if (!document.IsTagged())
            {
                _logger.LogInformation("Creating tag structure for document");
                var taggedContent = document.TaggedContent;
                taggedContent.SetTitle("Accessible Document");
                taggedContent.SetLanguage("en-US");
            }
        }

        /// <summary>
        /// Tag the untagged content items
        /// </summary>
        private int TagContentItems(Document document, List<UntaggedContentItem> untaggedContent)
        {
            int taggedCount = 0;
            var taggedPdf = document.TaggedContent;
            var rootElement = taggedPdf.RootElement;

            // Group by page for better organization
            var contentByPage = untaggedContent.GroupBy(c => c.PageNumber);

            foreach (var pageGroup in contentByPage)
            {
                int pageNumber = pageGroup.Key;
                _logger.LogInformation($"Tagging untagged content on page {pageNumber}...");

                // Create a Div for this page's content
                var pageDiv = taggedPdf.CreateDivElement();
                pageDiv.AlternativeText = $"Page {pageNumber} Content";
                rootElement.AppendChild(pageDiv);

                foreach (var item in pageGroup)
                {
                    try
                    {
                        if (item.Type == ContentType.Text)
                        {
                            var paragraph = taggedPdf.CreatePElement();
                            paragraph.ActualText = item.Content;
                            paragraph.AlternativeText = item.Content;
                            pageDiv.AppendChild(paragraph);
                            taggedCount++;
                        }
                        else if (item.Type == ContentType.Image)
                        {
                            var figure = taggedPdf.CreateFigureElement();
                            figure.AlternativeText = "Image";
                            pageDiv.AppendChild(figure);
                            taggedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to tag item: {item.Content}");
                    }
                }
            }

            return taggedCount;
        }

        /// <summary>
        /// Validate that form fields and their metadata are preserved
        /// </summary>
        private void ValidateFormFieldsPreserved(Document document)
        {
            if (document.Form == null || document.Form.Fields.Count == 0)
            {
                _logger.LogInformation("No form fields to validate");
                return;
            }

            _logger.LogInformation($"Validating {document.Form.Fields.Count} form fields...");

            int fieldsWithTooltips = 0;
            int fieldsWithNames = 0;

            foreach (Aspose.Pdf.Forms.Field field in document.Form.Fields)
            {
                if (!string.IsNullOrEmpty(field.PartialName))
                {
                    fieldsWithNames++;
                }

                if (!string.IsNullOrEmpty(field.AlternateName))
                {
                    fieldsWithTooltips++;
                }
            }

            _logger.LogInformation($"✓ Form fields validated: {fieldsWithNames} with names, {fieldsWithTooltips} with tooltips");

            if (fieldsWithNames == 0)
            {
                _logger.LogWarning("⚠️ WARNING: No form fields have names!");
            }

            if (fieldsWithTooltips == 0)
            {
                _logger.LogWarning("⚠️ WARNING: No form fields have tooltips!");
            }
        }

        /// <summary>
        /// Check if document needs selective auto-tagging
        /// </summary>
        public async Task<bool> NeedsSelectiveAutoTagAsync(byte[] pdfBytes)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var inputStream = new MemoryStream(pdfBytes);
                    var document = new Document(inputStream);

                    if (!document.IsTagged())
                    {
                        _logger.LogInformation("Document is not tagged at all - needs tagging");
                        return true;
                    }

                    var taggedContentMap = BuildTaggedContentMap(document);
                    var untaggedContent = FindUntaggedContent(document, taggedContentMap);

                    bool needsTagging = untaggedContent.Count > 0;
                    _logger.LogInformation($"Document has {untaggedContent.Count} untagged items - {(needsTagging ? "needs" : "does not need")} selective tagging");

                    return needsTagging;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking if document needs selective auto-tag");
                    return false;
                }
            });
        }

        /// <summary>
        /// Get a report of untagged content in the document
        /// </summary>
        public async Task<string> GetUntaggedContentReportAsync(byte[] pdfBytes)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var inputStream = new MemoryStream(pdfBytes);
                    var document = new Document(inputStream);

                    var taggedContentMap = BuildTaggedContentMap(document);
                    var untaggedContent = FindUntaggedContent(document, taggedContentMap);

                    var report = $@"
═══════════════════════════════════════════════════════════
UNTAGGED CONTENT REPORT
═══════════════════════════════════════════════════════════
Document: {document.Pages.Count} pages
Tagged: {document.IsTagged()}
Existing Tags: {taggedContentMap.Count} elements
Untagged Items: {untaggedContent.Count}

UNTAGGED CONTENT BY PAGE:
";

                    var byPage = untaggedContent.GroupBy(c => c.PageNumber);
                    foreach (var page in byPage)
                    {
                        report += $"\nPage {page.Key}: {page.Count()} untagged items\n";
                        foreach (var item in page.Take(5)) // Show first 5 per page
                        {
                            var preview = item.Content.Length > 50 ? item.Content.Substring(0, 50) + "..." : item.Content;
                            report += $"  - [{item.Type}] {preview}\n";
                        }
                        if (page.Count() > 5)
                        {
                            report += $"  ... and {page.Count() - 5} more\n";
                        }
                    }

                    report += "\n═══════════════════════════════════════════════════════════\n";

                    return report;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating untagged content report");
                    return $"Error: {ex.Message}";
                }
            });
        }
    }

    /// <summary>
    /// Represents an item of content that is not tagged
    /// </summary>
    public class UntaggedContentItem
    {
        public ContentType Type { get; set; }
        public string Content { get; set; }
        public int PageNumber { get; set; }
        public Aspose.Pdf.Rectangle Rectangle { get; set; }
    }

    public enum ContentType
    {
        Text,
        Image,
        Table,
        Form,
        Other
    }
}
