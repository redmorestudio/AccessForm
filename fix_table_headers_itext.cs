using System;
using System.IO;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Tagging;

public class TableHeaderFixer
{
    public static void FixTableHeaders(string inputPath, string outputPath)
    {
        using (var reader = new PdfReader(inputPath))
        using (var writer = new PdfWriter(outputPath))
        using (var pdfDoc = new PdfDocument(reader, writer))
        {
            if (!pdfDoc.IsTagged())
            {
                Console.WriteLine("Error: Document is not tagged");
                return;
            }

            var rootTag = pdfDoc.GetStructTreeRoot();

            // Iterate through children of root (they are PdfStructElem)
            for (int i = 0; i < rootTag.GetKidsObject().Size(); i++)
            {
                var kid = rootTag.GetKids()[i];
                if (kid is PdfStructElem elem)
                {
                    FixTableHeadersRecursive(elem);
                }
            }

            Console.WriteLine($"Fixed table headers. Output: {outputPath}");
        }
    }

    private static void FixTableHeadersRecursive(PdfStructElem element)
    {
        var role = element.GetRole();

        // If we found a table, process its rows
        if (role != null && role.GetValue() == "Table")
        {
            Console.WriteLine("Found table, processing headers...");

            var children = element.GetKids();
            if (children != null)
            {
                bool isFirstRow = true;
                foreach (var child in children)
                {
                    if (child is PdfStructElem childElem)
                    {
                        var childRole = childElem.GetRole();

                        // Process TR (table row)
                        if (childRole != null && childRole.GetValue() == "TR")
                        {
                            if (isFirstRow)
                            {
                                // First row should contain headers
                                FixFirstRowHeaders(childElem);
                                isFirstRow = false;
                            }
                        }
                        // Handle table sections (THead, TBody)
                        else if (childRole != null &&
                                (childRole.GetValue() == "THead" ||
                                 childRole.GetValue() == "TBody"))
                        {
                            // Recurse into sections
                            FixTableHeadersRecursive(childElem);
                        }
                    }
                }
            }
        }

        // Recurse into children
        var kids = element.GetKids();
        if (kids != null)
        {
            foreach (var kid in kids)
            {
                if (kid is PdfStructElem childElem)
                {
                    FixTableHeadersRecursive(childElem);
                }
            }
        }
    }

    private static void FixFirstRowHeaders(PdfStructElem row)
    {
        var children = row.GetKids();
        if (children == null) return;

        foreach (var child in children)
        {
            if (child is PdfStructElem cellElem)
            {
                var role = cellElem.GetRole();

                // Convert TD to TH if needed
                if (role != null && role.GetValue() == "TD")
                {
                    Console.WriteLine("Converting TD to TH...");
                    cellElem.SetRole(PdfName.TH);
                }

                // Add scope attribute
                if (role != null &&
                    (role.GetValue() == "TH" || role.GetValue() == "TD"))
                {
                    var attributes = cellElem.GetAttributes(false);
                    if (attributes == null)
                    {
                        attributes = new PdfDictionary();
                        cellElem.SetAttributes(attributes);
                    }

                    if (attributes is PdfDictionary attrDict)
                    {
                        // Add Scope attribute for column headers
                        attrDict.Put(new PdfName("Scope"), new PdfName("Column"));
                        Console.WriteLine("Added Scope='Column' attribute to header cell");
                    }
                }
            }
        }
    }

    public static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: TableHeaderFixer <input.pdf> <output.pdf>");
            return;
        }

        FixTableHeaders(args[0], args[1]);
    }
}