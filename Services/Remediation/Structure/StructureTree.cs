using System.Collections.Generic;

namespace WordToPdfConverter.Services.Remediation.Structure;

/// <summary>
/// Root container for a document's semantic structure.
/// Typically contains a single "Document" root node with all content as children.
/// </summary>
public record StructureTree(
    IReadOnlyList<StructureNode> Nodes);
