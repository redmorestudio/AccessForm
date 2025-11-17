using Microsoft.Extensions.Logging;
using Syncfusion.Drawing;
using WordToPdfConverter.Models.Layout;
using WordToPdfConverter.Services.Remediation.Structure;

namespace WordToPdfConverter.Services.Layout;

/// <summary>
/// Simple layout engine that orders nodes top-to-bottom, left-to-right.
/// All nodes are assigned to a single column in the "body" region.
/// Used for debugging and simple single-column documents.
/// </summary>
public sealed class SimpleTopDownLayoutEngine : IPageLayoutEngine
{
    private readonly ILogger<SimpleTopDownLayoutEngine> _logger;

    public SimpleTopDownLayoutEngine(ILogger<SimpleTopDownLayoutEngine> logger)
    {
        _logger = logger;
    }

    public PageLayoutPlan BuildLayoutPlan(StructureTree tree)
    {
        _logger.LogInformation("[SIMPLE-LAYOUT] Building simple top-down layout plan");

        var plan = new PageLayoutPlan();

        // Gather all nodes with bounds
        var nodesWithBounds = new List<(StructureNode node, int pageIndex)>();
        GatherNodesRecursive(tree.Nodes, 0, nodesWithBounds);

        // Group by page
        var pageGroups = nodesWithBounds
            .GroupBy(x => x.pageIndex)
            .OrderBy(g => g.Key);

        foreach (var pageGroup in pageGroups)
        {
            var pagePlan = new PagePlan { PageIndex = pageGroup.Key };

            // Sort by Y ascending, then X ascending
            var sortedNodes = pageGroup
                .OrderBy(x => x.node.Bounds!.Value.Y)
                .ThenBy(x => x.node.Bounds!.Value.X)
                .ToList();

            foreach (var (node, _) in sortedNodes)
            {
                var bounds = node.Bounds!.Value;
                pagePlan.Instructions.Add(new DrawInstruction
                {
                    Node = node,
                    TargetBounds = new RectangleF(
                        (float)bounds.X,
                        (float)bounds.Y,
                        (float)bounds.Width,
                        (float)bounds.Height),
                    ColumnIndex = 0,
                    Region = "body"
                });
            }

            plan.Pages.Add(pagePlan);
            _logger.LogInformation(
                $"[SIMPLE-LAYOUT] Page {pageGroup.Key}: {pagePlan.Instructions.Count} elements");
        }

        _logger.LogInformation(
            $"[SIMPLE-LAYOUT] Created plan with {plan.Pages.Count} pages, " +
            $"{plan.Pages.Sum(p => p.Instructions.Count)} total elements");

        return plan;
    }

    private void GatherNodesRecursive(
        IReadOnlyList<StructureNode> nodes,
        int pageIndex,
        List<(StructureNode, int)> collector)
    {
        foreach (var node in nodes)
        {
            // Skip artifacts
            if (node.IsArtifact)
                continue;

            // Collect nodes with bounds
            if (node.Bounds.HasValue)
            {
                collector.Add((node, pageIndex));
            }

            // Recurse into children
            if (node.Children != null && node.Children.Count > 0)
            {
                GatherNodesRecursive(node.Children, pageIndex, collector);
            }
        }
    }
}
