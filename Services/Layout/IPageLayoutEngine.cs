using WordToPdfConverter.Models.Layout;
using WordToPdfConverter.Services.Remediation.Structure;

namespace WordToPdfConverter.Services.Layout;

/// <summary>
/// Analyzes a StructureTree and produces a PageLayoutPlan describing
/// the exact drawing order and coordinates for all elements.
/// This enables column-aware reading order and multi-region layouts.
/// </summary>
public interface IPageLayoutEngine
{
    /// <summary>
    /// Builds a complete layout plan from a structure tree.
    /// The plan dictates the exact order and positioning of all elements
    /// across all pages.
    /// </summary>
    /// <param name="tree">The semantic structure tree to analyze</param>
    /// <returns>A layout plan with ordered drawing instructions per page</returns>
    PageLayoutPlan BuildLayoutPlan(StructureTree tree);
}
