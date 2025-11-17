using System.Collections.Generic;

namespace WordToPdfConverter.Models.Logical;

/// <summary>
/// Represents a single page of logical content produced by AI layout analysis.
/// Contains an ordered list of semantic blocks detected on this page.
/// </summary>
public record LogicalPage(
    int PageNumber,
    IReadOnlyList<LogicalBlock> Blocks);
