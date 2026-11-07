using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Core.Services;

namespace ReleaseUpdater.Core.Tests;

public sealed class ImportTableTests
{
    private static ImportTable Table(params string[][] rows) =>
        new(rows.Select(row => (IReadOnlyList<string>)row).ToList());

    // ---- the proposal for the columns ----

    // Verifies that a header that names the columns decides, wherever they are, in English or German.
    [Theory]
    [InlineData("Nr", "Titel", "Jahr", "Interpret", 1, 3)]
    [InlineData("Artist", "Title", "x", "y", 1, 0)]
    [InlineData("Song", "Band", "Note", "", 0, 1)]
    [InlineData("Lied", "Künstler", "", "", 0, 1)]
    public void GuessColumns_UsesTheNamesInTheHeader(string c0, string c1, string c2, string c3, int title, int artist)
    {
        ImportTable table = Table(new[] { c0, c1, c2, c3 }, new[] { "1", "Some Song", "2020", "Some Artist" });

        ImportColumnChoice guess = table.GuessColumns();

        Assert.True(guess.HasHeader);
        Assert.Equal(title, guess.TitleColumn);
        Assert.Equal(artist, guess.ArtistColumn);
    }

    // Verifies that without a header the first two columns that are not a numbering are taken (a column on the left with 1, 2, 3 ...).
    [Fact]
    public void GuessColumns_WithoutAHeader_SkipsNumberingColumns()
    {
        ImportTable table = Table(
            new[] { "1", "First Song", "First Artist", "note" },
            new[] { "2.", "Second Song", "Second Artist", "" },
            new[] { "#3", "Third Song", "Third Artist", "x" });

        ImportColumnChoice guess = table.GuessColumns();

        Assert.False(guess.HasHeader);
        Assert.Equal(1, guess.TitleColumn);
        Assert.Equal(2, guess.ArtistColumn);
    }

    // Verifies that empty columns are skipped as well (a table that starts a column further right).
    [Fact]
    public void GuessColumns_SkipsEmptyColumns()
    {
        ImportTable table = Table(
            new[] { "", "", "Song A", "Artist A" },
            new[] { "", " ", "Song B", "Artist B" });

        ImportColumnChoice guess = table.GuessColumns();

        Assert.Equal(2, guess.TitleColumn);
        Assert.Equal(3, guess.ArtistColumn);
    }

    // Verifies that a plain table of two columns is title and artist, in that order, without a header.
    [Fact]
    public void GuessColumns_ForTwoPlainColumns_IsTheirOrder()
    {
        ImportColumnChoice guess = Table(new[] { "Song A", "Artist A" }, new[] { "Song B", "Artist B" }).GuessColumns();

        Assert.Equal(new ImportColumnChoice(0, 1, false), guess);
    }

    // Verifies that the proposal is always two different columns, even for a table that gives no hint (or none at all).
    [Theory]
    [InlineData("1", "2", "3")]
    [InlineData("", "", "")]
    public void GuessColumns_AreTwoDifferentColumns_WhateverTheTable(string a, string b, string c)
    {
        ImportColumnChoice guess = Table(new[] { a, b, c }).GuessColumns();

        Assert.NotEqual(guess.TitleColumn, guess.ArtistColumn);
        Assert.InRange(guess.TitleColumn, 0, 2);
        Assert.InRange(guess.ArtistColumn, 0, 2);
        Assert.Equal(new ImportColumnChoice(guess.TitleColumn, guess.ArtistColumn, false), guess);

        ImportColumnChoice noRows = new ImportTable(Array.Empty<IReadOnlyList<string>>()).GuessColumns();
        Assert.NotEqual(noRows.TitleColumn, noRows.ArtistColumn);
    }

    // Verifies a header that only names one of the columns: the other one is found by content.
    [Fact]
    public void GuessColumns_WithAHalfHeader_FindsTheOtherColumnByContent()
    {
        ImportTable table = Table(
            new[] { "Nr", "Interpret", "Name2" },
            new[] { "1", "An Artist", "A Song" });

        ImportColumnChoice guess = table.GuessColumns();

        Assert.True(guess.HasHeader);
        Assert.Equal(1, guess.ArtistColumn);
        Assert.NotEqual(1, guess.TitleColumn);
    }

    // ---- reading with the chosen columns ----

