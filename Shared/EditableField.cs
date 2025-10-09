namespace WordToPdfConverter.Shared
{
    public class EditableField
    {
        public string Name { get; set; }
        public string OriginalName { get; set; } // Track the original name for updates
        public string Type { get; set; }
        public string Tooltip { get; set; }
        public bool IsRequired { get; set; }
        public int Page { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; } = 100;
        public float Height { get; set; } = 20;
        public int OriginalIndex { get; set; }
        public float PageWidth { get; set; } = 612f; // US Letter width
        public float PageHeight { get; set; } = 792f; // US Letter height
        public string ButtonValue { get; set; } // For radio button grouping - buttons with same Name but different ButtonValue form a group
    }

    public class FieldTypeOption
    {
        public string Value { get; set; }
        public string Display { get; set; }
    }
}