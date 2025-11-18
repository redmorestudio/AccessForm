using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.Layout;
using WordToPdfConverter.Models.Phase6H;
using WordToPdfConverter.Services.Remediation.Structure;

namespace WordToPdfConverter.Services.Phase6H;

/// <summary>
/// Phase 6H: Builds a McidRewritePlan from a StructureTree + PageLayoutPlan.
/// Walks the structure tree to collect all leaf content nodes with MCID assignments
/// and converts them into McidSegments for the external rewriter microservice.
/// </summary>
public sealed class McidRewritePlanBuilder
{
    private readonly ILogger<McidRewritePlanBuilder> _logger;

    public McidRewritePlanBuilder(ILogger<McidRewritePlanBuilder> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Builds a McidRewritePlan from a structure tree.
    /// </summary>
    /// <param name="tree">The structure tree with MCID assignments</param>
    /// <param name="layoutPlan">The page layout plan with geometric information</param>
    /// <param name="documentId">Optional document identifier for tracing</param>
    /// <param name="debug">Whether to enable debug mode in the microservice</param>
    /// <returns>A complete McidRewritePlan ready to send to the external microservice</returns>
    public McidRewritePlan BuildPlan(
        StructureTree tree,
        PageLayoutPlan layoutPlan,
        string? documentId = null,
        bool debug = false)
    {
        try
        {
            _logger.LogInformation("[PHASE-6H] Building MCID rewrite plan for document: {DocumentId}", documentId ?? "unknown");

            var plan = new McidRewritePlan
            {
                DocumentId = documentId,
                Version = "6H-1",
                Debug = debug,
                Segments = new List<McidSegment>()
            };

            // Collect all nodes from the tree
            var allNodes = new List<StructureNode>();
            WalkTree(tree.Nodes, allNodes);

            _logger.LogInformation("[PHASE-6H] Found {NodeCount} total nodes in tree", allNodes.Count);

            // Filter to leaf content nodes (nodes that have MCID references)
            var leafNodes = allNodes.Where(n => n.McidReferences != null && n.McidReferences.Count > 0).ToList();
            _logger.LogInformation("[PHASE-6H] Found {LeafCount} leaf content nodes with MCID references", leafNodes.Count);

            int segmentCount = 0;
            foreach (var node in leafNodes)
            {
                // Each node may have multiple MCID references (one per page if it spans pages)
                foreach (var mcidRef in node.McidReferences)
                {
                    // Skip if no bounds available
                    if (node.Bounds == null)
                    {
                        _logger.LogWarning("[PHASE-6H] Node {Role} has MCID {Mcid} but no bounds, skipping", node.Role, mcidRef.Mcid);
                        continue;
                    }

                    var segment = new McidSegment
                    {
                        PageIndex = mcidRef.PageIndex + 1, // Convert from 0-based to 1-based
                        Mcid = mcidRef.Mcid,
                        Role = node.Role,
                        X = node.Bounds.Value.X,
                        Y = node.Bounds.Value.Y,
                        Width = node.Bounds.Value.Width,
                        Height = node.Bounds.Value.Height,
                        SequenceIndex = segmentCount
                    };

                    plan.Segments.Add(segment);
                    segmentCount++;

                    _logger.LogDebug("[PHASE-6H] Added segment: Page={Page}, MCID={Mcid}, Role={Role}, Bounds=({X},{Y},{W},{H})",
                        segment.PageIndex, segment.Mcid, segment.Role, segment.X, segment.Y, segment.Width, segment.Height);
                }
            }

            _logger.LogInformation("[PHASE-6H] Built plan with {SegmentCount} segments across {PageCount} pages",
                plan.Segments.Count,
                plan.Segments.Select(s => s.PageIndex).Distinct().Count());

            return plan;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PHASE-6H] Failed to build MCID rewrite plan");
            throw;
        }
    }

    /// <summary>
    /// Recursively walks the structure tree to collect all nodes.
    /// </summary>
    private void WalkTree(IEnumerable<StructureNode> nodes, List<StructureNode> result)
    {
        foreach (var node in nodes)
        {
            result.Add(node);
            if (node.Children != null && node.Children.Count > 0)
            {
                WalkTree(node.Children, result);
            }
        }
    }
}
