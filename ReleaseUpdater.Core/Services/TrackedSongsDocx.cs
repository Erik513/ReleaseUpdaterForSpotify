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
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

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
                    new XElement(W + "gridCol", new XAttribute(W + "w", "3600")),
                    new XElement(W + "gridCol", new XAttribute(W + "w", "2800")),
                    new XElement(W + "gridCol", new XAttribute(W + "w", "3000"))),
                BuildRow("Title", "Artist", "Link", null, bold: true, header: true));

            // The link of a song is a hyperlink: its text is what the user wrote, the address is a relationship of the document.
            List<string> hyperlinkTargets = new();

            foreach (TrackedSong song in songs)
            {
                string? target = TrackedSongLinks.Normalize(song.Link);
                string? relationshipId = null;

                if (target is not null)
                {
                    hyperlinkTargets.Add(target);
                    relationshipId = "rId" + hyperlinkTargets.Count;
                }

                table.Add(BuildRow(song.Title, song.Artist, song.Link ?? string.Empty, relationshipId, bold: false, header: false));
            }

            XDocument document = new(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(W + "document",
                    new XAttribute(XNamespace.Xmlns + "w", W),
                    new XAttribute(XNamespace.Xmlns + "r", R),
                    new XElement(W + "body",
                        new XElement(W + "p",
                            new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "after", "200"))),
                            Run("Unreleased songs", bold: true, size: 32)),
                        table,
                        // (A paragraph behind a table is required: Word does not accept a table as the last element.)
                        new XElement(W + "p"))));

            AddEntry(zip, "word/document.xml", document.ToString(SaveOptions.DisableFormatting), declaration: true);

            if (hyperlinkTargets.Count > 0)
            {
                XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
                XElement relationships = new(rel + "Relationships");

                for (int i = 0; i < hyperlinkTargets.Count; i++)
                {
                    relationships.Add(new XElement(
                        rel + "Relationship",
                        new XAttribute("Id", "rId" + (i + 1)),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"),
                        new XAttribute("Target", hyperlinkTargets[i]),
                        new XAttribute("TargetMode", "External")));
                }

                AddEntry(zip, "word/_rels/document.xml.rels", relationships.ToString(SaveOptions.DisableFormatting), declaration: true);
            }
        }

        return stream.ToArray();
    }

    /// <exception cref="FormatException">The content is not a Word document, or has no table.</exception>
    public static IReadOnlyList<IReadOnlyList<string>> ReadRows(byte[] content) => ReadTable(content).Rows;

    /// <summary>
    /// The first table of the document with the text of every cell and the address behind it where the cell has a hyperlink (a text that
    /// links to a page: the link is in the relationships of the document, or in a HYPERLINK field).
    /// </summary>
    /// <exception cref="FormatException">The content is not a Word document, or has no table.</exception>
    public static ImportTable ReadTable(byte[] content)
    {
        try
        {
            using MemoryStream stream = new(content);
            using ZipArchive zip = new(stream, ZipArchiveMode.Read);
            ZipArchiveEntry entry = zip.GetEntry("word/document.xml") ?? throw new FormatException("There is no document.");
            XDocument document = OfficeXml.Load(entry);
            XElement table = document.Descendants(W + "tbl").FirstOrDefault() ?? throw new FormatException("The document has no table.");
            Dictionary<string, string> targets = ReadHyperlinkTargets(zip);
            List<IReadOnlyList<string>> rows = new();
            List<IReadOnlyList<string?>> hyperlinks = new();

            // The text (and the address) of the cell above, per column of the grid: a cell that is merged downwards ("continue") is empty
            // in the file and shows the text of the cell that began the merge.
            List<string> above = new();
            List<string?> aboveLinks = new();

            foreach (XElement row in table.Elements(W + "tr"))
            {
                List<string> cells = new();
                List<string?> links = new();

                // (A row can begin further right in the grid.)
                int skipped = (int?)row.Element(W + "trPr")?.Element(W + "gridBefore")?.Attribute(W + "val") ?? 0;

                for (int i = 0; i < skipped; i++)
                {
                    cells.Add(string.Empty);
                    links.Add(null);
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
                    string? link = continues
                        ? (column < aboveLinks.Count ? aboveLinks[column] : null)
                        : CellHyperlink(cell, targets);

                    // A cell that is merged to the right takes the room of its columns; its text is in the first of them, so that
                    // what comes behind it stays in its own column.
                    cells.Add(text);
                    links.Add(link);

                    for (int i = 1; i < span; i++)
                    {
                        cells.Add(string.Empty);
                        links.Add(null);
                    }

                    while (above.Count < cells.Count)
                    {
                        above.Add(string.Empty);
                        aboveLinks.Add(null);
                    }

                    above[column] = text;
                    aboveLinks[column] = link;

                    for (int i = 1; i < span; i++)
                    {
                        above[column + i] = string.Empty;
                        aboveLinks[column + i] = null;
                    }
                }

                rows.Add(cells);
                hyperlinks.Add(links);
            }

            return new ImportTable(rows, hyperlinks);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or IOException or InvalidOperationException)
        {
            throw new FormatException("The file is not a Word document.", ex);
        }
    }

    // The addresses of the hyperlinks of the document by the id of their relationship ("rId5" -> "https://...").
    private static Dictionary<string, string> ReadHyperlinkTargets(ZipArchive zip)
    {
        Dictionary<string, string> targets = new(StringComparer.Ordinal);
        ZipArchiveEntry? entry = zip.GetEntry("word/_rels/document.xml.rels");

        if (entry is null)
        {
            return targets;
        }

        XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";

        foreach (XElement relationship in OfficeXml.Load(entry).Descendants(rel + "Relationship"))
        {
            string? type = (string?)relationship.Attribute("Type");
            string? id = (string?)relationship.Attribute("Id");
            string? target = (string?)relationship.Attribute("Target");

            if (id is not null && target is not null && type is not null && type.EndsWith("/hyperlink", StringComparison.OrdinalIgnoreCase))
            {
                targets[id] = target;
            }
        }

        return targets;
    }

    // The address of the first hyperlink in a cell: a w:hyperlink that points to a relationship, a simple HYPERLINK field, or the
    // instruction text of a complex one.
    private static string? CellHyperlink(XElement cell, Dictionary<string, string> targets)
    {
        foreach (XElement hyperlink in cell.Descendants(W + "hyperlink"))
        {
            string? id = (string?)hyperlink.Attribute(R + "id");

            if (id is not null && targets.TryGetValue(id, out string? target) && !string.IsNullOrWhiteSpace(target))
            {
                return target.Trim();
            }
        }

        foreach (XElement field in cell.Descendants(W + "fldSimple"))
        {
            string? address = AddressOfFieldCode((string?)field.Attribute(W + "instr"));

            if (address is not null)
            {
                return address;
            }
        }

        // (A field in several runs: its instruction is the text of the w:instrText elements.)
        return AddressOfFieldCode(string.Concat(cell.Descendants(W + "instrText").Select(element => element.Value)));
    }

    // 'HYPERLINK "https://example.com/x" \o "tip"' -> the address.
    private static string? AddressOfFieldCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        System.Text.RegularExpressions.Match match = HyperlinkField.Match(code);

        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static readonly System.Text.RegularExpressions.Regex HyperlinkField =
        new(@"HYPERLINK\s+""([^""]+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static XElement BuildRow(string title, string artist, string link, string? linkRelationshipId, bool bold, bool header)
    {
        XElement row = new(W + "tr");

        if (header)
        {
            row.Add(new XElement(W + "trPr", new XElement(W + "tblHeader")));
        }

        row.Add(BuildCell(title, bold, 3600), BuildCell(artist, bold, 2800), BuildLinkCell(link, linkRelationshipId, bold, 3000));

        return row;
    }

    // The cell of the link: a hyperlink (blue and underlined, as Word shows one) when there is an address behind the text, else plain text.
    private static XElement BuildLinkCell(string text, string? relationshipId, bool bold, int width)
    {
        if (relationshipId is null)
        {
            return BuildCell(text, bold, width);
        }

        XElement run = Run(text, bold, size: 0);
        XElement properties = run.Element(W + "rPr") ?? new XElement(W + "rPr");
        properties.Add(new XElement(W + "color", new XAttribute(W + "val", "0563C1")), new XElement(W + "u", new XAttribute(W + "val", "single")));

        if (properties.Parent is null)
        {
            run.AddFirst(properties);
        }

        return new XElement(
            W + "tc",
            new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", width), new XAttribute(W + "type", "dxa"))),
            new XElement(
                W + "p",
                new XElement(W + "hyperlink", new XAttribute(R + "id", relationshipId), new XAttribute(W + "history", "1"), run)));
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