    // Verifies that the chosen columns are read, the header is skipped, blank rows are ignored and incomplete ones counted.
    [Fact]
    public void SongsFromTable_ReadsTheChosenColumns()
    {
        ImportTable table = Table(
            new[] { "Nr", "Artist", "x", "Title" },
            new[] { "1", " A1 ", "-", " T1 " },
            new[] { "", "", "", "" },
            new[] { "2", "A2", "-", "" },
            new[] { "3", "A3" },
            new[] { "4", "A4", "-", "T4" });

        TrackedSongsImport import = TrackedSongsTransfer.SongsFromTable(table, new ImportColumnChoice(3, 1, HasHeader: true));

        Assert.Equal(new[] { new TrackedSong("T1", "A1"), new TrackedSong("T4", "A4") }, import.Songs);
        Assert.Equal(2, import.SkippedCount);
    }

    // Verifies that without a header the first row is a song.
    [Fact]
    public void SongsFromTable_WithoutAHeader_ReadsTheFirstRowToo()
    {
        ImportTable table = Table(new[] { "T1", "A1" }, new[] { "T2", "A2" });

        TrackedSongsImport import = TrackedSongsTransfer.SongsFromTable(table, new ImportColumnChoice(0, 1, HasHeader: false));

        Assert.Equal(2, import.Songs.Count);
    }

    // ---- the clipboard ----

    // Verifies a table copied from Excel or Word: tabs between the cells, a line per row (also with the line ending of Windows).
    [Fact]
    public void ParseClipboardText_ReadsTabSeparatedCells()
    {
        ImportTable table = TrackedSongsTransfer.ParseClipboardText("1\tSong A\tArtist A\r\n2\tSong B\tArtist B\r\n");

        Assert.Equal(3, table.ColumnCount);
        Assert.Equal("Artist B", table.Cell(1, 2));
    }

    // Verifies a table copied from Word (the "HTML Format" of the clipboard): the address behind a hyperlink is found, the text of the
    // cells is clean (no tags, entities decoded, line breaks folded), and what is outside the copied part is not read.
    [Fact]
    public void ParseClipboardHtml_ReadsCellsAndTheAddressBehindAHyperlink()
    {
        string nl = Environment.NewLine;
        string html = "Version:1.0" + nl + "StartHTML:0000000105" + nl + "<html><body><table><tr><td>outside</td></tr></table>" + nl + "<!--StartFragment-->"
            + "<table border=1><tr><td><p>Platform</p></td><td><p>Name</p></td></tr>"
            + "<!--[if gte mso 9]><xml>conditional</xml><![endif]-->"
            + "<tr><td><p><a href='https://youtu.be/abc?a=1&amp;b=2'><span style='color:blue'>YT</span></a></p></td><td><p>One (ID" + nl + " Version) &amp; more</p></td></tr>"
            + "<tr><td>no link</td><td>Two</td></tr></table><!--EndFragment--></body></html>";

        ImportTable? table = TrackedSongsTransfer.ParseClipboardHtml(html);

        Assert.NotNull(table);
        Assert.Equal(3, table!.Rows.Count);
        Assert.Equal("YT", table.Cell(1, 0));
        Assert.Equal("One (ID Version) & more", table.Cell(1, 1));
        Assert.Equal("https://youtu.be/abc?a=1&b=2", table.Hyperlink(1, 0));
        Assert.Null(table.Hyperlink(2, 0));
        Assert.Null(table.Hyperlink(0, 0));
    }

    // Verifies a row as real Word puts it on the clipboard (copied from Word 2026-11-07): attributes in double quotes, the link around a part of
    // the text, the empty <o:p> tags of Word.
    [Fact]
    public void ParseClipboardHtml_ReadsWhatWordPutsOnTheClipboard()
    {
        string html = """
<!--StartFragment-->
<table class=MsoNormalTable border=0 cellspacing=0 cellpadding=0 style='border-collapse:collapse;mso-table-layout-alt:fixed'>
 <tr style='mso-yfti-irow:0;mso-yfti-firstrow:yes;mso-yfti-lastrow:yes'>
  <td width=202 valign=top style='width:151.2pt'>
  <p class=MsoNormal><a href="https://youtu.be/abc?a=1&amp;b=2">Y</a>T<o:p></o:p></p>
  </td>
  <td width=202 valign=top style='width:151.2pt'>
  <p class=MsoNormal>One (ID Version)<o:p></o:p></p>
  </td>
  <td width=202 valign=top style='width:151.2pt'>
  <p class=MsoNormal>Axwell<o:p></o:p></p>
  </td>
 </tr>
</table>
<!--EndFragment-->
""";

        ImportTable? table = TrackedSongsTransfer.ParseClipboardHtml(html);

        Assert.NotNull(table);
        Assert.Single(table!.Rows);
        Assert.Equal("YT", table.Cell(0, 0));
        Assert.Equal("One (ID Version)", table.Cell(0, 1));
        Assert.Equal("Axwell", table.Cell(0, 2));
        Assert.Equal("https://youtu.be/abc?a=1&b=2", table.Hyperlink(0, 0));
    }

