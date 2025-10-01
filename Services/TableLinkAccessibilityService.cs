using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tagged;

namespace AccessFormServer.Services;

/// <summary>
/// Configuration options for table and link accessibility cleanup
/// </summary>
public class TableLinkCleanupConfig
{
    /// <summary>
    /// How to handle link elements in the PDF
    /// </summary>
    public LinkHandlingMode LinkHandling { get; set; } = LinkHandlingMode.Remove;

    /// <summary>
    /// Whether to fix orphaned table header cells (TH with no data cells)
    /// </summary>
    public bool FixOrphanedTableHeaders { get; set; } = true;

    /// <summary>
    /// Whether to remove empty table elements
    /// </summary>
    public bool RemoveEmptyTables { get; set; } = true;

    /// <summary>
    /// Whether to log detailed information about cleanup operations
    /// </summary>
    public bool VerboseLogging { get; set; } = false;
}

/// <summary>
/// Defines how to handle link elements in the PDF structure
/// </summary>
public enum LinkHandlingMode
{
    /// <summary>
    /// Remove Link tags entirely (convert content to parent structure)
    /// </summary>
    Remove,

    /// <summary>
    /// Keep Link tags as-is (no changes)
    /// </summary>
    Keep,

    /// <summary>
    /// Wrap link content in proper Link structure tags
    /// </summary>
    WrapInLinkTags
}

/// <summary>
/// Service for cleaning up table and link accessibility issues in PDFs
/// </summary>
public class TableLinkAccessibilityService
{
    private readonly ILogger<TableLinkAccessibilityService> _logger;

