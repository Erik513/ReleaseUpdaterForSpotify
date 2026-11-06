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

    private UnreleasedSongsController CreateController() =>
        new(view, new TrackedSongsStore(new AppDataPaths(directory)), new UnreleasedSongChecker(gateway, reportWriter), reportWriter);

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
