using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using iText.IO.Source;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WordToPdfConverter.Services.Pdf;

/// <summary>
/// Phase 6b implementation: Full content stream reconstruction with MCID markers.
/// Rewrites page content streams to insert /Span <</MCID n>> BDC ... EMC wrappers
/// around content segments, enabling proper tag-to-content linking.
/// </summary>
public class ItextContentMcidMarker : IContentMcidMarker
{
    private readonly ILogger<ItextContentMcidMarker> _logger;
    private readonly IConfiguration _configuration;
    private const int MAX_LOOKBACK = 5; // For image segment start detection

    public ItextContentMcidMarker(
        ILogger<ItextContentMcidMarker> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public void Apply(
        PdfDocument doc,
        IReadOnlyDictionary<(int pageIndex, int mcid), McidTarget> targets)
    {
        try
        {
            _logger.LogInformation($"[MCID-MARKER-6b] Starting content stream rewriting for {targets.Count} MCID targets");

            // Group targets by page
            var targetsByPage = targets
                .GroupBy(kvp => kvp.Key.pageIndex)
                .ToDictionary(g => g.Key, g => g.ToList());

            _logger.LogInformation($"[MCID-MARKER-6b] Processing {targetsByPage.Count} pages");

            foreach (var (pageIndex, pageTargets) in targetsByPage)
            {
                ProcessPage(doc, pageIndex, pageTargets);
            }

            _logger.LogInformation("[MCID-MARKER-6b] Content stream rewriting complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MCID-MARKER-6b] Failed to apply MCID markers");

            var mcidRewriteMode = _configuration.GetValue<string>("AccessibilityRemediation:McidRewriteMode", "Experimental");
            if (mcidRewriteMode == "Strict")
            {
                throw;
            }
            else
            {
                _logger.LogWarning("[MCID-MARKER-6b] Experimental mode: continuing despite error");
            }
        }
    }

    private void ProcessPage(
        PdfDocument doc,
        int pageIndex,
        List<KeyValuePair<(int pageIndex, int mcid), McidTarget>> pageTargets)
    {
        try
        {
            var page = doc.GetPage(pageIndex + 1); // iText is 1-based
            _logger.LogInformation($"[MCID-MARKER-6b] Processing page {pageIndex} with {pageTargets.Count} targets");

            // Step 1: Extract operators with geometry
            var pageOps = ExtractPageOperators(page, pageIndex);
            if (pageOps == null)
            {
                _logger.LogWarning($"[MCID-MARKER-6b] Failed to extract operators for page {pageIndex}, skipping");
                return;
            }

            _logger.LogInformation($"[MCID-MARKER-6b] Extracted {pageOps.Ops.Count} operators from page {pageIndex}");

            // Step 2: Detect content segments
            var segments = DetectContentSegments(pageOps, page);
            _logger.LogInformation($"[MCID-MARKER-6b] Detected {segments.Count} content segments on page {pageIndex}");

            // Step 3: Compute segment-level geometry
            ComputeSegmentGeometry(segments, pageOps.Ops);

            // Step 4: Sort segments by visual order (Y desc, X asc)
            var sortedSegments = segments
                .OrderByDescending(s => s.ApproxTop)
                .ThenBy(s => s.ApproxLeft)
                .ToList();

            // Step 5: Sort targets by visual order (Y desc, X asc)
            // Bounds.Y represents top in PDF coordinates
            var sortedTargets = pageTargets
                .OrderByDescending(t => t.Value.Node.Bounds?.Y ?? float.MinValue)
                .ThenBy(t => t.Value.Node.Bounds?.X ?? float.MinValue)
                .ToList();

            // Step 6: Sequential MCID assignment
            var matchCount = Math.Min(sortedSegments.Count, sortedTargets.Count);
            for (int i = 0; i < matchCount; i++)
            {
                var segment = sortedSegments[i];
                var target = sortedTargets[i];

                // Get MCID from target node's McidReferences for this page
                var mcidRef = target.Value.Node.McidReferences
                    .FirstOrDefault(r => r.PageIndex == pageIndex);

                if (mcidRef != null)
                {
                    segment.AssignedMcid = mcidRef.Mcid;
                }
            }

            if (sortedSegments.Count != sortedTargets.Count)
            {
                _logger.LogWarning(
                    $"[MCID-MARKER-6b] Segment/target mismatch on page {pageIndex}: " +
                    $"{sortedSegments.Count} segments vs {sortedTargets.Count} targets");
            }

            _logger.LogInformation($"[MCID-MARKER-6b] Assigned MCIDs to {matchCount} segments on page {pageIndex}");

            // Step 7: Rebuild content stream with BDC/EMC markers
            RebuildPageContent(page, pageOps, sortedSegments);

            _logger.LogInformation($"[MCID-MARKER-6b] Successfully marked page {pageIndex}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[MCID-MARKER-6b] Failed to process page {pageIndex}");

            var mcidRewriteMode = _configuration.GetValue<string>("AccessibilityRemediation:McidRewriteMode", "Experimental");
            if (mcidRewriteMode == "Strict")
            {
                throw;
            }
            else
            {
                _logger.LogWarning($"[MCID-MARKER-6b] Experimental mode: skipping page {pageIndex}");
            }
        }
    }

    /// <summary>
    /// Extracts all operators from page content stream with geometry data.
    /// Uses PdfCanvasProcessor for geometry extraction.
    /// </summary>
    private PageOps? ExtractPageOperators(PdfPage page, int pageIndex)
    {
        try
        {
            var pageOps = new PageOps { PageIndex = pageIndex };

            // Parse content stream to extract operators
            var contentBytes = page.GetContentBytes();
            var tokenizer = new PdfTokenizer(new RandomAccessFileOrArray(
                new RandomAccessSourceFactory().CreateSource(contentBytes)));

            // Extract operators from content stream
            var ops = new List<PdfOp>();
            var operands = new List<object>();

            while (tokenizer.NextToken())
            {
                if (tokenizer.GetTokenType() == PdfTokenizer.TokenType.Other)
                {
                    var operatorName = Encoding.ASCII.GetString(tokenizer.GetByteContent());

                    // Create PdfOp with operands
                    ops.Add(new PdfOp
                    {
                        Operator = operatorName,
                        Operands = new List<object>(operands)
                    });

                    operands.Clear();
                }
                else
                {
                    // Collect operand
                    var operand = GetOperandValue(tokenizer);
                    if (operand != null)
                    {
                        operands.Add(operand);
                    }
                }
            }

            pageOps.Ops.AddRange(ops);

            // Now attach geometry using PdfCanvasProcessor
            AttachGeometryToOperators(page, pageOps);

            return pageOps;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[MCID-MARKER-6b] Failed to extract operators from page {pageIndex}");
            return null;
        }
    }

    /// <summary>
    /// Gets operand value from tokenizer based on token type.
    /// </summary>
    private object? GetOperandValue(PdfTokenizer tokenizer)
    {
        if (tokenizer.GetTokenType() == PdfTokenizer.TokenType.Number)
        {
            // Try to parse as float first (handles both ints and floats)
            var numString = Encoding.ASCII.GetString(tokenizer.GetByteContent());
            if (numString.Contains("."))
            {
                return float.Parse(numString, System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                return long.Parse(numString, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        else if (tokenizer.GetTokenType() == PdfTokenizer.TokenType.String)
        {
            return new PdfString(tokenizer.GetByteContent());
        }
        else if (tokenizer.GetTokenType() == PdfTokenizer.TokenType.Name)
        {
            return new PdfName(tokenizer.GetByteContent());
        }

        return null;
    }

    /// <summary>
    /// Attaches geometry data to operators using PdfCanvasProcessor.
    /// </summary>
    private void AttachGeometryToOperators(PdfPage page, PageOps pageOps)
    {
        try
        {
            var geometryListener = new GeometryAttachmentListener(pageOps, _logger);
            var processor = new PdfCanvasProcessor(geometryListener);
            processor.ProcessPageContent(page);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MCID-MARKER-6b] Failed to attach geometry, segments may lack positioning data");
        }
    }

    /// <summary>
    /// Detects content segments (BT/ET blocks, Do images, BI/EI inline images).
    /// </summary>
    private List<ContentSegment> DetectContentSegments(PageOps pageOps, PdfPage page)
    {
        var segments = new List<ContentSegment>();

        // Detect text segments (BT/ET pairs)
        segments.AddRange(DetectTextSegments(pageOps.Ops));

        // Detect image segments (Do operators)
        segments.AddRange(DetectImageSegments(pageOps.Ops, page));

        // Detect inline image segments (BI/EI pairs)
        segments.AddRange(DetectInlineImageSegments(pageOps.Ops));

        return segments;
    }

    /// <summary>
    /// Detects text segments (BT/ET blocks).
    /// </summary>
    private List<ContentSegment> DetectTextSegments(List<PdfOp> ops)
    {
        var segments = new List<ContentSegment>();
        int? btIndex = null;

        for (int i = 0; i < ops.Count; i++)
        {
            var op = ops[i];

            if (op.Operator == "BT")
            {
                if (btIndex.HasValue)
                {
                    _logger.LogWarning($"[MCID-MARKER-6b] Nested BT at index {i}, closing previous at {btIndex}");
                    // Close previous segment
                    segments.Add(new ContentSegment
                    {
                        StartIndex = btIndex.Value,
                        EndIndex = i - 1
                    });
                }
                btIndex = i;
            }
            else if (op.Operator == "ET")
            {
                if (btIndex.HasValue)
                {
                    segments.Add(new ContentSegment
                    {
                        StartIndex = btIndex.Value,
                        EndIndex = i
                    });
                    btIndex = null;
                }
                else
                {
                    _logger.LogWarning($"[MCID-MARKER-6b] ET without matching BT at index {i}");
                }
            }
        }

        if (btIndex.HasValue)
        {
            _logger.LogWarning($"[MCID-MARKER-6b] Unclosed BT at index {btIndex}");
        }

        return segments;
    }

    /// <summary>
    /// Detects image segments (Do operators) with bounded lookback.
    /// </summary>
    private List<ContentSegment> DetectImageSegments(List<PdfOp> ops, PdfPage page)
    {
        var segments = new List<ContentSegment>();
        var resources = page.GetResources();

        for (int i = 0; i < ops.Count; i++)
        {
            var op = ops[i];

            if (op.Operator == "Do" && op.Operands.Count > 0)
            {
                // Check if this is an image XObject
                var name = op.Operands[0] as PdfName;
                if (name != null && IsImageXObject(resources, name))
                {
                    // Bounded lookback for transform operators
                    var startIndex = i;
                    for (int j = i - 1; j >= Math.Max(0, i - MAX_LOOKBACK); j--)
                    {
                        if (ops[j].Operator == "cm" || ops[j].Operator == "q")
                        {
                            startIndex = j;
                            break;
                        }
                    }

                    segments.Add(new ContentSegment
                    {
                        StartIndex = startIndex,
                        EndIndex = i
                    });
                }
            }
        }

        return segments;
    }

    /// <summary>
    /// Checks if an XObject is an image.
    /// </summary>
    private bool IsImageXObject(PdfResources resources, PdfName name)
    {
        try
        {
            var xObject = resources.GetResource(PdfName.XObject);
            if (xObject is PdfDictionary xObjDict)
            {
                var stream = xObjDict.GetAsStream(name);
                if (stream != null)
                {
                    var subtype = stream.GetAsName(PdfName.Subtype);
                    return subtype != null && subtype.Equals(PdfName.Image);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, $"[MCID-MARKER-6b] Failed to check XObject type for {name}");
        }

        return false;
    }

    /// <summary>
    /// Detects inline image segments (BI/EI pairs).
    /// </summary>
    private List<ContentSegment> DetectInlineImageSegments(List<PdfOp> ops)
    {
        var segments = new List<ContentSegment>();
        int? biIndex = null;

        for (int i = 0; i < ops.Count; i++)
        {
            var op = ops[i];

            if (op.Operator == "BI")
            {
                biIndex = i;
            }
            else if (op.Operator == "EI" && biIndex.HasValue)
            {
                segments.Add(new ContentSegment
                {
                    StartIndex = biIndex.Value,
                    EndIndex = i
                });
                biIndex = null;
            }
        }

        return segments;
    }

    /// <summary>
    /// Computes segment-level geometry from operator geometry.
    /// </summary>
    private void ComputeSegmentGeometry(List<ContentSegment> segments, List<PdfOp> ops)
    {
        foreach (var segment in segments)
        {
            var tops = new List<float>();
            var lefts = new List<float>();

            for (int i = segment.StartIndex; i <= segment.EndIndex && i < ops.Count; i++)
            {
                var op = ops[i];
                if (op.ApproxTop.HasValue) tops.Add(op.ApproxTop.Value);
                if (op.ApproxLeft.HasValue) lefts.Add(op.ApproxLeft.Value);
            }

            segment.ApproxTop = tops.Count > 0 ? tops.Min() : float.MaxValue;
            segment.ApproxLeft = lefts.Count > 0 ? lefts.Min() : float.MaxValue;
        }
    }

    /// <summary>
    /// Rebuilds page content stream with BDC/EMC markers injected.
    /// </summary>
    private void RebuildPageContent(PdfPage page, PageOps pageOps, List<ContentSegment> segments)
    {
        try
        {
            var markedSegments = segments.Where(s => s.AssignedMcid.HasValue).ToList();
            if (markedSegments.Count == 0)
            {
                _logger.LogDebug("[MCID-MARKER-6b] No segments to mark on this page");
                return;
            }

            // Build lookup maps
            var segmentsByStart = markedSegments.ToDictionary(s => s.StartIndex);
            var segmentsByEnd = markedSegments.ToDictionary(s => s.EndIndex);

            // Build new content stream
            using var ms = new MemoryStream();
            var output = new PdfOutputStream(ms);

            for (int i = 0; i < pageOps.Ops.Count; i++)
            {
                // Inject BDC before segment start
                if (segmentsByStart.TryGetValue(i, out var startSeg))
                {
                    WriteBDC(output, startSeg.AssignedMcid.Value);
                }

                // Write original operator
                WriteOperator(output, pageOps.Ops[i]);

                // Inject EMC after segment end
                if (segmentsByEnd.TryGetValue(i, out var endSeg))
                {
                    WriteEMC(output);
                }
            }

            output.Flush();

            // Replace page content
            var newContent = ms.ToArray();
            page.Put(PdfName.Contents, new PdfStream(newContent));
            page.SetModified();

            _logger.LogInformation($"[MCID-MARKER-6b] Rewrote content stream with {markedSegments.Count} BDC/EMC pairs");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MCID-MARKER-6b] Failed to rebuild page content");
            throw;
        }
    }

    /// <summary>
    /// Writes BDC operator: /Span <</MCID n>> BDC
    /// </summary>
    private void WriteBDC(PdfOutputStream output, int mcid)
    {
        output.WriteBytes(Encoding.ASCII.GetBytes("/Span "));
        output.WriteBytes(Encoding.ASCII.GetBytes("<< /MCID "));
        output.WriteInteger(mcid);
        output.WriteBytes(Encoding.ASCII.GetBytes(" >> "));
        output.WriteBytes(Encoding.ASCII.GetBytes("BDC"));
        output.WriteNewLine();
    }

    /// <summary>
    /// Writes EMC operator.
    /// </summary>
    private void WriteEMC(PdfOutputStream output)
    {
        output.WriteBytes(Encoding.ASCII.GetBytes("EMC"));
        output.WriteNewLine();
    }

    /// <summary>
    /// Writes a single PDF operator with its operands.
    /// </summary>
    private void WriteOperator(PdfOutputStream output, PdfOp op)
    {
        // Write operands
        foreach (var operand in op.Operands)
        {
            WriteOperand(output, operand);
            output.WriteSpace();
        }

        // Write operator
        output.WriteBytes(Encoding.ASCII.GetBytes(op.Operator));
        output.WriteNewLine();
    }

    /// <summary>
    /// Writes a single operand to the output stream.
    /// </summary>
    private void WriteOperand(PdfOutputStream output, object operand)
    {
        switch (operand)
        {
            case double d:
                output.WriteDouble(d);
                break;
            case float f:
                output.WriteFloat(f);
                break;
            case long l:
                output.WriteLong(l);
                break;
            case int i:
                output.WriteInteger(i);
                break;
            case PdfName name:
                // Write name with its slash prefix
                output.WriteBytes(Encoding.ASCII.GetBytes("/" + name.GetValue()));
                break;
            case PdfString str:
                // Write string with PDF string syntax
                var strValue = str.GetValue();
                output.WriteBytes(Encoding.ASCII.GetBytes("(" + strValue.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)") + ")"));
                break;
            default:
                output.WriteBytes(Encoding.ASCII.GetBytes(operand.ToString() ?? ""));
                break;
        }
    }
}

/// <summary>
/// Represents a single PDF operator with operands and geometry.
/// </summary>
internal sealed class PdfOp
{
    public string Operator { get; set; } = "";
    public List<object> Operands { get; set; } = new();
    public float? ApproxTop { get; set; }
    public float? ApproxLeft { get; set; }
}

/// <summary>
/// Collection of operators for a page.
/// </summary>
internal sealed class PageOps
{
    public int PageIndex { get; set; }
    public List<PdfOp> Ops { get; } = new();
}

/// <summary>
/// Represents a content segment (contiguous range of operators).
/// </summary>
internal sealed class ContentSegment
{
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public float ApproxTop { get; set; }
    public float ApproxLeft { get; set; }
    public int? AssignedMcid { get; set; }
}

/// <summary>
/// Event listener that attaches geometry to operators during PdfCanvasProcessor pass.
/// </summary>
internal class GeometryAttachmentListener : IEventListener
{
    private readonly PageOps _pageOps;
    private readonly ILogger _logger;
    private int _currentTextOpIndex = 0;
    private int _currentImageOpIndex = 0;

    public GeometryAttachmentListener(PageOps pageOps, ILogger logger)
    {
        _pageOps = pageOps;
        _logger = logger;
    }

    public void EventOccurred(IEventData data, EventType type)
    {
        try
        {
            if (type == EventType.RENDER_TEXT && data is TextRenderInfo textInfo)
            {
                // Find next text operator (Tj, TJ, ', ")
                for (int i = _currentTextOpIndex; i < _pageOps.Ops.Count; i++)
                {
                    var op = _pageOps.Ops[i];
                    if (op.Operator == "Tj" || op.Operator == "TJ" ||
                        op.Operator == "'" || op.Operator == "\"")
                    {
                        var baseline = textInfo.GetBaseline().GetStartPoint();
                        op.ApproxTop = baseline.Get(Vector.I2);
                        op.ApproxLeft = baseline.Get(Vector.I1);
                        _currentTextOpIndex = i + 1;
                        break;
                    }
                }
            }
            else if (type == EventType.RENDER_IMAGE && data is ImageRenderInfo imageInfo)
            {
                // Find next Do operator
                for (int i = _currentImageOpIndex; i < _pageOps.Ops.Count; i++)
                {
                    var op = _pageOps.Ops[i];
                    if (op.Operator == "Do")
                    {
                        var startPoint = imageInfo.GetStartPoint();
                        op.ApproxTop = startPoint.Get(Vector.I2);
                        op.ApproxLeft = startPoint.Get(Vector.I1);
                        _currentImageOpIndex = i + 1;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[MCID-MARKER-6b] Failed to attach geometry to operator");
        }
    }

    public ICollection<EventType> GetSupportedEvents()
    {
        return new HashSet<EventType> { EventType.RENDER_TEXT, EventType.RENDER_IMAGE };
    }
}
