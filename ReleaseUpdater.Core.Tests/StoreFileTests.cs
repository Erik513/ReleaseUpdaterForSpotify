using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Data.Storage;

namespace ReleaseUpdater.Core.Tests;

// The three JSON stores share one way of failing (a missing, empty or damaged file is not an
// error, it is "nothing saved yet") and one way of writing (a temp file, then a replace).
public sealed class StoreFileTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "SpotifyReleaseTests",
        Guid.NewGuid().ToString("N"));

    private AppDataPaths Paths => new(directory);

    public StoreFileTests()
    {
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ---- JsonSettingsStore ----

    // Verifies that a first start (no settings file yet) gives the defaults.
    [Fact]
    public void Settings_Load_WithNoFile_ReturnsTheDefaults()
    {
        ReleaseSettings settings = new JsonSettingsStore(Paths).Load();

        Assert.Equal(ReleaseDefaults.PlaylistName, settings.PlaylistName);
        Assert.Equal(ReleaseDefaults.ReleaseLookbackDays, settings.ReleaseLookbackDays);
        Assert.False(settings.HasSpotifyClientId);
    }

    // Verifies that an empty or blank settings file (a write that was cut off) gives the defaults.
    [Theory]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    public void Settings_Load_WithEmptyFile_ReturnsTheDefaults(string content)
    {
        File.WriteAllText(Paths.SettingsPath, content);

        ReleaseSettings settings = new JsonSettingsStore(Paths).Load();

        Assert.Equal(ReleaseDefaults.PlaylistName, settings.PlaylistName);
    }

    // Verifies that a damaged settings file does not stop the app from starting.
    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("null")]
    [InlineData("{\"release_lookback_days\": \"many\"}")]
    public void Settings_Load_WithDamagedFile_ReturnsTheDefaults(string content)
    {
        File.WriteAllText(Paths.SettingsPath, content);

        ReleaseSettings settings = new JsonSettingsStore(Paths).Load();

        Assert.Equal(ReleaseDefaults.PlaylistName, settings.PlaylistName);
        Assert.Equal(ReleaseDefaults.ReleaseLookbackDays, settings.ReleaseLookbackDays);
    }

    // Verifies that what is saved comes back, with the limits applied (a hand-edited
    // file with 99 lookback days must not get past the maximum).
    [Fact]
    public void Settings_SaveThenLoad_RoundTrips_AndNormalizes()
    {
        JsonSettingsStore store = new(Paths);

        store.Save(new ReleaseSettings
        {
            PlaylistName = "  My releases  ",
            ReleaseLookbackDays = 99,
            CustomSpotifyClientId = "  ABCDEF0123456789ABCDEF0123456789  ",
            Language = "german"
        });

        ReleaseSettings loaded = store.Load();

        Assert.Equal("My releases", loaded.PlaylistName);
        Assert.Equal(ReleaseDefaults.MaxReleaseLookbackDays, loaded.ReleaseLookbackDays);
        Assert.Equal("abcdef0123456789abcdef0123456789", loaded.CustomSpotifyClientId);
        Assert.Equal("German", loaded.Language);
    }

    // Verifies the file format the app has always written (snake case), since an update must read old files.
    [Fact]
    public void Settings_Save_WritesSnakeCaseNames()
    {
        new JsonSettingsStore(Paths).Save(new ReleaseSettings { PlaylistName = "x" });

        string json = File.ReadAllText(Paths.SettingsPath);

        Assert.Contains("\"playlist_name\"", json);
        Assert.Contains("\"release_lookback_days\"", json);
    }

    // Verifies that saving twice (the second time replaces an existing file) works and leaves no temp file.
    [Fact]
    public void Settings_SaveTwice_ReplacesTheFile_AndLeavesNoTempFile()
    {
        JsonSettingsStore store = new(Paths);

        store.Save(new ReleaseSettings { PlaylistName = "first" });
        store.Save(new ReleaseSettings { PlaylistName = "second" });

        Assert.Equal("second", store.Load().PlaylistName);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    // Verifies that a save that fails (here: the target is a folder) reports the error and does
    // not leave its temp file behind.
    [Fact]
    public void Settings_Save_WhenTheWriteFails_Throws_AndRemovesTheTempFile()
    {
        Directory.CreateDirectory(Paths.SettingsPath);

        JsonSettingsStore store = new(Paths);

        Assert.ThrowsAny<IOException>(() => store.Save(new ReleaseSettings()));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    // ---- FileTokenStore ----

    // Verifies that "not signed in yet" is null, not an error.
    [Fact]
    public void Token_Load_WithNoFile_ReturnsNull()
    {
        Assert.Null(new FileTokenStore(Paths).Load());
    }

    // Verifies that a damaged token file means "not signed in" instead of a crash at start-up.
    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("42")]
    public void Token_Load_WithDamagedFile_ReturnsNull(string content)
    {
        File.WriteAllText(Paths.TokenPath, content);

        Assert.Null(new FileTokenStore(Paths).Load());
    }

    // Verifies that every field of the token comes back, including the client it belongs to.
    [Fact]
    public void Token_SaveThenLoad_RoundTripsEveryField()
    {
        FileTokenStore store = new(Paths);
        DateTimeOffset expires = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

        store.Save(new SpotifyToken
        {
            AccessToken = "access",
            RefreshToken = "refresh",
            ExpiresAtUtc = expires,
            Scope = "playlist-read-private user-follow-read",
            ClientId = "client"
        });

        SpotifyToken? loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal("access", loaded!.AccessToken);
        Assert.Equal("refresh", loaded.RefreshToken);
        Assert.Equal(expires, loaded.ExpiresAtUtc);
        Assert.Equal("playlist-read-private user-follow-read", loaded.Scope);
        Assert.Equal("client", loaded.ClientId);
    }

    // Verifies that a refreshed token replaces the old one (the replace path of the atomic write).
    [Fact]
    public void Token_SaveTwice_KeepsTheLatest()
    {
        FileTokenStore store = new(Paths);

        store.Save(new SpotifyToken { AccessToken = "old" });
        store.Save(new SpotifyToken { AccessToken = "new" });

        Assert.Equal("new", store.Load()!.AccessToken);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    // Verifies that signing out removes the file, and that signing out twice is not an error.
    [Fact]
    public void Token_Clear_RemovesTheFile_AndIsSafeToRepeat()
    {
        FileTokenStore store = new(Paths);
        store.Save(new SpotifyToken { AccessToken = "access" });

        store.Clear();
        store.Clear();

        Assert.Null(store.Load());
        Assert.False(File.Exists(Paths.TokenPath));
    }

    // ---- TrackedSongsStore (what the two tests in TrackedSongsStoreTests do not cover) ----

    // Verifies that a damaged song list gives an empty list instead of losing the window.
    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("{\"title\": \"x\"}")]
    public void Songs_Load_WithDamagedFile_ReturnsAnEmptyList(string content)
    {
        File.WriteAllText(Paths.UnreleasedSongsPath, content);

        Assert.Empty(new TrackedSongsStore(Paths).Load());
    }

    // Verifies that titles with quotes, unicode and line breaks survive the file.
    [Fact]
    public void Songs_SaveThenLoad_KeepsUnusualText()
    {
        TrackedSongsStore store = new(Paths);
        TrackedSong[] songs =
        {
            new("Say \"Hello\"", "Sigur Rós"),
            new("東京", "坂本龍一"),
            new("Line\nbreak", "A & B <C>")
        };

        store.Save(songs);

        Assert.Equal(songs, store.Load());
    }

    // Verifies that saving an empty list is a valid state (the user deleted every row).
    [Fact]
    public void Songs_SaveEmptyList_LoadsAsEmpty()
    {
        TrackedSongsStore store = new(Paths);
        store.Save(new[] { new TrackedSong("a", "b") });

        store.Save(Array.Empty<TrackedSong>());

        Assert.Empty(store.Load());
    }

    // Verifies that a failing save of the song list reports the error and leaves no temp file.
    [Fact]
    public void Songs_Save_WhenTheWriteFails_Throws_AndRemovesTheTempFile()
    {
        Directory.CreateDirectory(Paths.UnreleasedSongsPath);

        TrackedSongsStore store = new(Paths);

        Assert.ThrowsAny<IOException>(() => store.Save(new[] { new TrackedSong("a", "b") }));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }
}
