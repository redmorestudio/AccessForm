using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation.Fixes
{
    /// <summary>
    /// Handles cross-reference links like "See Figure 5" or "Table 3 shows"
    /// Provides enhanced alt text for these special link types
    /// </summary>
    public class CrossReferenceLinkService : IRemediationService
    {
        private readonly ILogger<CrossReferenceLinkService> _logger;
        private readonly AccessFormServer.Services.AnthropicService _anthropic;

        public string ServiceName => "Cross-Reference Link Service";
        public ViolationCategory TargetCategory => ViolationCategory.Links;
        public int Priority => 5; // Run before general link alt text
        public bool IsRequired => false;

        // Patterns for detecting cross-references
        private static readonly Regex FigureRefPattern = new Regex(
            @"\b(see|refer to|shown in|as in|from)\s+(figure|fig\.?|image)\s+(\d+|[a-z])\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TableRefPattern = new Regex(
            @"\b(see|refer to|shown in|as in|from|in)\s+(table|tbl\.?)\s+(\d+|[a-z])\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SectionRefPattern = new Regex(
            @"\b(see|refer to|as in)\s+(section|chapter|part)\s+(\d+(\.\d+)*|[a-z])\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public CrossReferenceLinkService(
            ILogger<CrossReferenceLinkService> logger,
            AccessFormServer.Services.AnthropicService anthropic)
        {
            _logger = logger;
            _anthropic = anthropic;
        }

        public async Task<ServiceResult> RemediateAsync(byte[] pdfBytes)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new ServiceResult
            {
                Success = false,
                OutputPdf = pdfBytes
            };

            try
            {
                _logger.LogInformation("[CROSS-REF] Starting cross-reference link remediation");

                using var ms = new MemoryStream(pdfBytes);
                using var outputMs = new MemoryStream();
                using var pdfDoc = new PdfDocument(new PdfReader(ms), new PdfWriter(outputMs));

                var fixedCount = 0;
                var skippedCount = 0;

                // Process all pages
                for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                {
                    var page = pdfDoc.GetPage(i);
                    var annotations = page.GetAnnotations();

                    if (annotations == null || annotations.Count == 0)
                        continue;

                    // Extract page text for context analysis
                    var strategy = new LocationTextExtractionStrategy();
                    var processor = new PdfCanvasProcessor(strategy);
                    processor.ProcessPageContent(page);
                    var pageText = strategy.GetResultantText();

                    foreach (var annot in annotations)
                    {
                        // Check if it's a link annotation
                        if (annot.GetSubtype() != PdfName.Link)
                            continue;

                        var linkAnnot = annot as PdfLinkAnnotation;
                        if (linkAnnot == null)
                            continue;

                        // Check if it already has Contents
                        var pdfDict = linkAnnot.GetPdfObject();
                        var existingContents = pdfDict.GetAsString(PdfName.Contents);
                        if (existingContents != null && !string.IsNullOrWhiteSpace(existingContents.GetValue()))
                        {
                            skippedCount++;
                            continue;
                        }

                        // Analyze if this is a cross-reference link
                        var crossRefInfo = AnalyzeCrossReference(pageText, linkAnnot);

                        if (crossRefInfo != null)
                        {
                            // Generate specialized alt text for cross-reference
                            var altText = await GenerateCrossRefAltTextAsync(crossRefInfo, pageText);

                            if (!string.IsNullOrWhiteSpace(altText))
                            {
                                pdfDict.Put(PdfName.Contents, new PdfString(altText));
                                fixedCount++;
                                _logger.LogInformation($"[CROSS-REF] Set alt text for {crossRefInfo.Type} reference: '{altText.Substring(0, Math.Min(50, altText.Length))}'");
                            }
                        }
                    }
                }

                pdfDoc.Close();

                if (fixedCount > 0)
                {
                    result.OutputPdf = outputMs.ToArray();
                    result.Success = true;
                    result.ChangesMade = true;
                    result.IssuesFixed = fixedCount;
                    _logger.LogInformation($"[CROSS-REF] Added alt text to {fixedCount} cross-reference links, skipped {skippedCount} links");
                }
                else
                {
                    _logger.LogInformation($"[CROSS-REF] No cross-reference links found or all already have alt text");
                    result.Success = true;
                }

                stopwatch.Stop();
                _logger.LogInformation($"[CROSS-REF] Completed in {stopwatch.ElapsedMilliseconds}ms");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CROSS-REF] Failed to remediate cross-reference links");
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private CrossReferenceInfo AnalyzeCrossReference(string pageText, PdfLinkAnnotation linkAnnot)
        {
            try
            {
                // Get destination to understand what the link points to
                var destination = GetLinkDestination(linkAnnot);

                // Check for figure references
                var figMatch = FigureRefPattern.Match(pageText);
                if (figMatch.Success)
                {
                    return new CrossReferenceInfo
                    {
                        Type = CrossRefType.Figure,
                        RefNumber = figMatch.Groups[3].Value,
                        Context = ExtractContext(pageText, figMatch.Index, 100),
                        Destination = destination
                    };
                }

                // Check for table references
                var tableMatch = TableRefPattern.Match(pageText);
                if (tableMatch.Success)
                {
                    return new CrossReferenceInfo
                    {
                        Type = CrossRefType.Table,
                        RefNumber = tableMatch.Groups[3].Value,
                        Context = ExtractContext(pageText, tableMatch.Index, 100),
                        Destination = destination
                    };
                }

                // Check for section references
                var sectionMatch = SectionRefPattern.Match(pageText);
                if (sectionMatch.Success)
                {
                    return new CrossReferenceInfo
                    {
                        Type = CrossRefType.Section,
                        RefNumber = sectionMatch.Groups[3].Value,
                        Context = ExtractContext(pageText, sectionMatch.Index, 100),
                        Destination = destination
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[CROSS-REF] Could not analyze cross-reference: {ex.Message}");
                return null;
            }
        }

        private string GetLinkDestination(PdfLinkAnnotation linkAnnot)
        {
            try
            {
                var action = linkAnnot.GetAction();
                if (action != null)
                {
                    if (action.Get(PdfName.S) == PdfName.URI)
                    {
                        var uri = action.GetAsString(PdfName.URI);
                        return uri?.GetValue() ?? "";
                    }
                    else if (action.Get(PdfName.S) == PdfName.GoTo)
                    {
                        return "internal";
                    }
                }

                var dest = linkAnnot.GetDestinationObject();
                if (dest != null)
                {
                    return "internal";
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[CROSS-REF] Could not get destination: {ex.Message}");
            }

            return "";
        }

        private string ExtractContext(string text, int position, int contextLength)
        {
            var start = Math.Max(0, position - contextLength / 2);
            var end = Math.Min(text.Length, position + contextLength / 2);
            var length = end - start;

            if (length <= 0) return "";

            var context = text.Substring(start, length);
            return context.Trim();
        }

        private async Task<string> GenerateCrossRefAltTextAsync(CrossReferenceInfo crossRef, string pageText)
        {
            try
            {
                var typeLabel = crossRef.Type switch
                {
                    CrossRefType.Figure => "Figure",
                    CrossRefType.Table => "Table",
                    CrossRefType.Section => "Section",
                    _ => "Reference"
                };

                var prompt = $@"Generate a concise, accessible description for a cross-reference link in a PDF document.

Cross-Reference Type: {typeLabel}
Reference Number: {crossRef.RefNumber}
Surrounding Context: {crossRef.Context}
Destination: {(crossRef.Destination == "internal" ? "Internal document location" : crossRef.Destination)}

Generate a brief, descriptive alt text (1 sentence) that clearly indicates:
1. What type of reference this is (figure, table, section)
2. The reference number
3. What action the user can take (e.g., 'View', 'Navigate to', 'See')

Examples:
- 'View Figure 5 showing the process diagram'
- 'Navigate to Table 3 with detailed results'
- 'See Section 2.1 for methodology'

Only return the alt text, nothing else.";

                var response = await _anthropic.AnalyzeFormFieldsAsync(prompt);

                var altText = response?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(altText))
                {
                    return altText;
                }

                // Fallback: Generate simple alt text
                return $"Navigate to {typeLabel} {crossRef.RefNumber}";
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[CROSS-REF] AI generation failed: {ex.Message}");

                // Fallback
                var typeLabel = crossRef.Type switch
                {
                    CrossRefType.Figure => "Figure",
                    CrossRefType.Table => "Table",
                    CrossRefType.Section => "Section",
                    _ => "Reference"
                };

                return $"Navigate to {typeLabel} {crossRef.RefNumber}";
            }
        }

        private enum CrossRefType
        {
            Figure,
            Table,
            Section
        }

        private class CrossReferenceInfo
        {
            public CrossRefType Type { get; set; }
            public string RefNumber { get; set; }
            public string Context { get; set; }
            public string Destination { get; set; }
        }
    }
}
