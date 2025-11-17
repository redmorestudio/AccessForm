using System;
using System.IO;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.Pdf.Tagutils;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.Remediation;
using WordToPdfConverter.Services.Remediation.Structure;

namespace WordToPdfConverter.Services.Pdf;

/// <summary>
/// iText7-based implementation of IPdfStructureWriter.
/// Creates or replaces PDF tag structure based on a StructureTree model.
///
/// CURRENT LIMITATION: This implementation creates the structure tree hierarchy
/// but does NOT link structure elements to actual PDF content via MCIDs yet.
/// Screen readers will see the structure but may not navigate to content correctly.
/// This is a known limitation documented in the spec for Phase 6.
/// </summary>
public sealed class ITextPdfStructureWriter : IPdfStructureWriter
{
    private readonly ILogger<ITextPdfStructureWriter> _logger;

    public ITextPdfStructureWriter(ILogger<ITextPdfStructureWriter> logger)
    {
        _logger = logger;
    }

    public byte[] Rewrite(byte[] originalPdf, StructureTree tree, StructureRebuildContext? context = null)
    {
        try
        {
            _logger.LogInformation("[ITEXT-STRUCTURE] Starting PDF structure tree rebuild");

            using var inputStream = new MemoryStream(originalPdf);
            using var outputStream = new MemoryStream();
            using var reader = new PdfReader(inputStream);
            using var writer = new PdfWriter(outputStream);
            using var pdfDoc = new PdfDocument(reader, writer);

            // Ensure document is tagged
            if (!pdfDoc.IsTagged())
            {
                _logger.LogInformation("[ITEXT-STRUCTURE] Marking document as tagged");
                pdfDoc.SetTagged();
            }

            // Remove existing structure tree
            RemoveExistingStructureTree(pdfDoc);

            // Create new structure tree from our model
            CreateStructureTree(pdfDoc, tree);

            // Log warning about MCID limitation
            _logger.LogWarning(
                "[ITEXT-STRUCTURE] WARNING: Structure tree created without MCID content links. " +
                "Screen readers can see structure but may not navigate to content correctly. " +
                "MCID implementation is deferred (see AI-STRUCTURE-REBUILD-SPEC.md Phase 6).");

            pdfDoc.Close();

            _logger.LogInformation("[ITEXT-STRUCTURE] Structure tree rebuild complete");
            return outputStream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ITEXT-STRUCTURE] Failed to rewrite PDF structure");
            return originalPdf; // Return original on failure
        }
    }

    private void RemoveExistingStructureTree(PdfDocument pdfDoc)
    {
        try
        {
            var catalog = pdfDoc.GetCatalog();
            var catalogDict = catalog.GetPdfObject();

            if (catalogDict.ContainsKey(PdfName.StructTreeRoot))
            {
                _logger.LogInformation("[ITEXT-STRUCTURE] Removing existing structure tree");
                catalogDict.Remove(PdfName.StructTreeRoot);

                // Also remove from the tagged document if present
                if (pdfDoc.IsTagged())
                {
                    // iText7 manages this internally, just log
                    _logger.LogInformation("[ITEXT-STRUCTURE] Cleared tagged document structure");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ITEXT-STRUCTURE] Could not remove existing structure tree, proceeding anyway");
        }
    }

    private void CreateStructureTree(PdfDocument pdfDoc, StructureTree tree)
    {
        try
        {
            // Get or create structure tree root
            var tagStructureContext = pdfDoc.GetTagStructureContext();
            tagStructureContext.SetForbidUnknownRoles(false); // Allow custom roles

            // Create role map
            CreateRoleMap(pdfDoc);

            _logger.LogInformation($"[ITEXT-STRUCTURE] Creating structure tree with {tree.Nodes.Count} root nodes");

            // Create structure elements for each root node
            var nodeCount = 0;
            foreach (var node in tree.Nodes)
            {
                CreateStructureElement(pdfDoc, null, node, ref nodeCount);
            }

            _logger.LogInformation($"[ITEXT-STRUCTURE] Created {nodeCount} structure elements");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ITEXT-STRUCTURE] Failed to create structure tree");
            throw;
        }
    }

    private void CreateRoleMap(PdfDocument pdfDoc)
    {
        try
        {
            var structTreeRoot = pdfDoc.GetStructTreeRoot();
            var roleMap = new PdfDictionary();

            // Map custom roles to standard PDF roles
            // Document role
            roleMap.Put(new PdfName("Document"), PdfName.Document);

            // Heading roles (H1-H6)
            for (int i = 1; i <= 6; i++)
            {
                roleMap.Put(new PdfName($"H{i}"), new PdfName($"H{i}"));
            }

            // Paragraph role
            roleMap.Put(new PdfName("P"), PdfName.P);

            // Table roles
            roleMap.Put(new PdfName("Table"), PdfName.Table);
            roleMap.Put(new PdfName("TR"), PdfName.TR);
            roleMap.Put(new PdfName("TH"), PdfName.TH);
            roleMap.Put(new PdfName("TD"), PdfName.TD);

            // Figure role
            roleMap.Put(new PdfName("Figure"), PdfName.Figure);

            structTreeRoot.GetPdfObject().Put(PdfName.RoleMap, roleMap);

            _logger.LogInformation("[ITEXT-STRUCTURE] Created role map with standard PDF roles");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ITEXT-STRUCTURE] Could not create role map");
        }
    }

    private PdfStructElem CreateStructureElement(
        PdfDocument pdfDoc,
        PdfStructElem parent,
        StructureNode node,
        ref int nodeCount)
    {
        try
        {
            nodeCount++;

            // Create structure element with role
            var role = new PdfName(node.Role);
            PdfStructElem elem;

            if (parent == null)
            {
                // Root level element
                elem = pdfDoc.GetStructTreeRoot().AddKid(new PdfStructElem(pdfDoc, role));
            }
            else
            {
                // Child element
                elem = parent.AddKid(new PdfStructElem(pdfDoc, role));
            }

            // Add attributes if present
            if (node.Attributes != null && node.Attributes.Count > 0)
            {
                AddAttributes(elem, node);
            }

            // Add text content if present (creates a PdfMcr - Marked Content Reference)
            // Note: This doesn't actually link to PDF content yet (MCID linking is deferred)
            if (!string.IsNullOrEmpty(node.TextContent))
            {
                // TODO: Implement MCID linking to actual PDF content
                // For now, just log the text content - structure exists but isn't linked
                _logger.LogDebug(
                    $"[ITEXT-STRUCTURE] Created {node.Role} element with text (MCID link deferred): " +
                    $"\"{(node.TextContent.Length > 50 ? node.TextContent.Substring(0, 50) + "..." : node.TextContent)}\"");
            }

            // Recursively create children
            if (node.Children != null && node.Children.Count > 0)
            {
                foreach (var child in node.Children)
                {
                    CreateStructureElement(pdfDoc, elem, child, ref nodeCount);
                }
            }

            return elem;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"[ITEXT-STRUCTURE] Failed to create structure element for role {node.Role}");
            throw;
        }
    }

    private void AddAttributes(PdfStructElem elem, StructureNode node)
    {
        try
        {
            var attributesDict = new PdfDictionary();

            foreach (var attr in node.Attributes!)
            {
                switch (attr.Key.ToLowerInvariant())
                {
                    case "alt":
                        // Alt text for figures
                        elem.SetAlt(new PdfString(attr.Value));
                        break;

                    case "scope":
                        // Table header scope (row/col)
                        attributesDict.Put(new PdfName("Scope"), new PdfName(attr.Value));
                        break;

                    case "row":
                        // Table cell row index
                        if (int.TryParse(attr.Value, out var rowIndex))
                        {
                            attributesDict.Put(new PdfName("RowIndex"), new PdfNumber(rowIndex));
                        }
                        break;

                    case "col":
                        // Table cell column index
                        if (int.TryParse(attr.Value, out var colIndex))
                        {
                            attributesDict.Put(new PdfName("ColIndex"), new PdfNumber(colIndex));
                        }
                        break;

                    case "decorative":
                        // Figure decorative flag
                        if (bool.TryParse(attr.Value, out var isDecorative) && isDecorative)
                        {
                            // Mark as artifact (decorative content)
                            attributesDict.Put(new PdfName("Decorative"), PdfBoolean.TRUE);
                        }
                        break;

                    default:
                        // Generic attribute
                        attributesDict.Put(new PdfName(attr.Key), new PdfString(attr.Value));
                        break;
                }
            }

            if (attributesDict.Size() > 0)
            {
                elem.GetPdfObject().Put(PdfName.A, attributesDict);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"[ITEXT-STRUCTURE] Failed to add attributes to element");
        }
    }
}
