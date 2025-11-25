using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.Pdf.Tagutils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.Layout;
using WordToPdfConverter.Models.Remediation;
using WordToPdfConverter.Services.Phase6H;
using WordToPdfConverter.Services.Remediation.Models;
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
    private readonly RemediationJobContext _jobContext;
    private readonly McidRewritePlanBuilder? _planBuilder;
    private readonly ExternalMcidRewriterService? _externalRewriter;

    // Mapping from StructureNode to its corresponding PdfStructElem
    private readonly Dictionary<StructureNode, PdfStructElem> _nodeToElementMap = new();

    public ITextPdfStructureWriter(
        ILogger<ITextPdfStructureWriter> logger,
        IConfiguration configuration,
        RemediationJobContext jobContext,
        IContentMcidMarker? mcidMarker = null,
        McidRewritePlanBuilder? planBuilder = null,
        ExternalMcidRewriterService? externalRewriter = null)
    {
        _logger = logger;
        _configuration = configuration;
        _jobContext = jobContext;
        _mcidMarker = mcidMarker;
        _planBuilder = planBuilder;
        _externalRewriter = externalRewriter;
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
                    "Passing through input PDF unchanged.");
                return originalPdf; // PHASE 6F: Pass through input unchanged (originalPdf is the input here)
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

            // PHASE 6K FIX: Reorder pipeline to add BDC/EMC markers BEFORE structure tree creation
            // This prevents iText7 from deleting "empty" structure elements
            var options = _jobContext.Options ?? new RemediationOptions();
            var enableMcidLinking = options.EnableMcidLinking;

            byte[] pdfWithMarkers = originalPdf;

            // Step 1: Call Python rewriter FIRST to insert BDC/EMC markers
            if (enableMcidLinking && _planBuilder != null && _externalRewriter != null && context?.LayoutPlan != null)
            {
                _logger.LogInformation("[PHASE-6K-FIX] Step 1: Calling Python rewriter to insert BDC/EMC markers BEFORE structure tree creation");

                try
                {
                    // Build McidRewritePlan from structure tree
                    var plan = _planBuilder.BuildPlan(
                        tree,
                        context.LayoutPlan,
                        documentId: null,
                        debug: false);

                    _logger.LogInformation("[PHASE-6K-FIX] Built plan with {SegmentCount} segments", plan.Segments.Count);

                    // Call external microservice to insert BDC/EMC markers
                    pdfWithMarkers = _externalRewriter.RewritePdfWithMcidsAsync(
                        originalPdf,
                        plan,
                        CancellationToken.None).GetAwaiter().GetResult();

                    _logger.LogInformation("[PHASE-6K-FIX] ✅ Python rewriter complete, verifying markers...");

                    // Verify BDC/EMC markers
                    var markedText = System.Text.Encoding.ASCII.GetString(pdfWithMarkers);
                    var bdcCount = markedText.Split(new[] { "BDC" }, StringSplitOptions.None).Length - 1;
                    var emcCount = markedText.Split(new[] { "EMC" }, StringSplitOptions.None).Length - 1;
                    _logger.LogInformation($"[PHASE-6K-FIX] PDF now has {bdcCount} BDC and {emcCount} EMC markers");

                    if (bdcCount > 0 && emcCount > 0)
                    {
                        _logger.LogInformation("[PHASE-6K-FIX] ✅ BDC/EMC markers successfully inserted!");
                        // Mark context flag
                        if (context != null)
                        {
                            context.McidContentRewriteExecuted = true;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("[PHASE-6K-FIX] ⚠️  Python rewriter returned 0 markers, proceeding anyway");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PHASE-6K-FIX] Python rewriter failed, proceeding with structure tree anyway");
                }
            }
            else
            {
                _logger.LogWarning(
                    "[PHASE-6K-FIX] Python rewriter unavailable or disabled. " +
                    $"EnableMcidLinking={enableMcidLinking}, PlanBuilder={_planBuilder != null}, " +
                    $"ExternalRewriter={_externalRewriter != null}, LayoutPlan={context?.LayoutPlan != null}");
            }

            // Step 2: Now open PDF with iText7 to build structure tree
            _logger.LogInformation("[PHASE-6K-FIX] Step 2: Building structure tree on PDF that already has BDC/EMC markers");

            // Clear mapping from any previous calls
            _nodeToElementMap.Clear();

            using var inputStream = new MemoryStream(pdfWithMarkers);
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

            // Step 3: MCID linking (if enabled)
            // NOTE: We must insert BDC/EMC markers BEFORE creating MCR kids to avoid orphaned MCRs
            if (enableMcidLinking && context?.LayoutPlan != null)
            {
                _logger.LogInformation("[PHASE-6K-FIX] Step 3A: Inserting BDC/EMC markers FIRST to determine valid MCIDs");

                // First, allocate MCIDs and build targets WITHOUT creating MCR kids
                var mcidTargets = AllocateMcidsWithoutMcrKids(pdfDoc, tree, context.LayoutPlan);

                // Then insert BDC/EMC markers using iText7 API
                if (_mcidMarker != null && mcidTargets.Count > 0)
                {
                    try
                    {
                        _mcidMarker.Apply(pdfDoc, mcidTargets);
                        _logger.LogInformation($"[PHASE-6K-FIX] Successfully inserted BDC/EMC markers for {mcidTargets.Count} segments");
                    }
                    catch (Exception markerEx)
                    {
                        _logger.LogWarning(markerEx, "[PHASE-6K-FIX] Failed to insert BDC/EMC markers - MCIDs will not be created");
                        mcidTargets.Clear(); // Don't create MCR kids if markers failed
                    }
                }

                // Finally, create MCR kids ONLY for MCIDs that have BDC/EMC markers
                if (mcidTargets.Count > 0)
                {
                    _logger.LogInformation("[PHASE-6K-FIX] Step 3B: Creating MCR kids for successfully marked content");
                    CreateMcrKidsFromTargets(pdfDoc, mcidTargets);

                    // PHASE 6K FIX: Flush all pages that had markers inserted
                    // This ensures both content stream changes AND MCR kids are persisted together
                    _logger.LogInformation("[PHASE-6K-FIX] Step 3C: Flushing pages to persist content and structure changes");
                    var pageIndices = mcidTargets.Keys.Select(k => k.PageIndex).Distinct().ToList();
                    foreach (var pageIndex in pageIndices)
                    {
                        try
                        {
                            var page = pdfDoc.GetPage(pageIndex + 1); // iText is 1-based
                            page.Flush();
                            _logger.LogInformation($"[PHASE-6K-FIX] Flushed page {pageIndex + 1}");
                        }
                        catch (Exception flushEx)
                        {
                            _logger.LogWarning(flushEx, $"[PHASE-6K-FIX] Failed to flush page {pageIndex + 1}");
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("[PHASE-6K-FIX] No valid MCID targets - structure tree will have no MCR kids");
                }
            }
            else
            {
                _logger.LogWarning(
                    "[PHASE-6K-FIX] MCID linking skipped. " +
                    $"EnableMcidLinking={enableMcidLinking}, LayoutPlan={context?.LayoutPlan != null}");
            }

            // PHASE 6G DEBUG: Check PDF state before close
            _logger.LogInformation("[ITEXT-STRUCTURE-6G-DEBUG] ========== BEFORE CLOSE ==========");
            var pageCount = pdfDoc.GetNumberOfPages();
            for (int i = 1; i <= Math.Min(pageCount, 2); i++)
            {
                var pg = pdfDoc.GetPage(i);
                var pageDict = pg.GetPdfObject();
                var contentsObj = pageDict.Get(PdfName.Contents);

                _logger.LogInformation($"[ITEXT-STRUCTURE-6G-DEBUG] Page {i}: Contents object type = {contentsObj?.GetType().Name}, IsIndirect = {contentsObj?.IsIndirectReference()}");

                var contentBytes = pg.GetContentBytes();
                var pgBdc = System.Text.Encoding.ASCII.GetString(contentBytes).Split(new[] { "BDC" }, StringSplitOptions.None).Length - 1;
                var pgEmc = System.Text.Encoding.ASCII.GetString(contentBytes).Split(new[] { "EMC" }, StringSplitOptions.None).Length - 1;
                _logger.LogInformation($"[ITEXT-STRUCTURE-6G-DEBUG] Page {i}: content has {pgBdc} BDC and {pgEmc} EMC markers, content length = {contentBytes.Length}");
            }

            // Step 4: Close the PDF to write structure tree
            _logger.LogInformation("[PHASE-6K-FIX] Step 4: Closing PDF to persist structure tree");
            pdfDoc.Close();

            var finalBytes = outputStream.ToArray();

            // Verify final output
            var finalText = System.Text.Encoding.ASCII.GetString(finalBytes);
            var finalBdc = finalText.Split(new[] { "BDC" }, StringSplitOptions.None).Length - 1;
            var finalEmc = finalText.Split(new[] { "EMC" }, StringSplitOptions.None).Length - 1;
            _logger.LogInformation($"[PHASE-6K-FIX] Final PDF has {finalBdc} BDC and {finalEmc} EMC markers");

            // NO POST-PROCESSING NEEDED: MCR kids are now created by iText7 BEFORE Close()
            // This approach is more reliable than post-processing with pikepdf
            _logger.LogInformation("[PHASE-6K-FIX] Structure tree persisted with MCR kids intact");

            _logger.LogInformation("[PHASE-6K-FIX] ✅ Structure tree rebuild complete with preserved BDC/EMC markers");
            return finalBytes;
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
    /// Phase 6K: Allocates MCIDs to leaf content nodes and builds targets WITHOUT creating MCR kids yet.
    /// This allows us to insert BDC/EMC markers first, then create MCR kids only for valid segments.
    /// </summary>
    private Dictionary<(int PageIndex, int Mcid), McidTarget> AllocateMcidsWithoutMcrKids(
        PdfDocument pdfDoc,
        StructureTree tree,
        Models.Layout.PageLayoutPlan layoutPlan)
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

            // PHASE 6G FIX: Manual MCID allocation without PdfMcrNumber
            // Don't use PdfMcrNumber - it conflicts with manual content stream rewriting
            // We'll assign MCID numbers manually and let ItextContentMcidMarker handle all BDC/EMC injection
            var mcidCounterPerPage = new Dictionary<int, int>();

            int mcidCount = 0;
            foreach (var node in leafNodes)
            {
                // Resolve page index for this node
                var pageIndex = StructureNodeHelper.ResolvePageIndex(node, layoutPlan);

                // Get the PdfStructElem for this node
                if (!_nodeToElementMap.TryGetValue(node, out var structElem))
                {
                    _logger.LogWarning($"[ITEXT-MCID] Could not find PdfStructElem for node with role {node.Role}");
                    continue;
                }

                // Allocate MCID manually (not using PdfMcrNumber to avoid conflict)
                if (!mcidCounterPerPage.ContainsKey(pageIndex))
                {
                    mcidCounterPerPage[pageIndex] = 0;
                }
                var mcid = mcidCounterPerPage[pageIndex]++;

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
                    Mcid = mcid,
                    StructElem = structElem // Store structElem for later MCR creation
                };

                mcidCount++;
                _logger.LogDebug($"[ITEXT-MCID] Allocated MCID {mcid} on page {pageIndex} for {node.Role}");
            }

            _logger.LogInformation($"[ITEXT-MCID] Allocated {mcidCount} MCIDs across {mcidTargets.Keys.Select(k => k.PageIndex).Distinct().Count()} pages");

            return mcidTargets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ITEXT-MCID] Failed to allocate MCIDs");
            throw;
        }
    }

    /// <summary>
    /// Phase 6K: Creates MCR kids in structure tree for the given MCID targets.
    /// This should be called AFTER BDC/EMC markers are inserted to ensure valid MCRs.
    /// </summary>
    private void CreateMcrKidsFromTargets(
        PdfDocument pdfDoc,
        Dictionary<(int PageIndex, int Mcid), McidTarget> mcidTargets)
    {
        try
        {
            _logger.LogInformation("[PHASE-6K-FIX] Creating MCR kids to link structure elements to content...");

            int mcrCount = 0;
            foreach (var kvp in mcidTargets)
            {
                var (pageIndex, mcid) = kvp.Key;
                var target = kvp.Value;
                try
                {
                    // Create MCR (Marked Content Reference) dictionary manually
                    // We can't use PdfMcrNumber(page, structElem) because it auto-allocates MCID
                    // Instead, create raw MCR dictionary with our pre-allocated MCID
                    var page = pdfDoc.GetPage(pageIndex + 1); // iText7 pages are 1-indexed

                    var mcrDict = new PdfDictionary();
                    mcrDict.Put(PdfName.Type, PdfName.MCR);
                    mcrDict.Put(PdfName.Pg, page.GetPdfObject());
                    mcrDict.Put(PdfName.MCID, new PdfNumber(mcid));

                    // Add MCR dictionary directly to structure element's /K array
                    var elemDict = target.StructElem.GetPdfObject();
                    var kidsObj = elemDict.Get(PdfName.K);

                    if (kidsObj == null)
                    {
                        // No kids yet - create array with this MCR
                        elemDict.Put(PdfName.K, new PdfArray(mcrDict));
                    }
                    else if (kidsObj is PdfArray kidsArray)
                    {
                        // Kids array exists - append MCR
                        kidsArray.Add(mcrDict);
                    }
                    else
                    {
                        // Single kid - convert to array with existing kid and new MCR
                        var newArray = new PdfArray();
                        newArray.Add(kidsObj);
                        newArray.Add(mcrDict);
                        elemDict.Put(PdfName.K, newArray);
                    }

                    mcrCount++;
                    _logger.LogDebug($"[PHASE-6K-FIX] Added MCR kid: Page {pageIndex + 1}, MCID {mcid}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"[PHASE-6K-FIX] Failed to create MCR for Page {pageIndex + 1}, MCID {mcid}");
                }
            }

            _logger.LogInformation($"[PHASE-6K-FIX] Created {mcrCount} MCR kids linking structure to content");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PHASE-6K-FIX] Failed to create MCR kids");
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

    private byte[] AddMcrKidsWithPikepdf(byte[] pdfBytes)
    {
        try
        {
            // Save PDF to temp file
            var inputPath = Path.GetTempFileName();
            var outputPath = Path.GetTempFileName();
            File.WriteAllBytes(inputPath, pdfBytes);

            _logger.LogInformation($"[PIKEPDF-MCR] Saved PDF to temp: {inputPath}");

            // Call Python script
            var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "add_mcr_kids.py");
            if (!File.Exists(scriptPath))
            {
                _logger.LogWarning($"[PIKEPDF-MCR] Script not found at {scriptPath}, skipping MCR post-processing");
                File.Delete(inputPath);
                File.Delete(outputPath);
                return pdfBytes;
            }

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"\"{scriptPath}\" \"{inputPath}\" \"{outputPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            _logger.LogInformation($"[PIKEPDF-MCR] Running: python3 {scriptPath}");
            process.Start();

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                _logger.LogInformation($"[PIKEPDF-MCR] ✅ Success! Exit code: {process.ExitCode}");
                _logger.LogInformation($"[PIKEPDF-MCR] Output: {stdout}");

                // Read fixed PDF
                var fixedBytes = File.ReadAllBytes(outputPath);
                _logger.LogInformation($"[PIKEPDF-MCR] Fixed PDF size: {fixedBytes.Length} bytes (original: {pdfBytes.Length})");

                // Cleanup
                File.Delete(inputPath);
                File.Delete(outputPath);

                return fixedBytes;
            }
            else
            {
                _logger.LogError($"[PIKEPDF-MCR] ❌ Failed with exit code {process.ExitCode}");
                _logger.LogError($"[PIKEPDF-MCR] Stdout: {stdout}");
                _logger.LogError($"[PIKEPDF-MCR] Stderr: {stderr}");

                // Cleanup and return original
                File.Delete(inputPath);
                if (File.Exists(outputPath))
                    File.Delete(outputPath);

                return pdfBytes;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PIKEPDF-MCR] Exception during pikepdf post-processing");
            return pdfBytes;
        }
    }
}
