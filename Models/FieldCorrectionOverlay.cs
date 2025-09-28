using System.Collections.Generic;

namespace WordToPdfConverter.Models
{
    /// <summary>
    /// Overlay for field corrections and deletions to apply during reprocessing
    /// </summary>
    public class FieldCorrectionOverlay
    {
        /// <summary>
        /// Field IDs that should be deleted/excluded
        /// </summary>
        public HashSet<string> DeletedFieldIds { get; set; } = new HashSet<string>();

        /// <summary>
        /// Manual field name corrections (FieldId -> NewName)
        /// </summary>
        public Dictionary<string, string> RenamedFields { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Preferred order for field processing
        /// </summary>
        public List<string> FieldProcessingOrder { get; set; } = new List<string>();

        /// <summary>
        /// Additional field properties to override
        /// </summary>
        public Dictionary<string, FieldProperties> FieldOverrides { get; set; } = new Dictionary<string, FieldProperties>();
    }

    /// <summary>
    /// Properties that can be overridden for a field
    /// </summary>
    public class FieldProperties
    {
        public string? FieldType { get; set; }
        public string? Tooltip { get; set; }
        public bool? RequiredField { get; set; }
        public float? X { get; set; }
        public float? Y { get; set; }
        public float? Width { get; set; }
        public float? Height { get; set; }
        public Dictionary<string, string>? ValidationRules { get; set; }
    }
}