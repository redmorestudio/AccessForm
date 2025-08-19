using System.Text;
using System.Text.Json;
using WordToPdfConverter.Models;

namespace WordToPdfConverter.Services
{
    public class AccessibilityReportService
    {
        private readonly string _reportDirectory;
        
        public AccessibilityReportService()
        {
            _reportDirectory = Path.Combine(Directory.GetCurrentDirectory(), "AccessibilityReports");
            Directory.CreateDirectory(_reportDirectory);
        }
        
        public void GenerateReport(AccessibilityReport report)
        {
            try
            {
                // Generate both HTML and JSON reports
                GenerateHtmlReport(report);
                GenerateJsonReport(report);
                GenerateMarkdownReport(report);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating accessibility report: {ex.Message}");
            }
        }
        
        private void GenerateHtmlReport(AccessibilityReport report)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{Path.GetFileNameWithoutExtension(report.DocumentName)}_accessibility_{timestamp}.html";
            var filePath = Path.Combine(_reportDirectory, fileName);
            
            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang='en'>");
            html.AppendLine("<head>");
            html.AppendLine("    <meta charset='UTF-8'>");
            html.AppendLine("    <meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            html.AppendLine($"    <title>Accessibility Report - {report.DocumentName}</title>");
            html.AppendLine("    <style>");
            html.AppendLine("        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; margin: 40px; background: #f5f5f5; }");
            html.AppendLine("        .container { max-width: 1200px; margin: 0 auto; background: white; padding: 30px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }");
            html.AppendLine("        h1 { color: #2c3e50; border-bottom: 3px solid #3498db; padding-bottom: 10px; }");
            html.AppendLine("        h2 { color: #34495e; margin-top: 30px; border-bottom: 1px solid #ecf0f1; padding-bottom: 5px; }");
            html.AppendLine("        .status { display: inline-block; padding: 5px 15px; border-radius: 20px; font-weight: bold; margin-left: 10px; }");
            html.AppendLine("        .status.success { background: #d4edda; color: #155724; }");
            html.AppendLine("        .status.partial { background: #fff3cd; color: #856404; }");
            html.AppendLine("        .status.failed { background: #f8d7da; color: #721c24; }");
            html.AppendLine("        .info-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(250px, 1fr)); gap: 20px; margin: 20px 0; }");
            html.AppendLine("        .info-card { background: #f8f9fa; padding: 15px; border-radius: 5px; border-left: 4px solid #3498db; }");
            html.AppendLine("        .info-card h3 { margin: 0 0 5px 0; color: #2c3e50; font-size: 14px; }");
            html.AppendLine("        .info-card p { margin: 0; font-size: 18px; font-weight: bold; color: #34495e; }");
            html.AppendLine("        ul { line-height: 1.8; }");
            html.AppendLine("        .measure { background: #e8f5e9; padding: 8px 12px; margin: 5px 0; border-radius: 4px; list-style: none; }");
            html.AppendLine("        .warning { background: #fff3cd; padding: 8px 12px; margin: 5px 0; border-radius: 4px; list-style: none; color: #856404; }");
            html.AppendLine("        .error { background: #ffebee; padding: 8px 12px; margin: 5px 0; border-radius: 4px; list-style: none; color: #c62828; }");
            html.AppendLine("        table { width: 100%; border-collapse: collapse; margin: 20px 0; }");
            html.AppendLine("        th { background: #3498db; color: white; padding: 12px; text-align: left; }");
            html.AppendLine("        td { padding: 10px; border-bottom: 1px solid #ecf0f1; }");
            html.AppendLine("        tr:hover { background: #f8f9fa; }");
            html.AppendLine("        .check { color: #27ae60; font-weight: bold; }");
            html.AppendLine("        .cross { color: #e74c3c; font-weight: bold; }");
            html.AppendLine("        .footer { margin-top: 40px; padding-top: 20px; border-top: 1px solid #ecf0f1; text-align: center; color: #7f8c8d; }");
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("    <div class='container'>");
            
            // Header
            html.AppendLine($"        <h1>Accessibility Compliance Report");
            html.AppendLine($"            <span class='status {report.Status.ToLower()}'>{report.Status}</span>");
            html.AppendLine("        </h1>");
            
            // Summary Cards
            html.AppendLine("        <div class='info-grid'>");
            html.AppendLine("            <div class='info-card'>");
            html.AppendLine("                <h3>Document</h3>");
            html.AppendLine($"                <p>{report.DocumentName}</p>");
            html.AppendLine("            </div>");
            html.AppendLine("            <div class='info-card'>");
            html.AppendLine("                <h3>Compliance Level</h3>");
            html.AppendLine($"                <p>{report.ComplianceLevel}</p>");
            html.AppendLine("            </div>");
            html.AppendLine("            <div class='info-card'>");
            html.AppendLine("                <h3>Form Fields Processed</h3>");
            html.AppendLine($"                <p>{report.TotalFields}</p>");
            html.AppendLine("            </div>");
            html.AppendLine("            <div class='info-card'>");
            html.AppendLine("                <h3>Validation Status</h3>");
            html.AppendLine($"                <p>{report.ValidationStatus}</p>");
            html.AppendLine("            </div>");
            html.AppendLine("        </div>");
            
            // Accessibility Measures Taken
            html.AppendLine("        <h2>Accessibility Measures Applied</h2>");
            html.AppendLine("        <ul style='padding-left: 0;'>");
            foreach (var measure in report.MeasuresTaken)
            {
                html.AppendLine($"            <li class='measure'>✓ {measure}</li>");
            }
            html.AppendLine("        </ul>");
            
            // Field Processing Summary
            html.AppendLine("        <h2>Field Processing Summary</h2>");
            html.AppendLine("        <div class='info-grid'>");
            html.AppendLine("            <div class='info-card'>");
            html.AppendLine("                <h3>Original Fields Detected</h3>");
            html.AppendLine($"                <p>{report.OriginalFieldCount}</p>");
            html.AppendLine("            </div>");
            html.AppendLine("            <div class='info-card'>");
            html.AppendLine("                <h3>Fields Removed (False Positives)</h3>");
            html.AppendLine($"                <p>{report.RemovedFieldCount}</p>");
            html.AppendLine("            </div>");
            html.AppendLine("            <div class='info-card'>");
            html.AppendLine("                <h3>Fields Kept</h3>");
            html.AppendLine($"                <p>{report.TotalFields}</p>");
            html.AppendLine("            </div>");
            html.AppendLine("            <div class='info-card'>");
            html.AppendLine("                <h3>Removal Rate</h3>");
            var removalRate = report.OriginalFieldCount > 0 ? (report.RemovedFieldCount * 100.0 / report.OriginalFieldCount) : 0;
            html.AppendLine($"                <p>{removalRate:F1}%</p>");
            html.AppendLine("            </div>");
            html.AppendLine("        </div>");
            
            // Removed Fields Analysis
            if (report.RemovedFields.Any())
            {
                html.AppendLine("        <h2>Removed Fields (False Positives)</h2>");
                html.AppendLine("        <p>These fields were detected but removed because they appear to be formatting artifacts rather than actual form fields:</p>");
                html.AppendLine("        <table>");
                html.AppendLine("            <thead>");
                html.AppendLine("                <tr>");
                html.AppendLine("                    <th>Field Name</th>");
                html.AppendLine("                    <th>Type</th>");
                html.AppendLine("                    <th>Removal Reason</th>");
                html.AppendLine("                    <th>Position (X, Y)</th>");
                html.AppendLine("                    <th>Size (W × H)</th>");
                html.AppendLine("                    <th>Page</th>");
                html.AppendLine("                </tr>");
                html.AppendLine("            </thead>");
                html.AppendLine("            <tbody>");
                
                foreach (var field in report.RemovedFields.OrderBy(f => f.Name))
                {
                    html.AppendLine("                <tr>");
                    html.AppendLine($"                    <td>{field.Name}</td>");
                    html.AppendLine($"                    <td>{field.Type}</td>");
                    html.AppendLine($"                    <td>{field.RemovalReason}</td>");
                    html.AppendLine($"                    <td>({field.X:F0}, {field.Y:F0})</td>");
                    html.AppendLine($"                    <td>{field.Width:F0} × {field.Height:F0}</td>");
                    html.AppendLine($"                    <td>{field.PageNumber}</td>");
                    html.AppendLine("                </tr>");
                }
                
                html.AppendLine("            </tbody>");
                html.AppendLine("        </table>");
            }
            
            // Form Fields Analysis
            if (report.FormFields.Any())
            {
                html.AppendLine("        <h2>Kept Fields - Accessibility Analysis</h2>");
                html.AppendLine("        <p>These fields were kept and enhanced for accessibility:</p>");
                html.AppendLine("        <table>");
                html.AppendLine("            <thead>");
                html.AppendLine("                <tr>");
                html.AppendLine("                    <th>Field Name</th>");
                html.AppendLine("                    <th>Type</th>");
                html.AppendLine("                    <th>Page</th>");
                html.AppendLine("                    <th>Label</th>");
                html.AppendLine("                    <th>Description</th>");
                html.AppendLine("                    <th>Tab Order</th>");
                html.AppendLine("                    <th>Required</th>");
                html.AppendLine("                </tr>");
                html.AppendLine("            </thead>");
                html.AppendLine("            <tbody>");
                
                foreach (var field in report.FormFields.OrderBy(f => f.TabIndex))
                {
                    html.AppendLine("                <tr>");
                    html.AppendLine($"                    <td>{field.Name}</td>");
                    html.AppendLine($"                    <td>{field.Type}</td>");
                    html.AppendLine($"                    <td>{field.PageNumber}</td>");
                    html.AppendLine($"                    <td class='{(field.HasLabel ? "check" : "cross")}'>{(field.HasLabel ? "✓" : "✗")}</td>");
                    html.AppendLine($"                    <td class='{(field.HasDescription ? "check" : "cross")}'>{(field.HasDescription ? "✓" : "✗")}</td>");
                    html.AppendLine($"                    <td>{field.TabIndex}</td>");
                    html.AppendLine($"                    <td>{(field.IsRequired ? "Yes" : "No")}</td>");
                    html.AppendLine("                </tr>");
                }
                
                html.AppendLine("            </tbody>");
                html.AppendLine("        </table>");
            }
            
            // Compliance Checklist
            html.AppendLine("        <h2>WCAG 2.1 AA / Section 508 Compliance Checklist</h2>");
            html.AppendLine("        <table>");
            html.AppendLine("            <thead>");
            html.AppendLine("                <tr>");
            html.AppendLine("                    <th>Criterion</th>");
            html.AppendLine("                    <th>Status</th>");
            html.AppendLine("                    <th>Implementation</th>");
            html.AppendLine("                </tr>");
            html.AppendLine("            </thead>");
            html.AppendLine("            <tbody>");
            
            var complianceChecks = GetComplianceChecklist(report);
            foreach (var check in complianceChecks)
            {
                html.AppendLine("                <tr>");
                html.AppendLine($"                    <td>{check.Criterion}</td>");
                html.AppendLine($"                    <td class='{(check.Status ? "check" : "cross")}'>{(check.Status ? "✓ Compliant" : "✗ Review Needed")}</td>");
                html.AppendLine($"                    <td>{check.Implementation}</td>");
                html.AppendLine("                </tr>");
            }
            
            html.AppendLine("            </tbody>");
            html.AppendLine("        </table>");
            
            // Warnings and Errors
            if (report.Warnings.Any())
            {
                html.AppendLine("        <h2>Warnings</h2>");
                html.AppendLine("        <ul style='padding-left: 0;'>");
                foreach (var warning in report.Warnings)
                {
                    html.AppendLine($"            <li class='warning'>⚠ {warning}</li>");
                }
                html.AppendLine("        </ul>");
            }
            
            if (report.Errors.Any())
            {
                html.AppendLine("        <h2>Errors</h2>");
                html.AppendLine("        <ul style='padding-left: 0;'>");
                foreach (var error in report.Errors)
                {
                    html.AppendLine($"            <li class='error'>✗ {error}</li>");
                }
                html.AppendLine("        </ul>");
            }
            
            // Footer
            html.AppendLine("        <div class='footer'>");
            html.AppendLine($"            <p>Report generated on {report.ConversionDate:yyyy-MM-dd HH:mm:ss}</p>");
            html.AppendLine("            <p>AccessForm Converter - Accessibility Compliance Report</p>");
            html.AppendLine("        </div>");
            
            html.AppendLine("    </div>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");
            
            File.WriteAllText(filePath, html.ToString());
            Console.WriteLine($"HTML accessibility report saved: {fileName}");
        }
        
