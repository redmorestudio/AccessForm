using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Syncfusion.Drawing;
using Syncfusion.Pdf.Interactive;
using Syncfusion.Pdf.Parsing;
using WordToPdfConverter.Models;
using WordToPdfConverter.Models.Logical;

namespace WordToPdfConverter.Services.Analysis;

/// <summary>
/// Enriches a LogicalDocument with form field information extracted from the original PDF.
/// This service acts as a post-processing step after AI layout analysis to merge in
/// form field intelligence from existing PDF AcroForm fields.
/// </summary>
public sealed class FormFieldEnrichmentService
{
    private readonly ILogger<FormFieldEnrichmentService> _logger;
    private int _fieldCounter = 0;

    public FormFieldEnrichmentService(ILogger<FormFieldEnrichmentService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Enriches the logical document with form fields extracted from the PDF.
    /// </summary>
    /// <param name="document">The LogicalDocument from AI analysis</param>
    /// <param name="pdfBytes">The original PDF bytes</param>
    /// <returns>Enriched LogicalDocument with form fields added to appropriate pages</returns>
    public LogicalDocument EnrichWithFormFields(LogicalDocument document, byte[] pdfBytes)
    {
        _logger.LogInformation("[FORM-ENRICHMENT] Starting form field enrichment");

        // Extract fields from PDF
        var detectedFields = ExtractFormFields(pdfBytes);

        if (detectedFields.Count == 0)
        {
            _logger.LogInformation("[FORM-ENRICHMENT] No form fields detected in PDF");
            return document;
        }

        _logger.LogInformation($"[FORM-ENRICHMENT] Detected {detectedFields.Count} form fields");

        // Convert to LogicalFormFieldBlocks and group by page
        var fieldsByPage = detectedFields
            .GroupBy(f => f.PageNumber)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Create new pages with form fields added
        var enrichedPages = new List<LogicalPage>();
        foreach (var page in document.Pages)
        {
            var pageNumber = page.PageNumber;
            var blocks = new List<LogicalBlock>(page.Blocks);

            // Add form fields for this page if any
            if (fieldsByPage.TryGetValue(pageNumber, out var pageFields))
            {
                foreach (var field in pageFields)
                {
                    var formFieldBlock = ConvertToLogicalFormFieldBlock(field);
                    blocks.Add(formFieldBlock);
                }

                _logger.LogInformation($"[FORM-ENRICHMENT] Added {pageFields.Count} form fields to page {pageNumber}");
            }

            enrichedPages.Add(new LogicalPage(pageNumber, blocks));
        }

        _logger.LogInformation("[FORM-ENRICHMENT] Form field enrichment complete");
        return new LogicalDocument(enrichedPages);
    }

    /// <summary>
    /// Extracts form fields from PDF using Syncfusion's PdfLoadedDocument.
    /// Based on logic from ConfigurableFieldDetectionService.
    /// </summary>
    private List<FieldDetectionResult> ExtractFormFields(byte[] pdfBytes)
    {
        var fields = new List<FieldDetectionResult>();

        using (var pdfStream = new MemoryStream(pdfBytes))
        using (var pdfDoc = new PdfLoadedDocument(pdfStream))
        {
            if (pdfDoc.Form?.Fields == null || pdfDoc.Form.Fields.Count == 0)
            {
                return fields;
            }

            _logger.LogInformation($"[FORM-ENRICHMENT] Found {pdfDoc.Form.Fields.Count} form fields in PDF");

            for (int i = 0; i < pdfDoc.Form.Fields.Count; i++)
            {
                var fieldObj = pdfDoc.Form.Fields[i];
                if (!(fieldObj is PdfLoadedField field))
                    continue;

                // Skip suspicious empty fields (false positives)
                if (IsSuspiciousField(field))
                    continue;

                var shortId = $"FF{++_fieldCounter}";
                var bounds = GetFieldBounds(field);
                var pageNum = GetFieldPageNumber(field, pdfDoc);

                var detectedField = new FieldDetectionResult
                {
                    ShortId = shortId,
                    FieldName = CleanFieldName(field.Name),
                    FieldType = DetermineFieldType(field),
                    X = bounds.X,
                    Y = bounds.Y,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    PageNumber = pageNum,
                    Source = "PDF_AcroForm",
                    Confidence = 0.95f, // High confidence for existing PDF fields
                    IsValid = true,
                    HasValidCoordinates = true
                };

                // Extract additional metadata
                if (field.ToolTip != null)
                {
                    detectedField.Tooltip = field.ToolTip;
                }

                // Extract options for combo/list boxes
                if (field is PdfLoadedComboBoxField comboField && comboField.Items != null)
                {
                    detectedField.Options = comboField.Items
                        .Cast<PdfLoadedListItem>()
                        .Select(item => item.Text)
                        .ToList();
                }
                else if (field is PdfLoadedListBoxField listField && listField.Items != null)
                {
                    detectedField.Options = listField.Items
                        .Cast<PdfLoadedListItem>()
                        .Select(item => item.Text)
                        .ToList();
                }

                // Extract radio button value for grouping
                if (field is PdfLoadedRadioButtonListField radioField)
                {
                    detectedField.ButtonValue = radioField.SelectedValue;
                }

                _logger.LogDebug($"[FORM-ENRICHMENT] Extracted field '{detectedField.FieldName}' " +
                    $"({detectedField.FieldType}) on page {pageNum} at ({bounds.X:F1}, {bounds.Y:F1})");

                fields.Add(detectedField);
            }
        }

        return fields;
    }

    /// <summary>
    /// Converts FieldDetectionResult to LogicalFormFieldBlock.
    /// </summary>
    private LogicalFormFieldBlock ConvertToLogicalFormFieldBlock(FieldDetectionResult field)
    {
        var fieldType = MapFieldType(field.FieldType);
        var bounds = new Rect(field.X, field.Y, field.Width, field.Height);

        return new LogicalFormFieldBlock(
            Bounds: bounds,
            PageIndex: field.PageNumber,
            FieldType: fieldType,
            FieldName: field.FieldName,
            Tooltip: string.IsNullOrEmpty(field.Tooltip) ? null : field.Tooltip,
            LabelText: null, // Will be populated by label association logic later if needed
            Options: field.Options?.Count > 0 ? field.Options : null,
            DefaultValue: null, // Could extract from field value if needed
            IsChecked: null // Could extract from checkbox/radio state if needed
        );
    }

    /// <summary>
    /// Maps FieldDetectionResult.FieldType string to LogicalFormFieldType enum.
    /// </summary>
    private LogicalFormFieldType MapFieldType(string fieldType)
    {
        return fieldType.ToLowerInvariant() switch
        {
            "text" or "textbox" => LogicalFormFieldType.Text,
            "textarea" or "multilinetext" => LogicalFormFieldType.MultilineText,
            "checkbox" => LogicalFormFieldType.Checkbox,
            "radio" or "radiobutton" => LogicalFormFieldType.Radio,
            "dropdown" or "combobox" or "select" => LogicalFormFieldType.ComboBox,
            "listbox" => LogicalFormFieldType.ListBox,
            "signature" => LogicalFormFieldType.Signature,
            "date" => LogicalFormFieldType.Date,
            "number" or "numeric" => LogicalFormFieldType.Numeric,
            _ => LogicalFormFieldType.Other
        };
    }

    /// <summary>
    /// Gets the page number for a form field using Syncfusion's official page detection.
    /// </summary>
    private int GetFieldPageNumber(PdfLoadedField field, PdfLoadedDocument pdfDoc)
    {
        if (field.Page != null)
        {
            try
            {
                // Find page index by iterating through pages
                for (int pageIdx = 0; pageIdx < pdfDoc.Pages.Count; pageIdx++)
                {
                    if (pdfDoc.Pages[pageIdx] == field.Page)
                    {
                        return pageIdx + 1; // Convert 0-based to 1-based
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[FORM-ENRICHMENT] Exception getting page for '{field.Name}': {ex.Message}");
            }
        }

        // Default to page 1 if page detection fails
        _logger.LogWarning($"[FORM-ENRICHMENT] Could not detect page for field '{field.Name}', defaulting to page 1");
        return 1;
    }

    /// <summary>
    /// Gets the bounding rectangle for a form field.
    /// Different field types store bounds differently.
    /// </summary>
    private RectangleF GetFieldBounds(PdfLoadedField field)
    {
        if (field is PdfLoadedTextBoxField textField)
            return textField.Bounds;
        if (field is PdfLoadedCheckBoxField checkField)
            return checkField.Bounds;
        if (field is PdfLoadedRadioButtonListField radioField && radioField.Items.Count > 0)
            return radioField.Items[0].Bounds;
        if (field is PdfLoadedComboBoxField comboField)
            return comboField.Bounds;
        if (field is PdfLoadedListBoxField listField)
            return listField.Bounds;
        if (field is PdfLoadedSignatureField sigField)
            return sigField.Bounds;

        // Fallback: try to access Bounds property via reflection
        var boundsProperty = field.GetType().GetProperty("Bounds");
        if (boundsProperty != null)
        {
            var bounds = boundsProperty.GetValue(field);
            if (bounds is RectangleF rect)
                return rect;
        }

        _logger.LogWarning($"[FORM-ENRICHMENT] Could not get bounds for field '{field.Name}' ({field.GetType().Name})");
        return new RectangleF(0, 0, 100, 20); // Fallback bounds
    }

    /// <summary>
    /// Determines the field type from a loaded PDF field.
    /// </summary>
    private string DetermineFieldType(PdfLoadedField field)
    {
        return field switch
        {
            PdfLoadedTextBoxField => "text",
            PdfLoadedCheckBoxField => "checkbox",
            PdfLoadedRadioButtonListField => "radio",
            PdfLoadedComboBoxField => "combobox",
            PdfLoadedListBoxField => "listbox",
            PdfLoadedSignatureField => "signature",
            _ => "text" // Default to text
        };
    }

    /// <summary>
    /// Checks if a field is suspicious (likely a false positive).
    /// Currently returns false to be liberal - we can add heuristics later.
    /// </summary>
    private bool IsSuspiciousField(PdfLoadedField field)
    {
        // For now, don't filter out any fields - let the rest of the pipeline handle validation
        // Could add heuristics here later, such as:
        // - Extremely small fields (< 5px)
        // - Fields with empty/whitespace-only names
        // - Hidden fields
        return false;
    }

    /// <summary>
    /// Cleans field names by removing problematic characters.
    /// </summary>
    private string CleanFieldName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "unnamed_field";

        // Remove problematic characters but keep underscores, spaces, and alphanumerics
        var cleaned = name.Trim();
        if (string.IsNullOrEmpty(cleaned))
            return "unnamed_field";

        return cleaned;
    }
}