    // Verifies the cells as real Excel puts them on the clipboard (copied from Excel 2026-11-07): the copied part starts inside the table
    // (before the column widths), and the attribute of a link may stand on a line of its own.
    [Fact]
    public void ParseClipboardHtml_ReadsWhatExcelPutsOnTheClipboard()
    {
        string html = """
<table border=0 cellpadding=0 cellspacing=0 width=240>
<!--StartFragment-->
 <col width=80 span=3 style='width:60pt'>
 <tr height=20 style='height:15.0pt'>
  <td height=20 width=80 style='height:15.0pt;width:60pt'>Platform</td>
  <td width=80 style='width:60pt'>Name</td>
  <td width=80 style='width:60pt'>Original Artists</td>
 </tr>
 <tr height=20 style='height:15.0pt'>
  <td height=20 class=xl64 style='height:15.0pt'><a
  href="https://youtu.be/abc?a=1&amp;b=2">YT</a></td>
  <td>One (ID Version)</td>
  <td>Axwell</td>
 </tr>
 <tr height=20 style='height:15.0pt'>
  <td height=20 class=xl64 style='height:15.0pt'><a href="https://youtu.be/def">YT</a></td>
  <td>Go Back</td>
  <td>Presi On, Kuuda</td>
 </tr>
<!--EndFragment-->
</table>
""";

        ImportTable? table = TrackedSongsTransfer.ParseClipboardHtml(html);

        Assert.NotNull(table);
        Assert.Equal(3, table!.Rows.Count);
        Assert.Equal("Presi On, Kuuda", table.Cell(2, 2));
        Assert.Equal("https://youtu.be/abc?a=1&b=2", table.Hyperlink(1, 0));
        Assert.Equal("https://youtu.be/def", table.Hyperlink(2, 0));
        Assert.Null(table.Hyperlink(1, 1));
    }

    // Verifies that the clipboard without a table (or without anything) gives no table, so that the plain text is used.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html><body><p>just text</p></body></html>")]
    public void ParseClipboardHtml_WithoutATable_GivesNull(string? html)
    {
        Assert.Null(TrackedSongsTransfer.ParseClipboardHtml(html));
    }

    // Verifies that a cell with a line break (Excel puts it in quotes) stays one cell.
    [Fact]
    public void ParseClipboardText_KeepsQuotedCellsTogether()
    {
        ImportTable table = TrackedSongsTransfer.ParseClipboardText("\"Line\nBreak\"\tArtist\r\nNext\tOne\r\n");

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("Line\nBreak", table.Cell(0, 0));
    }

    // Verifies that text with semicolons (the way a CSV file has it) is understood as well.
    [Fact]
    public void ParseClipboardText_UnderstandsSemicolonText()
    {
        ImportTable table = TrackedSongsTransfer.ParseClipboardText("Song A;Artist A\nSong B;Artist B");

        Assert.Equal(new ImportColumnChoice(0, 1, false), table.GuessColumns());
        Assert.Equal(2, table.Rows.Count);
    }

    // Verifies that a stray quote (12" vinyl) in text that is no CSV does not make the paste fail.
    [Fact]
    public void ParseClipboardText_SurvivesAStrayQuote()
    {
        ImportTable table = TrackedSongsTransfer.ParseClipboardText("Record 12\" Edit\tSome Artist\nOther\tArtist");

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("Record 12\" Edit", table.Cell(0, 0));
    }

    // Verifies that which formats count as a table (a column choice makes sense) and which do not.
    [Theory]
    [InlineData(TrackedSongsFormat.Csv, true)]
    [InlineData(TrackedSongsFormat.Xlsx, true)]
    [InlineData(TrackedSongsFormat.Docx, true)]
    [InlineData(TrackedSongsFormat.Json, false)]
    public void IsTable_IsTrueForTableFormats(TrackedSongsFormat format, bool expected)
    {
        Assert.Equal(expected, TrackedSongsTransfer.IsTable(format));
    }

