namespace WordToPdfConverter.Models.Phase6H;

/// <summary>
/// Phase 6H: Request body for the external MCID rewriter microservice.
/// </summary>
public sealed class McidRewriteRequest
{
    /// <summary>
    /// Base64-encoded PDF bytes to be rewritten.
    /// </summary>
    public string PdfBase64 { get; set; } = "";

    /// <summary>
    /// The MCID rewrite plan describing which segments get which MCID.
    /// </summary>
    public McidRewritePlan Plan { get; set; } = new();
}
