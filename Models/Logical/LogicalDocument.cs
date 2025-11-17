using System.Collections.Generic;

namespace WordToPdfConverter.Models.Logical;

/// <summary>
/// Represents a full document's logical structure across all pages.
/// This is the output of AI layout analysis and the input to structure tree building.
/// </summary>
public record LogicalDocument(IReadOnlyList<LogicalPage> Pages);
