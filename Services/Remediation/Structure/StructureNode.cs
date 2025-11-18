using System.Collections.Generic;
using WordToPdfConverter.Models.Logical;

namespace WordToPdfConverter.Services.Remediation.Structure;

/// <summary>
/// Represents a node in the semantic structure tree that will be mapped to PDF tags.
/// This is a PDF-neutral representation that can be written to any PDF backend.
/// </summary>
public record StructureNode(
    string Role,                                        // e.g. "Document", "H1", "P", "Table", "TR", "TH", "TD", "Figure"
    string? TextContent,                                // Text content for this node (if leaf node)
    IReadOnlyDictionary<string, string>? Attributes,    // Additional attributes (e.g., alt text, scope, row/col)
    IReadOnlyList<StructureNode>? Children)             // Child nodes (if container)
{
    /// <summary>
    /// Marks this node as decorative/layout-only content that should not appear in accessibility tree.
    /// Artifact nodes are skipped during PDF structure generation.
    /// </summary>
    public bool IsArtifact { get; init; }

    /// <summary>
    /// Table header scope: "column" for column headers (TH in row 0), "row" for row headers (TH in col 0).
    /// Only applicable for TH elements.
    /// </summary>
    public string? TableScope { get; init; }

    /// <summary>
    /// Bounding rectangle of this node in the logical coordinate system.
    /// Used by the layout engine for column detection and reading order computation.
    /// May be null for container nodes that don't have physical bounds (e.g., Document root).
    /// </summary>
    public Rect? Bounds { get; init; }

    /// <summary>
    /// MCID references assigned to this structure node (per page).
    /// Populated during Phase 6 MCID linking to connect structure elements to actual PDF content.
    /// </summary>
    public List<McidReference> McidReferences { get; init; } = new();
}
