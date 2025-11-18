using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation;

/// <summary>
/// Preflight service that runs BEFORE any structure rebuild or MCID content rewrite.
/// This service handles operations that would otherwise destroy MCID markers if run after tagging.
/// Primary use case: Aspose font optimization must run in preflight, not post-structure.
/// </summary>
public interface IPdfPreflightService
{
    /// <summary>
    /// Run preflight operations on the input PDF.
    /// This runs ONCE per remediation job, before AI structure analysis and MCID linking.
    /// </summary>
    /// <param name="inputPdf">Original PDF bytes</param>
    /// <param name="options">Remediation options</param>
    /// <returns>Preflight-optimized PDF bytes</returns>
    Task<byte[]> RunPreflightAsync(byte[] inputPdf, RemediationOptions options);
}