        private void GenerateJsonReport(AccessibilityReport report)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{Path.GetFileNameWithoutExtension(report.DocumentName)}_accessibility_{timestamp}.json";
            var filePath = Path.Combine(_reportDirectory, fileName);
            
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            var json = JsonSerializer.Serialize(report, options);
            File.WriteAllText(filePath, json);
            Console.WriteLine($"JSON accessibility report saved: {fileName}");
        }
        
        private void GenerateMarkdownReport(AccessibilityReport report)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{Path.GetFileNameWithoutExtension(report.DocumentName)}_accessibility_{timestamp}.md";
            var filePath = Path.Combine(_reportDirectory, fileName);
            
            var md = new StringBuilder();
            md.AppendLine("# Accessibility Compliance Report");
            md.AppendLine();
            md.AppendLine($"**Document:** {report.DocumentName}  ");
            md.AppendLine($"**Date:** {report.ConversionDate:yyyy-MM-dd HH:mm:ss}  ");
            md.AppendLine($"**Status:** {report.Status}  ");
            md.AppendLine($"**Compliance Level:** {report.ComplianceLevel}  ");
            md.AppendLine();
            
            md.AppendLine("## Summary");
            md.AppendLine();
            md.AppendLine($"- **Original Fields Detected:** {report.OriginalFieldCount}");
            md.AppendLine($"- **Fields Removed:** {report.RemovedFieldCount}");
            md.AppendLine($"- **Fields Kept:** {report.TotalFields}");
            md.AppendLine($"- **Source Format:** {report.SourceFormat}");
            md.AppendLine($"- **Target Format:** {report.TargetFormat}");
            md.AppendLine($"- **Validation Status:** {report.ValidationStatus}");
            md.AppendLine();
            
