using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WordToPdfConverter.Models.Logical;

namespace WordToPdfConverter.Services.Analysis;

/// <summary>
/// Temporary stub implementation of ILogicalLayoutAnalysisService.
/// Returns a trivial LogicalDocument for plumbing and testing.
/// This will be replaced with a real AI-backed implementation (ClaudeLogicalLayoutAnalysisService).
/// </summary>
public sealed class StubLogicalLayoutAnalysisService : ILogicalLayoutAnalysisService
{
    public Task<LogicalDocument> AnalyzeAsync(
        byte[] pdfBytes,
        LogicalLayoutOptions options,
        CancellationToken cancellationToken = default)
    {
        // Return a minimal LogicalDocument with one page and one paragraph
        var page = new LogicalPage(
            PageNumber: 1,
            Blocks: new List<LogicalBlock>
            {
                new ParagraphBlock(
                    new Rect(0, 0, 100, 20),
                    "AI layout analysis not yet implemented.")
            });

        var doc = new LogicalDocument(new[] { page });
        return Task.FromResult(doc);
    }
}