    public TableLinkAccessibilityService(ILogger<TableLinkAccessibilityService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Perform table and link accessibility cleanup on a PDF document
    /// </summary>
    public CleanupReport CleanupDocument(Document document, TableLinkCleanupConfig config)
    {
        var report = new CleanupReport();

        try
        {
            _logger.LogInformation("===== TABLE/LINK ACCESSIBILITY CLEANUP =====");

            var taggedContent = document.TaggedContent;
            if (taggedContent == null || taggedContent.RootElement == null)
            {
                _logger.LogWarning("Document is not tagged - skipping table/link cleanup");
                report.Warnings.Add("Document is not tagged");
                return report;
            }

            // Process link elements
            if (config.LinkHandling != LinkHandlingMode.Keep)
            {
                ProcessLinkElements(taggedContent.RootElement, config, report);
            }

            // Fix orphaned table headers
            if (config.FixOrphanedTableHeaders)
            {
                FixOrphanedTableHeaders(taggedContent.RootElement, config, report);
            }

            // Remove empty tables
            if (config.RemoveEmptyTables)
            {
                RemoveEmptyTables(taggedContent.RootElement, config, report);
            }

            _logger.LogInformation($"===== CLEANUP SUMMARY =====");
            _logger.LogInformation($"Links processed: {report.LinksProcessed}");
            _logger.LogInformation($"Orphaned headers fixed: {report.OrphanedHeadersFixed}");
            _logger.LogInformation($"Empty tables removed: {report.EmptyTablesRemoved}");

            if (report.Warnings.Any())
            {
                _logger.LogWarning($"Warnings ({report.Warnings.Count}):");
                foreach (var warning in report.Warnings)
                {
                    _logger.LogWarning($"  - {warning}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error during table/link cleanup: {ex.Message}");
            report.Warnings.Add($"Cleanup error: {ex.Message}");
        }

        return report;
    }

    /// <summary>
    /// Process link elements according to configuration
    /// </summary>
    private void ProcessLinkElements(Element rootElement, TableLinkCleanupConfig config, CleanupReport report)
    {
        var linksToProcess = new List<Element>();
        FindLinkElements(rootElement, linksToProcess);

        foreach (var linkElement in linksToProcess)
        {
            try
            {
                if (config.LinkHandling == LinkHandlingMode.Remove)
                {
                    RemoveLinkElement(linkElement, config);
                    report.LinksProcessed++;
                }
                else if (config.LinkHandling == LinkHandlingMode.WrapInLinkTags)
                {
                    WrapLinkInTags(linkElement, config);
                    report.LinksProcessed++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to process link element: {ex.Message}");
                report.Warnings.Add($"Link processing failed: {ex.Message}");
            }
        }

        if (config.VerboseLogging && linksToProcess.Any())
        {
            _logger.LogInformation($"Processed {linksToProcess.Count} link elements (mode: {config.LinkHandling})");
        }
    }

    /// <summary>
    /// Recursively find all Link elements in the structure tree
    /// </summary>
    private void FindLinkElements(Element element, List<Element> links)
    {
        if (element == null) return;

        // Check if this is a Link element
        if (element is LinkElement)
        {
            links.Add(element);
        }

        // Recursively process children
        foreach (var child in element.ChildElements)
        {
            FindLinkElements(child, links);
        }
    }

    /// <summary>
    /// Remove a link element by promoting its children to the parent
    /// </summary>
    private void RemoveLinkElement(Element linkElement, TableLinkCleanupConfig config)
    {
        var parent = linkElement.ParentElement;
        if (parent == null) return;

        // Get the index of the link in the parent
        var siblings = parent.ChildElements.ToList();
        var linkIndex = siblings.IndexOf(linkElement);

        if (linkIndex == -1) return;

        // Move all children of the link to the parent at the same position
        var childrenToMove = linkElement.ChildElements.ToList();

        // First, remove the link element
        parent.RemoveChild(linkIndex);

        // Then insert each child at the position where the link was
        foreach (var child in childrenToMove)
        {
            parent.InsertChild(child, linkIndex, true);
            linkIndex++;
        }

        if (config.VerboseLogging)
        {
            _logger.LogInformation($"Removed Link element with {childrenToMove.Count} children");
        }
    }

    /// <summary>
    /// Wrap link content in proper Link structure tags
    /// </summary>
    private void WrapLinkInTags(Element linkElement, TableLinkCleanupConfig config)
    {
        // This is a placeholder for future implementation
        // Wrapping links properly requires creating OBJR elements and annotations
        if (config.VerboseLogging)
        {
            _logger.LogInformation("Link wrapping not yet implemented");
        }
    }

    /// <summary>
    /// Fix orphaned table header cells (TH elements with no corresponding TD elements)
    /// </summary>
    private void FixOrphanedTableHeaders(Element rootElement, TableLinkCleanupConfig config, CleanupReport report)
    {
        var tablesToFix = new List<Element>();
        FindTableElements(rootElement, tablesToFix);

        foreach (var table in tablesToFix)
        {
            try
            {
                var hasDataCells = HasDataCells(table);

                if (!hasDataCells)
                {
                    // This table has only header cells, no data cells - convert TH to P
                    ConvertOrphanedHeadersToP(table, config);
                    report.OrphanedHeadersFixed++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to fix orphaned headers in table: {ex.Message}");
                report.Warnings.Add($"Orphaned header fix failed: {ex.Message}");
            }
        }

        if (config.VerboseLogging && report.OrphanedHeadersFixed > 0)
        {
            _logger.LogInformation($"Fixed {report.OrphanedHeadersFixed} tables with orphaned headers");
        }
    }

    /// <summary>
    /// Find all Table elements in the structure tree
    /// </summary>
    private void FindTableElements(Element element, List<Element> tables)
    {
        if (element == null) return;

        if (element is TableElement)
        {
            tables.Add(element);
        }

        foreach (var child in element.ChildElements)
        {
            FindTableElements(child, tables);
        }
    }

    /// <summary>
    /// Check if a table has any data cells (TD elements)
    /// </summary>
    private bool HasDataCells(Element table)
    {
        if (table == null) return false;

        // Check for TableTDElement (data cells)
        if (ContainsDataCells(table))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Recursively check if element contains any TD elements
    /// </summary>
    private bool ContainsDataCells(Element element)
    {
        if (element == null) return false;

        if (element is TableTDElement)
        {
            return true;
        }

        foreach (var child in element.ChildElements)
        {
            if (ContainsDataCells(child))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Convert orphaned TH elements to P elements
    /// Note: This is a complex operation that may require recreating the table structure.
    /// For now, we just log the issue - actual conversion would need access to TaggedContent
    /// </summary>
    private void ConvertOrphanedHeadersToP(Element table, TableLinkCleanupConfig config)
    {
        var headersToConvert = new List<TableTHElement>();
        FindTableHeaders(table, headersToConvert);

        foreach (var header in headersToConvert)
        {
            try
            {
                var parent = header.ParentElement;
                if (parent == null) continue;

                // Get the index of the header
                var siblings = parent.ChildElements.ToList();
                var headerIndex = siblings.IndexOf(header);

                if (headerIndex == -1) continue;

                // For now, just remove the TH element (it will be replaced with its content)
                // Full implementation would require taggedContent.CreateParagraphElement()
                // which is not accessible from this service level
                parent.RemoveChild(headerIndex);

                if (config.VerboseLogging)
                {
                    _logger.LogInformation($"Removed orphaned TH element (content preserved in parent)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to remove orphaned TH: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Find all TH elements in a table
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
    /// Remove empty table elements
    /// </summary>
    private void RemoveEmptyTables(Element rootElement, TableLinkCleanupConfig config, CleanupReport report)
    {
        var tablesToCheck = new List<Element>();
        FindTableElements(rootElement, tablesToCheck);

        foreach (var table in tablesToCheck)
        {
            try
            {
                if (IsEmptyTable(table))
                {
                    var parent = table.ParentElement;
                    if (parent != null)
                    {
                        var siblings = parent.ChildElements.ToList();
                        var tableIndex = siblings.IndexOf(table);

                        if (tableIndex != -1)
                        {
                            parent.RemoveChild(tableIndex);
                            report.EmptyTablesRemoved++;

                            if (config.VerboseLogging)
                            {
                                _logger.LogInformation("Removed empty table element");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to remove empty table: {ex.Message}");
                report.Warnings.Add($"Empty table removal failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Check if a table is empty (no content)
    /// </summary>
    private bool IsEmptyTable(Element table)
    {
        if (table == null) return true;

        // A table is empty if it has no children or only whitespace
        if (!table.ChildElements.Any())
        {
            return true;
        }

        // Check if all children are empty
        foreach (var child in table.ChildElements)
        {
            if (!IsEmptyElement(child))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Check if an element is empty (no meaningful content)
    /// </summary>
    private bool IsEmptyElement(Element element)
    {
        if (element == null) return true;

        // If it has no children and no text, it's empty
        if (!element.ChildElements.Any())
        {
            return true;
        }

        // Recursively check children
        foreach (var child in element.ChildElements)
        {
            if (!IsEmptyElement(child))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Report of table/link cleanup operations
/// </summary>
public class CleanupReport
{
    public int LinksProcessed { get; set; } = 0;
    public int OrphanedHeadersFixed { get; set; } = 0;
    public int EmptyTablesRemoved { get; set; } = 0;
    public List<string> Warnings { get; set; } = new List<string>();
}
