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
        private readonly WordToPdfConverter.Services.Remediation.Fixes.CrossReferenceLinkService _crossRefService;
        private readonly WordToPdfConverter.Services.Remediation.Fixes.LinkAltTextService _linkAltTextService;

        public string ServiceName => "Link Structure Fixes";
        public ViolationCategory TargetCategory => ViolationCategory.Links;
        public int Priority => 5;
        public bool IsRequired => false;

        public LinkServiceAdapter(
            ILogger<LinkServiceAdapter> logger,
            TocLinkFixServiceEnhanced tocLinkService,
            WordToPdfConverter.Services.Remediation.Fixes.CrossReferenceLinkService crossRefService,
            WordToPdfConverter.Services.Remediation.Fixes.LinkAltTextService linkAltTextService)
        {
            _logger = logger;
            _tocLinkService = tocLinkService;
            _crossRefService = crossRefService;
            _linkAltTextService = linkAltTextService;
        }

        public async Task<ServiceResult> RemediateAsync(byte[] pdfBytes)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new ServiceResult { Success = true };
            var totalFixed = 0;

            try
            {
                // Phase 1: Fix TOC links
                var linkResult = await _tocLinkService.FixTocLinksAsync(pdfBytes);

                if (linkResult.Success && linkResult.FixedPdf != null)
                {
                    result.OutputPdf = linkResult.FixedPdf;
                    totalFixed += linkResult.FixedLinks;
                    result.ChangesMade = linkResult.FixedLinks > 0;
                }
                else
                {
                    result.OutputPdf = pdfBytes;
                }

                // Phase 2: Add cross-reference alt text (higher priority - runs first)
                var crossRefResult = await _crossRefService.RemediateAsync(result.OutputPdf);

                if (crossRefResult.Success && crossRefResult.OutputPdf != null)
                {
                    result.OutputPdf = crossRefResult.OutputPdf;
                    totalFixed += crossRefResult.IssuesFixed;
                    result.ChangesMade = result.ChangesMade || crossRefResult.ChangesMade;
                }

                // Phase 3: Add general alt text to remaining links
                var altTextResult = await _linkAltTextService.RemediateAsync(result.OutputPdf);

                if (altTextResult.Success && altTextResult.OutputPdf != null)
                {
                    result.OutputPdf = altTextResult.OutputPdf;
                    totalFixed += altTextResult.IssuesFixed;
                    result.ChangesMade = result.ChangesMade || altTextResult.ChangesMade;
                }

                result.IssuesFixed = totalFixed;
                result.IssuesFound = totalFixed;
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
