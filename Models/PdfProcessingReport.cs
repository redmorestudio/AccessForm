using System;
using System.Collections.Generic;

namespace WordToPdfConverter.Models
{
    /// <summary>
    /// Comprehensive report of PDF processing statistics and transformations
    /// </summary>
    public class PdfProcessingReport
    {
        public DateTime ProcessingStartTime { get; set; }
        public DateTime ProcessingEndTime { get; set; }
        public TimeSpan TotalProcessingTime => ProcessingEndTime - ProcessingStartTime;

        // Input PDF statistics
        public InputStatistics Input { get; set; } = new InputStatistics();

        // Output PDF statistics
        public OutputStatistics Output { get; set; } = new OutputStatistics();

        // Processing steps
        public List<ProcessingStep> Steps { get; set; } = new List<ProcessingStep>();

        // Issues found and fixed
        public IssuesReport Issues { get; set; } = new IssuesReport();

        // Field statistics
        public FieldStatistics Fields { get; set; } = new FieldStatistics();

        // Accessibility enhancements
        public ProcessingAccessibilityInfo Accessibility { get; set; } = new ProcessingAccessibilityInfo();

        /// <summary>
        /// Generate a formatted text report
        /// </summary>
        public string GenerateTextReport()
        {
            var lines = new List<string>();

            lines.Add("╔══════════════════════════════════════════════════════════════════════╗");
            lines.Add("║                    PDF PROCESSING REPORT                             ║");
            lines.Add("╚══════════════════════════════════════════════════════════════════════╝");
            lines.Add("");

            // Timing
            lines.Add($"⏱️  Processing Time: {TotalProcessingTime.TotalSeconds:F2} seconds");
            lines.Add($"   Started:  {ProcessingStartTime:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"   Finished: {ProcessingEndTime:yyyy-MM-dd HH:mm:ss}");
            lines.Add("");

            // Input/Output
            lines.Add("📊 DOCUMENT STATISTICS");
            lines.Add("─────────────────────────────────────────────────────────────────────");
            lines.Add($"Input PDF:");
            lines.Add($"  • File size: {FormatBytes(Input.FileSizeBytes)}");
            lines.Add($"  • Pages: {Input.PageCount}");
            lines.Add($"  • PDF version: {Input.PdfVersion}");
            lines.Add($"  • Form fields: {Input.FormFieldCount}");
            lines.Add("");
            lines.Add($"Output PDF:");
            lines.Add($"  • File size: {FormatBytes(Output.FileSizeBytes)} ({GetSizeChange()})");
            lines.Add($"  • Pages: {Output.PageCount}");
            lines.Add($"  • PDF version: {Output.PdfVersion}");
            lines.Add($"  • Form fields: {Output.FormFieldCount}");
            lines.Add("");

            // Fields
            if (Fields.TotalFields > 0)
            {
                lines.Add("📝 FORM FIELDS");
                lines.Add("─────────────────────────────────────────────────────────────────────");
                lines.Add($"  • Total fields: {Fields.TotalFields}");
                lines.Add($"  • Text fields: {Fields.TextFields}");
                lines.Add($"  • Checkboxes: {Fields.Checkboxes}");
                lines.Add($"  • Radio buttons: {Fields.RadioButtons}");
                lines.Add($"  • Dropdowns: {Fields.Dropdowns}");
                lines.Add($"  • Calculated fields: {Fields.CalculatedFields}");
                lines.Add($"  • Fields renamed: {Fields.FieldsRenamed}");
                lines.Add($"  • Fields added: {Fields.FieldsAdded}");
                lines.Add("");
            }

            // Issues
            if (Issues.TotalIssuesFound > 0)
            {
                lines.Add("🔧 ISSUES FOUND & FIXED");
                lines.Add("─────────────────────────────────────────────────────────────────────");
                lines.Add($"  • Artifact violations: {Issues.ArtifactViolations.Found} found, {Issues.ArtifactViolations.Fixed} fixed");
                lines.Add($"  • Font issues: {Issues.FontIssues.Found} found, {Issues.FontIssues.Fixed} fixed");
                lines.Add($"  • Tag structure issues: {Issues.TagStructureIssues.Found} found, {Issues.TagStructureIssues.Fixed} fixed");
                lines.Add($"  • Metadata issues: {Issues.MetadataIssues.Found} found, {Issues.MetadataIssues.Fixed} fixed");
                lines.Add("");
            }

            // Accessibility
            lines.Add("♿ ACCESSIBILITY ENHANCEMENTS");
            lines.Add("─────────────────────────────────────────────────────────────────────");
            lines.Add($"  • PDF/UA compliant: {(Accessibility.IsPdfUaCompliant ? "✅ Yes" : "⚠️  No")}");
            lines.Add($"  • Tagged PDF: {(Accessibility.IsTagged ? "✅ Yes" : "❌ No")}");
            lines.Add($"  • Language set: {(string.IsNullOrEmpty(Accessibility.Language) ? "❌ No" : $"✅ {Accessibility.Language}")}");
            lines.Add($"  • Alt text added: {Accessibility.AltTextAdded}");
            lines.Add($"  • Tooltips added: {Accessibility.TooltipsAdded}");
            lines.Add($"  • Reading order fixed: {(Accessibility.ReadingOrderFixed ? "✅ Yes" : "No")}");
            lines.Add("");

            // Processing steps
            if (Steps.Count > 0)
            {
                lines.Add("⚙️  PROCESSING STEPS");
                lines.Add("─────────────────────────────────────────────────────────────────────");
                int stepNum = 1;
                foreach (var step in Steps)
                {
                    var status = step.Success ? "✅" : "❌";
                    var duration = $"({step.DurationMs}ms)";
                    lines.Add($"  {stepNum}. {status} {step.Name} {duration}");
                    if (!string.IsNullOrEmpty(step.Details))
                    {
                        lines.Add($"     {step.Details}");
                    }
                    stepNum++;
                }
                lines.Add("");
            }

            lines.Add("╚══════════════════════════════════════════════════════════════════════╝");

            return string.Join(Environment.NewLine, lines);
        }

        private string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F2} MB";
        }

