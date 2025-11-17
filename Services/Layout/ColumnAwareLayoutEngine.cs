using Microsoft.Extensions.Logging;
using Syncfusion.Drawing;
using WordToPdfConverter.Models.Layout;
using WordToPdfConverter.Models.Logical;
using WordToPdfConverter.Services.Remediation.Structure;

namespace WordToPdfConverter.Services.Layout;

/// <summary>
/// Advanced layout engine that detects columns, regions (header/body/sidebar/footer),
/// and synthesizes proper reading order for multi-column documents.
/// Implements the full Phase 3c specification.
/// </summary>
public sealed class ColumnAwareLayoutEngine : IPageLayoutEngine
{
    private readonly ILogger<ColumnAwareLayoutEngine> _logger;

    // Page dimensions (US Letter default)
    private const double DEFAULT_PAGE_WIDTH = 612;
    private const double DEFAULT_PAGE_HEIGHT = 792;

    // Column detection threshold: 15% of page width
    private const double COLUMN_THRESHOLD = 0.15;

    // Region detection thresholds
    private const double HEADER_THRESHOLD = 0.15;  // Top 15% of page
    private const double FOOTER_THRESHOLD = 0.85;  // Bottom 15% of page

    // Sidebar detection: column width < 50% of max column width
    private const double SIDEBAR_THRESHOLD = 0.5;

    public ColumnAwareLayoutEngine(ILogger<ColumnAwareLayoutEngine> logger)
    {
        _logger = logger;
    }

    public PageLayoutPlan BuildLayoutPlan(StructureTree tree)
    {
        _logger.LogInformation("[COLUMN-LAYOUT] Building column-aware layout plan");

        var plan = new PageLayoutPlan();

        // Step 1: Gather all nodes with bounds
        var nodesWithBounds = new List<NodeWithMetadata>();
        GatherNodesRecursive(tree.Nodes, 0, nodesWithBounds);

        // Group by page
        var pageGroups = nodesWithBounds
            .GroupBy(x => x.PageIndex)
            .OrderBy(g => g.Key);

        foreach (var pageGroup in pageGroups)
        {
            var pagePlan = ProcessPage(pageGroup.Key, pageGroup.ToList());
            plan.Pages.Add(pagePlan);
        }

        _logger.LogInformation(
            $"[COLUMN-LAYOUT] Created plan with {plan.Pages.Count} pages, " +
            $"{plan.Pages.Sum(p => p.Instructions.Count)} total elements");

        return plan;
    }

    private PagePlan ProcessPage(int pageIndex, List<NodeWithMetadata> nodes)
    {
        _logger.LogInformation($"[COLUMN-LAYOUT] Processing page {pageIndex} with {nodes.Count} nodes");

        // Step 2: Column detection
        DetectColumns(nodes, DEFAULT_PAGE_WIDTH);

        // Step 3: Region detection
        ClassifyRegions(nodes, DEFAULT_PAGE_HEIGHT);

        // Step 4: Reading order synthesis
        var orderedNodes = SynthesizeReadingOrder(nodes);

        // Step 5: Build DrawInstructions
        var pagePlan = new PagePlan { PageIndex = pageIndex };
        foreach (var nodeMetadata in orderedNodes)
        {
            var bounds = nodeMetadata.Node.Bounds!.Value;
            pagePlan.Instructions.Add(new DrawInstruction
            {
                Node = nodeMetadata.Node,
                TargetBounds = new RectangleF(
                    (float)bounds.X,
                    (float)bounds.Y,
                    (float)bounds.Width,
                    (float)bounds.Height),
                ColumnIndex = nodeMetadata.ColumnIndex,
                Region = nodeMetadata.Region
            });
        }

        _logger.LogInformation(
            $"[COLUMN-LAYOUT] Page {pageIndex}: " +
            $"{pagePlan.Instructions.Count} elements in " +
            $"{nodes.Select(n => n.ColumnIndex).Distinct().Count()} columns");

        return pagePlan;
    }

    private void DetectColumns(List<NodeWithMetadata> nodes, double pageWidth)
    {
        if (nodes.Count == 0)
            return;

        // Calculate centerX for each node
        foreach (var node in nodes)
        {
            var bounds = node.Node.Bounds!.Value;
            node.CenterX = bounds.X + bounds.Width / 2.0;
        }

        // Sort by centerX
        nodes.Sort((a, b) => a.CenterX.CompareTo(b.CenterX));

        // Assign column indices
        int currentColumn = 0;
        double previousCenterX = nodes[0].CenterX;
        nodes[0].ColumnIndex = currentColumn;

        for (int i = 1; i < nodes.Count; i++)
        {
            double currentCenterX = nodes[i].CenterX;
            double gap = Math.Abs(currentCenterX - previousCenterX);

            // Start new column if gap exceeds threshold
            if (gap > pageWidth * COLUMN_THRESHOLD)
            {
                currentColumn++;
            }

            nodes[i].ColumnIndex = currentColumn;
            previousCenterX = currentCenterX;
        }

        _logger.LogInformation(
            $"[COLUMN-LAYOUT] Detected {currentColumn + 1} columns");
    }

