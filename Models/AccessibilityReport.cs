namespace WordToPdfConverter.Models
{
    public class AccessibilityReport
    {
        public string DocumentName { get; set; }
        public DateTime ConversionDate { get; set; }
        public string Status { get; set; } // Success, Partial, Failed
        public string ComplianceLevel { get; set; }
        public string SourceFormat { get; set; }
        public string TargetFormat { get; set; }
        public string ValidationStatus { get; set; }
        
        public int TotalFields { get; set; }
        public List<FieldAccessibilityInfo> FormFields { get; set; } = new List<FieldAccessibilityInfo>();
        public List<string> MeasuresTaken { get; set; } = new List<string>();
        public List<string> StructuralElements { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
        
        // Field processing tracking
        public int OriginalFieldCount { get; set; }
        public int RemovedFieldCount { get; set; }
        public List<RemovedFieldInfo> RemovedFields { get; set; } = new List<RemovedFieldInfo>();
        public List<FieldAnalysisInfo> FieldAnalysis { get; set; } = new List<FieldAnalysisInfo>();
    }
    
    public class FieldAccessibilityInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool HasLabel { get; set; }
        public bool HasDescription { get; set; }
        public int TabIndex { get; set; }
        public bool IsRequired { get; set; }
        public int PageNumber { get; set; }
    }
    
    public class FieldAnalysisInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Action { get; set; } // "Kept", "Removed"
        public string Category { get; set; } // "DefinitelyKeep", "ProbablyKeep", etc.
        public double ConfidenceScore { get; set; }
        public string RecommendedAction { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public int PageNumber { get; set; }
    }
    
    public class RemovedFieldInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string RemovalReason { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public int PageNumber { get; set; }
    }
}