        private string GetSizeChange()
        {
            if (Input.FileSizeBytes == 0) return "";

            var change = Output.FileSizeBytes - Input.FileSizeBytes;
            var percentChange = (change / (double)Input.FileSizeBytes) * 100;

            if (change > 0)
                return $"+{FormatBytes(change)}, +{percentChange:F1}%";
            else if (change < 0)
                return $"{FormatBytes(change)}, {percentChange:F1}%";
            else
                return "no change";
        }
    }

    public class InputStatistics
    {
        public long FileSizeBytes { get; set; }
        public int PageCount { get; set; }
        public string PdfVersion { get; set; } = "";
        public int FormFieldCount { get; set; }
    }

    public class OutputStatistics
    {
        public long FileSizeBytes { get; set; }
        public int PageCount { get; set; }
        public string PdfVersion { get; set; } = "";
        public int FormFieldCount { get; set; }
    }

    public class ProcessingStep
    {
        public string Name { get; set; } = "";
        public bool Success { get; set; }
        public long DurationMs { get; set; }
        public string Details { get; set; } = "";
    }

    public class IssuesReport
    {
        public IssueCategory ArtifactViolations { get; set; } = new IssueCategory();
        public IssueCategory FontIssues { get; set; } = new IssueCategory();
        public IssueCategory TagStructureIssues { get; set; } = new IssueCategory();
        public IssueCategory MetadataIssues { get; set; } = new IssueCategory();

        public int TotalIssuesFound =>
            ArtifactViolations.Found +
            FontIssues.Found +
            TagStructureIssues.Found +
            MetadataIssues.Found;

        public int TotalIssuesFixed =>
            ArtifactViolations.Fixed +
            FontIssues.Fixed +
            TagStructureIssues.Fixed +
            MetadataIssues.Fixed;
    }

    public class IssueCategory
    {
        public int Found { get; set; }
        public int Fixed { get; set; }
    }

    public class FieldStatistics
    {
        public int TotalFields { get; set; }
        public int TextFields { get; set; }
        public int Checkboxes { get; set; }
        public int RadioButtons { get; set; }
        public int Dropdowns { get; set; }
        public int CalculatedFields { get; set; }
        public int FieldsRenamed { get; set; }
        public int FieldsAdded { get; set; }
    }

    public class ProcessingAccessibilityInfo
    {
        public bool IsPdfUaCompliant { get; set; }
        public bool IsTagged { get; set; }
        public string Language { get; set; } = "";
        public int AltTextAdded { get; set; }
        public int TooltipsAdded { get; set; }
        public bool ReadingOrderFixed { get; set; }
    }
}
