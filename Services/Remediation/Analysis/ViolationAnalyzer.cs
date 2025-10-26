using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WordToPdfConverter.Models.PdfUA;
using WordToPdfConverter.Services.Remediation.Models;

namespace WordToPdfConverter.Services.Remediation.Analysis
{
    /// <summary>
    /// Analyzes violations to categorize, prioritize, and identify patterns
    /// </summary>
    public class ViolationAnalyzer
    {
        private readonly ILogger<ViolationAnalyzer> _logger;

        public ViolationAnalyzer(ILogger<ViolationAnalyzer> logger)
        {
            _logger = logger;
        }

        public async Task<ViolationAnalysis> AnalyzeAsync(
            List<PdfUAViolation> violations,
            RemediationSession session)
        {
            var analysis = new ViolationAnalysis();

            // Categorize violations
            analysis.Categories = CategorizeViolations(violations);

            // Identify patterns if we have history
            if (session.History.Count > 0)
            {
                analysis.Patterns = IdentifyPatterns(violations, session.History);
                analysis.Regressions = DetectRegressions(violations, session.History);
            }

            // Prioritize
            analysis.PriorityQueue = PrioritizeViolations(violations, session);

            _logger.LogInformation(
                $"Analyzed {violations.Count} violations: " +
                $"Structure={analysis.Categories.GetValueOrDefault(ViolationCategory.Structure)?.Count ?? 0}, " +
                $"Content={analysis.Categories.GetValueOrDefault(ViolationCategory.Content)?.Count ?? 0}, " +
                $"Whitespace={analysis.Categories.GetValueOrDefault(ViolationCategory.Whitespace)?.Count ?? 0}, " +
                $"Metadata={analysis.Categories.GetValueOrDefault(ViolationCategory.Metadata)?.Count ?? 0}");

            return await Task.FromResult(analysis);
        }

        private Dictionary<ViolationCategory, List<PdfUAViolation>> CategorizeViolations(
            List<PdfUAViolation> violations)
        {
            return violations
                .GroupBy(v => ClassifyViolation(v))
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        private ViolationCategory ClassifyViolation(PdfUAViolation violation)
        {
            var clause = violation.Clause ?? "";
            var desc = violation.Description?.ToLowerInvariant() ?? "";

            // Table and List issues (7.2, 7.5, 7.6) - Check FIRST for proper categorization
            if (clause.StartsWith("7.2") || clause.StartsWith("7.5") || clause.StartsWith("7.6") ||
                desc.Contains("table") || desc.Contains("th") || desc.Contains("td") ||
                desc.Contains("scope") || desc.Contains("headers"))
                return ViolationCategory.TableAndList;

            // Form fields (7.18.4 specifically for widget nesting)
            if (clause.StartsWith("7.18.4") ||
                (clause.StartsWith("7.18") && (desc.Contains("widget") || desc.Contains("form"))))
                return ViolationCategory.FormFields;

            // Structure issues (7.1) - Including artifact/tagged content
            if (clause.StartsWith("7.1"))
                return ViolationCategory.Structure;

            // Annotations (7.18 - other than form fields)
            if (clause.StartsWith("7.18"))
                return ViolationCategory.Annotations;

            // Content issues (7.3)
            if (clause.StartsWith("7.3"))
                return ViolationCategory.Content;

            // Whitespace
            if (desc.Contains("whitespace") || desc.Contains("white space"))
                return ViolationCategory.Whitespace;

            // Metadata (5 - PDF/UA identifier, 6.1, 6.2)
            if (clause.StartsWith("5") || clause.StartsWith("6.1") || clause.StartsWith("6.2"))
                return ViolationCategory.Metadata;

            // Alternative Text issues
            if (desc.Contains("alt text") || desc.Contains("alternative text") ||
                desc.Contains("figure") || clause.StartsWith("7.3"))
                return ViolationCategory.AlternateText;

            // Links
            if (desc.Contains("link") || clause.StartsWith("7.18.5"))
                return ViolationCategory.Links;

            // Fonts (7.21)
            if (clause.StartsWith("7.21") || desc.Contains("font"))
                return ViolationCategory.Fonts;

            // Language
            if (desc.Contains("language") || desc.Contains("lang"))
                return ViolationCategory.Language;

            return ViolationCategory.Unknown;
        }

        private List<ViolationPattern> IdentifyPatterns(
            List<PdfUAViolation> currentViolations,
            List<IterationSnapshot> history)
        {
            var patterns = new List<ViolationPattern>();

            if (history.Count < 2)
                return patterns;

            // Group current violations by rule ID
            var currentByRule = currentViolations
                .GroupBy(v => v.RuleId)
                .ToDictionary(g => g.Key, g => g.Count());

            // Check previous iteration
            var previousSnapshot = history[history.Count - 1];

            foreach (var kvp in currentByRule)
            {
                var ruleId = kvp.Key;
                var currentCount = kvp.Value;

                // Check if this rule appeared in previous iterations
                var historyCount = history.Count(s =>
                    s.ViolationsByCategory.Values.Sum() > 0);

                if (historyCount >= 2)
                {
                    // Recurring pattern - appears in multiple iterations
                    patterns.Add(new ViolationPattern
                    {
                        PatternId = $"recurring-{ruleId}",
                        Description = $"Violation {ruleId} recurring across iterations",
                        Occurrences = historyCount,
                        AffectedClauses = new List<string> { ruleId },
                        Type = PatternType.Recurring
                    });
                }
            }

            return patterns;
        }

        private List<PdfUAViolation> DetectRegressions(
            List<PdfUAViolation> currentViolations,
            List<IterationSnapshot> history)
        {
            // Regressions are violations that were previously fixed but reappeared
            // For now, just return empty list - would need to track which specific
            // violations were fixed to detect true regressions
            return new List<PdfUAViolation>();
        }

        private List<PdfUAViolation> PrioritizeViolations(
            List<PdfUAViolation> violations,
            RemediationSession session)
        {
            return violations
                .OrderByDescending(v => v.Severity)
                .ThenBy(v => v.Clause)
                .ToList();
        }
    }

    /// <summary>
    /// Result of violation analysis
    /// </summary>
    public class ViolationAnalysis
    {
        public Dictionary<ViolationCategory, List<PdfUAViolation>> Categories { get; set; }
            = new();

        public List<ViolationPattern> Patterns { get; set; } = new();
        public List<PdfUAViolation> Regressions { get; set; } = new();
        public List<PdfUAViolation> PriorityQueue { get; set; } = new();

        public int TotalCount => Categories.Values.Sum(list => list.Count);
    }

    /// <summary>
    /// Pattern detected in violations across iterations
    /// </summary>
    public class ViolationPattern
    {
        public string PatternId { get; set; }
        public string Description { get; set; }
        public int Occurrences { get; set; }
        public List<string> AffectedClauses { get; set; }
        public PatternType Type { get; set; }
    }

    public enum PatternType
    {
        Recurring,        // Same violation keeps appearing
        Multiplying,      // Violation count increasing
        Regressing,       // Fixed violations reappearing
        Cascading,        // One fix causing new violations
        Stable            // Violation persists unchanged
    }
}
