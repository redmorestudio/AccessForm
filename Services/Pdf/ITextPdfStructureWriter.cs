using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.Pdf.Tagutils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.Remediation;
using WordToPdfConverter.Services.Remediation.Structure;

namespace WordToPdfConverter.Services.Pdf;

/// <summary>
/// iText7-based implementation of IPdfStructureWriter.
/// Creates or replaces PDF tag structure based on a StructureTree model.
/// Phase 6: Implements MCID linking to connect structure elements to actual PDF content.
/// </summary>
public sealed class ITextPdfStructureWriter : IPdfStructureWriter
{
    private readonly ILogger<ITextPdfStructureWriter> _logger;
    private readonly IContentMcidMarker? _mcidMarker;
    private readonly IConfiguration _configuration;

    // Mapping from StructureNode to its corresponding PdfStructElem
    private readonly Dictionary<StructureNode, PdfStructElem> _nodeToElementMap = new();

    public ITextPdfStructureWriter(
        ILogger<ITextPdfStructureWriter> logger,
        IConfiguration configuration,
        IContentMcidMarker? mcidMarker = null)
    {
        _logger = logger;
        _configuration = configuration;
        _mcidMarker = mcidMarker;
    }

    public byte[] Rewrite(byte[] originalPdf, StructureTree tree, StructureRebuildContext? context = null)
    {
        try
        {
            // Phase 6b Pipeline Integration: Guard against secondary rebuilds
            // This is a safety net in case finalizer is bypassed
            if (context != null && context.McidContentRewriteExecuted)
            {
                _logger.LogWarning(
                    "[ITEXT-STRUCTURE] Rebuild skipped — MCID content rewrite already executed. " +
                    "Proceeding with rebuild would overwrite BDC/EMC markers. " +
                    "Returning original PDF bytes.");
                return originalPdf;
            }

            if (context != null && context.StructureRebuildExecuted)
            {
                _logger.LogWarning(
                    "[ITEXT-STRUCTURE] Structure rebuild already executed. " +
                    "Multiple rebuilds may indicate a pipeline architecture issue. " +
                    "Consider using ITaggedPdfFinalizer for single final rebuild.");
                // Allow this for now (backward compatibility) but log the warning
            }

            _logger.LogInformation("[ITEXT-STRUCTURE] Starting PDF structure tree rebuild");

            // Clear mapping from any previous calls
            _nodeToElementMap.Clear();

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

            // Phase 6: MCID linking (if enabled and context available)
            var enableMcidLinking = _configuration.GetValue<bool>("AccessibilityRemediation:EnableMcidLinking", false);
            if (enableMcidLinking && _mcidMarker != null && context?.LayoutPlan != null)
            {
                _logger.LogInformation("[ITEXT-STRUCTURE] Starting Phase 6 MCID linking");
                AllocateMcids(pdfDoc, tree, context.LayoutPlan);
            }
            else
            {
                _logger.LogWarning(
                    "[ITEXT-STRUCTURE] MCID linking disabled or unavailable. " +
                    "Structure tree created without MCID content links. " +
                    $"EnableMcidLinking={enableMcidLinking}, Marker={_mcidMarker != null}, LayoutPlan={context?.LayoutPlan != null}");
            }

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

            // Track mapping from node to element for MCID allocation later
            _nodeToElementMap[node] = elem;

            // Add attributes if present
            if (node.Attributes != null && node.Attributes.Count > 0)
            {
                AddAttributes(elem, node);
            }

            // Add text content if present (creates a PdfMcr - Marked Content Reference)
            // TODO Phase 6: Implement MCID linking to actual PDF content
            //
            // CRITICAL LIMITATION: Structure elements are created but not linked to content via MCIDs
            // This means tags exist but aren't bound to page content - screen readers won't navigate correctly
            //
            // To fix, we need to:
            // 1. Mark all content in PDF content streams with BDC/EMC and MCID values
            // 2. Create PdfMcrNumber or PdfMcrDictionary objects that reference those MCIDs
            // 3. Add them as kids to structure elements via elem.AddKid()
            // 4. Ensure /Pg references point to correct pages
            //
            // Example (pseudo-code):
            //   var targetPage = pdfDoc.GetPage(pageNum);
            //   var mcid = AssignMcidToContentOnPage(targetPage, node.TextContent);
            //   elem.AddKid(new PdfMcrNumber(mcid, targetPage.GetPdfObject()));
            //
            // This requires content stream manipulation which is complex in iText7.
            // See Services/Pdf/ITextMcidContentMarker.cs for initial research.
            //
            if (!string.IsNullOrEmpty(node.TextContent))
            {
                _logger.LogDebug(
                    $"[ITEXT-STRUCTURE] Created {node.Role} element (MCID link pending): " +
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

    /// <summary>
    /// Phase 6: Allocates MCIDs to leaf content nodes and creates PdfMcrNumber references.
    /// </summary>
    private void AllocateMcids(PdfDocument pdfDoc, StructureTree tree, Models.Layout.PageLayoutPlan layoutPlan)
    {
        try
        {
            _logger.LogInformation("[ITEXT-MCID] Allocating MCIDs to leaf content nodes");

            // Build mcidTargets dictionary: (pageIndex, mcid) → McidTarget
            var mcidTargets = new Dictionary<(int PageIndex, int Mcid), McidTarget>();

            // Collect all nodes from tree
            var allNodes = new List<StructureNode>();
            WalkTree(tree.Nodes, allNodes);

            _logger.LogInformation($"[ITEXT-MCID] Found {allNodes.Count} total nodes in tree");

            // Filter to leaf content nodes
            var leafNodes = allNodes.Where(StructureNodeHelper.IsLeafContentNode).ToList();
            _logger.LogInformation($"[ITEXT-MCID] Found {leafNodes.Count} leaf content nodes");

            int mcidCount = 0;
            foreach (var node in leafNodes)
            {
                // Resolve page index for this node
                var pageIndex = StructureNodeHelper.ResolvePageIndex(node, layoutPlan);

                // Get the PdfPage (1-based in iText)
                var page = pdfDoc.GetPage(pageIndex + 1);

                // Get the PdfStructElem for this node
                if (!_nodeToElementMap.TryGetValue(node, out var structElem))
                {
                    _logger.LogWarning($"[ITEXT-MCID] Could not find PdfStructElem for node with role {node.Role}");
                    continue;
                }

                // Allocate MCID using PdfMcrNumber
                var mcr = new PdfMcrNumber(page, structElem);
                var mcid = mcr.GetMcid();

                // Add to node's MCID references
                node.McidReferences.Add(new McidReference
                {
                    PageIndex = pageIndex,
                    Mcid = mcid
                });

                // Add to targets for content marking
                mcidTargets[(pageIndex, mcid)] = new McidTarget
                {
                    Node = node,
                    Mcid = mcid
                };

                mcidCount++;
                _logger.LogDebug($"[ITEXT-MCID] Allocated MCID {mcid} on page {pageIndex} for {node.Role}");
            }

            _logger.LogInformation($"[ITEXT-MCID] Allocated {mcidCount} MCIDs across {mcidTargets.Keys.Select(k => k.PageIndex).Distinct().Count()} pages");

            // Phase 6b: Call marker to insert BDC/EMC in content streams (if enabled)
            var enableMcidContentRewrite = _configuration.GetValue<bool>("AccessibilityRemediation:EnableMcidContentRewrite", true);
            if (_mcidMarker != null && enableMcidContentRewrite)
            {
                _logger.LogInformation("[ITEXT-MCID] Calling Phase 6b content marker to rewrite streams with BDC/EMC operators");
                _mcidMarker.Apply(pdfDoc, mcidTargets);
            }
            else
            {
                _logger.LogWarning(
                    "[ITEXT-MCID] Phase 6b content stream rewriting disabled or unavailable. " +
                    $"Structure tree has MCIDs but content streams not marked. " +
                    $"EnableMcidContentRewrite={enableMcidContentRewrite}, Marker={_mcidMarker != null}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ITEXT-MCID] Failed to allocate MCIDs");
            throw;
        }
    }

    /// <summary>
    /// Recursively walks the structure tree to collect all nodes.
    /// </summary>
    private void WalkTree(IEnumerable<StructureNode> nodes, List<StructureNode> result)
    {
        foreach (var node in nodes)
        {
            result.Add(node);
            if (node.Children != null && node.Children.Count > 0)
            {
                WalkTree(node.Children, result);
            }
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
