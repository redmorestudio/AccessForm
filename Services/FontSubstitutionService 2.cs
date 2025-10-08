using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;

namespace AccessFormServer.Services
{
    public class FontSubstitutionService
    {
        private static readonly Dictionary<string, string> FontReplacements = new()
        {
            // Problematic fonts → Safe alternatives
            { "Times New Roman", "Liberation Serif" },
            { "Arial", "Liberation Sans" },
            { "Arial Bold", "Liberation Sans" },
            { "Calibri", "Liberation Sans" },
            { "Calibri Light", "Liberation Sans" },
            { "Courier New", "Liberation Mono" },
            { "Symbol", "DejaVu Sans" },  // Will need symbol → Unicode conversion
            { "Wingdings", "DejaVu Sans" } // Will need symbol → Unicode conversion
        };

        private static readonly Dictionary<string, string> SymbolReplacements = new()
        {
            // Common Wingdings/Symbol characters → Unicode equivalents
            { "☑", "✓" },  // Checked box
            { "☐", "☐" },  // Empty box  
            { "→", "→" },  // Arrow
            { "•", "•" },  // Bullet
            { "◆", "♦" },  // Diamond
            { "★", "★" },  // Star
            // Add more as needed
        };

        public static WordDocument ProcessFontSubstitution(WordDocument document)
        {
            Console.WriteLine("🔄 Starting font substitution process...");
            
            try
            {
                var substitutionCount = 0;
                
                // Process all paragraphs in the document
                foreach (WSection section in document.Sections)
                {
                    substitutionCount += ProcessSection(section);
                }
                
                Console.WriteLine($"✅ Font substitution complete. {substitutionCount} replacements made.");
                return document;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Font substitution failed: {ex.Message}");
                return document; // Return original if substitution fails
            }
        }

        private static int ProcessSection(WSection section)
        {
            var count = 0;
            
            foreach (WParagraph paragraph in section.Paragraphs)
            {
                count += ProcessParagraph(paragraph);
            }
            
            // Process tables
            foreach (WTable table in section.Tables)
            {
                count += ProcessTable(table);
            }
            
            return count;
        }

        private static int ProcessParagraph(WParagraph paragraph)
        {
            var count = 0;
            
            foreach (ParagraphItem item in paragraph.ChildEntities)
            {
                if (item is WTextRange textRange)
                {
                    count += ProcessTextRange(textRange);
                }
            }
            
            return count;
        }

        private static int ProcessTable(WTable table)
        {
            var count = 0;
            
            foreach (WTableRow row in table.Rows)
            {
                foreach (WTableCell cell in row.Cells)
                {
                    foreach (WParagraph paragraph in cell.Paragraphs)
                    {
                        count += ProcessParagraph(paragraph);
                    }
                }
            }
            
            return count;
        }

        private static int ProcessTextRange(WTextRange textRange)
        {
            var count = 0;
            
            // Check if font needs replacement
            if (FontReplacements.ContainsKey(textRange.CharacterFormat.FontName))
            {
                var oldFont = textRange.CharacterFormat.FontName;
                var newFont = FontReplacements[oldFont];
                
                textRange.CharacterFormat.FontName = newFont;
                Console.WriteLine($"  📝 Replaced font: {oldFont} → {newFont}");
                count++;
                
                // If it's a symbol font, also replace the text content
                if (oldFont == "Symbol" || oldFont == "Wingdings")
                {
                    textRange.Text = ConvertSymbolText(textRange.Text);
                    Console.WriteLine($"  🔣 Converted symbol text in range");
                }
            }
            
            return count;
        }

        private static string ConvertSymbolText(string symbolText)
        {
            var result = symbolText;
            
            foreach (var replacement in SymbolReplacements)
            {
                result = result.Replace(replacement.Key, replacement.Value);
            }
            
            // Additional common symbol conversions
            // This is where we'll expand the symbol → Unicode mapping
            result = ConvertCommonSymbols(result);
            
            return result;
        }

        private static string ConvertCommonSymbols(string text)
        {
            // Convert common symbol font characters to Unicode
            // This will need to be expanded based on actual symbol usage
            
            // Common checkbox symbols
            text = text.Replace("þ", "☐");  // Empty checkbox
            text = text.Replace("ý", "☑");  // Checked checkbox
            
            // Common arrows and symbols  
            text = text.Replace("à", "→");  // Right arrow
            text = text.Replace("á", "←");  // Left arrow
            text = text.Replace("·", "•");  // Bullet point
            
            return text;
        }

        public static void ConfigureFontSubstitution()
        {
            Console.WriteLine("🔧 Configuring global font substitution...");
            
            // Configure Syncfusion font substitution
            foreach (var replacement in FontReplacements)
            {
                Console.WriteLine($"  📋 Mapping: {replacement.Key} → {replacement.Value}");
            }
        }
    }
}
