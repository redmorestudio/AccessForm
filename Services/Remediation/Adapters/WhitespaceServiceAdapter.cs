using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Syncfusion.Pdf.Parsing;

namespace WordToPdfConverter.Services.Remediation.Adapters
{
    /// <summary>
    /// Adapter for whitespace remediation services
    /// </summary>
    public class WhitespaceServiceAdapter : IRemediationService
    {
        private readonly ILogger<WhitespaceServiceAdapter> _logger;
        private readonly TaggedWhitespaceFixService _whitespaceService;
        private readonly OrphanedWhitespaceAdoptionService _adoptionService;

        public string ServiceName => "Whitespace Cleanup";
        public ViolationCategory TargetCategory => ViolationCategory.Whitespace;
        public int Priority => 1;
        public bool IsRequired => false;

        public WhitespaceServiceAdapter(
            ILogger<WhitespaceServiceAdapter> logger,
            TaggedWhitespaceFixService whitespaceService,
            OrphanedWhitespaceAdoptionService adoptionService)
        {
            _logger = logger;
            _whitespaceService = whitespaceService;
            _adoptionService = adoptionService;
        }

        public async Task<ServiceResult> RemediateAsync(byte[] pdfBytes)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new ServiceResult { Success = true };

            try
            {
                // Step 1: Adopt orphaned whitespace
                var adoptResult = await _adoptionService.AdoptOrphanedWhitespaceAsync(pdfBytes);

                if (adoptResult.Success && adoptResult.FixedPdf != null)
                {
                    pdfBytes = adoptResult.FixedPdf;
                    result.IssuesFixed += adoptResult.OrphansAdopted;
                }

                // Step 2: Untag whitespace
                var untagResult = await _whitespaceService.FixTaggedWhitespaceAsync(pdfBytes);

                if (untagResult.Success && untagResult.FixedPdf != null)
                {
                    pdfBytes = untagResult.FixedPdf;
                    result.IssuesFixed += untagResult.ViolationsFixed;
                }

                result.OutputPdf = pdfBytes;
                result.ChangesMade = result.IssuesFixed > 0;
                result.IssuesFound = result.IssuesFixed; // Approximate

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Whitespace service failed");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.OutputPdf = pdfBytes; // Return original
            }

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;

            return result;
        }
    }
}
