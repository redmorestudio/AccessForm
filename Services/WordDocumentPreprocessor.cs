using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;

namespace WordToPdfConverter.Services
{
    /// <summary>
    /// Pre-processes Word documents to clean up problematic whitespace that causes
    /// untagged text issues in Syncfusion's PDF conversion.
    /// </summary>
    public class WordDocumentPreprocessor
    {
        private readonly ILogger<WordDocumentPreprocessor> _logger;

        public WordDocumentPreprocessor(ILogger<WordDocumentPreprocessor> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Pre-process a Word document to fix whitespace issues before PDF conversion.
        /// </summary>
        public byte[] PreprocessForAccessibility(byte[] wordBytes, string fileName = "document.docx")
        {
            try
            {
                _logger.LogInformation($"Pre-processing Word document: {fileName}");

                using (var inputStream = new MemoryStream(wordBytes))
                using (var wordDoc = new WordDocument(inputStream, FormatType.Docx))
                {
                    int changesCount = 0;

                    // Process all sections
                    foreach (WSection section in wordDoc.Sections)
                    {
                        // Clean headers and footers (where page numbers often are)
                        changesCount += ProcessHeadersFooters(section);

                        // Process body content
                        changesCount += ProcessBody(section.Body);
                    }

                    // Clean document-level formatting
                    changesCount += CleanDocumentFormatting(wordDoc);

                    _logger.LogInformation($"Pre-processing complete: {changesCount} changes made");

                    // Save the cleaned document
                    using (var outputStream = new MemoryStream())
                    {
                        wordDoc.Save(outputStream, FormatType.Docx);
                        return outputStream.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pre-processing Word document");
                // Return original if pre-processing fails
                return wordBytes;
            }
        }

        private int ProcessHeadersFooters(WSection section)
        {
            int changes = 0;

            // Process all header/footer types
            var headerFooters = new[]
            {
                section.HeadersFooters.OddHeader,
                section.HeadersFooters.EvenHeader,
                section.HeadersFooters.FirstPageHeader,
                section.HeadersFooters.OddFooter,
                section.HeadersFooters.EvenFooter,
                section.HeadersFooters.FirstPageFooter
            };

            foreach (var hf in headerFooters)
            {
                if (hf != null)
                {
                    changes += ProcessTextBody(hf);
                }
            }

            return changes;
        }

        private int ProcessBody(WTextBody body)
        {
            return ProcessTextBody(body);
        }

        private int ProcessTextBody(WTextBody textBody)
        {
            int changes = 0;

            foreach (var bodyItem in textBody.ChildEntities)
            {
                if (bodyItem is WParagraph paragraph)
                {
                    changes += ProcessParagraph(paragraph);
                }
                else if (bodyItem is WTable table)
                {
                    changes += ProcessTable(table);
                }
            }

            return changes;
        }

        private int ProcessParagraph(WParagraph paragraph)
        {
            int changes = 0;

            // Clean up paragraph-level whitespace issues
            for (int i = paragraph.ChildEntities.Count - 1; i >= 0; i--)
            {
                var item = paragraph.ChildEntities[i];

                if (item is WTextRange textRange)
                {
                    string originalText = textRange.Text;
                    string cleanedText = originalText;

                    // Fix known problematic patterns that cause untagged whitespace

                    // 1. Remove isolated spaces (single space between other elements)
                    if (cleanedText == " " && IsIsolatedSpace(paragraph, i))
                    {
                        paragraph.ChildEntities.RemoveAt(i);
                        changes++;
                        _logger.LogDebug("Removed isolated space");
                        continue;
                    }

                    // 2. Clean multiple consecutive spaces
                    cleanedText = Regex.Replace(cleanedText, @"\s{2,}", " ");

                    // 3. Remove trailing spaces before page numbers
                    if (IsPageNumberContext(paragraph) && cleanedText.EndsWith(" "))
                    {
                        cleanedText = cleanedText.TrimEnd();
                    }

                    // 4. Fix spaces around certain operators that Syncfusion mishandles
                    cleanedText = FixOperatorSpacing(cleanedText);

                    // 5. Remove zero-width spaces and other invisible characters
                    cleanedText = RemoveInvisibleCharacters(cleanedText);

                    if (cleanedText != originalText)
                    {
                        textRange.Text = cleanedText;
                        changes++;
                        _logger.LogDebug($"Cleaned text: '{originalText}' -> '{cleanedText}'");
                    }

                    // Remove empty text ranges
                    if (string.IsNullOrEmpty(cleanedText))
                    {
                        paragraph.ChildEntities.RemoveAt(i);
                        changes++;
                    }
                }
            }

            // Merge adjacent text ranges with same formatting
            changes += MergeAdjacentTextRanges(paragraph);

            return changes;
        }

        private bool IsIsolatedSpace(WParagraph paragraph, int index)
        {
            // Check if this is a single space between other elements
            if (index == 0 || index == paragraph.ChildEntities.Count - 1)
                return false;

            var prev = paragraph.ChildEntities[index - 1];
            var next = paragraph.ChildEntities[index + 1];

            // If surrounded by non-text elements (like fields), it's likely isolated
            return !(prev is WTextRange) || !(next is WTextRange);
        }

        private bool IsPageNumberContext(WParagraph paragraph)
        {
            // Check if this paragraph contains page number fields
            foreach (var entity in paragraph.ChildEntities)
            {
                if (entity is WField field)
                {
                    if (field.FieldCode != null &&
                        (field.FieldCode.Contains("PAGE") ||
                         field.FieldCode.Contains("NUMPAGES")))
                    {
                        return true;
                    }
                }
            }

            // Check for common page number patterns
            string text = paragraph.Text;
            return Regex.IsMatch(text, @"\bPage\s+\d+\b|\b\d+\s+of\s+\d+\b", RegexOptions.IgnoreCase);
        }

        private string FixOperatorSpacing(string text)
        {
            // Fix spaces that Syncfusion adds around operators
            // Known issue: Syncfusion adds spaces around dots in numbers
            text = Regex.Replace(text, @"(\d)\s+\.\s+(\d)", "$1.$2");

            // Fix spaces around other operators in numeric contexts
            text = Regex.Replace(text, @"(\d)\s+([+\-*/])\s+(\d)", "$1$2$3");

            return text;
        }

        private string RemoveInvisibleCharacters(string text)
        {
            // Remove zero-width spaces and other invisible Unicode characters
            text = Regex.Replace(text, @"[\u200B-\u200D\uFEFF]", "");

            // Remove non-breaking spaces at ends
            text = text.Replace('\u00A0', ' ');

            return text;
        }

        private int MergeAdjacentTextRanges(WParagraph paragraph)
        {
            int changes = 0;

            for (int i = paragraph.ChildEntities.Count - 2; i >= 0; i--)
            {
                if (paragraph.ChildEntities[i] is WTextRange current &&
                    paragraph.ChildEntities[i + 1] is WTextRange next)
                {
                    // Check if they have the same formatting
                    if (HaveSameFormatting(current, next))
                    {
                        // Merge the text
                        current.Text += next.Text;
                        paragraph.ChildEntities.RemoveAt(i + 1);
                        changes++;
                        _logger.LogDebug("Merged adjacent text ranges");
                    }
                }
            }

            return changes;
        }

        private bool HaveSameFormatting(WTextRange range1, WTextRange range2)
        {
            var cf1 = range1.CharacterFormat;
            var cf2 = range2.CharacterFormat;

            return cf1.FontName == cf2.FontName &&
                   cf1.FontSize == cf2.FontSize &&
                   cf1.Bold == cf2.Bold &&
                   cf1.Italic == cf2.Italic &&
                   cf1.UnderlineStyle == cf2.UnderlineStyle;
        }

        private int ProcessTable(WTable table)
        {
            int changes = 0;

            foreach (WTableRow row in table.Rows)
            {
                foreach (WTableCell cell in row.Cells)
                {
                    changes += ProcessTextBody(cell);
                }
            }

            return changes;
        }

        private int CleanDocumentFormatting(WordDocument wordDoc)
        {
            int changes = 0;

            // Clean up document-level settings that might cause issues

            // 1. Ensure consistent paragraph spacing
            foreach (WSection section in wordDoc.Sections)
            {
                foreach (WParagraph para in section.Paragraphs)
                {
                    // Remove extra spacing that might become untagged whitespace
                    if (para.ParagraphFormat.BeforeSpacing > 12)
                    {
                        para.ParagraphFormat.BeforeSpacing = 12;
                        changes++;
                    }
                    if (para.ParagraphFormat.AfterSpacing > 12)
                    {
                        para.ParagraphFormat.AfterSpacing = 12;
                        changes++;
                    }
                }
            }

            // 2. Clean up section breaks that might cause issues
            foreach (WSection section in wordDoc.Sections)
            {
                // Ensure proper section break types
                if (section.BreakCode == SectionBreakCode.NoBreak)
                {
                    section.BreakCode = SectionBreakCode.NewPage;
                    changes++;
                }
            }

            return changes;
        }
    }
}