namespace WordToPdfConverter.Models.Phase6H;

/// <summary>
/// Phase 6H: Response from the external MCID rewriter microservice.
/// </summary>
public sealed class McidRewriteResponse
{
    /// <summary>
    /// True if the rewrite was successful, false otherwise.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Human-readable message (success confirmation or error description).
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Base64-encoded rewritten PDF bytes (only present on success).
    /// </summary>
    public string? PdfBase64 { get; set; }

    /// <summary>
    /// Statistics from the rewrite operation (only present on success).
    /// </summary>
    public McidRewriteStats? Stats { get; set; }
}

/// <summary>
/// Statistics from an MCID rewrite operation.
/// </summary>
public sealed class McidRewriteStats
{
    /// <summary>
    /// Number of PDF pages processed.
    /// </summary>
    public int PagesProcessed { get; set; }

    /// <summary>
    /// Number of MCID segments processed.
    /// </summary>
    public int SegmentsProcessed { get; set; }

    /// <summary>
    /// Number of BDC markers inserted into content streams.
    /// </summary>
    public int BdcCount { get; set; }

    /// <summary>
    /// Number of EMC markers inserted into content streams.
    /// </summary>
    public int EmcCount { get; set; }
}
