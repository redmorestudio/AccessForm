using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Action;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Canvas.Parser.Filter;
using iText.Kernel.Pdf.Navigation;
using iText.Kernel.Geom;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.Pdf.Tagutils;
using Microsoft.Extensions.Logging;

namespace AccessFormServer.Services;

/// <summary>
/// Service for recreating Table of Contents with PDF/UA compliant links
/// Fixes PAC errors: "Link annotation is not nested inside a Link structure element"
/// and "Alternative description missing for an annotation"
/// </summary>
public class TocRecreationService
{
    private readonly ILogger<TocRecreationService> _logger;

    public TocRecreationService(ILogger<TocRecreationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Recreate ToC page with PDF/UA compliant links
    /// </summary>
    public async Task<TocRecreationResult> RecreateTableOfContentsAsync(byte[] pdfBytes, int tocPageNumber = 2)
    {
        var result = new TocRecreationResult();

        try
        {
            _logger.LogInformation("===== TOC RECREATION SERVICE =====");
            _logger.LogInformation($"Recreating ToC on page {tocPageNumber}");

            using var inputMs = new MemoryStream(pdfBytes);
            using var outputMs = new MemoryStream();
            using var pdfDoc = new PdfDocument(new PdfReader(inputMs), new PdfWriter(outputMs));

            // Validate page exists
            if (tocPageNumber > pdfDoc.GetNumberOfPages())
            {
                result.Success = false;
                result.ErrorMessage = $"Page {tocPageNumber} does not exist in document";
                result.FixedPdf = pdfBytes;
                return result;
            }

            var tocPage = pdfDoc.GetPage(tocPageNumber);

            // Step 1: Extract ToC entries from existing page
            _logger.LogInformation($"Step 1: Extracting ToC entries from page {tocPageNumber}...");
            var entries = ExtractTocEntries(tocPage, pdfDoc);

            if (!entries.Any())
            {
                _logger.LogWarning("No ToC entries found on page");
                result.Success = false;
                result.ErrorMessage = "No ToC entries found";
                result.FixedPdf = pdfBytes;
                return result;
            }

            _logger.LogInformation($"Found {entries.Count} ToC entries");
            foreach (var entry in entries)
            {
                _logger.LogInformation($"  - {entry.Text} → Page {entry.DestinationPage}");
            }

            // Step 2: Store page dimensions and properties
            var pageSize = tocPage.GetPageSize();
            var pageRotation = tocPage.GetRotation();

            // Step 3: Remove old ToC page
            _logger.LogInformation($"Step 2: Removing old ToC page {tocPageNumber}...");
            pdfDoc.RemovePage(tocPageNumber);

            // Step 4: Create new ToC page with proper structure
            _logger.LogInformation($"Step 3: Creating new PDF/UA compliant ToC page...");
            var newPage = pdfDoc.AddNewPage(tocPageNumber, new iText.Kernel.Geom.PageSize(pageSize));
            newPage.SetRotation(pageRotation);

            // Create document for layout
            using var document = new Document(pdfDoc);

            // Ensure PDF is tagged for accessibility
            pdfDoc.SetTagged();
            pdfDoc.GetCatalog().SetLang(new PdfString("en-US"));
            pdfDoc.GetCatalog().SetViewerPreferences(
                new PdfViewerPreferences().SetDisplayDocTitle(true));

            // Add ToC title
            var title = new Paragraph("Table of Contents")
                .SetFontSize(18)
                .SetBold()
                .SetMarginBottom(20);
            document.Add(title);

            // Step 5: Add ToC entries as proper Link elements
            _logger.LogInformation($"Step 4: Adding {entries.Count} ToC entries as PDF/UA compliant links...");
            int linksAdded = 0;

            foreach (var entry in entries)
            {
                try
                {
                    // Create GoTo action to destination page
                    var action = PdfAction.CreateGoTo(PdfExplicitDestination.CreateFit(
                        pdfDoc.GetPage(entry.DestinationPage)));

                    // Create Link element with proper accessibility
                    var link = new Link(entry.Text, action);

                    // Set alternate description for accessibility (PAC requirement)
                    link.GetAccessibilityProperties()
                        .SetAlternateDescription($"Link to page {entry.DestinationPage}: {entry.Text}");

                    // Remove default link border
                    link.GetLinkAnnotation().SetBorder(new PdfAnnotationBorder(0, 0, 0));

                    // Create paragraph with the link
                    var para = new Paragraph()
                        .Add(link)
                        .Add(new Text($" {'.',-50} {entry.DestinationPage}")
                            .SetFontColor(iText.Kernel.Colors.ColorConstants.GRAY));

                    document.Add(para);
                    linksAdded++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to add ToC entry '{entry.Text}': {ex.Message}");
                    result.Warnings.Add($"Could not add entry: {entry.Text}");
                }
            }

            // Close document and save
            document.Close();
            pdfDoc.Close();

            result.FixedPdf = outputMs.ToArray();
            result.Success = true;
            result.EntriesRecreated = linksAdded;

            _logger.LogInformation($"===== TOC RECREATION COMPLETE =====");
            _logger.LogInformation($"Successfully recreated {linksAdded} ToC entries");

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
            _logger.LogError($"Error recreating ToC: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.FixedPdf = pdfBytes;
        }

        return result;
    }

    /// <summary>
    /// Extract ToC entries from a page by analyzing link annotations and text
    /// </summary>
    private List<TocEntry> ExtractTocEntries(PdfPage page, PdfDocument pdfDoc)
    {
        var entries = new List<TocEntry>();

        try
        {
            // Get all link annotations on the page
            var annotations = page.GetAnnotations();
            var linkAnnotations = annotations
                .Where(a => a is PdfLinkAnnotation)
                .Cast<PdfLinkAnnotation>()
                .OrderBy(a => a.GetRectangle().ToRectangle().GetY()) // Sort by Y position (top to bottom)
                .ToList();

            _logger.LogInformation($"Found {linkAnnotations.Count} link annotations on page");

            // Extract text from page for matching
            var textStrategy = new LocationTextExtractionStrategy();
            PdfTextExtractor.GetTextFromPage(page, textStrategy);

            // Extract ALL text from page first
            string allText = PdfTextExtractor.GetTextFromPage(page);
            var lines = allText.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList();

            _logger.LogInformation($"Extracted {lines.Count} text lines from page");

            int linkIndex = 0;
            foreach (var linkAnnot in linkAnnotations)
            {
                try
                {
                    linkIndex++;
                    _logger.LogInformation($"\n>>> PROCESSING LINK {linkIndex}/{linkAnnotations.Count} <<<");

                    var rect = linkAnnot.GetRectangle().ToRectangle();
                    _logger.LogInformation($"  Rectangle: ({rect.GetX()}, {rect.GetY()}) - ({rect.GetWidth()} x {rect.GetHeight()})");

                    // Get destination page from link action
                    var action = linkAnnot.GetAction();

                    // Debug: Check if action is null and what type it is
                    _logger.LogInformation($"  Action is null: {action == null}");
                    if (action != null)
                    {
                        _logger.LogInformation($"  Action type: {action.GetType().Name}");
                        try
                        {
                            var keys = action.KeySet().Select(k => k.ToString()).ToList();
                            _logger.LogInformation($"  Action keys: {string.Join(", ", keys)}");
                        }
                        catch (Exception keyEx)
                        {
                            _logger.LogWarning($"  Could not get action keys: {keyEx.Message}");
                        }
                    }

                    int destPage = GetDestinationPage(action, pdfDoc);

                    _logger.LogInformation($"  >>> RESULT for link {linkIndex}: Page {destPage} <<<");

                    if (destPage > 0)
                    {
                        // Try to extract text from the link area
                        var filter = new TextRegionEventFilter(rect);
                        var filteredStrategy = new FilteredTextEventListener(
                            new LocationTextExtractionStrategy(), filter);
                        var linkText = PdfTextExtractor.GetTextFromPage(page, filteredStrategy)?.Trim();

                        // Fallback: if no text extracted, use a generic label
                        if (string.IsNullOrWhiteSpace(linkText) && lines.Any())
                        {
                            // Take the first available line as fallback
                            linkText = $"Section {entries.Count + 1}";
                            _logger.LogWarning($"Could not extract text for link, using fallback: {linkText}");
                        }

                        if (!string.IsNullOrWhiteSpace(linkText))
                        {
                            entries.Add(new TocEntry
                            {
                                Text = linkText.Trim(),
                                DestinationPage = destPage
                            });
                            _logger.LogInformation($"  Added entry: '{linkText}' → Page {destPage}");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Could not determine destination page for link at ({rect.GetX()}, {rect.GetY()})");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not extract ToC entry: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error extracting ToC entries: {ex.Message}");
        }

        return entries;
    }

    /// <summary>
    /// Get destination page number from a PDF action
    /// </summary>
    private int GetDestinationPage(PdfDictionary action, PdfDocument pdfDoc)
    {
        _logger.LogInformation("=== GetDestinationPage START ===");

        try
        {
            if (action == null)
            {
                _logger.LogInformation("    Action is NULL - returning -1");
                return -1;
            }

            _logger.LogInformation($"    Action is NOT null, type: {action.GetType().Name}");
            _logger.LogInformation($"    Action.IsIndirect: {action.IsIndirect()}");
            _logger.LogInformation($"    Action.IsDictionary: {action.IsDictionary()}");

            var actionType = action.GetAsName(PdfName.S);
            _logger.LogInformation($"    Action type (/S): {actionType}");

            if (actionType == null)
            {
                _logger.LogInformation("    Action type is NULL - no /S entry");
                _logger.LogInformation($"    All action keys: {string.Join(", ", action.KeySet().Select(k => k.ToString()))}");
            }

            // Handle GoTo actions
            if (PdfName.GoTo.Equals(actionType))
            {
                _logger.LogInformation("    This is a GoTo action");

                var dest = action.Get(PdfName.D);
                _logger.LogInformation($"    Destination (/D) object type: {dest?.GetType().Name ?? "NULL"}");

                if (dest == null)
                {
                    _logger.LogInformation("    Destination is NULL");
                    return -1;
                }

                // Try as array first (explicit destination)
                if (dest is PdfArray destArray)
                {
                    _logger.LogInformation($"    Destination is PdfArray with {destArray.Size()} elements");

                    if (destArray.Size() > 0)
                    {
                        var pageRef = destArray.Get(0);
                        _logger.LogInformation($"    First element type: {pageRef?.GetType().Name ?? "NULL"}");

                        if (pageRef != null)
                        {
                            _logger.LogInformation($"    First element IsIndirectReference: {pageRef is PdfIndirectReference}");
                            _logger.LogInformation($"    First element IsNumber: {pageRef is PdfNumber}");
                        }

                        // Direct page object reference
                        if (pageRef is PdfIndirectReference indirectRef)
                        {
                            _logger.LogInformation($"    Processing indirect reference: {indirectRef}");

                            // Find the page by indirect reference
                            for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                            {
                                var pageObj = pdfDoc.GetPage(i).GetPdfObject();
                                var pageIndirectRef = pageObj?.GetIndirectReference();

                                if (i == 1) // Log details for first page
                                {
                                    _logger.LogInformation($"    Page {i}: pageObj={pageObj?.GetType().Name}, indirectRef={pageIndirectRef}");
                                }

                                if (pageObj != null && pageIndirectRef != null && pageIndirectRef.Equals(indirectRef))
                                {
                                    _logger.LogInformation($"    ✓ FOUND MATCH: Page {i}");
                                    return i;
                                }
                            }

                            _logger.LogInformation($"    No matching page found for indirect ref {indirectRef}");
                        }
                        // Direct page number (sometimes the array is [pageNum fitType ...])
                        else if (pageRef is PdfNumber pageNum)
                        {
                            int result = pageNum.IntValue() + 1;
                            _logger.LogInformation($"    Direct page number: {pageNum.IntValue()} → returning page {result}");
                            return result;
                        }
                        else if (pageRef is PdfDictionary pageDict)
                        {
                            _logger.LogInformation($"    First element is PdfDictionary (page object)");
                            _logger.LogInformation($"    Dictionary keys: {string.Join(", ", pageDict.KeySet().Select(k => k.ToString()))}");

                            // Try to find page by object reference
                            for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                            {
                                if (pdfDoc.GetPage(i).GetPdfObject().Equals(pageDict))
                                {
                                    _logger.LogInformation($"    ✓ FOUND MATCH by dictionary: Page {i}");
                                    return i;
                                }
                            }
                        }
                    }
                }
                // Try as name (named destination)
                else if (dest is PdfString || dest is PdfName)
                {
                    _logger.LogInformation($"    Named destination: {dest}");
                    // Would need to look up in document's Dests dictionary
                    // For now, skip named destinations
                }
                else
                {
                    _logger.LogInformation($"    Unexpected destination type: {dest.GetType().Name}");
                }
            }
            else
            {
                _logger.LogInformation($"    Not a GoTo action (type is {actionType})");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"    EXCEPTION in GetDestinationPage: {ex.Message}");
            _logger.LogError($"    Stack trace: {ex.StackTrace}");
        }

        _logger.LogInformation("=== GetDestinationPage END: returning -1 ===");
        return -1;
    }
}

/// <summary>
/// ToC entry with text and destination page
/// </summary>
public class TocEntry
{
    public string Text { get; set; } = "";
    public int DestinationPage { get; set; }
}

/// <summary>
/// Result of ToC recreation operation
/// </summary>
public class TocRecreationResult
{
    public bool Success { get; set; }
    public byte[] FixedPdf { get; set; } = Array.Empty<byte>();
    public int EntriesRecreated { get; set; }
    public List<string> Warnings { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
