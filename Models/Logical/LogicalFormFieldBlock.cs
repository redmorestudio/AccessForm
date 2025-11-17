using System.Collections.Generic;

namespace WordToPdfConverter.Models.Logical;

/// <summary>
/// Form field type enumeration matching the types from FieldDetectionResult.
/// Maps to both PDF form field types and visual form field types detected by AI.
/// </summary>
public enum LogicalFormFieldType
{
    Text,
    MultilineText,
    Checkbox,
    Radio,
    ComboBox,
    ListBox,
    Signature,
    Date,
    Numeric,
    Other
}

/// <summary>
/// A form field block representing an interactive form field on the page.
/// This wraps the existing FieldDetectionResult metadata for use in the
/// AI remediation pipeline's LogicalDocument model.
///
/// Form fields can come from:
/// - Existing PDF AcroForm fields (detected by ConfigurableFieldDetectionService)
/// - Visual form fields detected by Claude Vision API
/// - Hybrid detection combining both sources
/// </summary>
public record LogicalFormFieldBlock(
    Rect Bounds,
    int PageIndex,
    LogicalFormFieldType FieldType,
    string FieldName,
    string? Tooltip,
    string? LabelText,
    IReadOnlyList<string>? Options,
    string? DefaultValue,
    bool? IsChecked
) : LogicalBlock(Bounds);
