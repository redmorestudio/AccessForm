namespace WordToPdfConverter.Models.Phase6H;

/// <summary>
/// Phase 6H: Represents the full MCID mapping for a document.
/// This plan is sent to the external MCID rewriter microservice.
/// </summary>
public sealed class McidRewritePlan
{
    /// <summary>
    /// Optional document identifier for tracing back to a remediation job.
    /// </summary>
    public string? DocumentId { get; set; }

    /// <summary>
    /// Version string (e.g., "6H-1") to allow future evolution of the contract.
    /// </summary>
    public string Version { get; set; } = "6H-1";

    /// <summary>
    /// All segments across all pages that need MCID wrapping.
    /// </summary>
    public List<McidSegment> Segments { get; set; } = new();

    /// <summary>
    /// If true, microservice can add extra diagnostics (e.g., visual boxes as artifacts).
    /// </summary>
    public bool Debug { get; set; }
}
