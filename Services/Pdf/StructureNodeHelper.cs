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
        // PRIORITY 1: Use node's PageIndex property if set (for remediation)
        // This allows nodes from AI structure rebuild to carry their page information
        if (node.PageIndex > 0)
        {
            return node.PageIndex;
        }

        // PRIORITY 2: Look for node in layout plan (for new PDF generation)
        foreach (var pagePlan in plan.Pages)
        {
            if (pagePlan.Instructions.Any(i => ReferenceEquals(i.Node, node)))
            {
                return pagePlan.PageIndex;
            }
        }

        // PRIORITY 3: Use node's PageIndex even if 0 (could be legitimate page 0)
        // PRIORITY 4: Default to page 0 as fallback
        return node.PageIndex;
    }
}
