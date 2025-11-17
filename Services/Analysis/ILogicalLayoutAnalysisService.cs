using System.Threading;
using System.Threading.Tasks;
using WordToPdfConverter.Models.Logical;

namespace WordToPdfConverter.Services.Analysis;

/// <summary>
/// Analyzes a PDF's visual and textual layout to produce a logical document model.
/// Backed by AI/vision models in concrete implementations.
/// This is the core abstraction for AI-powered layout understanding.
/// </summary>
public interface ILogicalLayoutAnalysisService
{
    /// <summary>
    /// Analyzes the provided PDF and returns a structured logical document model.
    /// </summary>
    /// <param name="pdfBytes">Raw PDF bytes to analyze</param>
    /// <param name="options">Analysis options controlling behavior</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>A LogicalDocument containing pages with semantic blocks</returns>
    Task<LogicalDocument> AnalyzeAsync(
        byte[] pdfBytes,
        LogicalLayoutOptions options,
        CancellationToken cancellationToken = default);
}
