using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.PdfUA;
using WordToPdfConverter.Services.Remediation.Analysis;
using WordToPdfConverter.Services.Remediation.Decision;
using WordToPdfConverter.Services.Remediation.Execution;
using WordToPdfConverter.Services.Remediation.Models;
using WordToPdfConverter.Services.Remediation.Reporting;
using WordToPdfConverter.Services.Remediation.Strategy;
using WordToPdfConverter.Services.Remediation.Tracking;

namespace WordToPdfConverter.Services.Remediation
{
    /// <summary>
    /// Orchestrates the closed-loop PDF/UA remediation process
    /// </summary>
    public class RemediationOrchestrator
    {
        private readonly ILogger<RemediationOrchestrator> _logger;
        private readonly VeraPdfService _veraPdfService;
        private readonly ViolationAnalyzer _violationAnalyzer;
        private readonly RemediationStrategySelector _strategySelector;
        private readonly RemediationExecutor _executor;
        private readonly ExitConditionEvaluator _exitEvaluator;
        private readonly ProgressTracker _progressTracker;
        private readonly RemediationReporter _reporter;

        public RemediationOrchestrator(
            ILogger<RemediationOrchestrator> logger,
            VeraPdfService veraPdfService,
            ViolationAnalyzer violationAnalyzer,
            RemediationStrategySelector strategySelector,
            RemediationExecutor executor,
            ExitConditionEvaluator exitEvaluator,
            ProgressTracker progressTracker,
            RemediationReporter reporter)
        {
            _logger = logger;
            _veraPdfService = veraPdfService;
            _violationAnalyzer = violationAnalyzer;
            _strategySelector = strategySelector;
            _executor = executor;
            _exitEvaluator = exitEvaluator;
            _progressTracker = progressTracker;
            _reporter = reporter;
        }

        /// <summary>
        /// Main entry point for closed-loop remediation
        /// </summary>
        public async Task<RemediationResult> RemediateAsync(
            byte[] inputPdf,
            RemediationOptions options = null)
        {
            // Use default options if none provided
            options ??= RemediationOptions.Production;

            // Create session
            var session = new RemediationSession(options)
            {
                OriginalPdf = inputPdf,
                CurrentPdf = inputPdf
            };

            _logger.LogInformation("=== CLOSED-LOOP REMEDIATION STARTED ===");
            _logger.LogInformation($"Session ID: {session.SessionId}");
            _logger.LogInformation($"Max Iterations: {options.MaxIterations}");
            _logger.LogInformation($"Max Duration: {options.MaxDuration.TotalMinutes:F1} minutes");

            try
            {
                // Save PDF to temp file for validation
                var tempPath = await SaveToTempFileAsync(inputPdf);

                // Main remediation loop
                while (!session.IsComplete)
                {
                    session.IterationCount++;
                    _logger.LogInformation($"\n--- ITERATION {session.IterationCount} ---");

                    // Step 1: Validate current PDF
                    var validation = await ValidateAsync(session, tempPath);
                    session.CurrentValidation = validation;

                    // Step 2: Check exit conditions
                    if (_exitEvaluator.ShouldExit(validation, session, out var exitReason))
                    {
                        session.ExitReason = exitReason;
                        session.Complete(validation);
                        break;
                    }

                    // Step 3: Analyze violations
                    var analysis = await _violationAnalyzer.AnalyzeAsync(
                        validation.Violations, session);

                    // Step 4: Determine if this is final iteration
                    session.IsFinalIteration = (session.IterationCount == options.MaxIterations - 1);

                    // Step 5: Select remediation strategy
                    var strategy = await _strategySelector.SelectAsync(analysis, session);

                    // Step 6: Execute remediation
                    var execution = await _executor.ExecuteAsync(
                        session.CurrentPdf, strategy, session);

                    // Step 7: Track progress
                    _progressTracker.RecordIteration(session, validation, execution);

                    // Step 8: Update session
                    session.NextIteration(execution);

                    // Update temp file for next validation
                    tempPath = await SaveToTempFileAsync(session.CurrentPdf);
                }

                // Generate final report
                var result = _reporter.GenerateReport(session);

                _logger.LogInformation("\n=== REMEDIATION COMPLETE ===");
                _logger.LogInformation($"Exit Reason: {result.ExitReason}");
                _logger.LogInformation($"Total Iterations: {result.Summary.TotalIterations}");
                _logger.LogInformation($"Compliant: {result.Summary.IsCompliant}");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Remediation failed with exception");

                session.ExitReason = ExitReason.FatalError;
                session.Complete(session.CurrentValidation);

                var result = _reporter.GenerateReport(session);
                result.Success = false;

                return result;
            }
        }

        private async Task<ValidationResult> ValidateAsync(
            RemediationSession session,
            string pdfPath)
        {
            _logger.LogInformation("Validating PDF...");

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var validation = await _veraPdfService.ValidatePdfAsync(pdfPath);

                stopwatch.Stop();

                _logger.LogInformation(
                    $"Validation complete: {validation.Violations.Count} violations, " +
                    $"{validation.Summary.ComplianceScore:F1}% compliant " +
                    $"({stopwatch.ElapsedMilliseconds}ms)");

                return validation;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Validation failed");
                throw;
            }
        }

        private async Task<string> SaveToTempFileAsync(byte[] pdfBytes)
        {
            var tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"remediation-{Guid.NewGuid()}.pdf");

            await System.IO.File.WriteAllBytesAsync(tempPath, pdfBytes);

            return tempPath;
        }
    }
}
