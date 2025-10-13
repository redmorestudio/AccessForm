using Aspose.Pdf;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tagged;
using Aspose.Pdf.Text;

namespace AccessFormServer.Services;

/// <summary>
/// Service for fixing link annotation accessibility issues in PDFs
/// Addresses PAC errors: "Link annotation is not nested inside a Link structure element"
/// and "Alternative description missing for an annotation"
/// </summary>
public class LinkAnnotationAccessibilityService
{
    private readonly ILogger<LinkAnnotationAccessibilityService> _logger;

    public LinkAnnotationAccessibilityService(ILogger<LinkAnnotationAccessibilityService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Fix link annotation accessibility issues
    /// </summary>
    public async Task<LinkFixResult> FixLinkAnnotationsAsync(byte[] pdfBytes)
    {
        var result = new LinkFixResult();

        try
        {
            _logger.LogInformation("===== LINK ANNOTATION ACCESSIBILITY FIX =====");

            using var ms = new MemoryStream(pdfBytes);
            using var document = new Document(ms);

            var taggedContent = document.TaggedContent;
            if (taggedContent == null || taggedContent.RootElement == null)
            {
                _logger.LogWarning("Document is not tagged - cannot fix link annotations");
                result.Success = false;
                result.ErrorMessage = "Document is not tagged";
                result.FixedPdf = pdfBytes;
                return result;
            }

            // Set required tagged PDF properties
            taggedContent.SetTitle(document.Info.Title ?? "Document");
            taggedContent.SetLanguage("en-US");

            int linksFixed = 0;
            int altTextAdded = 0;

            // Process each page
            for (int pageNum = 1; pageNum <= document.Pages.Count; pageNum++)
            {
                var page = document.Pages[pageNum];

                // Find all link annotations on this page
                var linkAnnotations = page.Annotations
                    .OfType<LinkAnnotation>()
                    .ToList();

                if (linkAnnotations.Any())
                {
                    _logger.LogInformation($"Found {linkAnnotations.Count} link annotations on page {pageNum}");
                }

                foreach (var linkAnnotation in linkAnnotations)
                {
                    try
                    {
                        // Add alternative text if missing
                        if (string.IsNullOrEmpty(linkAnnotation.Contents))
                        {
                            var altText = GenerateAltTextForLink(linkAnnotation, pageNum, document);
                            linkAnnotation.Contents = altText;
                            altTextAdded++;
                            _logger.LogInformation($"  Added alt text: \"{altText}\"");
                        }

                        // Create Link structure element for this annotation
                        var linkStructure = CreateLinkStructureElement(
                            taggedContent,
                            linkAnnotation,
                            page,
                            pageNum);

                        if (linkStructure != null)
                        {
                            linksFixed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Failed to fix link annotation on page {pageNum}: {ex.Message}");
                        result.Warnings.Add($"Page {pageNum}: {ex.Message}");
                    }
                }
            }

            result.LinksFixed = linksFixed;
            result.AltTextAdded = altTextAdded;

            // Save the fixed document
            using var outputMs = new MemoryStream();
            document.Save(outputMs);
            result.FixedPdf = outputMs.ToArray();
            result.Success = true;

            _logger.LogInformation($"===== FIX SUMMARY =====");
            _logger.LogInformation($"Links with structure created: {linksFixed}");
            _logger.LogInformation($"Links with alt text added: {altTextAdded}");

            if (result.Warnings.Any())
            {
                _logger.LogWarning($"Warnings: {result.Warnings.Count}");
                foreach (var warning in result.Warnings)
                {
                    _logger.LogWarning($"  - {warning}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error fixing link annotations: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.FixedPdf = pdfBytes; // Return original on error
        }

        return result;
    }

    /// <summary>
    /// Create a Link structure element for a link annotation
    /// </summary>
    private LinkElement? CreateLinkStructureElement(
        ITaggedContent taggedContent,
        LinkAnnotation annotation,
        Page page,
        int pageNum)
    {
        try
        {
            // Create a Link structure element
            var linkElement = taggedContent.CreateLinkElement();

            // Set alternative text on the structure element
            linkElement.AlternativeText = annotation.Contents ?? $"Link on page {pageNum}";

            // Try to find appropriate parent element (usually a paragraph or list item)
            var parentElement = FindAppropriateParent(taggedContent.RootElement, page, annotation.Rect);

            if (parentElement != null)
            {
                // Create a span element to hold the link text
                var spanElement = taggedContent.CreateSpanElement();
                spanElement.SetText(ExtractTextFromAnnotation(annotation, page) ?? "Link");

                // Add span to link
                linkElement.AppendChild(spanElement);

                // Add link to parent
                parentElement.AppendChild(linkElement);
            }
            else
            {
                // If we can't find a good parent, add to root
                taggedContent.RootElement.AppendChild(linkElement);
            }

            // Note: OBJR (object reference) creation is handled automatically by Aspose
            // when structure elements are properly connected

            _logger.LogInformation($"    Created Link structure element for annotation at ({annotation.Rect.LLX}, {annotation.Rect.LLY})");

            return linkElement;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to create link structure: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Generate descriptive alternative text for a link
    /// </summary>
    private string GenerateAltTextForLink(LinkAnnotation annotation, int pageNum, Document document)
    {
        try
        {
            var action = annotation.Action;

            if (action is GoToAction gotoAction)
            {
                // Internal link to another page
                var destination = gotoAction.Destination;
                if (destination is ExplicitDestination explicitDest)
                {
                    var targetPageNum = document.Pages.IndexOf(explicitDest.Page) + 1;
                    return $"Link to page {targetPageNum}";
                }
                return "Internal link";
            }
            else if (action is GoToURIAction uriAction)
            {
                // External URL
                var uri = uriAction.URI;
                return $"External link to {uri}";
            }
            else
            {
                // Generic link
                return $"Link on page {pageNum}";
            }
        }
        catch
        {
            return $"Link on page {pageNum}";
        }
    }

    /// <summary>
    /// Find appropriate parent structure element for a link based on position
    /// </summary>
    private Element? FindAppropriateParent(Element rootElement, Page page, Rectangle linkRect)
    {
        // Try to find a paragraph or list item that contains this link's position
        // This is a simplified approach - a more sophisticated algorithm would
        // analyze the structure tree more carefully

        var candidates = new List<Element>();
        FindCandidateParents(rootElement, candidates, new[] {
            typeof(ParagraphElement),
            typeof(SpanElement)
        });

        // For now, just return the first suitable parent
        // A more sophisticated approach would check coordinates
        return candidates.FirstOrDefault() ?? rootElement;
    }

    /// <summary>
    /// Recursively find candidate parent elements
    /// </summary>
    private void FindCandidateParents(Element element, List<Element> candidates, Type[] targetTypes)
    {
        if (element == null) return;

        var elementType = element.GetType();
        if (targetTypes.Any(t => t.IsAssignableFrom(elementType)))
        {
            candidates.Add(element);
        }

        foreach (var child in element.ChildElements)
        {
            FindCandidateParents(child, candidates, targetTypes);
        }
    }

    /// <summary>
    /// Extract text content from the area covered by a link annotation
    /// </summary>
    private string? ExtractTextFromAnnotation(LinkAnnotation annotation, Page page)
    {
        try
        {
            // Use text absorber to extract text from the link's rectangle
            var absorber = new TextFragmentAbsorber();
            page.Accept(absorber);

            // Filter fragments that overlap with the annotation rectangle
            var relevantFragments = absorber.TextFragments
                .Where(f => RectanglesOverlap(f.Rectangle, annotation.Rect))
                .Select(f => f.Text);

            var text = string.Join(" ", relevantFragments);
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Check if two rectangles overlap
    /// </summary>
    private bool RectanglesOverlap(Rectangle rect1, Rectangle rect2)
    {
        return !(rect1.URX < rect2.LLX ||
                 rect1.LLX > rect2.URX ||
                 rect1.URY < rect2.LLY ||
                 rect1.LLY > rect2.URY);
    }
}

/// <summary>
/// Result of link annotation fix operation
/// </summary>
public class LinkFixResult
{
    public bool Success { get; set; }
    public byte[] FixedPdf { get; set; } = Array.Empty<byte>();
    public int LinksFixed { get; set; }
    public int AltTextAdded { get; set; }
    public List<string> Warnings { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