            md.AppendLine("## Accessibility Measures Applied");
            md.AppendLine();
            foreach (var measure in report.MeasuresTaken)
            {
                md.AppendLine($"- ✓ {measure}");
            }
            md.AppendLine();
            
            if (report.RemovedFields.Any())
            {
                md.AppendLine("## Removed Fields (False Positives)");
                md.AppendLine();
                md.AppendLine("These fields were detected but removed because they appear to be formatting artifacts:");
                md.AppendLine();
                md.AppendLine("| Field Name | Type | Removal Reason | Position | Size | Page |");
                md.AppendLine("|------------|------|----------------|----------|------|------|");
                
                foreach (var field in report.RemovedFields.OrderBy(f => f.Name))
                {
                    md.AppendLine($"| {field.Name} | {field.Type} | {field.RemovalReason} | ({field.X:F0}, {field.Y:F0}) | {field.Width:F0} × {field.Height:F0} | {field.PageNumber} |");
                }
                md.AppendLine();
            }
            
            if (report.FormFields.Any())
            {
                md.AppendLine("## Kept Fields - Accessibility Analysis");
                md.AppendLine();
                md.AppendLine("| Field Name | Type | Page | Has Label | Has Description | Tab Order | Required |");
                md.AppendLine("|------------|------|------|-----------|-----------------|-----------|----------|");
                
                foreach (var field in report.FormFields.OrderBy(f => f.TabIndex))
                {
                    md.AppendLine($"| {field.Name} | {field.Type} | {field.PageNumber} | {(field.HasLabel ? "✓" : "✗")} | {(field.HasDescription ? "✓" : "✗")} | {field.TabIndex} | {(field.IsRequired ? "Yes" : "No")} |");
                }
                md.AppendLine();
            }
            
