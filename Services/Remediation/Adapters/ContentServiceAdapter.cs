using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WordToPdfConverter.Services.Remediation.Adapters
{
    /// <summary>
    /// Adapter for content/artifact remediation services
    /// </summary>
    public class ContentServiceAdapter : IRemediationService
    {
        private readonly ILogger<ContentServiceAdapter> _logger;
        private readonly ArtifactViolationFixService _artifactService;

        public string ServiceName => "Content & Artifact Remediation";
        public ViolationCategory TargetCategory => ViolationCategory.Content;
        public int Priority => 2;
        public bool IsRequired => false;

        public ContentServiceAdapter(
            ILogger<ContentServiceAdapter> logger,
            ArtifactViolationFixService artifactService)
        {
            _logger = logger;
            _artifactService = artifactService;
        }

        public async Task<ServiceResult> RemediateAsync(byte[] pdfBytes)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new ServiceResult { Success = true };

            try
            {
                var artifactResult = await _artifactService.FixArtifactViolationsAsync(pdfBytes);

                if (artifactResult.Success && artifactResult.FixedPdf != null)
                {
                    result.OutputPdf = artifactResult.FixedPdf;
                    result.IssuesFixed = artifactResult.ViolationsFixed;
                    result.IssuesFound = artifactResult.ViolationsFound;
                    result.ChangesMade = artifactResult.ViolationsFixed > 0;
                }
                else
                {
                    result.OutputPdf = pdfBytes;
                    result.ChangesMade = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Content service failed");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.OutputPdf = pdfBytes;
            }

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;

            return result;
        }
    }
}
