using System.IO.Compression;
using System.Text;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Core.Services;

namespace ReleaseUpdater.Core.Tests;

public sealed class TrackedSongsTransferTests
{
    private static readonly TrackedSong[] Songs =
    {
        new("Plain Title", "Plain Artist"),
        new("Semi; Colon", "Comma, Artist"),
        new("She said \"hi\"", "Quote Artist"),
        new("Line\r\nBreak", "Multi Artist"),
        new("Über Größe", "Ärzte")
    };

    // ---- formats ----

    // Verifies which file gets which format.
    [Theory]
    [InlineData(@"C:\x\songs.csv", TrackedSongsFormat.Csv)]
    [InlineData(@"C:\x\SONGS.CSV", TrackedSongsFormat.Csv)]
    [InlineData(@"C:\x\songs.xlsx", TrackedSongsFormat.Xlsx)]
    [InlineData(@"C:\x\SONGS.XLSX", TrackedSongsFormat.Xlsx)]
    [InlineData(@"C:\x\songs.docx", TrackedSongsFormat.Docx)]
    [InlineData(@"C:\x\songs.json", TrackedSongsFormat.Json)]
    [InlineData(@"C:\x\songs", TrackedSongsFormat.Json)]
    [InlineData(@"C:\x\songs.txt", TrackedSongsFormat.Json)]
    public void FormatForPath_IsCsvOnlyForCsvFiles(string path, TrackedSongsFormat expected)
    {
        Assert.Equal(expected, TrackedSongsTransfer.FormatForPath(path));
    }

    // ---- round trips ----

    // Verifies that what is written can be read back unchanged, delimiters, quotes, line breaks and umlauts included.
    [Theory]
    [InlineData(TrackedSongsFormat.Json)]
    [InlineData(TrackedSongsFormat.Csv)]
    public void Serialize_ThenParse_GivesTheSameSongs(TrackedSongsFormat format)
    {
        string text = TrackedSongsTransfer.Serialize(Songs, format);

        TrackedSongsImport import = TrackedSongsTransfer.Parse(text, format);

        Assert.Equal(Songs, import.Songs);
        Assert.Equal(0, import.SkippedCount);
    }

    // Verifies the same through the byte API that the app uses, for all four formats (Excel and Word included).
    [Theory]
    [InlineData(TrackedSongsFormat.Json)]
    [InlineData(TrackedSongsFormat.Csv)]
    [InlineData(TrackedSongsFormat.Xlsx)]
    [InlineData(TrackedSongsFormat.Docx)]
    public void Write_ThenRead_GivesTheSameSongs_InEveryFormat(TrackedSongsFormat format)
    {
        TrackedSong[] songs = Songs.Append(new TrackedSong("  spaced  ", "Tab\tArtist")).ToArray();

        TrackedSongsImport import = TrackedSongsTransfer.Read(TrackedSongsTransfer.Write(songs, format), format);

        // (XML and Word store a line break as a plain LF; the other formats keep CR LF.)
        Assert.Equal(songs.Select(song => Normalize(song)), import.Songs.Select(song => Normalize(song)));
        Assert.Equal(0, import.SkippedCount);
    }

    // Verifies that an empty list is a valid file in every format, and gives an empty list.
    [Theory]
    [InlineData(TrackedSongsFormat.Json)]
    [InlineData(TrackedSongsFormat.Csv)]
    [InlineData(TrackedSongsFormat.Xlsx)]
    [InlineData(TrackedSongsFormat.Docx)]
    public void EmptyList_RoundTrips_InEveryFormat(TrackedSongsFormat format)
    {
        TrackedSongsImport import = TrackedSongsTransfer.Read(TrackedSongsTransfer.Write(Array.Empty<TrackedSong>(), format), format);

        Assert.Empty(import.Songs);
        Assert.Equal(0, import.SkippedCount);
    }

    // Verifies that the files are zip packages with the parts that Excel and Word look for first.
    [Fact]
    public void OfficeFiles_AreZipPackagesWithTheirMainPart()
    {
        using ZipArchive xlsx = new(new MemoryStream(TrackedSongsTransfer.Write(Songs, TrackedSongsFormat.Xlsx)));
        using ZipArchive docx = new(new MemoryStream(TrackedSongsTransfer.Write(Songs, TrackedSongsFormat.Docx)));

        Assert.NotNull(xlsx.GetEntry("[Content_Types].xml"));
        Assert.NotNull(xlsx.GetEntry("xl/workbook.xml"));
        Assert.NotNull(xlsx.GetEntry("xl/worksheets/sheet1.xml"));
        Assert.NotNull(docx.GetEntry("[Content_Types].xml"));
        Assert.NotNull(docx.GetEntry("word/document.xml"));
    }