    private void ClassifyRegions(List<NodeWithMetadata> nodes, double pageHeight)
    {
        // Group nodes by column to calculate column widths
        var columnGroups = nodes.GroupBy(n => n.ColumnIndex).ToList();
        var columnWidths = new Dictionary<int, double>();

        foreach (var columnGroup in columnGroups)
        {
            var columnNodes = columnGroup.ToList();
            if (columnNodes.Count == 0)
                continue;

            double minX = columnNodes.Min(n => n.Node.Bounds!.Value.X);
            double maxX = columnNodes.Max(n =>
                n.Node.Bounds!.Value.X + n.Node.Bounds!.Value.Width);

            columnWidths[columnGroup.Key] = maxX - minX;
        }

        double maxColumnWidth = columnWidths.Count > 0
            ? columnWidths.Values.Max()
            : 0;

        // Classify each node's region
        foreach (var node in nodes)
        {
            var bounds = node.Node.Bounds!.Value;
            double top = bounds.Y;

            // Header: top 15% of page
            if (top < pageHeight * HEADER_THRESHOLD)
            {
                node.Region = "header";
            }
            // Footer: bottom 15% of page
            else if (top > pageHeight * FOOTER_THRESHOLD)
            {
                node.Region = "footer";
            }
            // Body or sidebar candidate
            else
            {
                // Check if this column is a sidebar
                if (columnWidths.TryGetValue(node.ColumnIndex, out double columnWidth))
                {
                    bool isSidebar = columnWidth < maxColumnWidth * SIDEBAR_THRESHOLD;

                    // Only leftmost or rightmost columns can be sidebars
                    int minColumn = nodes.Min(n => n.ColumnIndex);
                    int maxColumn = nodes.Max(n => n.ColumnIndex);
                    bool isEdgeColumn = node.ColumnIndex == minColumn || node.ColumnIndex == maxColumn;

                    if (isSidebar && isEdgeColumn)
                    {
                        node.Region = "sidebar";
                    }
                    else
                    {
                        node.Region = "body";
                    }
                }
                else
                {
                    node.Region = "body";
                }
            }
        }

        var regionCounts = nodes.GroupBy(n => n.Region)
            .ToDictionary(g => g.Key, g => g.Count());
        _logger.LogInformation(
            $"[COLUMN-LAYOUT] Regions: {string.Join(", ", regionCounts.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }

    private List<NodeWithMetadata> SynthesizeReadingOrder(List<NodeWithMetadata> nodes)
    {
        var ordered = new List<NodeWithMetadata>();

        // 1. Header (sorted Y asc, X asc)
        var headerNodes = nodes
            .Where(n => n.Region == "header")
            .OrderBy(n => n.Node.Bounds!.Value.Y)
            .ThenBy(n => n.Node.Bounds!.Value.X)
            .ToList();
        ordered.AddRange(headerNodes);

        // 2. Body (sorted by column, then Y asc, X asc within each column)
        var bodyNodes = nodes
            .Where(n => n.Region == "body")
            .OrderBy(n => n.ColumnIndex)
            .ThenBy(n => n.Node.Bounds!.Value.Y)
            .ThenBy(n => n.Node.Bounds!.Value.X)
            .ToList();
        ordered.AddRange(bodyNodes);

        // 3. Sidebar (sorted X asc, Y asc)
        var sidebarNodes = nodes
            .Where(n => n.Region == "sidebar")
            .OrderBy(n => n.Node.Bounds!.Value.X)
            .ThenBy(n => n.Node.Bounds!.Value.Y)
            .ToList();
        ordered.AddRange(sidebarNodes);

        // 4. Footer (sorted Y asc, X asc)
        var footerNodes = nodes
            .Where(n => n.Region == "footer")
            .OrderBy(n => n.Node.Bounds!.Value.Y)
            .ThenBy(n => n.Node.Bounds!.Value.X)
            .ToList();
        ordered.AddRange(footerNodes);

        _logger.LogInformation(
            $"[COLUMN-LAYOUT] Reading order: " +
            $"{headerNodes.Count} header, " +
            $"{bodyNodes.Count} body, " +
            $"{sidebarNodes.Count} sidebar, " +
            $"{footerNodes.Count} footer");

        return ordered;
    }

    private void GatherNodesRecursive(
        IReadOnlyList<StructureNode> nodes,
        int pageIndex,
        List<NodeWithMetadata> collector)
    {
        foreach (var node in nodes)
        {
            // Skip artifacts
            if (node.IsArtifact)
                continue;

            // Collect nodes with bounds
            if (node.Bounds.HasValue)
            {
                collector.Add(new NodeWithMetadata
                {
                    Node = node,
                    PageIndex = pageIndex
                });
            }

            // Recurse into children
            if (node.Children != null && node.Children.Count > 0)
            {
                GatherNodesRecursive(node.Children, pageIndex, collector);
            }
        }
    }

    /// <summary>
    /// Internal helper class to track node metadata during layout computation
    /// </summary>
    private sealed class NodeWithMetadata
    {
        public StructureNode Node { get; set; } = default!;
        public int PageIndex { get; set; }
        public double CenterX { get; set; }
        public int ColumnIndex { get; set; }
        public string Region { get; set; } = "body";
    }
}
