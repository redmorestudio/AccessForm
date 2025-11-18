using System.Linq;
using WordToPdfConverter.Models.Layout;
using WordToPdfConverter.Services.Remediation.Structure;

namespace WordToPdfConverter.Services.Pdf;

/// <summary>
/// Helper utilities for working with StructureNodes during MCID linking.
/// </summary>
public static class StructureNodeHelper
{
    /// <summary>
    /// Determines if a structure node should receive MCIDs (i.e., it represents actual content, not just a container).
    /// </summary>
    /// <param name="node">The structure node to check</param>
    /// <returns>True if this node should get MCID references</returns>
    public static bool IsLeafContentNode(StructureNode node)
    {
        // Nodes with children are containers, not leaf content
        if (node.Children != null && node.Children.Count > 0)
            return false;

        // Only specific roles get MCIDs (paragraphs, headings, figures, table cells)
        var role = node.Role;
        return role switch
        {
            "P" => true,
            "H" => true,
            "H1" => true,
            "H2" => true,
            "H3" => true,
            "H4" => true,
            "H5" => true,
            "H6" => true,
            "Figure" => true,
            "Tbl" => true,
            "TR" => true,
            "TH" => true,
            "TD" => true,
            _ => false
        };
    }

    /// <summary>
    /// Resolves which page a structure node appears on by scanning the PageLayoutPlan.
    /// </summary>
    /// <param name="node">The structure node to locate</param>
    /// <param name="plan">The page layout plan containing page-to-node mappings</param>
    /// <returns>Zero-based page index, or 0 if not found</returns>
    public static int ResolvePageIndex(StructureNode node, PageLayoutPlan plan)
    {
        foreach (var pagePlan in plan.Pages)
        {
            if (pagePlan.Instructions.Any(i => ReferenceEquals(i.Node, node)))
            {
                return pagePlan.PageIndex;
            }
        }

        // Not found - default to page 0
        // This can happen for structural container nodes that don't have draw instructions
        return 0;
    }
}
