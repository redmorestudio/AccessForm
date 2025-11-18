using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.Phase6H;

namespace WordToPdfConverter.Services.Phase6H;

/// <summary>
/// Phase 6H: HTTP client service for calling the external MCID rewriter microservice.
/// This service sends a PDF and McidRewritePlan to a Python/pikepdf microservice
/// that rewrites PDF content streams with BDC/EMC markers.
/// </summary>
public sealed class ExternalMcidRewriterService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ExternalMcidRewriterService> _logger;
    private readonly string _microserviceBaseUrl;

    public ExternalMcidRewriterService(
        HttpClient httpClient,
        ILogger<ExternalMcidRewriterService> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;

        // Get microservice URL from configuration (default to localhost:8000)
        _microserviceBaseUrl = configuration["Phase6H:MicroserviceUrl"] ?? "http://localhost:8000";

        _logger.LogInformation("[PHASE-6H] Initialized ExternalMcidRewriterService with URL: {Url}", _microserviceBaseUrl);
    }

    /// <summary>
    /// Rewrites a PDF's content streams with MCID markers according to the provided plan.
    /// </summary>
    /// <param name="pdfBytes">The original PDF bytes</param>
    /// <param name="plan">The MCID rewrite plan</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Rewritten PDF bytes on success</returns>
    /// <exception cref="InvalidOperationException">Thrown when microservice returns failure</exception>
    public async Task<byte[]> RewritePdfWithMcidsAsync(
        byte[] pdfBytes,
        McidRewritePlan plan,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("[PHASE-6H] Calling external MCID rewriter with {SegmentCount} segments across {PageCount} pages",
                plan.Segments.Count,
                plan.Segments.Select(s => s.PageIndex).Distinct().Count());

            // Convert PDF bytes to Base64
            var pdfBase64 = Convert.ToBase64String(pdfBytes);

            // Build request
            var request = new McidRewriteRequest
            {
                PdfBase64 = pdfBase64,
                Plan = plan
            };

            _logger.LogDebug("[PHASE-6H] Request size: PDF={PdfSizeKb}KB, Plan={PlanSegments} segments",
                pdfBytes.Length / 1024,
                plan.Segments.Count);

            // Call microservice
            var endpoint = $"{_microserviceBaseUrl}/api/mcid-rewrite";
            _logger.LogInformation("[PHASE-6H] Posting to {Endpoint}", endpoint);

            var response = await _httpClient.PostAsJsonAsync(endpoint, request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("[PHASE-6H] Microservice returned HTTP {StatusCode}: {Error}",
                    response.StatusCode, errorContent);
                throw new InvalidOperationException($"MCID rewriter microservice returned HTTP {response.StatusCode}: {errorContent}");
            }

            // Parse response
            var result = await response.Content.ReadFromJsonAsync<McidRewriteResponse>(cancellationToken);

            if (result == null)
            {
                _logger.LogError("[PHASE-6H] Failed to deserialize microservice response");
                throw new InvalidOperationException("Failed to deserialize MCID rewriter response");
            }

            if (!result.Success)
            {
                _logger.LogError("[PHASE-6H] Microservice reported failure: {Message}", result.Message);
                throw new InvalidOperationException($"MCID rewriter failed: {result.Message}");
            }

            // Log success stats
            if (result.Stats != null)
            {
                _logger.LogInformation("[PHASE-6H] ✅ MCID rewrite successful: Pages={Pages}, Segments={Segments}, BDC={Bdc}, EMC={Emc}",
                    result.Stats.PagesProcessed,
                    result.Stats.SegmentsProcessed,
                    result.Stats.BdcCount,
                    result.Stats.EmcCount);
            }
            else
            {
                _logger.LogInformation("[PHASE-6H] ✅ MCID rewrite successful: {Message}", result.Message);
            }

            // Decode Base64 PDF
            if (string.IsNullOrEmpty(result.PdfBase64))
            {
                _logger.LogError("[PHASE-6H] Microservice returned success but no PDF bytes");
                throw new InvalidOperationException("MCID rewriter returned success but no PDF data");
            }

            var rewrittenPdfBytes = Convert.FromBase64String(result.PdfBase64);

            _logger.LogInformation("[PHASE-6H] Received rewritten PDF: {SizeKb}KB", rewrittenPdfBytes.Length / 1024);

            return rewrittenPdfBytes;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[PHASE-6H] HTTP request failed - is the microservice running at {Url}?", _microserviceBaseUrl);
            throw new InvalidOperationException($"Failed to connect to MCID rewriter microservice at {_microserviceBaseUrl}. Ensure the Python microservice is running.", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "[PHASE-6H] Request timeout - microservice may be overloaded");
            throw new InvalidOperationException("MCID rewriter request timed out", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[PHASE-6H] Failed to parse microservice response");
            throw new InvalidOperationException("Failed to parse MCID rewriter response", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PHASE-6H] Unexpected error calling MCID rewriter");
            throw;
        }
    }

    /// <summary>
    /// Checks if the external microservice is running and healthy.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if microservice is healthy</returns>
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = $"{_microserviceBaseUrl}/health";
            var response = await _httpClient.GetAsync(endpoint, cancellationToken);

            var isHealthy = response.IsSuccessStatusCode;

            if (isHealthy)
            {
                _logger.LogInformation("[PHASE-6H] Microservice health check passed");
            }
            else
            {
                _logger.LogWarning("[PHASE-6H] Microservice health check failed: HTTP {StatusCode}", response.StatusCode);
            }

            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PHASE-6H] Microservice health check failed - service may not be running");
            return false;
        }
    }
}
