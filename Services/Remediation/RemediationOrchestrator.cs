using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WordToPdfConverter.Models.PdfUA;
using WordToPdfConverter.Services;
using AccessFormServer.Services;
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
        private readonly CostTrackingService _costTracking;
        private readonly IPdfPreflightService _preflightService;
        private readonly IOptions<RemediationOptions> _defaultOptions;
        private readonly WordToPdfConverter.Models.Remediation.RemediationJobContext _jobContext;

        public RemediationOrchestrator(
            ILogger<RemediationOrchestrator> logger,
            VeraPdfService veraPdfService,
            ViolationAnalyzer violationAnalyzer,
            RemediationStrategySelector strategySelector,
            RemediationExecutor executor,
            ExitConditionEvaluator exitEvaluator,
            ProgressTracker progressTracker,
            RemediationReporter reporter,
            CostTrackingService costTracking,
            IPdfPreflightService preflightService,
            IOptions<RemediationOptions> defaultOptions,
            WordToPdfConverter.Models.Remediation.RemediationJobContext jobContext,
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
            _costTracking = costTracking;
            _preflightService = preflightService;
            _defaultOptions = defaultOptions;
            _jobContext = jobContext;
            _progressService = progressService;
            _gptService = gptService;
        }

        /// <summary>
        /// Main entry point for closed-loop remediation
        /// </summary>
        public async Task<WordToPdfConverter.Services.Remediation.Models.RemediationResult> RemediateAsync(
            byte[] inputPdf,
            RemediationOptions options = null)
        {
            // PHASE 6E: Merge configuration settings with passed-in options
            var configOptions = _defaultOptions.Value;

            // Use default options if none provided, then merge with config
            options ??= RemediationOptions.Production;

            // Merge MCID settings from configuration (config takes precedence for MCID flags)
            options.EnableMcidLinking = configOptions.EnableMcidLinking;
            options.EnableMcidContentRewrite = configOptions.EnableMcidContentRewrite;

            // Also merge other Phase 6C/6D settings if not explicitly set
            if (string.IsNullOrEmpty(options.AsposeOptimizationMode) || options.AsposeOptimizationMode == "PreStructureOnly")
                options.AsposeOptimizationMode = configOptions.AsposeOptimizationMode;
            if (string.IsNullOrEmpty(options.ArtifactFixMode) || options.ArtifactFixMode == "PreStructureOnly")
                options.ArtifactFixMode = configOptions.ArtifactFixMode;

            var fileInfo = !string.IsNullOrEmpty(options.FileName) ? $" [{options.FileName}]" : "";
            _logger.LogInformation($"=== CLOSED-LOOP REMEDIATION STARTED ==={fileInfo}");

            // PHASE 6E: Log MCID configuration at job start for diagnostics
            _logger.LogInformation(
                "REMEDIATION JOB: MCID linking={Link}, MCID content rewrite={Rewrite}, " +
                "AsposeMode={AsposeMode}, ArtifactMode={ArtifactMode}",
                options.EnableMcidLinking,
                options.EnableMcidContentRewrite,
                options.AsposeOptimizationMode,
                options.ArtifactFixMode);

            // PHASE 6E: Store options in job context so all services can access them
            _jobContext.Options = options;

            // PHASE 6C: Run preflight BEFORE any structure rebuild or MCID work
            // This handles font fixes via Aspose Cloud that would otherwise destroy MCID markers
            _logger.LogInformation("\n--- PREFLIGHT PHASE ---");
            var preflightPdf = await _preflightService.RunPreflightAsync(inputPdf, options);

            // Create session with preflight-optimized PDF
            var session = new RemediationSession(options)
            {
                OriginalPdf = inputPdf,  // Keep original for comparison
                CurrentPdf = preflightPdf,  // Start remediation with preflight-optimized PDF
                FileName = options.FileName
            };

            _logger.LogInformation($"Session ID: {session.SessionId}");
            _logger.LogInformation($"Max Iterations: {options.MaxIterations}");
            _logger.LogInformation($"Max Duration: {options.MaxDuration.TotalMinutes:F1} minutes");

            // Start cost tracking for this remediation session
            _costTracking.StartSession(session.SessionId.ToString());

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

                // Initialize best PDF tracking with baseline
                session.BestPdf = session.LastValidatedPdf;
                session.BestValidation = initialValidation;
                session.BestIterationNumber = 0;

                _logger.LogInformation(
                    $"Baseline established: {initialValidation.Violations.Count} violations, " +
                    $"{initialValidation.Summary.ComplianceScore:F1}% compliant");

                // Save baseline as initial "best" PDF
                await SaveBestPdfAsync(session);

                // Main remediation loop
                while (!session.IsComplete)
                {
                    session.IterationCount++;
                    var fileLabel = !string.IsNullOrEmpty(session.FileName) ? $" [{session.FileName}]" : "";
                    _logger.LogInformation($"\n--- ITERATION {session.IterationCount}{fileLabel} ---");

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

                    // Track best PDF - PHASE 6E: Prioritize PDFs with MCID markers
                    // Check if this PDF has MCID markers (structure rebuild was executed)
                    bool currentHasMcidMarkers = _jobContext.StructureContext.McidContentRewriteExecuted;
                    bool bestHasMcidMarkers = session.BestIterationNumber > 0; // Assumes MCID work happens after iteration 0

                    // Prefer PDFs with MCID markers over raw compliance score
                    bool shouldUpdateBest = false;
                    if (currentHasMcidMarkers && !bestHasMcidMarkers)
                    {
                        // Always prefer PDF with MCID markers
                        shouldUpdateBest = true;
                        _logger.LogInformation("🎯 Prioritizing PDF with MCID markers over compliance score");
                    }
                    else if (!currentHasMcidMarkers && bestHasMcidMarkers)
                    {
                        // Keep the one with MCID markers
                        shouldUpdateBest = false;
                    }
                    else
                    {
                        // Both have same MCID status, use compliance score
                        shouldUpdateBest = session.BestValidation == null ||
                            validation.Summary.ComplianceScore > session.BestValidation.Summary.ComplianceScore;
                    }

                    if (shouldUpdateBest)
                    {
                        session.BestPdf = session.CurrentPdf;
                        session.BestValidation = validation;
                        session.BestIterationNumber = session.IterationCount;

                        _logger.LogInformation(
                            $"🏆 New best PDF found at iteration {session.IterationCount}: " +
                            $"{validation.Summary.ComplianceScore:F1}% compliant " +
                            $"({validation.Violations.Count} violations)" +
                            (currentHasMcidMarkers ? " [WITH MCID MARKERS]" : ""));

                        // Save the best PDF to disk
                        await SaveBestPdfAsync(session);
                    }

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
                    // PHASE 6F: Skip cleanup if MCID content rewrite was executed, as cleanup services may destroy MCID markers
                    if (execution.Success && execution.OutputPdf != null && !_jobContext.StructureContext.McidContentRewriteExecuted)
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
                    else if (_jobContext.StructureContext.McidContentRewriteExecuted)
                    {
                        _logger.LogInformation("\n--- POST-REMEDIATION CLEANUP SKIPPED ---");
                        _logger.LogInformation("⚠ Cleanup skipped to preserve MCID markers in content streams");
                    }

                    // Step 7: If violations remain AND GPT is available, try AI-powered remediation
                    // GPT is invoked as a FINAL SWEEP before metadata finalization,
                    // catching any violations that standard remediation couldn't handle.
                    // This makes GPT a true "last resort" safety net, not just a stagnation bailout.
                    bool hasViolations = validation.Violations.Count > 0;
                    bool standardRemediationMadeProgress = execution.Success && execution.Phases.Any(p => p.ServiceResults.Any(s => s.ChangesMade));
                    bool shouldUseGpt = _gptService != null && hasViolations;

                    if (hasViolations && standardRemediationMadeProgress)
                    {
                        _logger.LogInformation($"Standard remediation made progress, but {validation.Violations.Count} violations remain - trying GPT final sweep");
                    }
                    else if (hasViolations && !standardRemediationMadeProgress)
                    {
                        _logger.LogInformation($"Standard remediation stalled with {validation.Violations.Count} violations - trying GPT fallback");
                    }

                    if (shouldUseGpt)
                    {
                        _logger.LogInformation("\n--- GPT FALLBACK REMEDIATION ---");
                        _logger.LogInformation($"Violations remaining: {validation.Violations.Count}, attempting GPT-powered fixes");

                        // Track pre-GPT violation count
                        var violationsBeforeGpt = validation.Violations.Count;

                        try
                        {
                            // Initialize GPT attempt tracking
                            if (!session.Metadata.ContainsKey("GptAttempts"))
                                session.Metadata["GptAttempts"] = 0;
                            if (!session.Metadata.ContainsKey("GptLastViolationCount"))
                                session.Metadata["GptLastViolationCount"] = violationsBeforeGpt;

                            // Set violations context and use GPT to fix remaining violations
                            _gptService.SetViolations(validation.Violations);
                            var gptResult = await _gptService.RemediateAsync(
                                execution.OutputPdf ?? session.CurrentPdf);

                            if (gptResult.Success && gptResult.ChangesMade)
                            {
                                _logger.LogInformation(
                                    $"✓ GPT claims to have fixed {gptResult.IssuesFixed}/{gptResult.IssuesFound} violations");

                                // Update execution result to include GPT changes
                                execution.OutputPdf = gptResult.OutputPdf;
                                execution.Phases.Add(new PhaseResult
                                {
                                    PhaseName = "GPT-Powered Remediation",
                                    Success = true,
                                    OutputPdf = gptResult.OutputPdf,
                                    ServiceResults = { gptResult }
                                });

                                // CRITICAL FIX: Validate GPT's claims by re-validating the PDF
                                // Save GPT output to temp file and validate
                                var postGptTempPath = await SaveToTempFileAsync(gptResult.OutputPdf);
                                var postGptValidation = await ValidateAsync(session, postGptTempPath);

                                var actuallyFixed = Math.Max(0, violationsBeforeGpt - postGptValidation.Violations.Count);

                                if (postGptValidation.Violations.Count >= violationsBeforeGpt)
                                {
                                    // GPT didn't actually help
                                    _logger.LogWarning(
                                        $"[GPT-STAGNATION] GPT claimed {gptResult.IssuesFixed} fixes but violations unchanged: " +
                                        $"{violationsBeforeGpt} → {postGptValidation.Violations.Count}");

                                    // Increment stagnation counter
                                    session.Metadata["GptAttempts"] = (int)session.Metadata["GptAttempts"] + 1;

                                    // Check for GPT stagnation (3 failed attempts)
                                    if ((int)session.Metadata["GptAttempts"] >= 3)
                                    {
                                        _logger.LogError(
                                            "[GPT-STAGNATION] GPT failed to make progress after 3 attempts - stopping");
                                        session.ExitReason = ExitReason.NoProgress;
                                        session.Complete(postGptValidation);
                                        break;
                                    }
                                }
                                else
                                {
                                    // GPT actually helped!
                                    _logger.LogInformation(
                                        $"[GPT-VALIDATION] ✓ GPT actually fixed {actuallyFixed} violations " +
                                        $"({violationsBeforeGpt} → {postGptValidation.Violations.Count})");

                                    // Reset stagnation counter
                                    session.Metadata["GptAttempts"] = 0;
                                    session.Metadata["GptLastViolationCount"] = postGptValidation.Violations.Count;

                                    // Update the service result with actual fix count
                                    gptResult.IssuesFixed = actuallyFixed;

                                    // CRITICAL FIX: Cache the solution now that we know it works
                                    // This is handled by the GPT service's solution cache
                                    // We don't need to do anything here - the validation proves it worked
                                }

                                // Clean up temp file
                                try { File.Delete(postGptTempPath); } catch { }
                            }
                            else
                            {
                                _logger.LogWarning("GPT remediation made no changes");

                                // Increment stagnation counter even if GPT returns no changes
                                session.Metadata["GptAttempts"] = (int)session.Metadata["GptAttempts"] + 1;
                            }
                        }
                        catch (Exception gptEx)
                        {
                            _logger.LogError(gptEx, "GPT remediation failed, continuing with standard output");

                            // Increment stagnation counter on exception
                            if (session.Metadata.ContainsKey("GptAttempts"))
                                session.Metadata["GptAttempts"] = (int)session.Metadata["GptAttempts"] + 1;
                        }
                    }

                    // Step 7: Track progress
                    _progressTracker.RecordIteration(session, validation, execution);

                    // Step 8: Update session
                    session.NextIteration(execution);

                    // PHASE 6F FIX: Update best PDF AFTER remediation if MCID markers were just added
                    // The best PDF selection happens BEFORE remediation, but MCID markers are created DURING remediation
                    // So we need to capture the post-remediation PDF as the new best if MCID was added
                    // NOTE: IterationCount was just incremented by NextIteration, so we check (IterationCount - 1)
                    if (_jobContext.StructureContext.McidContentRewriteExecuted &&
                        session.CurrentPdf != null &&
                        session.BestIterationNumber == (session.IterationCount - 1))
                    {
                        // This iteration was already selected as best, but now it has MCID markers
                        // Update the best PDF to the post-remediation version
                        session.BestPdf = session.CurrentPdf;
                        _logger.LogInformation(
                            "📌 Updated best PDF to post-remediation version with MCID markers");

                        // Save the updated best PDF
                        await SaveBestPdfAsync(session);
                    }

                    // Update temp file for next validation
                    tempPath = await SaveToTempFileAsync(session.CurrentPdf);
                }

                // Aggregate costs into session metrics
                AggregateSessionCosts(session);

                // Generate final report
                var result = _reporter.GenerateReport(session);

                var fileLabelEnd = !string.IsNullOrEmpty(session.FileName) ? $" [{session.FileName}]" : "";
                _logger.LogInformation($"\n=== REMEDIATION COMPLETE ==={fileLabelEnd}");
                _logger.LogInformation($"Exit Reason: {result.ExitReason}");
                _logger.LogInformation($"Total Iterations: {result.Summary.TotalIterations}");
                _logger.LogInformation($"Compliant: {result.Summary.IsCompliant}");

                // Log best PDF information
                if (session.BestPdf != null && session.BestValidation != null)
                {
                    _logger.LogInformation(
                        $"Best PDF achieved at iteration {session.BestIterationNumber}: " +
                        $"{session.BestValidation.Summary.ComplianceScore:F1}% compliant " +
                        $"({session.BestValidation.Violations.Count} violations)");

                    if (options.SaveBestPdf)
                    {
                        _logger.LogInformation($"Best PDF saved to: {options.BestPdfOutputPath}");
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Remediation failed with exception");

                session.ExitReason = ExitReason.FatalError;
                session.Complete(session.LastValidation ?? session.InitialValidation);

                // Aggregate costs even on failure
                AggregateSessionCosts(session);

                var result = _reporter.GenerateReport(session);
                result.Success = false;
                result.OutputPdf = session.LastValidatedPdf ?? session.OriginalPdf; // Use last good PDF

                return result;
            }
        }

        private void AggregateSessionCosts(RemediationSession session)
        {
            try
            {
                var costSummary = _costTracking.GetSessionSummary(session.SessionId.ToString());
                if (costSummary != null)
                {
                    session.Metrics.TotalCost = costSummary.TotalCost;

                    foreach (var service in costSummary.ServiceBreakdown)
                    {
                        session.Metrics.CostsByService[service.Key] = service.Value.TotalCost;
                        session.Metrics.TokensByService[service.Key] = new TokenUsage
                        {
                            InputTokens = service.Value.TotalInputTokens,
                            OutputTokens = service.Value.TotalOutputTokens
                        };
                    }

                    _logger.LogInformation($"💰 Total remediation cost: ${session.Metrics.TotalCost:F4}");
                }

                // End the cost tracking session
                _costTracking.EndSession(session.SessionId.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to aggregate session costs");
            }
        }

        private async Task<WordToPdfConverter.Models.PdfUA.ValidationResult> ValidateAsync(
            RemediationSession session,
            string pdfPath)
        {
            _logger.LogInformation("Validating PDF...");

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var validation = await _veraPdfService.ValidatePdfAsync(pdfPath);

                stopwatch.Stop();

                // Check if validation failed - log but DON'T throw
                // Let the exit evaluator handle the failure
                if (validation.Status == ValidationStatus.Failed)
                {
                    _logger.LogError($"PDF validation failed: {validation.ErrorMessage}");
                    _logger.LogWarning("Validation failure indicates PDF corruption - will use last good PDF");
                    return validation;
                }

                _logger.LogInformation(
                    $"Validation complete: {validation.Violations.Count} violations, " +
                    $"{validation.Summary.ComplianceScore:F1}% compliant " +
                    $"({stopwatch.ElapsedMilliseconds}ms)");

                return validation;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Validation failed with exception");
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

        private async Task SaveBestPdfAsync(RemediationSession session)
        {
            if (!session.Options.SaveBestPdf || session.BestPdf == null)
                return;

            try
            {
                // Create output directory if it doesn't exist
                var outputDir = session.Options.BestPdfOutputPath;
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // Generate filename with iteration number and timestamp
                var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var baseFileName = !string.IsNullOrEmpty(session.FileName)
                    ? Path.GetFileNameWithoutExtension(session.FileName)
                    : "document";

                var fileName = $"{baseFileName}_best_iter{session.BestIterationNumber}_{timestamp}.pdf";
                var outputPath = Path.Combine(outputDir, fileName);

                await File.WriteAllBytesAsync(outputPath, session.BestPdf);

                _logger.LogInformation(
                    $"💾 Best PDF saved to: {outputPath} " +
                    $"({session.BestValidation.Summary.ComplianceScore:F1}% compliant)");

                // Save violation report alongside the PDF
                await SaveViolationReportAsync(session, outputDir, baseFileName, timestamp);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save best PDF to disk");
            }
        }

        private async Task SaveViolationReportAsync(RemediationSession session, string outputDir, string baseFileName, string timestamp)
        {
            try
            {
                var reportFileName = $"{baseFileName}_best_iter{session.BestIterationNumber}_{timestamp}_violations.txt";
                var reportPath = Path.Combine(outputDir, reportFileName);

                var report = new StringBuilder();
                report.AppendLine($"PDF/UA Violation Report");
                report.AppendLine($"=======================");
                report.AppendLine($"Document: {session.FileName ?? "Unknown"}");
                report.AppendLine($"Iteration: {session.BestIterationNumber}");
                report.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                report.AppendLine($"Compliance Score: {session.BestValidation.Summary.ComplianceScore:F1}%");
                report.AppendLine($"Total Violations: {session.BestValidation.Violations.Count}");
                report.AppendLine();

                // Add cost breakdown if available
                if (session.Metrics.TotalCost > 0)
                {
                    report.AppendLine($"COST BREAKDOWN");
                    report.AppendLine($"==============");
                    report.AppendLine($"Total Cost: ${session.Metrics.TotalCost:F4}");

                    if (session.Metrics.CostsByService.Any())
                    {
                        foreach (var service in session.Metrics.CostsByService.OrderBy(s => s.Key))
                        {
                            var tokens = session.Metrics.TokensByService.ContainsKey(service.Key)
                                ? session.Metrics.TokensByService[service.Key]
                                : null;

                            if (tokens != null)
                            {
                                report.AppendLine($"  - {service.Key}: ${service.Value:F4} " +
                                    $"({tokens.InputTokens:N0} input, {tokens.OutputTokens:N0} output tokens)");
                            }
                            else
                            {
                                report.AppendLine($"  - {service.Key}: ${service.Value:F4}");
                            }
                        }
                    }
                    report.AppendLine();
                }

                if (session.BestValidation.Violations.Any())
                {
                    // Group violations by category
                    var violationsByCategory = session.BestValidation.Violations
                        .GroupBy(v => v.RuleId?.Split('-').FirstOrDefault() ?? "Unknown")
                        .OrderBy(g => g.Key);

                    foreach (var group in violationsByCategory)
                    {
                        report.AppendLine($"Category: {group.Key} ({group.Count()} violations)");
                        report.AppendLine(new string('-', 50));

                        foreach (var violation in group)
                        {
                            report.AppendLine($"  Rule: {violation.RuleId}");
                            report.AppendLine($"  Description: {violation.Description}");
                            if (!string.IsNullOrEmpty(violation.Context))
                                report.AppendLine($"  Context: {violation.Context}");
                            if (violation.Location != null)
                                report.AppendLine($"  Location: Page {violation.Location.PageNumber}");
                            report.AppendLine();
                        }
                    }
                }
                else
                {
                    report.AppendLine("✅ No violations found - PDF is fully compliant!");
                }

                await File.WriteAllTextAsync(reportPath, report.ToString());
                _logger.LogInformation($"📄 Violation report saved to: {reportPath}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save violation report");
            }
        }
    }
}
