using System.Collections.Generic;

namespace WordToPdfConverter.Models
{
    /// <summary>
    /// Configuration for field detection services
    /// </summary>
    public class FieldDetectionConfig
    {
        /// <summary>
        /// Which detection services to use
        /// </summary>
        public ServiceSelection Services { get; set; } = new ServiceSelection();
        
        /// <summary>
        /// Processing mode - simultaneous or sequential
        /// </summary>
        public ProcessingMode Mode { get; set; } = ProcessingMode.Sequential;
        
        /// <summary>
        /// Enable debug mode with field IDs in boxes
        /// </summary>
        public bool DebugMode { get; set; } = true;
        
        /// <summary>
        /// Add short IDs to fields for debugging
        /// </summary>
        public bool ShowFieldIds { get; set; } = true;
        
        /// <summary>
        /// Use Claude to validate bounding boxes
        /// </summary>
        public bool ValidateBounds { get; set; } = true;
    }
    
    public class ServiceSelection
    {
        /// <summary>
        /// Use Syncfusion's built-in field detection
        /// </summary>
        public bool UseSyncfusion { get; set; } = true;
        
        /// <summary>
        /// Use Google Document AI
        /// </summary>
        public bool UseGoogle { get; set; } = false;
        
        /// <summary>
        /// Use Claude Vision for field detection
        /// </summary>
        public bool UseClaudeVision { get; set; } = false;
        
        /// <summary>
        /// Use Claude for text analysis
        /// </summary>
        public bool UseClaudeText { get; set; } = false;
        
        /// <summary>
        /// Use Claude to validate and adjust bounding boxes
        /// </summary>
        public bool UseClaudeValidation { get; set; } = false;
        
        /// <summary>
        /// Only use specified services, no combinations
        /// </summary>
        public bool ExclusiveMode { get; set; } = false;
    }
    
    public enum ProcessingMode
    {
        /// <summary>
        /// Run all services at the same time
        /// </summary>
        Simultaneous,
        
        /// <summary>
        /// Run services in sequence: Syncfusion/Google first, then Claude
        /// </summary>
        Sequential,
        
        /// <summary>
        /// Only run Syncfusion, then validate with Claude
        /// </summary>
        SyncfusionWithValidation
    }
    
    /// <summary>
    /// Result from field detection with debug info
    /// </summary>
    public class FieldDetectionResult
    {
        public string ShortId { get; set; }  // e.g., "F1", "F2", etc.
        public string FieldName { get; set; }
        public string FieldType { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public int PageNumber { get; set; }
        public string Source { get; set; }
        public float Confidence { get; set; }
        public bool IsValid { get; set; } = true;
        public string ValidationNotes { get; set; }
        public string Tooltip { get; set; } = "";
        public bool RequiredField { get; set; } = false;
        public List<string> Options { get; set; } = new List<string>();
        public Dictionary<string, string> ValidationRules { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, object> DebugInfo { get; set; } = new Dictionary<string, object>();
    }
}