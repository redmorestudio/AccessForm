using System.Collections.Generic;
using WordToPdfConverter.Models.Logical;

namespace WordToPdfConverter.Services.Remediation.Structure;

/// <summary>
/// Converts a LogicalDocument into a StructureTree suitable for PDF tag writing.
/// This builder handles headings, paragraphs, tables, figures, and creates a proper
/// hierarchical structure with appropriate attributes for accessibility.
/// </summary>
public static class StructureTreeBuilder
{
    /// <summary>
    /// Builds a complete StructureTree from a LogicalDocument.
    /// Creates a Document root node with all pages' content as children.
    /// </summary>
    public static StructureTree Build(LogicalDocument doc)
    {
        var nodes = new List<StructureNode>
        {
            new StructureNode(
                Role: "Document",
                TextContent: null,
                Attributes: null,
                Children: BuildChildren(doc))
        };

        return new StructureTree(nodes);
    }

    /// <summary>
    /// Iterates through all pages and blocks, converting each to appropriate StructureNodes.
    /// </summary>
    private static IReadOnlyList<StructureNode> BuildChildren(LogicalDocument doc)
    {
        var children = new List<StructureNode>();

        for (int pageIndex = 0; pageIndex < doc.Pages.Count; pageIndex++)
        {
            var page = doc.Pages[pageIndex];
            foreach (var block in page.Blocks)
            {
                switch (block)
                {
                    case HeadingBlock heading:
                        children.Add(new StructureNode(
                            Role: $"H{heading.Level}",
                            TextContent: heading.Text,
                            Attributes: null,
                            Children: null)
                        {
                            Bounds = heading.Bounds,
                            PageIndex = pageIndex
                        });
                        break;

                    case ParagraphBlock paragraph:
                        children.Add(new StructureNode(
                            Role: "P",
                            TextContent: paragraph.Text,
                            Attributes: null,
                            Children: null)
                        {
                            Bounds = paragraph.Bounds,
                            PageIndex = pageIndex
                        });
                        break;

                    case TableBlock table:
                        children.Add(BuildTableNode(table, pageIndex));
                        break;

                    case FigureBlock figure:
                        children.Add(BuildFigureNode(figure));
                        break;

                    case LogicalFormFieldBlock formField:
                        children.Add(BuildFormFieldNode(formField));
                        break;

                    default:
                        // Ignore unknown block types for now.
                        // Could log a warning here in production.
                        break;
                }
            }
        }

        return children;
    }

    /// <summary>
    /// Builds a Table structure node with proper TR/TH/TD hierarchy.
    /// Includes row/col attributes for table cells to support scope and headers.
    /// </summary>
    private static StructureNode BuildTableNode(TableBlock table, int pageIndex)
    {
        var rowNodes = new List<StructureNode>();

        foreach (var row in table.Rows)
        {
            var cellNodes = new List<StructureNode>();
            foreach (var cell in row.Cells)
            {
                var role = cell.IsHeader ? "TH" : "TD";
                var attributes = new Dictionary<string, string>
                {
                    ["row"] = cell.RowIndex.ToString(),
                    ["col"] = cell.ColumnIndex.ToString()
                };

                // Add scope attribute for header cells
                if (cell.IsHeader)
                {
                    // Heuristic: row 0 headers are column headers, others are row headers
                    attributes["scope"] = cell.RowIndex == 0 ? "col" : "row";
                }

                cellNodes.Add(new StructureNode(
                    Role: role,
                    TextContent: cell.Text,
                    Attributes: attributes,
                    Children: null)
                {
                    PageIndex = pageIndex
                });
            }

            rowNodes.Add(new StructureNode(
                Role: "TR",
                TextContent: null,
                Attributes: null,
                Children: cellNodes)
            {
                PageIndex = pageIndex
            });
        }

        return new StructureNode(
            Role: "Table",
            TextContent: null,
            Attributes: null,
            Children: rowNodes)
        {
            Bounds = table.Bounds,
            PageIndex = pageIndex
        };
    }

    /// <summary>
    /// Builds a Figure structure node with alt text, decorative flag, page index, and source image ID.
    /// These attributes are used by the PDF writer to locate and render the image.
    /// </summary>
    private static StructureNode BuildFigureNode(FigureBlock figure)
    {
        var attributes = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(figure.AltTextSuggestion))
        {
            attributes["alt"] = figure.AltTextSuggestion!;
        }

        if (figure.IsLikelyDecorative)
        {
            attributes["decorative"] = "true";
        }

        // Add page index for page mapping
        attributes["pageIndex"] = figure.PageIndex.ToString();

        // Add source image ID for ImageCache lookup
        if (!string.IsNullOrWhiteSpace(figure.SourceImageId))
        {
            attributes["sourceImageId"] = figure.SourceImageId!;
        }

        return new StructureNode(
            Role: "Figure",
            TextContent: null,
            Attributes: attributes.Count > 0 ? attributes : null,
            Children: null)
        {
            Bounds = figure.Bounds,
            PageIndex = figure.PageIndex
        };
    }

    /// <summary>
    /// Builds a Form Field structure node with appropriate role, attributes, and label.
    /// Maps LogicalFormFieldType to specific form roles (FormTextField, FormCheckBox, etc.).
    /// </summary>
    private static StructureNode BuildFormFieldNode(LogicalFormFieldBlock field)
    {
        // Map field type to structure role
        var fieldRole = field.FieldType switch
        {
            LogicalFormFieldType.Checkbox => "FormCheckBox",
            LogicalFormFieldType.Radio => "FormRadioButton",
            LogicalFormFieldType.ComboBox => "FormComboBox",
            LogicalFormFieldType.ListBox => "FormListBox",
            LogicalFormFieldType.Signature => "FormSignature",
            LogicalFormFieldType.MultilineText => "FormTextField",
            _ => "FormTextField" // Default to text field for Text, Date, Numeric, Other
        };

        // Build attributes
        var attributes = new Dictionary<string, string>
        {
            ["fieldName"] = field.FieldName,
            ["fieldType"] = field.FieldType.ToString()
        };

        if (!string.IsNullOrWhiteSpace(field.Tooltip))
        {
            attributes["tooltip"] = field.Tooltip!;
        }

        if (field.Options != null && field.Options.Count > 0)
        {
            // Store options as JSON array for later extraction
            attributes["optionsJson"] = System.Text.Json.JsonSerializer.Serialize(field.Options);
        }

        if (!string.IsNullOrWhiteSpace(field.DefaultValue))
        {
            attributes["defaultValue"] = field.DefaultValue!;
        }

        if (field.IsChecked.HasValue)
        {
            attributes["checked"] = field.IsChecked.Value.ToString().ToLowerInvariant();
        }

        // Build children (label if present)
        List<StructureNode>? children = null;
        if (!string.IsNullOrWhiteSpace(field.LabelText))
        {
            children = new List<StructureNode>
            {
                new StructureNode(
                    Role: "Label",
                    TextContent: field.LabelText,
                    Attributes: null,
                    Children: null)
                {
                    PageIndex = field.PageIndex
                }
            };
        }

        return new StructureNode(
            Role: fieldRole,
            TextContent: null, // Form field value is interactive, not static text
            Attributes: attributes,
            Children: children)
        {
            Bounds = field.Bounds,
            PageIndex = field.PageIndex
        };
    }
}
