namespace WordToPdfConverter.Models
{
    public class FieldProcessingData
    {
        public int OriginalFieldCount { get; set; }
        public List<RemovedFieldInfo> RemovedFields { get; set; } = new List<RemovedFieldInfo>();
    }
}
