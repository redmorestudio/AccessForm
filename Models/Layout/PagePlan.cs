namespace WordToPdfConverter.Models.Layout;

/// <summary>
/// Layout plan for a single page, containing ordered drawing instructions.
/// </summary>
public sealed class PagePlan
{
    public int PageIndex { get; set; }
    public List<DrawInstruction> Instructions { get; } = new();
}
