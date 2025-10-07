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
        /// Use Groq to validate field labels for semantic accuracy
        /// DEPRECATED: Use UseMultiStageValidation instead
        /// </summary>
        public bool UseGroqValidation { get; set; } = false;

        /// <summary>
        /// Use multi-stage validation with Claude Sonnet 4.5 + GPT-5 consensus
        /// Replaces UseGroqValidation with comprehensive 3-stage validation pipeline
        /// </summary>
        public bool UseMultiStageValidation { get; set; } = false;

        /// <summary>
        /// Auto-detect signature fields marked with X placeholders
        /// </summary>
        public bool UseSignatureDetection { get; set; } = false;

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
    /// IMPORTANT: All coordinates are standardized to PDF coordinates (bottom-left origin, 72 DPI)
    /// </summary>
    public class FieldDetectionResult
    {
        public string ShortId { get; set; }  // e.g., "F1", "F2", etc.
        public string FieldName { get; set; }
        public string FieldType { get; set; }

        // PDF Coordinates (bottom-left origin, 72 DPI) - our canonical format
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }

        // Coordinate system tracking
        public string CoordinateSystem { get; set; } = "PDF";  // Always "PDF" after standardization
        public string CoordinateOrigin { get; set; } = "Bottom-Left";  // Always "Bottom-Left" for PDF

        public int PageNumber { get; set; }
        public float PageWidth { get; set; } = 612f;  // Default to US Letter width
        public float PageHeight { get; set; } = 792f; // Default to US Letter height
        public string Source { get; set; }  // Detection source (Syncfusion, Claude Vision, etc.)
        public float Confidence { get; set; }
        public bool IsValid { get; set; } = true;
        public string ValidationNotes { get; set; }
        public string Tooltip { get; set; } = "";
        public bool RequiredField { get; set; } = false;
        public bool HasValidCoordinates { get; set; } = false;  // Track if field has valid coordinates
        public List<string> Options { get; set; } = new List<string>();
        public Dictionary<string, string> ValidationRules { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, object> DebugInfo { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Track the original coordinate system for debugging
        /// </summary>
        public string OriginalCoordinateSystem { get; set; }
    }
}