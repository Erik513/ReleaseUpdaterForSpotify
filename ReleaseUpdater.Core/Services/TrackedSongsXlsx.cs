using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Core.Services;

/// <summary>
/// An Excel workbook (.xlsx) with one sheet: a bold header row (Title, Artist) and a row per song. An .xlsx file is a zip of
/// XML files; only what a workbook with one table needs is written, and what Excel (or another program) saved is read: the
/// first sheet, texts from the shared strings or in the cell, numbers as they are shown.
/// </summary>
internal static class TrackedSongsXlsx
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static byte[] Write(IReadOnlyList<TrackedSong> songs)
    {
        using MemoryStream stream = new();

        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>
                """);
            AddEntry(zip, "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
                """);
            AddEntry(zip, "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Songs" sheetId="1" r:id="rId1"/></sheets></workbook>
                """);
            AddEntry(zip, "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
                """);
            // Two cell styles: 0 = normal, 1 = bold (the header). Wrapped text so that a line break in a title is shown.
            AddEntry(zip, "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1" applyAlignment="1"><alignment vertical="top"/></xf></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
                """);

            XElement data = new(Main + "sheetData", BuildRow(1, "Title", "Artist", style: 1));

            for (int i = 0; i < songs.Count; i++)
            {
                data.Add(BuildRow(i + 2, songs[i].Title, songs[i].Artist, style: 0));
            }

            XDocument sheet = new(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(Main + "worksheet",
                    new XElement(Main + "sheetViews", new XElement(Main + "sheetView", new XAttribute("workbookViewId", "0"),
                        new XElement(Main + "pane", new XAttribute("ySplit", "1"), new XAttribute("topLeftCell", "A2"),
                            new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                    new XElement(Main + "cols",
                        new XElement(Main + "col", new XAttribute("min", "1"), new XAttribute("max", "1"), new XAttribute("width", "48"), new XAttribute("customWidth", "1")),
                        new XElement(Main + "col", new XAttribute("min", "2"), new XAttribute("max", "2"), new XAttribute("width", "36"), new XAttribute("customWidth", "1"))),
                    data));

            AddEntry(zip, "xl/worksheets/sheet1.xml", sheet.ToString(SaveOptions.DisableFormatting), declaration: true);
        }

        return stream.ToArray();
    }

    /// <exception cref="FormatException">The content is not an Excel workbook.</exception>
    public static IReadOnlyList<IReadOnlyList<string>> ReadRows(byte[] content)
    {
        try
        {
            using MemoryStream stream = new(content);
            using ZipArchive zip = new(stream, ZipArchiveMode.Read);

            string sheetPath = FindFirstSheet(zip);
            List<string> sharedStrings = ReadSharedStrings(zip);
            XDocument sheet = Load(zip, sheetPath) ?? throw new FormatException("The sheet is missing.");
            List<List<string>> rows = new();

            // Rows that are not in the file at all are empty rows and left out; the number a row has in the sheet (the "r" of the
            // merged ranges) is kept for each one that is there.
            Dictionary<int, int> indexOfSheetRow = new();
            int sheetRow = 0;

            foreach (XElement row in sheet.Descendants(Main + "row"))
            {
                sheetRow = (int?)row.Attribute("r") ?? sheetRow + 1;
                indexOfSheetRow[sheetRow] = rows.Count;
                List<string> cells = new();

                foreach (XElement cell in row.Elements(Main + "c"))
                {
                    int column = ColumnIndex((string?)cell.Attribute("r"), cells.Count);

                    while (cells.Count < column)
                    {
                        cells.Add(string.Empty);
                    }

                    if (cells.Count == column)
                    {
                        cells.Add(CellText(cell, sharedStrings));
                    }
                }

                rows.Add(cells);
            }

            FillMergedCells(sheet, rows, indexOfSheetRow);

            return rows.Select(row => (IReadOnlyList<string>)row).ToList();
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or IOException or FormatException or InvalidOperationException)
        {
            throw new FormatException("The file is not an Excel workbook.", ex);
        }
    }

    // Excel keeps the value of a merged range in its top left cell only; the other cells of the range are empty in the file. A range
    // merged downwards (an artist with several songs) counts for every row it spans, so the value is put into the cells under it.
    // A range that spans several columns keeps its value in the first column; the columns behind stay as they are.
    private static void FillMergedCells(XDocument sheet, List<List<string>> rows, Dictionary<int, int> indexOfSheetRow)
    {
        foreach (XElement merge in sheet.Descendants(Main + "mergeCell"))
        {
            string[] ends = ((string?)merge.Attribute("ref") ?? string.Empty).Split(':');

            if (ends.Length != 2 ||
                !TryParseCellReference(ends[0], out int firstRow, out int column) ||
                !TryParseCellReference(ends[1], out int lastRow, out _) ||
                !indexOfSheetRow.TryGetValue(firstRow, out int firstIndex) ||
                column >= rows[firstIndex].Count)
            {
                continue;
            }

            string text = rows[firstIndex][column];

            for (int row = firstRow + 1; row <= lastRow; row++)
            {
                // (A row that is not in the file has no cells at all: nothing to fill.)
                if (!indexOfSheetRow.TryGetValue(row, out int index))
                {
                    continue;
                }

                List<string> cells = rows[index];

                while (cells.Count <= column)
                {
                    cells.Add(string.Empty);
                }

                cells[column] = text;
            }
        }
    }

    // "B7" -> row 7, column 1 (the first column is 0).
    private static bool TryParseCellReference(string reference, out int row, out int column)
    {
        row = 0;
        column = 0;
        int letters = 0;

        while (letters < reference.Length && char.IsLetter(reference[letters]))
        {
            letters++;
        }

        if (letters == 0 || !int.TryParse(reference.AsSpan(letters), out row) || row < 1)
        {
            return false;
        }

        column = ColumnIndex(reference, 0);

        return true;
    }

    private static XElement BuildRow(int number, string title, string artist, int style) =>
        new(Main + "row", new XAttribute("r", number),
            BuildCell("A" + number, title, style),
            BuildCell("B" + number, artist, style));

    private static XElement BuildCell(string reference, string text, int style) =>
        new(Main + "c", new XAttribute("r", reference), new XAttribute("t", "inlineStr"), new XAttribute("s", style),
            new XElement(Main + "is",
                new XElement(Main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), Clean(text))));

    // XML 1.0 has no room for most control characters; a line break and a tab stay.
    private static string Clean(string text)
    {
        StringBuilder builder = new(text.Length);

        foreach (char character in text)
        {
            if (character >= ' ' || character is '\t' or '\n' or '\r')
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static void AddEntry(ZipArchive zip, string name, string xml, bool declaration = false)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        // (The text blocks above begin with their declaration; a document built in code gets one here.)
        writer.Write(declaration ? "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" + xml : xml.Trim());
    }

    private static XDocument? Load(ZipArchive zip, string path)
    {
        ZipArchiveEntry? entry = zip.GetEntry(path.TrimStart('/'));

        if (entry == null)
        {
            return null;
        }

        using Stream stream = entry.Open();

        return XDocument.Load(stream);
    }

    // The first sheet of the workbook, by the relationship that the workbook names (Excel does not always call it sheet1.xml).
    private static string FindFirstSheet(ZipArchive zip)
    {
        XDocument workbook = Load(zip, "xl/workbook.xml") ?? throw new FormatException("There is no workbook.");
        string? relationId = (string?)workbook.Descendants(Main + "sheet").FirstOrDefault()?.Attribute(Rel + "id");
        XDocument? relations = Load(zip, "xl/_rels/workbook.xml.rels");
        string? target = relations?.Descendants(PackageRel + "Relationship")
            .FirstOrDefault(relation => (string?)relation.Attribute("Id") == relationId)?.Attribute("Target")?.Value;

        if (target == null)
        {
            return "xl/worksheets/sheet1.xml";
        }

        return target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        XDocument? document = Load(zip, "xl/sharedStrings.xml");

        return document == null
            ? new List<string>()
            : document.Descendants(Main + "si").Select(item => string.Concat(item.Descendants(Main + "t").Select(text => text.Value))).ToList();
    }

    private static string CellText(XElement cell, List<string> sharedStrings)
    {
        string type = (string?)cell.Attribute("t") ?? string.Empty;

        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(Main + "t").Select(text => text.Value));
        }

        string value = (string?)cell.Element(Main + "v") ?? string.Empty;

        if (type == "s")
        {
            return int.TryParse(value, out int index) && index >= 0 && index < sharedStrings.Count ? sharedStrings[index] : string.Empty;
        }

        return value;
    }

    // "B7" -> 1 (the letters are the column); a cell without a reference follows the one before it.
    private static int ColumnIndex(string? reference, int fallback)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return fallback;
        }

        int column = 0;

        foreach (char character in reference)
        {
            if (!char.IsLetter(character))
            {
                break;
            }

            column = column * 26 + (char.ToUpperInvariant(character) - 'A' + 1);
        }

        return Math.Max(0, column - 1);
    }
}