    // Verifies that a workbook the way Excel saves it is read: other sheet file name, shared strings, rich text, numbers, a gap in a row.
    [Fact]
    public void ReadXlsx_UnderstandsAWorkbookSavedByExcel()
    {
        byte[] workbook = Zip(
            ("xl/workbook.xml", """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Tabelle1" sheetId="1" r:id="rId7"/></sheets></workbook>"""),
            ("xl/_rels/workbook.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId7" Type="x" Target="worksheets/tab.xml"/></Relationships>"""),
            ("xl/sharedStrings.xml", """<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>Titel</t></si><si><t>Interpret</t></si><si><r><t>Rich </t></r><r><t>Song</t></r></si><si><t>Some Artist</t></si></sst>"""),
            ("xl/worksheets/tab.xml", """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row><row r="2"><c r="A2" t="s"><v>2</v></c><c r="B2" t="s"><v>3</v></c></row><row r="3"><c r="A3"><v>42</v></c><c r="B3" t="inlineStr"><is><t>Number Artist</t></is></c></row><row r="4"><c r="B4" t="s"><v>3</v></c></row><row r="5"><c r="A5" t="s"><v>2</v></c><c r="C5"><v>1</v></c></row></sheetData></worksheet>"""));

        TrackedSongsImport import = TrackedSongsTransfer.Read(workbook, TrackedSongsFormat.Xlsx);

        // (The German header "Titel / Interpret" is a header too.)
        Assert.Equal(
            new[] { new TrackedSong("Rich Song", "Some Artist"), new TrackedSong("42", "Number Artist") },
            import.Songs);
        Assert.Equal(2, import.SkippedCount);
    }

    // Verifies that the first table of a Word document is read, whatever is around it, with several paragraphs in a cell.
    [Fact]
    public void ReadDocx_TakesTheFirstTable()
    {
        const string w = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";
        byte[] document = Zip(("word/document.xml",
            "<w:document " + w + "><w:body><w:p><w:r><w:t>Intro</w:t></w:r></w:p>" +
            "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>Title</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Artist</w:t></w:r></w:p></w:tc></w:tr>" +
            "<w:tr><w:tc><w:p><w:r><w:t>Two</w:t></w:r></w:p><w:p><w:r><w:t>Lines</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>A</w:t></w:r><w:r><w:t>rtist</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" +
            "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>ignored</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>table</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" +
            "</w:body></w:document>"));

        TrackedSongsImport import = TrackedSongsTransfer.Read(document, TrackedSongsFormat.Docx);

        Assert.Equal(new[] { new TrackedSong("Two\nLines", "Artist") }, import.Songs);
    }

    // Verifies that files that are not what their ending says are refused, not crashed on.
    [Theory]
    [InlineData(TrackedSongsFormat.Xlsx)]
    [InlineData(TrackedSongsFormat.Docx)]
    public void ReadOffice_RefusesWhatIsNotAnOfficeFile(TrackedSongsFormat format)
    {
        Assert.Throws<FormatException>(() => TrackedSongsTransfer.Read(Encoding.UTF8.GetBytes("just text"), format));
        Assert.Throws<FormatException>(() => TrackedSongsTransfer.Read(Array.Empty<byte>(), format));
        Assert.Throws<FormatException>(() => TrackedSongsTransfer.Read(Zip(("other.txt", "x")), format));
    }

    // Verifies that characters that XML cannot hold do not break the file (they are left out).
    [Fact]
    public void Write_LeavesOutControlCharacters_InTheOfficeFiles()
    {
        TrackedSong[] songs = { new("Bell\u0007 Song", "Art\u0001ist") };

        foreach (TrackedSongsFormat format in new[] { TrackedSongsFormat.Xlsx, TrackedSongsFormat.Docx })
        {
            TrackedSongsImport import = TrackedSongsTransfer.Read(TrackedSongsTransfer.Write(songs, format), format);

            Assert.Equal(new[] { new TrackedSong("Bell Song", "Artist") }, import.Songs);
        }
    }

    private static TrackedSong Normalize(TrackedSong song) =>
        new(song.Title.Trim().Replace("\r\n", "\n"), song.Artist.Trim().Replace("\r\n", "\n"));

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using MemoryStream stream = new();

        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in entries)
            {
                using StreamWriter writer = new(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    // Verifies that an empty list is a valid file, and gives an empty list.
    [Theory]
    [InlineData(TrackedSongsFormat.Json)]
    [InlineData(TrackedSongsFormat.Csv)]
    public void EmptyList_RoundTrips(TrackedSongsFormat format)
    {
        string text = TrackedSongsTransfer.Serialize(Array.Empty<TrackedSong>(), format);

        TrackedSongsImport import = TrackedSongsTransfer.Parse(text, format);

        Assert.Empty(import.Songs);
        Assert.Equal(0, import.SkippedCount);
    }

    // Verifies that the JSON keeps non-ASCII letters readable (not \u00DC) and uses the names title / artist.
    [Fact]
    public void Json_IsReadable_AndUsesTitleAndArtist()
    {
        string text = TrackedSongsTransfer.Serialize(new[] { new TrackedSong("Über", "Ärzte") }, TrackedSongsFormat.Json);

        Assert.Contains("\"title\": \"Über\"", text);
        Assert.Contains("\"artist\": \"Ärzte\"", text);
    }

    // ---- reading JSON ----

    // Verifies that property names are read whatever their case, and values are trimmed.
    [Fact]
    public void ParseJson_IgnoresCaseOfNames_AndTrims()
    {
        TrackedSongsImport import = TrackedSongsTransfer.Parse(
            """[ { "Title": "  A  ", "ARTIST": " B " } ]""",
            TrackedSongsFormat.Json);

        Assert.Equal(new[] { new TrackedSong("A", "B") }, import.Songs);
    }

    // Verifies that entries without a usable title or artist are counted, not imported.
    [Fact]
    public void ParseJson_SkipsIncompleteEntries()
    {
        TrackedSongsImport import = TrackedSongsTransfer.Parse(
            """[ { "title": "A", "artist": "B" }, { "title": "only" }, { "title": " ", "artist": "x" }, 5, { "title": 1, "artist": 2 } ]""",
            TrackedSongsFormat.Json);

        Assert.Single(import.Songs);
        Assert.Equal(4, import.SkippedCount);
    }

    // Verifies that a file that is not a list of songs is refused.
    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("""{ "title": "A", "artist": "B" }""")]
    [InlineData("""[ { "title": "A" """)]
    public void ParseJson_RefusesWhatIsNotAList(string text)
    {
        Assert.Throws<FormatException>(() => TrackedSongsTransfer.Parse(text, TrackedSongsFormat.Json));
    }

    // ---- reading CSV ----

    // Verifies that the delimiter of the file is found (semicolon, comma or tab) and a header line is skipped.
    [Theory]
    [InlineData("Title;Artist\r\nA;B\r\nC;D\r\n")]
    [InlineData("title,artist\nA,B\nC,D")]
    [InlineData("Title\tArtist\nA\tB\nC\tD\n")]
    [InlineData("A;B\r\nC;D\r\n")]
    public void ParseCsv_FindsTheDelimiter_AndSkipsAHeader(string text)
    {
        TrackedSongsImport import = TrackedSongsTransfer.Parse(text, TrackedSongsFormat.Csv);

        Assert.Equal(new[] { new TrackedSong("A", "B"), new TrackedSong("C", "D") }, import.Songs);
        Assert.Equal(0, import.SkippedCount);
    }

    // Verifies that a byte order mark (what a spreadsheet writes) does not become part of the first title.
    [Fact]
    public void ParseCsv_IgnoresAByteOrderMark()
    {
        TrackedSongsImport import = TrackedSongsTransfer.Parse("\uFEFFTitle;Artist\r\nA;B\r\n", TrackedSongsFormat.Csv);

        Assert.Equal(new[] { new TrackedSong("A", "B") }, import.Songs);
    }

    // Verifies that empty lines are ignored and short or half-empty lines are counted as skipped.
    [Fact]
    public void ParseCsv_SkipsBrokenLines_AndIgnoresEmptyOnes()
    {
        TrackedSongsImport import = TrackedSongsTransfer.Parse("A;B\r\n\r\nonly one\r\n;nothing\r\nC;D\r\n;;\r\n", TrackedSongsFormat.Csv);

        Assert.Equal(2, import.Songs.Count);
        Assert.Equal(2, import.SkippedCount);
    }

    // Verifies that a quote that is never closed is refused instead of swallowing the rest of the file.
    [Fact]
    public void ParseCsv_RefusesAnOpenQuote()
    {
        Assert.Throws<FormatException>(() => TrackedSongsTransfer.Parse("A;\"B\r\nC;D\r\n", TrackedSongsFormat.Csv));
    }

    // Verifies that a row can use further columns (extra data of a spreadsheet) without harm.
    [Fact]
    public void ParseCsv_IgnoresFurtherColumns()
    {
        TrackedSongsImport import = TrackedSongsTransfer.Parse("A;B;note;1\r\n", TrackedSongsFormat.Csv);

        Assert.Equal(new[] { new TrackedSong("A", "B") }, import.Songs);
    }

    // ---- merging ----

    // Verifies that an import is added behind the list, without songs that are in it already (case does not matter) and without twins.
    [Fact]
    public void Merge_AddsOnlyWhatIsNew()
    {
        TrackedSong[] existing = { new("Song", "Artist"), new("Other", "Artist") };
        TrackedSong[] imported =
        {
            new("SONG", "artist"),
            new("New", "Artist"),
            new("New", "ARTIST"),
            new(" Another ", "One")
        };

        TrackedSongsMerge merge = TrackedSongsTransfer.Merge(existing, imported);

        Assert.Equal(2, merge.AddedCount);
        Assert.Equal(2, merge.DuplicateCount);
        Assert.Equal(
            new[] { existing[0], existing[1], imported[1], imported[3] },
            merge.Songs);
    }

    // Verifies that merging into an empty list takes everything.
    [Fact]
    public void Merge_IntoAnEmptyList_TakesEverything()
    {
        TrackedSongsMerge merge = TrackedSongsTransfer.Merge(Array.Empty<TrackedSong>(), Songs);

        Assert.Equal(Songs.Length, merge.AddedCount);
        Assert.Equal(0, merge.DuplicateCount);
    }
}
