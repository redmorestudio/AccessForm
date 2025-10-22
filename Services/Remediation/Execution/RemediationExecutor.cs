using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Services.Remediation.Models;
using WordToPdfConverter.Services.Remediation.Strategy;

namespace WordToPdfConverter.Services.Remediation.Execution
{
    /// <summary>
    /// Executes remediation phases and services
    /// </summary>
    public class RemediationExecutor
    {
        private readonly ILogger<RemediationExecutor> _logger;

        public RemediationExecutor(ILogger<RemediationExecutor> logger)
        {
            _logger = logger;
        }

        public async Task<ExecutionResult> ExecuteAsync(
            byte[] pdfBytes,
            RemediationStrategy strategy,
            RemediationSession session)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new ExecutionResult { Success = true };
            byte[] currentPdf = pdfBytes;

            _logger.LogInformation($"Executing {strategy.Phases.Count} phases");

            foreach (var phase in strategy.Phases)
            {
                _logger.LogInformation(
                    $"Starting phase: {phase.Name} (Order: {phase.Order}, MaxIter: {phase.MaxIterations})");

                var phaseResult = await ExecutePhaseAsync(currentPdf, phase, session);

                result.Phases.Add(phaseResult);

                if (phaseResult.Success && phaseResult.OutputPdf != null)
                {
                    currentPdf = phaseResult.OutputPdf;
                }
                else if (phase.Required)
                {
                    _logger.LogError($"Required phase '{phase.Name}' failed: {phaseResult.ErrorMessage}");
                    result.Success = false;
                    result.ErrorMessage = $"Required phase '{phase.Name}' failed";
                    break;
                }
                else
                {
                    _logger.LogWarning($"Optional phase '{phase.Name}' failed, continuing");
                }
            }

            stopwatch.Stop();
            result.OutputPdf = currentPdf;
            result.Duration = stopwatch.Elapsed;

            return result;
        }

        private async Task<PhaseResult> ExecutePhaseAsync(
            byte[] pdfBytes,
            RemediationPhase phase,
            RemediationSession session)
        {
            var stopwatch = Stopwatch.StartNew();
            var phaseResult = new PhaseResult
            {
                PhaseName = phase.Name,
                Success = true
            };

            byte[] currentPdf = pdfBytes;
            int iteration = 0;

            while (iteration < phase.MaxIterations)
            {
                iteration++;
                bool madeProgress = false;

                _logger.LogInformation($"  Phase '{phase.Name}' - Iteration {iteration}/{phase.MaxIterations}");

                foreach (var service in phase.Services)
                {
                    try
                    {
                        _logger.LogInformation($"    Executing service: {service.ServiceName}");

                        var serviceResult = await service.RemediateAsync(currentPdf);
                        phaseResult.ServiceResults.Add(serviceResult);

                        if (serviceResult.Success && serviceResult.ChangesMade)
                        {
                            currentPdf = serviceResult.OutputPdf;
                            madeProgress = true;

                            _logger.LogInformation(
                                $"    ✓ {service.ServiceName}: Fixed {serviceResult.IssuesFixed}/{serviceResult.IssuesFound} issues");
                        }
                        else if (!serviceResult.Success)
                        {
                            _logger.LogWarning(
                                $"    ⚠ {service.ServiceName} failed: {serviceResult.ErrorMessage}");
                        }
                        else
                        {
                            _logger.LogInformation($"    - {service.ServiceName}: No changes needed");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Service {service.ServiceName} threw exception");
                        phaseResult.ServiceResults.Add(new ServiceResult
                        {
                            Success = false,
                            ErrorMessage = ex.Message,
                            OutputPdf = currentPdf
                        });
                    }
                }

                // If no service made progress, stop iterating
                if (!madeProgress)
                {
                    _logger.LogInformation($"  Phase '{phase.Name}' - No progress, stopping early");
                    break;
                }
            }

            stopwatch.Stop();
            phaseResult.OutputPdf = currentPdf;
            phaseResult.Iterations = iteration;
            phaseResult.Duration = stopwatch.Elapsed;

            return phaseResult;
        }
    }
}
