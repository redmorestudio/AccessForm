namespace WordToPdfConverter.Models.Layout;

/// <summary>
/// Complete layout plan for all pages in the PDF.
/// Contains per-page drawing instructions with target bounds for each structure element.
/// </summary>
public sealed class PageLayoutPlan
{
    public List<PagePlan> Pages { get; } = new();
}
