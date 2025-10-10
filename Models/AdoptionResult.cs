namespace WordToPdfConverter.Models
{
    /// <summary>
    /// Result of orphaned whitespace adoption operation
    /// </summary>
    public class AdoptionResult
    {
        public bool Success { get; set; }
        public int OrphansFound { get; set; }
        public int OrphansAdopted { get; set; }
        public byte[]? FixedPdf { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
