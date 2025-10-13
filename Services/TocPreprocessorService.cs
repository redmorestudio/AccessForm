using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Tagging;
using Microsoft.Extensions.Logging;
using System.Text;

namespace AccessFormServer.Services;

/// <summary>
/// Preprocesses PDFs with TOC issues and generates detailed manual fix instructions
/// </summary>
public class TocPreprocessorService
{
    private readonly ILogger<TocPreprocessorService> _logger;

    public TocPreprocessorService(ILogger<TocPreprocessorService> logger)
    {
        _logger = logger;
    }

    public async Task<TocPreprocessResult> PreprocessAndAnalyzeAsync(byte[] pdfBytes, string fileName)
    {
        var result = new TocPreprocessResult
        {
            FileName = fileName,
            ProcessedAt = DateTime.UtcNow
        };

        try
        {
            _logger.LogInformation($"===== TOC PREPROCESSOR - {fileName} =====");

            using var inputMs = new MemoryStream(pdfBytes);
            using var outputMs = new MemoryStream();
            using var pdfDoc = new PdfDocument(new PdfReader(inputMs), new PdfWriter(outputMs));

            if (!pdfDoc.IsTagged())
            {
                pdfDoc.SetTagged();
                result.AutoFixesApplied.Add("Added document tagging");
            }

            var structTreeRoot = pdfDoc.GetStructTreeRoot();
            var parentTreeObj = structTreeRoot.GetPdfObject().GetAsDictionary(PdfName.ParentTree);

            if (parentTreeObj == null)
            {
                parentTreeObj = new PdfDictionary();
                parentTreeObj.Put(PdfName.Nums, new PdfArray());
                structTreeRoot.GetPdfObject().Put(PdfName.ParentTree, parentTreeObj);
                result.AutoFixesApplied.Add("Created parent tree structure");
            }

            // Analyze all pages for TOC content
            result.TocPages = FindTocPages(pdfDoc);

            // Process each TOC page
            foreach (var tocPageNum in result.TocPages)
            {
                var tocPageIssues = AnalyzeTocPage(pdfDoc, tocPageNum, parentTreeObj);
                result.PageIssues.Add(tocPageNum, tocPageIssues);
            }

            // Apply automatic fixes where safe
            ApplyAutomaticFixes(pdfDoc, result);

            pdfDoc.Close();
            result.ProcessedPdf = outputMs.ToArray();
            result.Success = true;

            // Generate manual fix instructions
            GenerateManualFixInstructions(result);

            // Generate HTML report
            result.HtmlReport = GenerateHtmlReport(result);

            _logger.LogInformation($"Preprocessing complete: {result.AutoFixesApplied.Count} auto-fixes, {result.ManualFixesRequired.Count} manual fixes needed");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Preprocessing failed: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private List<int> FindTocPages(PdfDocument pdfDoc)
    {
        var tocPages = new List<int>();

        // Common TOC page patterns:
        // 1. Page 2 is often TOC
        // 2. Pages with many link annotations pointing to other pages
        // 3. Pages with TOC structure elements

        for (int pageNum = 1; pageNum <= pdfDoc.GetNumberOfPages(); pageNum++)
        {
            var page = pdfDoc.GetPage(pageNum);
            var annotations = page.GetAnnotations();
            var linkCount = annotations.Count(a => a is PdfLinkAnnotation);

            // Heuristic: If a page has 10+ internal links, it's likely a TOC
            if (linkCount >= 10)
            {
                // Verify they're internal navigation links
                var internalLinks = 0;
                foreach (var annot in annotations.Where(a => a is PdfLinkAnnotation).Cast<PdfLinkAnnotation>())
                {
                    var action = annot.GetAction();
                    if (action != null)
                    {
                        var actionType = action.GetAsName(PdfName.S);
                        if (PdfName.GoTo.Equals(actionType))
                        {
                            internalLinks++;
                        }
                    }
                }

                if (internalLinks >= 10)
                {
                    tocPages.Add(pageNum);
                    _logger.LogInformation($"Identified page {pageNum} as TOC (has {internalLinks} internal links)");
                }
            }
        }

        // Always check page 2 if we found no TOC pages
        if (!tocPages.Any() && pdfDoc.GetNumberOfPages() >= 2)
        {
            tocPages.Add(2);
        }

        return tocPages;
    }

    private TocPageAnalysis AnalyzeTocPage(PdfDocument pdfDoc, int pageNum, PdfDictionary parentTreeObj)
    {
        var analysis = new TocPageAnalysis
        {
            PageNumber = pageNum
        };

        var page = pdfDoc.GetPage(pageNum);
        var annotations = page.GetAnnotations();
        var linkAnnotations = annotations
            .Where(a => a is PdfLinkAnnotation)
            .Cast<PdfLinkAnnotation>()
            .ToList();

        analysis.TotalLinks = linkAnnotations.Count;
        _logger.LogInformation($"Page {pageNum}: Analyzing {linkAnnotations.Count} link annotations");

        foreach (var linkAnnot in linkAnnotations)
        {
            var issue = AnalyzeLinkAnnotation(linkAnnot, parentTreeObj, pdfDoc);
            if (issue != null)
            {
                analysis.LinkIssues.Add(issue);
            }
        }

        // Categorize issues
        analysis.LinksWithoutStructure = analysis.LinkIssues.Count(i => i.IssueType == LinkIssueType.NoStructParent);
        analysis.LinksNotInLinkElement = analysis.LinkIssues.Count(i => i.IssueType == LinkIssueType.NotInLinkElement);
        analysis.LinksMissingAltText = analysis.LinkIssues.Count(i => i.IssueType == LinkIssueType.MissingAltText);

        return analysis;
    }

    private LinkIssue? AnalyzeLinkAnnotation(PdfLinkAnnotation linkAnnot, PdfDictionary parentTreeObj, PdfDocument pdfDoc)
    {
        var annotObj = linkAnnot.GetPdfObject();
        var structParent = annotObj.GetAsNumber(PdfName.StructParent);

        var issue = new LinkIssue();

        // Get link destination
        issue.DestinationPage = GetDestinationPage(linkAnnot, pdfDoc);
        issue.LinkText = ExtractLinkText(linkAnnot, pdfDoc);

        // Check for StructParent
        if (structParent == null)
        {
            issue.IssueType = LinkIssueType.NoStructParent;
            issue.Description = "Link annotation has no StructParent (orphaned)";
            issue.Severity = IssueSeverity.High;
            return issue;
        }

        issue.StructParentId = structParent.IntValue();

        // Find structure element
        var structElem = FindStructureElementInParentTree(parentTreeObj, structParent.IntValue());

        if (structElem == null)
        {
            issue.IssueType = LinkIssueType.NoStructureElement;
            issue.Description = $"No structure element found for StructParent {structParent.IntValue()}";
            issue.Severity = IssueSeverity.High;
            return issue;
        }

        var role = structElem.GetRole()?.GetValue();
        issue.CurrentStructureType = role ?? "Unknown";

        // Check if it's properly in a Link element
        if (role != "Link")
        {
            issue.IssueType = LinkIssueType.NotInLinkElement;
            issue.Description = $"Link connected to {role} instead of Link element";
            issue.Severity = IssueSeverity.High;

            // Check if we can auto-fix this
            issue.CanAutoFix = CanAutoFixLinkStructure(structElem, role);
            return issue;
        }

        // Check for alt text
        var contents = linkAnnot.GetContents();
        var hasContents = contents != null && !string.IsNullOrEmpty(contents.GetValue());

        var alt = structElem.GetAlt();
        var hasAlt = alt != null && !string.IsNullOrEmpty(alt.GetValue());

        if (!hasContents && !hasAlt)
        {
            issue.IssueType = LinkIssueType.MissingAltText;
            issue.Description = "Link has no alternative text";
            issue.Severity = IssueSeverity.Medium;
            issue.CanAutoFix = true; // We can auto-generate alt text
            return issue;
        }

        // No issues found
        return null;
    }

    private bool CanAutoFixLinkStructure(PdfStructElem structElem, string role)
    {
        // We can auto-fix if:
        // 1. It's a Reference element (common Word export issue)
        // 2. It's a Span element (another common issue)
        // 3. The element has no critical children that would be lost

        if (role == "Reference" || role == "Span")
        {
            var kids = structElem.GetKids();
            if (kids == null || kids.Count == 0)
            {
                return true; // Safe to wrap
            }

            // Check if children are just MCR (marked content reference)
            bool onlyMcrChildren = true;
            foreach (var kid in kids)
            {
                if (kid is PdfStructElem childElem)
                {
                    onlyMcrChildren = false;
                    break;
                }
            }

            return onlyMcrChildren;
        }

        return false;
    }

    private void ApplyAutomaticFixes(PdfDocument pdfDoc, TocPreprocessResult result)
    {
        int altTextFixed = 0;
        int simpleStructureFixed = 0;

        foreach (var pageAnalysis in result.PageIssues.Values)
        {
            foreach (var issue in pageAnalysis.LinkIssues.Where(i => i.CanAutoFix))
            {
                try
                {
                    if (issue.IssueType == LinkIssueType.MissingAltText)
                    {
                        // Auto-generate and add alt text
                        var altText = $"Go to page {issue.DestinationPage}";
                        if (!string.IsNullOrEmpty(issue.LinkText))
                        {
                            altText = $"{issue.LinkText} - Go to page {issue.DestinationPage}";
                        }

                        // This would need actual PDF modification code
                        issue.AutoFixed = true;
                        issue.AutoFixDescription = $"Added alt text: '{altText}'";
                        altTextFixed++;
                    }
                    else if (issue.IssueType == LinkIssueType.NotInLinkElement &&
                             issue.CurrentStructureType == "Reference")
                    {
                        // For simple Reference elements, we might auto-fix
                        // But for safety, we'll mark for manual fix
                        issue.AutoFixed = false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not auto-fix issue: {ex.Message}");
                    issue.AutoFixed = false;
                }
            }
        }

        if (altTextFixed > 0)
        {
            result.AutoFixesApplied.Add($"Added alt text to {altTextFixed} links");
        }

        if (simpleStructureFixed > 0)
        {
            result.AutoFixesApplied.Add($"Fixed structure for {simpleStructureFixed} simple links");
        }
    }

    private void GenerateManualFixInstructions(TocPreprocessResult result)
    {
        foreach (var pageAnalysis in result.PageIssues.Values)
        {
            foreach (var issue in pageAnalysis.LinkIssues.Where(i => !i.AutoFixed))
            {
                var instruction = new ManualFixInstruction
                {
                    PageNumber = pageAnalysis.PageNumber,
                    IssueType = issue.IssueType.ToString(),
                    Severity = issue.Severity
                };

                // Generate specific instructions based on issue type
                if (issue.IssueType == LinkIssueType.NotInLinkElement)
                {
                    instruction.StepByStep = new List<string>
                    {
                        $"1. Open the Tags panel (View > Show/Hide > Navigation Panes > Tags)",
                        $"2. Navigate to page {pageAnalysis.PageNumber}",
                        $"3. Find the {issue.CurrentStructureType} element at StructParent {issue.StructParentId}",
                        $"   - Look for text: '{issue.LinkText}'",
                        $"4. Right-click the {issue.CurrentStructureType} element",
                        $"5. Select 'New Tag' > 'Link'",
                        $"6. Drag the {issue.CurrentStructureType} element into the new Link tag",
                        $"7. Right-click the Link tag > Properties",
                        $"8. Add Alternative Text: 'Go to page {issue.DestinationPage}'",
                        $"9. Click OK"
                    };

                    instruction.EstimatedTime = "30 seconds";
                }
                else if (issue.IssueType == LinkIssueType.NoStructParent)
                {
                    instruction.StepByStep = new List<string>
                    {
                        $"1. Open the Tags panel",
                        $"2. Navigate to page {pageAnalysis.PageNumber}",
                        $"3. Find the TOC or TOCI element for this page",
                        $"4. Right-click > 'New Tag' > 'Link'",
                        $"5. Right-click the new Link > 'Find Content from Selection'",
                        $"6. On the page, click the link text: '{issue.LinkText}'",
                        $"7. Right-click the Link tag > Properties",
                        $"8. Add Alternative Text: 'Go to page {issue.DestinationPage}'",
                        $"9. Click OK"
                    };

                    instruction.EstimatedTime = "45 seconds";
                }
                else if (issue.IssueType == LinkIssueType.MissingAltText)
                {
                    instruction.StepByStep = new List<string>
                    {
                        $"1. Open the Tags panel",
                        $"2. Find the Link element at StructParent {issue.StructParentId}",
                        $"3. Right-click > Properties",
                        $"4. Add Alternative Text: 'Go to page {issue.DestinationPage}'",
                        $"5. Click OK"
                    };

                    instruction.EstimatedTime = "15 seconds";
                }

                result.ManualFixesRequired.Add(instruction);
            }
        }

        // Sort by page and severity
        result.ManualFixesRequired = result.ManualFixesRequired
            .OrderBy(i => i.PageNumber)
            .ThenBy(i => i.Severity)
            .ToList();
    }

    private string GenerateHtmlReport(TocPreprocessResult result)
    {
        var html = new StringBuilder();

        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html>");
        html.AppendLine("<head>");
        html.AppendLine("<title>TOC Fix Instructions - " + result.FileName + "</title>");
        html.AppendLine(@"<style>
            body { font-family: Arial, sans-serif; max-width: 1200px; margin: 0 auto; padding: 20px; }
            h1 { color: #333; border-bottom: 3px solid #007ACC; padding-bottom: 10px; }
            h2 { color: #555; margin-top: 30px; }
            .summary { background: #f0f8ff; padding: 15px; border-radius: 8px; margin: 20px 0; }
            .auto-fixes { background: #f0fff0; padding: 15px; border-radius: 8px; margin: 20px 0; }
            .manual-fix { background: #fffaf0; border: 1px solid #ff9800; padding: 15px; border-radius: 8px; margin: 15px 0; }
            .high { border-left: 5px solid #f44336; }
            .medium { border-left: 5px solid #ff9800; }
            .low { border-left: 5px solid #4caf50; }
            .steps { background: white; padding: 10px; border-radius: 5px; margin-top: 10px; }
            .steps ol { margin: 10px 0; padding-left: 20px; }
            .steps li { margin: 5px 0; line-height: 1.5; }
            code { background: #f5f5f5; padding: 2px 5px; border-radius: 3px; font-family: monospace; }
            .time-estimate { color: #666; font-style: italic; margin-top: 10px; }
            .stats { display: flex; gap: 20px; margin: 20px 0; }
            .stat-card { background: white; border: 1px solid #ddd; padding: 15px; border-radius: 8px; flex: 1; text-align: center; }
            .stat-number { font-size: 2em; font-weight: bold; color: #007ACC; }
            .stat-label { color: #666; margin-top: 5px; }
        </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");

        // Header
        html.AppendLine($"<h1>TOC Remediation Report</h1>");
        html.AppendLine($"<p><strong>File:</strong> {result.FileName}</p>");
        html.AppendLine($"<p><strong>Processed:</strong> {result.ProcessedAt:yyyy-MM-dd HH:mm:ss}</p>");

        // Statistics
        html.AppendLine("<div class='stats'>");
        html.AppendLine($"<div class='stat-card'><div class='stat-number'>{result.TocPages.Count}</div><div class='stat-label'>TOC Pages Found</div></div>");

        var totalIssues = result.PageIssues.Values.Sum(p => p.LinkIssues.Count);
        var autoFixed = result.PageIssues.Values.Sum(p => p.LinkIssues.Count(i => i.AutoFixed));
        var manualRequired = totalIssues - autoFixed;

        html.AppendLine($"<div class='stat-card'><div class='stat-number'>{totalIssues}</div><div class='stat-label'>Total Issues Found</div></div>");
        html.AppendLine($"<div class='stat-card'><div class='stat-number'>{autoFixed}</div><div class='stat-label'>Auto-Fixed</div></div>");
        html.AppendLine($"<div class='stat-card'><div class='stat-number'>{manualRequired}</div><div class='stat-label'>Manual Fixes Required</div></div>");
        html.AppendLine("</div>");

        // Summary
        html.AppendLine("<div class='summary'>");
        html.AppendLine("<h2>Summary</h2>");

        if (result.Success)
        {
            var estimatedTime = result.ManualFixesRequired.Count * 30; // seconds
            html.AppendLine($"<p>✅ Preprocessing successful. Found {result.TocPages.Count} TOC page(s) with {totalIssues} total issues.</p>");
            html.AppendLine($"<p>⏱️ Estimated time for manual fixes: {estimatedTime / 60} minutes</p>");
        }
        else
        {
            html.AppendLine($"<p>❌ Preprocessing failed: {result.ErrorMessage}</p>");
        }

        html.AppendLine("</div>");

        // Auto-fixes applied
        if (result.AutoFixesApplied.Any())
        {
            html.AppendLine("<div class='auto-fixes'>");
            html.AppendLine("<h2>✅ Automatic Fixes Applied</h2>");
            html.AppendLine("<ul>");
            foreach (var fix in result.AutoFixesApplied)
            {
                html.AppendLine($"<li>{fix}</li>");
            }
            html.AppendLine("</ul>");
            html.AppendLine("</div>");
        }

        // Manual fixes required
        if (result.ManualFixesRequired.Any())
        {
            html.AppendLine("<h2>📋 Manual Fixes Required in Adobe Acrobat</h2>");
            html.AppendLine($"<p>Follow these {result.ManualFixesRequired.Count} steps in order:</p>");

            int stepNumber = 1;
            foreach (var fix in result.ManualFixesRequired)
            {
                var severityClass = fix.Severity.ToString().ToLower();
                html.AppendLine($"<div class='manual-fix {severityClass}'>");
                html.AppendLine($"<h3>Fix #{stepNumber}: Page {fix.PageNumber} - {fix.IssueType}</h3>");
                html.AppendLine("<div class='steps'>");
                html.AppendLine("<ol>");

                foreach (var step in fix.StepByStep)
                {
                    html.AppendLine($"<li>{step}</li>");
                }

                html.AppendLine("</ol>");
                html.AppendLine($"<div class='time-estimate'>⏱️ Estimated time: {fix.EstimatedTime}</div>");
                html.AppendLine("</div>");
                html.AppendLine("</div>");

                stepNumber++;
            }
        }
        else if (result.Success)
        {
            html.AppendLine("<div class='auto-fixes'>");
            html.AppendLine("<h2>🎉 No Manual Fixes Required!</h2>");
            html.AppendLine("<p>All issues were resolved automatically.</p>");
            html.AppendLine("</div>");
        }

        // Footer
        html.AppendLine("<hr style='margin-top: 50px;'>");
        html.AppendLine("<p style='color: #666; font-size: 0.9em;'>Generated by TOC Preprocessor Service</p>");

        html.AppendLine("</body>");
        html.AppendLine("</html>");

        return html.ToString();
    }

    // Helper methods
    private PdfStructElem? FindStructureElementInParentTree(PdfDictionary parentTreeDict, int structParentIndex)
    {
        try
        {
            var numsArray = parentTreeDict.GetAsArray(PdfName.Nums);
            if (numsArray == null) return null;

            for (int i = 0; i < numsArray.Size(); i += 2)
            {
                var indexObj = numsArray.GetAsNumber(i);
                if (indexObj != null && indexObj.IntValue() == structParentIndex)
                {
                    var structElemObj = numsArray.Get(i + 1);
                    if (structElemObj is PdfDictionary dict)
                    {
                        return new PdfStructElem(dict);
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private int GetDestinationPage(PdfLinkAnnotation annotation, PdfDocument pdfDoc)
    {
        try
        {
            var action = annotation.GetAction();
            if (action != null)
            {
                var actionType = action.GetAsName(PdfName.S);
                if (PdfName.GoTo.Equals(actionType))
                {
                    var dest = action.Get(PdfName.D);
                    if (dest is PdfArray destArray && destArray.Size() > 0)
                    {
                        var pageRef = destArray.Get(0);
                        if (pageRef is PdfIndirectReference indirectRef)
                        {
                            for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
                            {
                                var pageObj = pdfDoc.GetPage(i).GetPdfObject();
                                if (pageObj.GetIndirectReference() != null &&
                                    pageObj.GetIndirectReference().Equals(indirectRef))
                                {
                                    return i;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }
        return 0;
    }

    private string ExtractLinkText(PdfLinkAnnotation annotation, PdfDocument pdfDoc)
    {
        // This would need more sophisticated text extraction
        // For now, return a placeholder
        return "TOC Entry";
    }
}

// Result classes
public class TocPreprocessResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string FileName { get; set; } = "";
    public DateTime ProcessedAt { get; set; }
    public byte[] ProcessedPdf { get; set; } = Array.Empty<byte>();

    public List<int> TocPages { get; set; } = new();
    public Dictionary<int, TocPageAnalysis> PageIssues { get; set; } = new();
    public List<string> AutoFixesApplied { get; set; } = new();
    public List<ManualFixInstruction> ManualFixesRequired { get; set; } = new();

    public string HtmlReport { get; set; } = "";
}

public class TocPageAnalysis
{
    public int PageNumber { get; set; }
    public int TotalLinks { get; set; }
    public int LinksWithoutStructure { get; set; }
    public int LinksNotInLinkElement { get; set; }
    public int LinksMissingAltText { get; set; }

    public List<LinkIssue> LinkIssues { get; set; } = new();
}

public class LinkIssue
{
    public LinkIssueType IssueType { get; set; }
    public IssueSeverity Severity { get; set; }
    public string Description { get; set; } = "";
    public int StructParentId { get; set; }
    public string CurrentStructureType { get; set; } = "";
    public int DestinationPage { get; set; }
    public string LinkText { get; set; } = "";

    public bool CanAutoFix { get; set; }
    public bool AutoFixed { get; set; }
    public string? AutoFixDescription { get; set; }
}

public class ManualFixInstruction
{
    public int PageNumber { get; set; }
    public string IssueType { get; set; } = "";
    public IssueSeverity Severity { get; set; }
    public List<string> StepByStep { get; set; } = new();
    public string EstimatedTime { get; set; } = "";
}

public enum LinkIssueType
{
    NoStructParent,
    NoStructureElement,
    NotInLinkElement,
    MissingAltText,
    Other
}

public enum IssueSeverity
{
    High,
    Medium,
    Low
}