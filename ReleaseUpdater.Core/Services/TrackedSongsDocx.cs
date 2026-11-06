using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Core.Services;

/// <summary>
/// A Word document (.docx) with a heading and a table: a bold header row (Title, Artist) and a row per song. A .docx file is a zip of
/// XML files; only what a document with one table needs is written, and the first table of a document is read.
/// </summary>
internal static class TrackedSongsDocx
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static byte[] Write(IReadOnlyList<TrackedSong> songs)
    {
        using MemoryStream stream = new();

        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
                """);
            AddEntry(zip, "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """);

            XElement table = new(W + "tbl",
                new XElement(W + "tblPr",
                    new XElement(W + "tblW", new XAttribute(W + "w", "5000"), new XAttribute(W + "type", "pct")),
                    new XElement(W + "tblBorders", new[] { "top", "left", "bottom", "right", "insideH", "insideV" }.Select(side =>
                        new XElement(W + side, new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"),
                            new XAttribute(W + "space", "0"), new XAttribute(W + "color", "808080")))),
                    new XElement(W + "tblCellMar",
                        new XElement(W + "left", new XAttribute(W + "w", "100"), new XAttribute(W + "type", "dxa")),
                        new XElement(W + "right", new XAttribute(W + "w", "100"), new XAttribute(W + "type", "dxa")))),
                new XElement(W + "tblGrid",
                    new XElement(W + "gridCol", new XAttribute(W + "w", "5200")),
                    new XElement(W + "gridCol", new XAttribute(W + "w", "4200"))),
                BuildRow("Title", "Artist", bold: true, header: true));

            foreach (TrackedSong song in songs)
            {
                table.Add(BuildRow(song.Title, song.Artist, bold: false, header: false));
            }

            XDocument document = new(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(W + "document",
                    new XAttribute(XNamespace.Xmlns + "w", W),
                    new XElement(W + "body",
                        new XElement(W + "p",
                            new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "after", "200"))),
                            Run("Unreleased songs", bold: true, size: 32)),
                        table,
                        // (A paragraph behind a table is required: Word does not accept a table as the last element.)
                        new XElement(W + "p"))));

            AddEntry(zip, "word/document.xml", document.ToString(SaveOptions.DisableFormatting), declaration: true);
        }

        return stream.ToArray();
    }

    /// <exception cref="FormatException">The content is not a Word document, or has no table.</exception>
    public static IReadOnlyList<IReadOnlyList<string>> ReadRows(byte[] content)
    {
        try
        {
            using MemoryStream stream = new(content);
            using ZipArchive zip = new(stream, ZipArchiveMode.Read);
            ZipArchiveEntry entry = zip.GetEntry("word/document.xml") ?? throw new FormatException("There is no document.");
            using Stream part = entry.Open();
            XDocument document = XDocument.Load(part);
            XElement table = document.Descendants(W + "tbl").FirstOrDefault() ?? throw new FormatException("The document has no table.");
            List<IReadOnlyList<string>> rows = new();

            // The text of the cell above, per column of the grid: a cell that is merged downwards ("continue") is empty in the file and
            // shows the text of the cell that began the merge.
            List<string> above = new();

            foreach (XElement row in table.Elements(W + "tr"))
            {
                List<string> cells = new();

                // (A row can begin further right in the grid.)
                int skipped = (int?)row.Element(W + "trPr")?.Element(W + "gridBefore")?.Attribute(W + "val") ?? 0;

                for (int i = 0; i < skipped; i++)
                {
                    cells.Add(string.Empty);
                }

                foreach (XElement cell in row.Elements(W + "tc"))
                {
                    XElement? properties = cell.Element(W + "tcPr");
                    int span = Math.Max(1, (int?)properties?.Element(W + "gridSpan")?.Attribute(W + "val") ?? 1);
                    XElement? vertical = properties?.Element(W + "vMerge");
                    int column = cells.Count;

                    bool continues = vertical != null &&
                        !string.Equals((string?)vertical.Attribute(W + "val"), "restart", StringComparison.OrdinalIgnoreCase);
                    string text = continues
                        ? (column < above.Count ? above[column] : string.Empty)
                        : CellText(cell);

                    // A cell that is merged to the right takes the room of its columns; its text is in the first of them, so that
                    // what comes behind it stays in its own column.
                    cells.Add(text);

                    for (int i = 1; i < span; i++)
                    {
                        cells.Add(string.Empty);
                    }

                    while (above.Count < cells.Count)
                    {
                        above.Add(string.Empty);
                    }

                    above[column] = text;

                    for (int i = 1; i < span; i++)
                    {
                        above[column + i] = string.Empty;
                    }
                }

                rows.Add(cells);
            }

            return rows;
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or IOException or InvalidOperationException)
        {
            throw new FormatException("The file is not a Word document.", ex);
        }
    }

    private static XElement BuildRow(string title, string artist, bool bold, bool header)
    {
        XElement row = new(W + "tr");

        if (header)
        {
            row.Add(new XElement(W + "trPr", new XElement(W + "tblHeader")));
        }

        row.Add(BuildCell(title, bold, 5200), BuildCell(artist, bold, 4200));

        return row;
    }

    private static XElement BuildCell(string text, bool bold, int width) =>
        new(W + "tc",
            new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", width), new XAttribute(W + "type", "dxa"))),
            new XElement(W + "p", Run(text, bold, size: 0)));

    // A run of text; a tab and a line break are elements of their own, not characters in the text.
    private static XElement Run(string text, bool bold, int size)
    {
        XElement run = new(W + "r");

        if (bold || size > 0)
        {
            XElement properties = new(W + "rPr");

            if (bold)
            {
                properties.Add(new XElement(W + "b"));
            }

            if (size > 0)
            {
                properties.Add(new XElement(W + "sz", new XAttribute(W + "val", size)));
            }

            run.Add(properties);
        }

        StringBuilder piece = new();

        void Flush()
        {
            if (piece.Length > 0)
            {
                run.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), piece.ToString()));
                piece.Clear();
            }
        }

        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];

            if (character == '\t')
            {
                Flush();
                run.Add(new XElement(W + "tab"));
            }
            else if (character == '\n' || character == '\r')
            {
                if (character == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                Flush();
                run.Add(new XElement(W + "br"));
            }
            else if (character >= ' ')
            {
                piece.Append(character);
            }
        }

        Flush();

        return run;
    }

    // The text of a cell: its paragraphs one under the other, tabs and line breaks as the characters they stand for.
    private static string CellText(XElement cell)
    {
        List<string> paragraphs = new();

        foreach (XElement paragraph in cell.Elements(W + "p"))
        {
            StringBuilder text = new();

            foreach (XElement element in paragraph.Descendants())
            {
                if (element.Name == W + "t")
                {
                    text.Append(element.Value);
                }
                else if (element.Name == W + "tab")
                {
                    text.Append('\t');
                }
                else if (element.Name == W + "br" || element.Name == W + "cr")
                {
                    text.Append('\n');
                }
            }

            paragraphs.Add(text.ToString());
        }

        return string.Join("\n", paragraphs);
    }

    private static void AddEntry(ZipArchive zip, string name, string xml, bool declaration = false)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        writer.Write(declaration ? "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" + xml : xml.Trim());
    }
}
