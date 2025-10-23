using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Remediation.AI;

namespace WordToPdfConverter.Services.Remediation.Adapters
{
    /// <summary>
    /// Adapter for GPT-powered remediation service
    /// </summary>
    public class GptServiceAdapter : IRemediationService
    {
        private readonly ILogger<GptServiceAdapter> _logger;
        private readonly GptRemediationService _gptService;

        public string ServiceName => "GPT Service Adapter";
        public ViolationCategory TargetCategory => ViolationCategory.Unknown;
        public int Priority => 100; // Low priority (fallback)
        public bool IsRequired => false;

        public GptServiceAdapter(
            ILogger<GptServiceAdapter> logger,
            GptRemediationService gptService)
        {
            _logger = logger;
            _gptService = gptService;
        }

        public async Task<ServiceResult> RemediateAsync(byte[] pdfBytes)
        {
            _logger.LogInformation("[GPT-ADAPTER] Starting GPT-powered remediation");

            // Call the GPT service
            var result = await _gptService.RemediateAsync(pdfBytes);

            if (result.Success)
            {
                _logger.LogInformation(
                    $"[GPT-ADAPTER] Remediation completed: {result.IssuesFixed}/{result.IssuesFound} fixed");
            }
            else
            {
                _logger.LogWarning($"[GPT-ADAPTER] Remediation failed: {result.ErrorMessage}");
            }

            return result;
        }
    }
}