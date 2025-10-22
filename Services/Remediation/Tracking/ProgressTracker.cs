using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.PdfUA;
using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation.Tracking
{
    /// <summary>
    /// Tracks progress across remediation iterations
    /// </summary>
    public class ProgressTracker
    {
        private readonly ILogger<ProgressTracker> _logger;

        public ProgressTracker(ILogger<ProgressTracker> logger)
        {
            _logger = logger;
        }

        public void RecordIteration(
            RemediationSession session,
            ValidationResult validation,
            ExecutionResult execution)
        {
            var snapshot = new IterationSnapshot
            {
                IterationNumber = session.IterationCount,
                Timestamp = DateTime.UtcNow,
                ViolationCount = validation.Violations.Count,
                ComplianceScore = validation.Summary.ComplianceScore,
                PhasesExecuted = execution.Phases.Select(p => p.PhaseName).ToList(),
                FixesApplied = execution.Phases
                    .SelectMany(p => p.ServiceResults)
                    .Sum(s => s.IssuesFixed),
                Duration = execution.Duration
            };

            // Group violations by category
            snapshot.ViolationsByCategory = GroupViolationsByCategory(validation);

            session.History.Add(snapshot);

            // Calculate metrics
            if (session.History.Count >= 2)
            {
                CalculateProgressMetrics(session);
            }

            _logger.LogInformation(
                $"Iteration {session.IterationCount} complete: " +
                $"{validation.Violations.Count} violations, " +
                $"{validation.Summary.ComplianceScore:F1}% compliant, " +
                $"{snapshot.FixesApplied} fixes applied");
        }

        private System.Collections.Generic.Dictionary<ViolationCategory, int> GroupViolationsByCategory(
            ValidationResult validation)
        {
            var dict = new System.Collections.Generic.Dictionary<ViolationCategory, int>();

            foreach (ViolationCategory category in Enum.GetValues(typeof(ViolationCategory)))
            {
                dict[category] = 0;
            }

            // Count violations by category (simplified - would need proper categorization)
            foreach (var violation in validation.Violations)
            {
                var clause = violation.Clause ?? "";

                if (clause.StartsWith("7.1"))
                    dict[ViolationCategory.Structure]++;
                else if (clause.StartsWith("6.1") || clause.StartsWith("6.2"))
                    dict[ViolationCategory.Metadata]++;
                else if (clause.StartsWith("7.3"))
                    dict[ViolationCategory.Content]++;
                else
                    dict[ViolationCategory.Unknown]++;
            }

            return dict;
        }

        private void CalculateProgressMetrics(RemediationSession session)
        {
            var current = session.History.Last();
            var previous = session.History[session.History.Count - 2];

            // Violation reduction rate
            if (previous.ViolationCount > 0)
            {
                session.Metrics.ViolationReductionRate =
                    (previous.ViolationCount - current.ViolationCount) /
                    (double)previous.ViolationCount;
            }

            // Compliance improvement
            session.Metrics.ComplianceImprovement =
                current.ComplianceScore - previous.ComplianceScore;

            // Average fix rate
            session.Metrics.AverageFixRate =
                session.History.Average(h => h.FixesApplied);

            // Efficiency score (fixes per second)
            var totalFixes = session.History.Sum(h => h.FixesApplied);
            var totalSeconds = session.History.Sum(h => h.Duration.TotalSeconds);
            session.Metrics.EfficiencyScore = totalSeconds > 0
                ? totalFixes / totalSeconds
                : 0;
        }
    }
}
