using System;
using System.Diagnostics;
using System.Threading.Tasks;
using AccessFormServer.Services;
using Microsoft.Extensions.Logging;

namespace WordToPdfConverter.Services.Remediation.Adapters
{
    /// <summary>
    /// Adapter for link structure remediation services
    /// </summary>
    public class LinkServiceAdapter : IRemediationService
    {
        private readonly ILogger<LinkServiceAdapter> _logger;
        private readonly TocLinkFixServiceEnhanced _tocLinkService;

        public string ServiceName => "Link Structure Fixes";
        public ViolationCategory TargetCategory => ViolationCategory.Links;
        public int Priority => 5;
        public bool IsRequired => false;

        public LinkServiceAdapter(
            ILogger<LinkServiceAdapter> logger,
            TocLinkFixServiceEnhanced tocLinkService)
        {
            _logger = logger;
            _tocLinkService = tocLinkService;
        }

        public async Task<ServiceResult> RemediateAsync(byte[] pdfBytes)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new ServiceResult { Success = true };

            try
            {
                var linkResult = await _tocLinkService.FixTocLinksAsync(pdfBytes);

                if (linkResult.Success && linkResult.FixedPdf != null)
                {
                    result.OutputPdf = linkResult.FixedPdf;
                    result.IssuesFixed = linkResult.FixedLinks;
                    result.IssuesFound = linkResult.FixedLinks;
                    result.ChangesMade = linkResult.FixedLinks > 0;
                }
                else
                {
                    result.OutputPdf = pdfBytes;
                    result.ChangesMade = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Link service failed");
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
