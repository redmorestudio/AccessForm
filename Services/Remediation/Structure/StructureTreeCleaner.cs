using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace WordToPdfConverter.Services.Remediation.Structure;

/// <summary>
/// Preprocesses structure trees to:
/// 1. Detect and mark artifact nodes (decorative/layout-only content)
/// 2. Identify table headers and assign scope attributes
/// 3. Convert TD to TH for header cells
/// 4. Clean up empty/decorative table cells
/// </summary>
public sealed class StructureTreeCleaner
{
    private readonly ILogger<StructureTreeCleaner> _logger;

    public StructureTreeCleaner(ILogger<StructureTreeCleaner> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Cleans the structure tree by identifying artifacts and improving table semantics.
    /// </summary>
    public StructureTree Clean(StructureTree tree)
    {
        _logger.LogInformation("[STRUCTURE-CLEANER] Starting structure tree cleaning");

        var cleanedNodes = new List<StructureNode>();
        foreach (var node in tree.Nodes)
        {
            var cleaned = CleanNode(node);
            if (cleaned != null)
            {
                cleanedNodes.Add(cleaned);
            }
        }

        var cleanedTree = new StructureTree(cleanedNodes);

        _logger.LogInformation("[STRUCTURE-CLEANER] Cleaning complete");
        return cleanedTree;
    }

    private StructureNode? CleanNode(StructureNode node)
    {
        // Process based on role
        if (string.Equals(node.Role, "Table", StringComparison.OrdinalIgnoreCase))
        {
            return ProcessTable(node);
        }

        // Detect artifacts for non-table nodes
        if (IsArtifact(node))
        {
            _logger.LogDebug($"[STRUCTURE-CLEANER] Marking {node.Role} as artifact (empty content)");
            return node with { IsArtifact = true, Role = "Artifact" };
        }

        // Recursively clean children
        if (node.Children != null && node.Children.Count > 0)
        {
            var cleanedChildren = new List<StructureNode>();
            foreach (var child in node.Children)
            {
                var cleaned = CleanNode(child);
                if (cleaned != null)
                {
                    cleanedChildren.Add(cleaned);
                }
            }

            return node with { Children = cleanedChildren };
        }

        return node;
    }

    private StructureNode ProcessTable(StructureNode tableNode)
    {
        _logger.LogDebug("[STRUCTURE-CLEANER] Processing table for header detection");

        if (tableNode.Children == null || tableNode.Children.Count == 0)
        {
            return tableNode;
        }

        var rows = tableNode.Children.Where(n => n.Role == "TR").ToList();
        if (rows.Count == 0)
        {
            return tableNode;
        }

        var processedRows = new List<StructureNode>();

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var processedRow = ProcessTableRow(row, rowIndex, rows.Count);
            processedRows.Add(processedRow);
        }

        return tableNode with { Children = processedRows };
    }

    private StructureNode ProcessTableRow(StructureNode rowNode, int rowIndex, int totalRows)
    {
        if (rowNode.Children == null || rowNode.Children.Count == 0)
        {
            return rowNode;
        }

        var cells = rowNode.Children.Where(n => n.Role is "TD" or "TH").ToList();
        var processedCells = new List<StructureNode>();

        for (int colIndex = 0; colIndex < cells.Count; colIndex++)
        {
            var cell = cells[colIndex];
            var processedCell = ProcessTableCell(cell, rowIndex, colIndex, totalRows, cells.Count);
            processedCells.Add(processedCell);
        }

        return rowNode with { Children = processedCells };
    }

    private StructureNode ProcessTableCell(StructureNode cellNode, int rowIndex, int colIndex, int totalRows, int totalCols)
    {
        // Check if this is an artifact cell (empty/decorative)
        if (IsArtifact(cellNode))
        {
            _logger.LogDebug($"[STRUCTURE-CLEANER] Marking table cell at [{rowIndex},{colIndex}] as artifact");
            return cellNode with { IsArtifact = true, Role = "Artifact" };
        }

        // Position-based header detection
        bool isHeaderRow = rowIndex == 0 && totalRows > 1;
        bool isHeaderCol = colIndex == 0 && totalCols > 1;

        // Convert to TH if in header position
        if (isHeaderRow || isHeaderCol)
        {
            string scope = isHeaderRow ? "column" : "row";

            _logger.LogDebug($"[STRUCTURE-CLEANER] Converting cell at [{rowIndex},{colIndex}] to TH with scope=\"{scope}\"");

            return cellNode with
            {
                Role = "TH",
                TableScope = scope
            };
        }

        // Regular data cell - clean children
        if (cellNode.Children != null && cellNode.Children.Count > 0)
        {
            var cleanedChildren = new List<StructureNode>();
            foreach (var child in cellNode.Children)
            {
                var cleaned = CleanNode(child);
                if (cleaned != null)
                {
                    cleanedChildren.Add(cleaned);
                }
            }

            return cellNode with { Children = cleanedChildren };
        }

        return cellNode;
    }

    /// <summary>
    /// Determines if a node is an artifact based on simple heuristic:
    /// - Has no text content (null or empty/whitespace)
    /// - Not a container element (Table, TR, Document)
    /// </summary>
    private bool IsArtifact(StructureNode node)
    {
        // Container elements are not artifacts themselves
        if (node.Role is "Table" or "TR" or "Document" or "Figure")
        {
            return false;
        }

        // Check if text content is null or empty/whitespace
        if (string.IsNullOrWhiteSpace(node.TextContent))
        {
            // If it has children, it's a container, not an artifact
            if (node.Children != null && node.Children.Count > 0)
            {
                return false;
            }

            // Empty leaf node = artifact
            return true;
        }

        return false;
    }
}
