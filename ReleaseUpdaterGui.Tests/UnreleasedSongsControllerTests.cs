using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Core.Services;
using ReleaseUpdater.Data.Storage;

namespace ReleaseUpdaterGui.Tests;

public sealed class UnreleasedSongsControllerTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly string directory = Path.Combine(Path.GetTempPath(), "SpotifyReleaseTests", Guid.NewGuid().ToString("N"));
    private readonly FakeUnreleasedView view = new();
    private readonly FakeGateway gateway = new();
    private readonly FakeUnreleasedReportWriter reportWriter = new();

    public UnreleasedSongsControllerTests()
    {
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
    }

    private readonly FakeSettingsStore settings = new();

    private UnreleasedSongsController CreateController() =>
        new(view, new TrackedSongsStore(new AppDataPaths(directory)), new UnreleasedSongChecker(gateway, reportWriter), reportWriter, settings);

    // ---- export and import ----

    // Verifies that the list is written as JSON or CSV by the file name, and read back by the import.
    [Theory]
    [InlineData("songs.json")]
    [InlineData("songs.csv")]
    [InlineData("songs.xlsx")]
    [InlineData("songs.docx")]
    public void Export_ThenImport_GivesTheSameSongs(string fileName)
    {
        string path = Path.Combine(directory, fileName);
        TrackedSong[] songs = { new("Song; A", "Artist \"A\""), new("Über", "Ärzte") };

        Assert.True(CreateController().ExportSongs(songs, path));
        IReadOnlyList<TrackedSong>? imported = CreateController().ImportSongs(path, Array.Empty<TrackedSong>());

        Assert.Equal(songs, imported);
        Assert.True(view.Has("SetStatus:"));
        Assert.False(view.Has("ShowError:"));
    }

    // Verifies that rows that are completely blank (the grid's empty row) are not exported, and a row with only one text is.
    [Fact]
    public void Export_LeavesOutBlankRows()
    {
        string path = Path.Combine(directory, "songs.json");

        CreateController().ExportSongs(new[] { new TrackedSong("A", "B"), new TrackedSong("", " "), new TrackedSong("Half", "") }, path);

        IReadOnlyList<TrackedSong>? imported = CreateController().ImportSongs(path, Array.Empty<TrackedSong>());
        Assert.Equal(new[] { new TrackedSong("A", "B") }, imported);
    }

    // Verifies that nothing is written, and the user is told, when there is nothing to export.
    [Fact]
    public void Export_WithoutSongs_SaysSo_AndWritesNothing()
    {
        string path = Path.Combine(directory, "songs.json");

        bool exported = CreateController().ExportSongs(new[] { new TrackedSong("", "") }, path);

        Assert.False(exported);
        Assert.True(view.Has("ShowInfo:"));
        Assert.False(File.Exists(path));
    }

    // Verifies that a path that cannot be written is reported instead of crashing the window.
    [Fact]
    public void Export_ToAFolderThatIsNotThere_ShowsAnError()
    {
        string path = Path.Combine(directory, "no-such-folder", "songs.json");

        bool exported = CreateController().ExportSongs(new[] { new TrackedSong("A", "B") }, path);

        Assert.False(exported);
        Assert.True(view.Has("ShowError:"));
    }

    // Verifies that an import is added behind the songs in the list, without the ones that are in it already.
    [Fact]
    public void Import_AddsOnlyNewSongs_AndReportsTheCounts()
    {
        string path = Path.Combine(directory, "songs.csv");
        File.WriteAllText(path, "Title;Artist\r\nsong a;artist a\r\nSong B;Artist B\r\nbroken\r\n");

        IReadOnlyList<TrackedSong>? merged = CreateController().ImportSongs(path, new[] { new TrackedSong("Song A", "Artist A") });

        Assert.Equal(new[] { new TrackedSong("Song A", "Artist A"), new TrackedSong("Song B", "Artist B") }, merged);
        Assert.True(view.Has("SetStatus:"));
    }

    // Verifies that a file that is no song list, an empty list and a missing file leave the list alone and say why.
    [Theory]
    [InlineData("not json at all", "ShowError:")]
    [InlineData("[]", "ShowInfo:")]
    [InlineData("[ { \"title\": \"only a title\" } ]", "ShowInfo:")]
    public void Import_OfAnUnusableFile_ReturnsNull_AndTellsTheUser(string content, string expectedCall)
    {
        string path = Path.Combine(directory, "songs.json");
        File.WriteAllText(path, content);

        IReadOnlyList<TrackedSong>? merged = CreateController().ImportSongs(path, new[] { new TrackedSong("Keep", "Me") });

        Assert.Null(merged);
        Assert.True(view.Has(expectedCall));
    }

    // ---- the order of the columns ----

    // Verifies that the order of the columns is kept in the settings (and read from there), and starts as Title first.
    [Fact]
    public void ArtistFirst_IsKeptInTheSettings()
    {
        Assert.False(CreateController().LoadArtistFirst());

        CreateController().SaveArtistFirst(true);

        Assert.True(settings.Stored.UnreleasedArtistFirst);
        Assert.True(CreateController().LoadArtistFirst());

        CreateController().SaveArtistFirst(false);

        Assert.False(CreateController().LoadArtistFirst());
    }

    // Verifies that the rest of the settings is not touched by it, and that nothing is written when nothing changed.
    [Fact]
    public void SavingTheColumnOrder_KeepsTheOtherSettings_AndWritesOnlyChanges()
    {
        settings.Stored.PlaylistName = "My playlist";

        CreateController().SaveArtistFirst(false);
        Assert.Equal(0, settings.SaveCount);

        CreateController().SaveArtistFirst(true);

        Assert.Equal(1, settings.SaveCount);
        Assert.Equal("My playlist", settings.Stored.PlaylistName);
    }

    // Verifies that a settings file that cannot be read or written does not crash the window (it is only a preference).
    [Fact]
    public void ColumnOrder_SurvivesAFileThatCannotBeWritten()
    {
        settings.SaveFailure = new IOException("disk full");

        CreateController().SaveArtistFirst(true);

        Assert.False(view.Has("ShowError:"));
        Assert.False(CreateController().LoadArtistFirst());
    }

    // ---- the list shows the columns in the order of the imported table ----

    // Verifies that a table with the artist in front of the title makes the list show the Artist column first (the user's choice counts).
    [Fact]
    public void Import_WithTheArtistBeforeTheTitle_ShowsTheArtistColumnFirst()
    {
        string path = Path.Combine(directory, "artist-first.csv");
        File.WriteAllText(path, "1;Artist A;Song A;2020\r\n2;Artist B;Song B;2021\r\n");
        view.ColumnChoice = new ImportColumnChoice(TitleColumn: 2, ArtistColumn: 1, HasHeader: false);

        CreateController().ImportSongs(path, Array.Empty<TrackedSong>());

        Assert.True(view.Has("ApplyColumnOrder:True"));
    }

    // Verifies that a table with the title in front makes the list show the Title column first again.
    [Fact]
    public void Import_WithTheTitleBeforeTheArtist_ShowsTheTitleColumnFirst()
    {
        string path = Path.Combine(directory, "title-first.csv");
        File.WriteAllText(path, "1;Song A;Artist A;2020\r\n");

        CreateController().ImportSongs(path, Array.Empty<TrackedSong>());

        Assert.True(view.Has("ApplyColumnOrder:False"));
    }

    // Verifies that a header that names the columns (Interpret before Titel) is enough, also for a table of two columns.
    [Fact]
    public void Import_OfATwoColumnTableWithAHeader_FollowsTheHeader()
    {
        string path = Path.Combine(directory, "header.csv");
        File.WriteAllText(path, "Interpret;Titel\r\nArtist A;Song A\r\n");

        IReadOnlyList<TrackedSong>? merged = CreateController().ImportSongs(path, Array.Empty<TrackedSong>());

        Assert.Equal(new[] { new TrackedSong("Song A", "Artist A") }, merged);
        Assert.True(view.Has("ApplyColumnOrder:True"));
        Assert.False(view.Has("ChooseImportColumns"));
    }

    // Verifies that a plain table of two columns without a header says nothing about the order, so the order of the user stays.
    [Fact]
    public void Import_OfAPlainTwoColumnTable_LeavesTheColumnOrder()
    {
        string path = Path.Combine(directory, "plain.csv");
        File.WriteAllText(path, "Song A;Artist A\r\n");

        CreateController().ImportSongs(path, Array.Empty<TrackedSong>());

        Assert.False(view.Has("ApplyColumnOrder"));
    }

    // Verifies that JSON, a cancelled choice and an import without songs leave the order alone too.
    [Fact]
    public void Import_WithoutSongsOrCancelled_LeavesTheColumnOrder()
    {
        string json = Path.Combine(directory, "songs.json");
        File.WriteAllText(json, "[ { \"title\": \"A\", \"artist\": \"B\" } ]");
        CreateController().ImportSongs(json, Array.Empty<TrackedSong>());

        string wide = Path.Combine(directory, "wide.csv");
        File.WriteAllText(wide, "1;Song;Artist\r\n");
        view.CancelColumnChoice = true;
        CreateController().ImportSongs(wide, Array.Empty<TrackedSong>());

        string empty = Path.Combine(directory, "empty.csv");
        File.WriteAllText(empty, "Interpret;Titel\r\n");
        CreateController().ImportSongs(empty, Array.Empty<TrackedSong>());

        Assert.False(view.Has("ApplyColumnOrder"));
    }

    // Verifies that a paste follows the order of the copied table the same way, and that an earlier import does not leak into it.
    [Fact]
    public void Paste_FollowsTheColumnOrderOfTheCopiedTable()
    {
        IReadOnlyList<TrackedSong>? merged = CreateController().PasteSongs(
            "Interpret\tTitel\r\nArtist A\tSong A\r\n",
            Array.Empty<TrackedSong>());

        Assert.Single(merged!);
        Assert.True(view.Has("ApplyColumnOrder:True"));
    }

    // ---- the choice of the columns, and paste ----

    // Verifies that a table with more than two columns asks for the columns, and the songs are the ones in the columns that were chosen.
    [Fact]
    public void Import_OfAWideTable_AsksForTheColumns_AndReadsTheChosenOnes()
    {
        string path = Path.Combine(directory, "wide.csv");
        File.WriteAllText(path, "Nr;Note;Title;Artist\r\n1;x;Song A;Artist A\r\n2;y;Song B;Artist B\r\n");
        view.ColumnChoice = new ImportColumnChoice(TitleColumn: 3, ArtistColumn: 2, HasHeader: true);

        IReadOnlyList<TrackedSong>? merged = CreateController().ImportSongs(path, Array.Empty<TrackedSong>());

        // (The user swapped the two on purpose: what was chosen is what is read.)
        Assert.Equal(new[] { new TrackedSong("Artist A", "Song A"), new TrackedSong("Artist B", "Song B") }, merged);
        Assert.True(view.Has("ChooseImportColumns:4"));
    }

    // Verifies that the proposal of the program is what the dialog starts with: a numbering column in front is skipped.
    [Fact]
    public void Import_OfAWideTable_ProposesTheColumnsWithoutTheNumbering()
    {
        string path = Path.Combine(directory, "numbered.csv");
        File.WriteAllText(path, "1;Song A;Artist A\r\n2;Song B;Artist B\r\n");

        IReadOnlyList<TrackedSong>? merged = CreateController().ImportSongs(path, Array.Empty<TrackedSong>());

        Assert.Equal(new[] { new TrackedSong("Song A", "Artist A"), new TrackedSong("Song B", "Artist B") }, merged);
        Assert.Equal(new ImportColumnChoice(1, 2, false), AskedGuess());
    }

    private ImportColumnChoice AskedGuess() => view.AskedTable!.GuessColumns();

    // Verifies that a table of two columns is read without asking.
    [Fact]
    public void Import_OfATwoColumnTable_DoesNotAsk()
    {
        string path = Path.Combine(directory, "two.csv");
        File.WriteAllText(path, "Title;Artist\r\nSong A;Artist A\r\n");

        IReadOnlyList<TrackedSong>? merged = CreateController().ImportSongs(path, Array.Empty<TrackedSong>());

        Assert.Single(merged!);
        Assert.False(view.Has("ChooseImportColumns"));
    }

    // Verifies that Cancel in the dialog leaves the list alone, with no message.
    [Fact]
    public void Import_CancelledInTheColumnDialog_ChangesNothing()
    {
        string path = Path.Combine(directory, "wide.csv");
        File.WriteAllText(path, "1;Song A;Artist A\r\n");
        view.CancelColumnChoice = true;

        IReadOnlyList<TrackedSong>? merged = CreateController().ImportSongs(path, new[] { new TrackedSong("Keep", "Me") });

        Assert.Null(merged);
        Assert.False(view.Has("SetStatus:"));
        Assert.False(view.Has("ShowInfo:"));
        Assert.False(view.Has("ShowError:"));
    }

    // Verifies that an Excel table is read like the other tables.
    [Fact]
    public void Import_OfAnExcelTable_Works()
    {
        string path = Path.Combine(directory, "wide.xlsx");
        TrackedSongsFormat format = TrackedSongsFormat.Xlsx;
        File.WriteAllBytes(path, TrackedSongsTransfer.Write(new[] { new TrackedSong("Song A", "Artist A") }, format));

        IReadOnlyList<TrackedSong>? merged = CreateController().ImportSongs(path, Array.Empty<TrackedSong>());

        Assert.Equal(new[] { new TrackedSong("Song A", "Artist A") }, merged);
    }

    // Verifies that a table copied from Excel or Word (tabs, one line per row, a numbering column) can be pasted.
    [Fact]
    public void Paste_OfACopiedTable_AsksForTheColumns_AndAddsTheSongs()
    {
        IReadOnlyList<TrackedSong>? merged = CreateController().PasteSongs(
            "Nr\tTitle\tArtist\r\n1\tSong A\tArtist A\r\n2\tSong B\tArtist B\r\n",
            new[] { new TrackedSong("Old", "One") });

        Assert.Equal(
            new[] { new TrackedSong("Old", "One"), new TrackedSong("Song A", "Artist A"), new TrackedSong("Song B", "Artist B") },
            merged);
        Assert.True(view.Has("ChooseImportColumns:3"));
        Assert.True(view.Has("SetStatus:"));
    }

    // Verifies that songs that are in the list already are not added again by a paste.
    [Fact]
    public void Paste_DoesNotAddDuplicates()
    {
        IReadOnlyList<TrackedSong>? merged = CreateController().PasteSongs(
            "song a\tARTIST A\nSong B\tArtist B",
            new[] { new TrackedSong("Song A", "Artist A") });

        Assert.Equal(2, merged!.Count);
        Assert.False(view.Has("ChooseImportColumns"));
    }

    // Verifies that an empty clipboard, or text without songs, is reported and changes nothing.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    [InlineData("just one word")]
    [InlineData("a\t")]
    public void Paste_WithoutSongs_SaysSo(string? clipboard)
    {
        IReadOnlyList<TrackedSong>? merged = CreateController().PasteSongs(clipboard, new[] { new TrackedSong("Keep", "Me") });

        Assert.Null(merged);
        Assert.True(view.Has("ShowInfo:"));
    }

    // Verifies that a file that is not what its ending says (a text file called .xlsx) is reported, and the list is left alone.
    [Theory]
    [InlineData("songs.xlsx")]
    [InlineData("songs.docx")]
    public void Import_OfAFakeOfficeFile_ShowsAnError(string fileName)
    {
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, "this is only text");

        IReadOnlyList<TrackedSong>? merged = CreateController().ImportSongs(path, new[] { new TrackedSong("Keep", "Me") });

        Assert.Null(merged);
        Assert.True(view.Has("ShowError:"));
    }

    // Verifies that a file that is not there is reported.
    [Fact]
    public void Import_OfAMissingFile_ShowsAnError()
    {
        IReadOnlyList<TrackedSong>? merged = CreateController().ImportSongs(Path.Combine(directory, "missing.json"), Array.Empty<TrackedSong>());

        Assert.Null(merged);
        Assert.True(view.Has("ShowError:"));
    }

    // Verifies that with nothing to search for the user is told, and nothing starts.
    [Fact]
    public async Task StartSearch_WithoutSongs_SaysSo_AndDoesNotRun()
    {
        await CreateController().StartSearchAsync(Array.Empty<TrackedSong>());

        Assert.True(view.Has("ShowInfo:"));
        Assert.False(view.Has("PrepareRunUi"));
        Assert.Empty(gateway.Searches);
    }

    // Verifies that rows with an empty title or artist (the grid's blank row, a half-typed song) are left out.
    [Fact]
    public async Task StartSearch_SearchesOnlyCompleteRows()
    {
        await CreateController().StartSearchAsync(new[]
        {
            new TrackedSong("Song A", "Artist A"),
            new TrackedSong("", "Artist B"),
            new TrackedSong("Song C", "  "),
            new TrackedSong("   ", "   "),
            new TrackedSong("Song D", "Artist D")
        });

        Assert.Equal(new[] { "Song A|Artist A", "Song D|Artist D" }, gateway.Searches);
        Assert.True(view.Has("ShowCompleted:0/2"));
    }

    // Verifies that only blank rows count as "nothing to search".
    [Fact]
    public async Task StartSearch_WithOnlyBlankRows_SaysSo()
    {
        await CreateController().StartSearchAsync(new[] { new TrackedSong("", ""), new TrackedSong(" ", " ") });

        Assert.True(view.Has("ShowInfo:"));
        Assert.False(view.Has("PrepareRunUi"));
    }

    // Verifies that a search that finds nothing ends as "completed" (0 of n), not as an error.
    [Fact]
    public async Task StartSearch_WhenNothingIsFound_Completes()
    {
        UnreleasedSongsController controller = CreateController();

        await controller.StartSearchAsync(new[] { new TrackedSong("Song", "Artist") });

        Assert.True(view.Has("PrepareRunUi"));
        Assert.True(view.Has("ShowCompleted:0/1"));
        Assert.False(controller.IsRunInProgress);
    }

    // Verifies that a failing search shows its message and leaves the controller ready for the next one.
    [Fact]
    public async Task StartSearch_WhenItFails_ShowsTheMessage_AndCanRunAgain()
    {
        gateway.OnSearch = (_, _, _) => throw new InvalidOperationException("search broke");
        UnreleasedSongsController controller = CreateController();

        await controller.StartSearchAsync(new[] { new TrackedSong("Song", "Artist") });

        Assert.True(view.Has("ShowRunFailed:search broke"));
        Assert.False(controller.IsRunInProgress);

        gateway.OnSearch = (_, _, _) => Task.FromResult<IReadOnlyList<SpotifyTrackItem>>(Array.Empty<SpotifyTrackItem>());
        await controller.StartSearchAsync(new[] { new TrackedSong("Song", "Artist") });

        Assert.True(view.Has("ShowCompleted"));
    }

    // Verifies that Spotify's request limit gets a message that says how long to wait (rounded up,
    // at least one minute), not the raw technical text.
    [Theory]
    [InlineData(20 * 60, 20)]
    [InlineData(61, 2)]
    [InlineData(60, 1)]
    [InlineData(10, 1)]
    [InlineData(0, 1)]
    public async Task StartSearch_WhenRateLimited_SaysHowLongToWait(int retryAfterSeconds, int expectedMinutes)
    {
        gateway.OnSearch = (_, _, _) => throw new SpotifyRateLimitException("GET", "search", TimeSpan.FromSeconds(retryAfterSeconds), "slow down");
        UnreleasedSongsController controller = CreateController();

        await controller.StartSearchAsync(new[] { new TrackedSong("Song", "Artist") });

        Assert.Contains(
            "ShowRunFailed:" + ErikwnkCore.AppLocalization.Get(
                ReleaseUpdater.Core.Localization.AppStrings.UnreleasedRateLimitedFormat,
                expectedMinutes),
            view.Calls);
        Assert.False(controller.IsRunInProgress);
    }

    // Verifies that cancelling ends the search as "cancelled".
    [Fact]
    public async Task CancelSearch_EndsTheSearchAsCancelled()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.OnSearch = async (_, _, cancellationToken) =>
        {
            started.TrySetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
            return Array.Empty<SpotifyTrackItem>();
        };
        UnreleasedSongsController controller = CreateController();

        Task run = controller.StartSearchAsync(new[] { new TrackedSong("Song", "Artist") });
        await started.Task.WaitAsync(Timeout);
        Assert.True(controller.IsRunInProgress);
        controller.CancelSearch();
        await run.WaitAsync(Timeout);

        Assert.True(view.Has("ShowCancelled"));
        Assert.False(view.Has("ShowRunFailed"));
        Assert.False(controller.IsRunInProgress);
    }

    // Verifies that cancelling with nothing running does nothing.
    [Fact]
    public void CancelSearch_WithoutARun_DoesNothing()
    {
        CreateController().CancelSearch();

        Assert.Empty(view.Calls);
    }

    // Verifies the close-during-a-search case: once the window is closing, a search that ends
    // (cancelled, failed or finished) must not touch it - a message box owned by a closed window would throw.
    [Theory]
    [InlineData("fail")]
    [InlineData("cancel")]
    [InlineData("finish")]
    public async Task WhenTheWindowIsShuttingDown_ASearchThatEndsTouchesNothing(string ending)
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.OnSearch = async (_, _, _) =>
        {
            started.TrySetResult();
            await release.Task;

            return ending switch
            {
                "fail" => throw new InvalidOperationException("boom"),
                "cancel" => throw new OperationCanceledException(),
                _ => Array.Empty<SpotifyTrackItem>()
            };
        };
        UnreleasedSongsController controller = CreateController();

        Task run = controller.StartSearchAsync(new[] { new TrackedSong("Song", "Artist") });
        await started.Task.WaitAsync(Timeout);

        view.IsShuttingDown = true;
        int callsBefore = view.Calls.Count;
        release.SetResult();
        await run.WaitAsync(Timeout);

        Assert.Equal(callsBefore, view.Calls.Count);
        Assert.False(controller.IsRunInProgress);
    }

    // Verifies that the song list is saved and loaded through the store.
    [Fact]
    public void SaveSongs_ThenLoadSongs_RoundTrips()
    {
        UnreleasedSongsController controller = CreateController();
        TrackedSong[] songs = { new("A", "B"), new("C", "D") };

        controller.SaveSongs(songs);

        Assert.Equal(songs, controller.LoadSongs());
    }

    // Verifies that "open report" without a report file says so instead of failing.
    [Fact]
    public void OpenReport_WithoutAReport_SaysSo()
    {
        CreateController().OpenReport();

        Assert.True(view.Has("ShowInfo:"));
        Assert.False(view.Has("ShowError"));
    }
}