    // Verifies that the table of a Word or Excel file written by the app has the cells the list had.
    [Theory]
    [InlineData(TrackedSongsFormat.Csv)]
    [InlineData(TrackedSongsFormat.Xlsx)]
    [InlineData(TrackedSongsFormat.Docx)]
    public void ReadTable_GivesTheCellsOfTheFile(TrackedSongsFormat format)
    {
        byte[] file = TrackedSongsTransfer.Write(new[] { new TrackedSong("T", "A") }, format);

        ImportTable table = TrackedSongsTransfer.ReadTable(file, format);

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("Title", table.Cell(0, 0));
        Assert.Equal("A", table.Cell(1, 1));
        Assert.Equal(new ImportColumnChoice(0, 1, true, 2), table.GuessColumns());
    }

    // ---- the column of links ----

    // Verifies that a header that names the links ("Link", "URL", "Quelle", "Source") is found, wherever the column is.
    [Theory]
    [InlineData("Link")]
    [InlineData("URL")]
    [InlineData("Quelle")]
    [InlineData("source")]
    public void GuessColumns_FindsTheLinkColumnByItsHeader(string name)
    {
        ImportTable table = Table(
            new[] { "Nr", name, "Titel", "Interpret" },
            new[] { "1", "not an address, but named so", "Song", "Artist" });

        ImportColumnChoice guess = table.GuessColumns();

        Assert.Equal(new ImportColumnChoice(2, 3, true, 1), guess);
    }

    // Verifies that a column full of addresses is the link column without any header, and is not taken for the title or the artist.
    [Fact]
    public void GuessColumns_FindsAColumnFullOfAddresses_WithoutAHeader()
    {
        ImportTable table = Table(
            new[] { "1", "Song A", "Artist A", "https://example.com/a" },
            new[] { "2", "Song B", "Artist B", "www.example.com/b" },
            new[] { "3", "Song C", "Artist C", "" },
            new[] { "4", "Song D", "Artist D", "https://example.com/d" });

        ImportColumnChoice guess = table.GuessColumns();

        Assert.False(guess.HasHeader);
        Assert.Equal(new ImportColumnChoice(1, 2, false, 3), guess);
    }

    // Verifies that a link column in front of the others (the table of the user) does not take the place of the title or the artist.
    [Fact]
    public void GuessColumns_ALinkColumnInFront_IsNotTheTitle()
    {
        ImportTable table = Table(
            new[] { "https://example.com/a", "Song A", "Artist A" },
            new[] { "https://example.com/b", "Song B", "Artist B" });

        Assert.Equal(new ImportColumnChoice(1, 2, false, 0), table.GuessColumns());
    }

    // Verifies that a few texts that only look a little like addresses do not make a link column.
    [Fact]
    public void GuessColumns_AColumnWithMostlyOtherTexts_IsNoLinkColumn()
    {
        ImportTable table = Table(
            new[] { "Song A", "Artist A", "see www.example.com" },
            new[] { "Song B", "Artist B", "a note" },
            new[] { "Song C", "Artist C", "another note" });

        Assert.Null(table.GuessColumns().LinkColumn);
    }

    // Verifies that the songs of a table get their links, and that an empty cell is no link.
    [Fact]
    public void SongsFromTable_ReadsTheLinkColumn()
    {
        ImportTable table = Table(
            new[] { "Title", "Artist", "Link" },
            new[] { "Song A", "Artist A", " https://example.com/a " },
            new[] { "Song B", "Artist B", "" },
            new[] { "Song C", "Artist C" });

        TrackedSongsImport import = TrackedSongsTransfer.SongsFromTable(table, table.GuessColumns());

        Assert.Equal(
            new[] { new TrackedSong("Song A", "Artist A", "https://example.com/a"), new TrackedSong("Song B", "Artist B"), new TrackedSong("Song C", "Artist C") },
            import.Songs);
    }

    // Verifies that a table with the three names of the app in its header needs no question about the columns, in any order, and that other
    // tables do (a fourth column, another name, a name twice, no title).
    [Theory]
    [InlineData("Title;Artist;Link", true)]
    [InlineData("Titel;Interpret;URL", true)]
    [InlineData("Artist;Title", true)]
    [InlineData("Link;Artist;Title", true)]
    [InlineData("Nr;Title;Artist", false)]
    [InlineData("Title;Artist;Link;Year", false)]
    [InlineData("Title;Title;Artist", false)]
    [InlineData("Artist;Link;Link", false)]
    [InlineData("Song;Link", false)]
    [InlineData("A;B;C", false)]
    public void HasRecognizedHeader_IsTrueOnlyForTheOwnFormat(string header, bool expected)
    {
        string[] names = header.Split(';');
        ImportTable table = Table(names, names.Select(_ => "x").ToArray());

        Assert.Equal(expected, table.HasRecognizedHeader());
    }
}
