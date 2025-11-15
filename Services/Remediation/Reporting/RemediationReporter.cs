using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.PdfUA;
using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation.Reporting
{
    /// <summary>
    /// Generates comprehensive reports of remediation process
    /// </summary>
    public class RemediationReporter
    {
        private readonly ILogger<RemediationReporter> _logger;

        public RemediationReporter(ILogger<RemediationReporter> logger)
        {
            _logger = logger;
        }

        public RemediationResult GenerateReport(RemediationSession session)
        {
            // Determine which PDF to return based on options and quality
            byte[] outputPdf = session.CurrentPdf;
            ValidationResult finalValidation = session.CurrentValidation;

            if (session.Options.ReturnBestPdf &&
                session.BestPdf != null &&
                session.BestValidation != null)
            {
                // Use best PDF if it's better than current
                var currentScore = session.CurrentValidation?.Summary.ComplianceScore ?? 0;
                var bestScore = session.BestValidation.Summary.ComplianceScore;

                if (bestScore > currentScore)
                {
                    outputPdf = session.BestPdf;
                    finalValidation = session.BestValidation;

                    _logger.LogInformation(
                        $"📊 Using best PDF from iteration {session.BestIterationNumber} " +
                        $"({bestScore:F1}% vs current {currentScore:F1}%)");
                }
            }

            var result = new RemediationResult
            {
                Success = session.IsCompliant,
                ExitReason = session.ExitReason,
                OutputPdf = outputPdf,
                Summary = GenerateSummary(session),
                PhaseResults = session.AllPhaseResults,
                IterationHistory = session.History,
                Metrics = session.Metrics,
                RemainingViolations = finalValidation?.Violations ?? new List<PdfUAViolation>(),
                Recommendations = GenerateRecommendations(session),
                InitialValidation = session.InitialValidation,
                FinalValidation = finalValidation
            };

            LogSummary(result);

            return result;
        }

        private RemediationSummary GenerateSummary(RemediationSession session)
        {
            var initialViolationCount = session.InitialValidation?.Violations.Count ?? 0;
            var finalViolationCount = session.CurrentValidation?.Violations.Count ?? 0;
            var initialCompliance = session.InitialValidation?.Summary.ComplianceScore ?? 0;
            var finalCompliance = session.CurrentValidation?.Summary.ComplianceScore ?? 0;

            return new RemediationSummary
            {
                TotalIterations = session.IterationCount,
                TotalDuration = session.ElapsedTime,
                InitialViolationCount = initialViolationCount,
                FinalViolationCount = finalViolationCount,
                ViolationsFixed = initialViolationCount - finalViolationCount,
                ComplianceImprovement = finalCompliance - initialCompliance,
                IsCompliant = session.IsCompliant
            };
        }

        private List<Recommendation> GenerateRecommendations(RemediationSession session)
        {
            var recommendations = new List<Recommendation>();

            switch (session.ExitReason)
            {
                case ExitReason.Success:
                    recommendations.Add(new Recommendation
                    {
                        Type = RecommendationType.Validation,
                        Description = "Document is now PDF/UA compliant. Consider final validation with Adobe Acrobat Pro.",
                        Priority = RecommendationPriority.Low
                    });
                    break;

                case ExitReason.NoProgress:
                    recommendations.Add(new Recommendation
                    {
                        Type = RecommendationType.ManualIntervention,
                        Description = "Remediation stagnated. Remaining violations may require manual intervention or new tooling.",
                        Priority = RecommendationPriority.High,
                        SuggestedAction = "Review remaining violations and develop targeted fixes"
                    });
                    break;

                case ExitReason.Regression:
                    recommendations.Add(new Recommendation
                    {
                        Type = RecommendationType.ProcessImprovement,
                        Description = "Regression detected. Review service execution order and add validation between phases.",
                        Priority = RecommendationPriority.Critical,
                        SuggestedAction = "Analyze which services are conflicting"
                    });
                    break;

                case ExitReason.MaxIterationsReached:
                    recommendations.Add(new Recommendation
                    {
                        Type = RecommendationType.ProcessImprovement,
                        Description = "Maximum iterations reached. Document may be complex or require additional services.",
                        Priority = RecommendationPriority.Medium,
                        SuggestedAction = "Review remaining violations and consider increasing iteration limit"
                    });
                    break;

                case ExitReason.ThresholdMet:
                    recommendations.Add(new Recommendation
                    {
                        Type = RecommendationType.Validation,
                        Description = "Acceptable compliance threshold met. Review remaining violations for importance.",
                        Priority = RecommendationPriority.Low
                    });
                    break;
            }

            return recommendations;
        }

        private void LogSummary(RemediationResult result)
        {
            _logger.LogInformation("=== REMEDIATION COMPLETE ===");
            _logger.LogInformation($"Result: {result.ExitReason}");
            _logger.LogInformation($"Compliant: {result.Summary.IsCompliant}");
            _logger.LogInformation($"Iterations: {result.Summary.TotalIterations}");
            _logger.LogInformation($"Duration: {result.Summary.FormattedDuration}");
            _logger.LogInformation($"Violations Fixed: {result.Summary.ViolationsFixed}");
            _logger.LogInformation($"Remaining: {result.Summary.FinalViolationCount}");
            _logger.LogInformation($"Compliance: {result.Summary.ComplianceImprovement:+0.0;-0.0}% improvement");
        }
    }
}
