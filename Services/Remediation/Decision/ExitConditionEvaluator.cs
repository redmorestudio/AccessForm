using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.PdfUA;
using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation.Decision
{
    /// <summary>
    /// Determines when to stop the remediation loop
    /// </summary>
    public class ExitConditionEvaluator
    {
        private readonly ILogger<ExitConditionEvaluator> _logger;

        public ExitConditionEvaluator(ILogger<ExitConditionEvaluator> logger)
        {
            _logger = logger;
        }

        public bool ShouldExit(
            ValidationResult validation,
            RemediationSession session,
            out ExitReason reason)
        {
            reason = ExitReason.NotSet;

            // Success condition - fully compliant
            if (validation.Summary.IsCompliant)
            {
                _logger.LogInformation("✅ PDF is fully compliant!");
                reason = ExitReason.Success;
                return true;
            }

            // Zero violations - exit immediately (nothing to fix)
            if (validation.Violations.Count == 0)
            {
                _logger.LogInformation("✅ No violations found - document already compliant");
                reason = ExitReason.Success;
                return true;
            }

            // Max iterations reached
            if (session.IterationCount >= session.Options.MaxIterations)
            {
                _logger.LogWarning(
                    $"Max iterations ({session.Options.MaxIterations}) reached");
                reason = ExitReason.MaxIterationsReached;
                return true;
            }

            // Timeout
            if (session.ElapsedTime >= session.Options.MaxDuration)
            {
                _logger.LogWarning(
                    $"Timeout exceeded: {session.ElapsedTime.TotalMinutes:F1} minutes");
                reason = ExitReason.Timeout;
                return true;
            }

            // Acceptable threshold met
            if (session.Options.AcceptableComplianceScore.HasValue &&
                validation.Summary.ComplianceScore >=
                session.Options.AcceptableComplianceScore.Value)
            {
                _logger.LogInformation(
                    $"Acceptable compliance threshold met: " +
                    $"{validation.Summary.ComplianceScore:F1}%");
                reason = ExitReason.ThresholdMet;
                return true;
            }

            // No progress (stagnation)
            if (session.Options.DetectStagnation && IsStagnant(session))
            {
                _logger.LogWarning("No progress detected - violations not decreasing");
                reason = ExitReason.NoProgress;
                return true;
            }

            // Regression - violations increasing
            if (session.Options.StopOnRegression && IsRegressing(session))
            {
                _logger.LogWarning("Regression detected - violation count increased");
                reason = ExitReason.Regression;
                return true;
            }

            return false;
        }

        private bool IsStagnant(RemediationSession session)
        {
            var threshold = session.Options.StagnationThreshold;

            if (session.History.Count < threshold + 1)
                return false;

            // Check last N iterations for same violation count
            var recent = session.History.TakeLast(threshold + 1).ToList();
            var violationCounts = recent.Select(h => h.ViolationCount).ToList();

            // If all counts are the same, we're stagnant
            return violationCounts.Distinct().Count() == 1;
        }

        private bool IsRegressing(RemediationSession session)
        {
            if (session.History.Count < 2)
                return false;

            var current = session.History.Last();
            var previous = session.History[session.History.Count - 2];

            // Violation count increased
            return current.ViolationCount > previous.ViolationCount;
        }
    }
}
