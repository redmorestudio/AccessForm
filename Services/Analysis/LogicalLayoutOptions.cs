namespace WordToPdfConverter.Services.Analysis;

/// <summary>
/// Options controlling logical layout analysis behavior.
/// These options allow fine-tuning what the AI analyzes and how deeply.
/// </summary>
public sealed class LogicalLayoutOptions
{
    /// <summary>
    /// Whether to attempt to detect and include figures.
    /// </summary>
    public bool IncludeFigures { get; init; } = true;

    /// <summary>
    /// Whether to attempt to detect and include tables.
    /// </summary>
    public bool IncludeTables { get; init; } = true;

    /// <summary>
    /// If true, prefer AI-determined heading levels even if a tag tree exists.
    /// </summary>
    public bool PreferAiHeadings { get; init; } = true;

    /// <summary>
    /// Maximum pages to analyze. 0 = all pages.
    /// Useful for limiting costs or speeding up analysis for large documents.
    /// </summary>
    public int MaxPages { get; init; } = 0;
}
