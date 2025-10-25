using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.PdfUA;
using WordToPdfConverter.Services;
using WordToPdfConverter.Services.Remediation.Analysis;
using WordToPdfConverter.Services.Remediation.Decision;
using WordToPdfConverter.Services.Remediation.Execution;
using WordToPdfConverter.Services.Remediation.Models;
using WordToPdfConverter.Services.Remediation.Reporting;
using WordToPdfConverter.Services.Remediation.Strategy;
using WordToPdfConverter.Services.Remediation.Tracking;
using WordToPdfConverter.Services.Remediation.AI;

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
        private readonly GptRemediationService _gptService;
        private readonly ProcessingProgressService _progressService;

        public RemediationOrchestrator(
            ILogger<RemediationOrchestrator> logger,
            VeraPdfService veraPdfService,
            ViolationAnalyzer violationAnalyzer,
            RemediationStrategySelector strategySelector,
            RemediationExecutor executor,
            ExitConditionEvaluator exitEvaluator,
            ProgressTracker progressTracker,
            RemediationReporter reporter,
            ProcessingProgressService progressService = null,
            GptRemediationService gptService = null)
        {
            _logger = logger;
            _veraPdfService = veraPdfService;
            _violationAnalyzer = violationAnalyzer;
            _strategySelector = strategySelector;
            _executor = executor;
            _exitEvaluator = exitEvaluator;
            _progressTracker = progressTracker;
            _reporter = reporter;
            _progressService = progressService;
            _gptService = gptService;
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

                // STEP 0: Run initial validation to establish baseline
                _logger.LogInformation("\n--- INITIAL VALIDATION (Baseline) ---");
                var initialValidation = await ValidateAsync(session, tempPath);
                session.InitialValidation = initialValidation;

                // Store as last good validation
                session.LastValidatedPdf = await File.ReadAllBytesAsync(tempPath);
                session.LastValidation = initialValidation;

                _logger.LogInformation(
                    $"Baseline established: {initialValidation.Violations.Count} violations, " +
                    $"{initialValidation.Summary.ComplianceScore:F1}% compliant");

                // Main remediation loop
                while (!session.IsComplete)
                {
                    session.IterationCount++;
                    _logger.LogInformation($"\n--- ITERATION {session.IterationCount} ---");

                    // Update progress if tracking enabled
                    if (_progressService != null && !string.IsNullOrEmpty(options.ProgressSessionId))
                    {
                        _progressService.UpdateRemediationProgress(
                            options.ProgressSessionId,
                            session.IterationCount,
                            session.InitialValidation?.Violations.Count ?? 0,
                            (session.InitialValidation?.Violations.Count ?? 0) - (session.CurrentValidation?.Violations.Count ?? 0),
                            "Validating PDF");
                    }

                    // Step 1: Validate current PDF
                    var validation = await ValidateAsync(session, tempPath);
                    session.CurrentValidation = validation;

                    // Store as last good validation (in case next iteration fails)
                    session.LastValidatedPdf = session.CurrentPdf;
                    session.LastValidation = validation;

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

                    // Update progress
                    if (_progressService != null && !string.IsNullOrEmpty(options.ProgressSessionId))
                    {
                        _progressService.UpdateRemediationProgress(
                            options.ProgressSessionId,
                            session.IterationCount,
                            validation.Violations.Count,
                            (session.InitialValidation?.Violations.Count ?? 0) - validation.Violations.Count,
                            $"Executing remediation strategy");
                    }

                    // Step 6: Execute remediation
                    var execution = await _executor.ExecuteAsync(
                        session.CurrentPdf, strategy, session);

                    // Step 6.5: Run Post-Remediation Cleanup Phase
                    // This phase runs AFTER normal remediation but BEFORE GPT
                    // It fixes common structural issues that are often introduced during remediation
                    if (execution.Success && execution.OutputPdf != null)
                    {
                        _logger.LogInformation("\n--- POST-REMEDIATION CLEANUP ---");

                        // Build cleanup strategy with all fix services
                        var cleanupStrategy = _strategySelector.BuildCleanupStrategy();

                        if (cleanupStrategy.Phases.Any())
                        {
                            var cleanupResult = await _executor.ExecuteAsync(
                                execution.OutputPdf, cleanupStrategy, session);

                            if (cleanupResult.Success && cleanupResult.OutputPdf != null &&
                                cleanupResult.Phases.Any(p => p.ServiceResults.Any(s => s.ChangesMade)))
                            {
                                _logger.LogInformation("✓ Cleanup phase fixed structural issues");
                                execution.OutputPdf = cleanupResult.OutputPdf;

                                // Add cleanup results to main execution
                                foreach (var phase in cleanupResult.Phases)
                                {
                                    execution.Phases.Add(phase);
                                }
                            }
                        }
                    }

                    // Step 7: If violations remain AND GPT is available, try AI-powered remediation
                    // Invoke GPT when: no progress was made OR violations still exist
                    bool shouldUseGpt = _gptService != null &&
                        ((!execution.Success || !execution.Phases.Any(p => p.ServiceResults.Any(s => s.ChangesMade))) ||
                         (validation.Violations.Count > 0));

                    if (shouldUseGpt)
                    {
                        _logger.LogInformation("\n--- GPT FALLBACK REMEDIATION ---");
                        _logger.LogInformation($"Violations remaining: {validation.Violations.Count}, attempting GPT-powered fixes");

                        try
                        {
                            // Set violations context and use GPT to fix remaining violations
                            _gptService.SetViolations(validation.Violations);
                            var gptResult = await _gptService.RemediateAsync(
                                execution.OutputPdf ?? session.CurrentPdf);

                            if (gptResult.Success && gptResult.ChangesMade)
                            {
                                _logger.LogInformation(
                                    $"✓ GPT fixed {gptResult.IssuesFixed}/{gptResult.IssuesFound} violations");

                                // Update execution result to include GPT changes
                                execution.OutputPdf = gptResult.OutputPdf;
                                execution.Phases.Add(new PhaseResult
                                {
                                    PhaseName = "GPT-Powered Remediation",
                                    Success = true,
                                    OutputPdf = gptResult.OutputPdf,
                                    ServiceResults = { gptResult }
                                });
                            }
                            else
                            {
                                _logger.LogWarning("GPT remediation made no changes");
                            }
                        }
                        catch (Exception gptEx)
                        {
                            _logger.LogError(gptEx, "GPT remediation failed, continuing with standard output");
                        }
                    }

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
                session.Complete(session.LastValidation ?? session.InitialValidation);

                var result = _reporter.GenerateReport(session);
                result.Success = false;
                result.OutputPdf = session.LastValidatedPdf ?? session.OriginalPdf; // Use last good PDF

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

                // Check if validation failed
                if (validation.Status == ValidationStatus.Failed)
                {
                    _logger.LogError($"PDF validation failed: {validation.ErrorMessage}");
                    throw new InvalidOperationException(
                        $"PDF/UA validation failed: {validation.ErrorMessage ?? "VeraPDF returned an error"}");
                }

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
