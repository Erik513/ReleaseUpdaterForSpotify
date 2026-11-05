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