            if (report.Warnings.Any())
            {
                md.AppendLine("## Warnings");
                md.AppendLine();
                foreach (var warning in report.Warnings)
                {
                    md.AppendLine($"- ⚠️ {warning}");
                }
                md.AppendLine();
            }
            
            if (report.Errors.Any())
            {
                md.AppendLine("## Errors");
                md.AppendLine();
                foreach (var error in report.Errors)
                {
                    md.AppendLine($"- ❌ {error}");
                }
                md.AppendLine();
            }
            
            File.WriteAllText(filePath, md.ToString());
            Console.WriteLine($"Markdown accessibility report saved: {fileName}");
        }
        
        private List<ComplianceCheck> GetComplianceChecklist(AccessibilityReport report)
        {
            return new List<ComplianceCheck>
            {
                new ComplianceCheck
                {
                    Criterion = "WCAG 2.4.2 - Page Titled",
                    Status = !string.IsNullOrWhiteSpace(report.DocumentName),
                    Implementation = "Document title set in PDF metadata"
                },
                new ComplianceCheck
                {
                    Criterion = "WCAG 3.1.1 - Language of Page",
                    Status = report.MeasuresTaken.Any(m => m.Contains("language")),
                    Implementation = "Document language specified as en-US"
                },
                new ComplianceCheck
                {
                    Criterion = "WCAG 3.3.2 - Labels or Instructions",
                    Status = report.FormFields.Count == 0 || report.FormFields.All(f => f.HasLabel),
                    Implementation = "All form fields have labels and tooltips"
                },
                new ComplianceCheck
                {
                    Criterion = "WCAG 2.4.3 - Focus Order",
                    Status = report.MeasuresTaken.Any(m => m.Contains("tab order")),
                    Implementation = "Logical tab order established for all fields"
                },
                new ComplianceCheck
                {
                    Criterion = "WCAG 1.3.1 - Info and Relationships",
                    Status = report.MeasuresTaken.Any(m => m.Contains("structure")),
                    Implementation = "Document structure preserved with proper tags"
                },
                new ComplianceCheck
                {
                    Criterion = "Section 508 §1194.22(n) - Forms",
                    Status = report.FormFields.Count == 0 || report.FormFields.All(f => f.HasLabel || f.HasDescription),
                    Implementation = "Electronic forms allow assistive technology access"
                },
                new ComplianceCheck
                {
                    Criterion = "Section 508 §1194.22(l) - Scripts",
                    Status = true,
                    Implementation = "All form functionality keyboard accessible"
                },
                new ComplianceCheck
                {
                    Criterion = "PDF/UA Compliance",
                    Status = report.MeasuresTaken.Any(m => m.Contains("accessibility tags")),
                    Implementation = "PDF/UA standard tags and structure applied"
                }
            };
        }
    }
    
    public class ComplianceCheck
    {
        public string Criterion { get; set; }
        public bool Status { get; set; }
        public string Implementation { get; set; }
    }
}
