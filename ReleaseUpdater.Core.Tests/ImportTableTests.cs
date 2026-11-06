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
        Assert.Equal(new ImportColumnChoice(0, 1, true), table.GuessColumns());
    }
}
