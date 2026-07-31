using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Core.Services;

namespace ReleaseUpdater.Core.Tests;

public sealed class SpotifyReleaseUpdaterTests
{
    private static readonly DateOnly Today = new(2026, 7, 11);

    // Verifies the main happy path: reuse playlist, rename it, remove duplicates, and write the report.
    [Fact]
    public async Task RunAsync_ReusesSavedPlaylistAndWritesUniqueRecentTracks()
    {
        SpotifyArtist artist = Artist("artist-1", "Artist One");
        SpotifyArtist collaborator = Artist("artist-2", "Collaborator");
        SpotifyAlbum album = Album("album-1", "New Album", Today.AddDays(-1));
        SpotifyAlbum single = Album("single-1", "New Single", Today.AddDays(-2));
        FakeSpotifyGateway spotify = new();
        FakeSettingsStore settingsStore = Settings(new ReleaseSettings
        {
            PlaylistId = "playlist-1",
            PlaylistName = "Target Playlist",
            ReleaseLookbackDays = 10
        });
        FakeReportWriter reportWriter = new();
        SpotifyReleaseUpdater updater = CreateUpdater(spotify, settingsStore, reportWriter);

        spotify.FollowedArtists.AddRange(new[] { artist, collaborator });
        spotify.AddPlaylist(new SpotifyPlaylist("playlist-1", "Old Name", spotify.CurrentUser.Id));
        spotify.AlbumsByArtistId[artist.Id] = new List<SpotifyAlbum> { album };
        spotify.TracksByAlbumId[album.Id] = new List<SpotifyTrackItem>
        {
            Track("track-1", "Release Song", "spotify:track:1", null, artist)
        };
        spotify.SearchResultsByArtistName[artist.Name] = new List<SpotifyTrackItem>
        {
            Track("track-1-search", "Release Song", "spotify:track:1", album, artist)
        };
        spotify.SearchResultsByArtistName[collaborator.Name] = new List<SpotifyTrackItem>
        {
            Track("track-2", "Collab Song", "spotify:track:2", single, collaborator)
        };

        PlaylistUpdateResult result = await updater.RunAsync(
            new ProgressRecorder(),
            CancellationToken.None);

        Assert.Equal(2, result.FollowedArtistCount);
        Assert.Equal(1, result.ReleaseCount);
        Assert.Equal(3, result.TrackCount);
        Assert.Equal(2, result.UniqueTrackCount);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Equal(0, spotify.CreatePlaylistCalls);
        Assert.Equal("playlist-1", spotify.ReplacedPlaylistIds.Single());
        Assert.Equal(
            new[] { "spotify:track:1", "spotify:track:2" },
            spotify.ReplacedTrackUris.Single());
        Assert.Equal("Target Playlist", spotify.PlaylistsById["playlist-1"].Name);
        Assert.Equal(2, reportWriter.LastUniqueTracks.Count);
        Assert.Single(reportWriter.LastDuplicateTracks);
    }

    // Verifies that an existing playlist is cleared when no current songs are found.
    [Fact]
    public async Task RunAsync_ClearsExistingPlaylistWhenNoSongsAreFound()
    {
        SpotifyArtist artist = Artist("artist-1", "Artist One");
        FakeSpotifyGateway spotify = new();
        FakeSettingsStore settingsStore = Settings(new ReleaseSettings
        {
            PlaylistId = "playlist-1",
            PlaylistName = "Target Playlist",
            ReleaseLookbackDays = 10
        });
        FakeReportWriter reportWriter = new();
        SpotifyReleaseUpdater updater = CreateUpdater(spotify, settingsStore, reportWriter);

        spotify.FollowedArtists.Add(artist);
        spotify.AddPlaylist(new SpotifyPlaylist("playlist-1", "Target Playlist", spotify.CurrentUser.Id));
        spotify.AlbumsByArtistId[artist.Id] = new List<SpotifyAlbum>
        {
            Album("old-album", "Old Album", Today.AddDays(-30))
        };

        PlaylistUpdateResult result = await updater.RunAsync(
            new ProgressRecorder(),
            CancellationToken.None);

        Assert.Equal(0, result.UniqueTrackCount);
        Assert.Equal(0, spotify.CreatePlaylistCalls);
        Assert.Equal("playlist-1", spotify.ReplacedPlaylistIds.Single());
        Assert.Empty(spotify.ReplacedTrackUris.Single());
        Assert.Empty(reportWriter.LastUniqueTracks);
    }

    // Verifies that the updater creates a new playlist only when songs exist and no reusable playlist is found.
    [Fact]
    public async Task RunAsync_CreatesPlaylistWhenNoReusablePlaylistExists()
    {
        SpotifyArtist artist = Artist("artist-1", "Artist One");
        SpotifyAlbum album = Album("album-1", "New Album", Today.AddDays(-1));
        FakeSpotifyGateway spotify = new();
        FakeSettingsStore settingsStore = Settings(new ReleaseSettings
        {
            PlaylistName = "New Playlist",
            ReleaseLookbackDays = 10
        });
        SpotifyReleaseUpdater updater = CreateUpdater(
            spotify,
            settingsStore,
            new FakeReportWriter());

        spotify.FollowedArtists.Add(artist);
        spotify.AlbumsByArtistId[artist.Id] = new List<SpotifyAlbum> { album };
        spotify.TracksByAlbumId[album.Id] = new List<SpotifyTrackItem>
        {
            Track("track-1", "Release Song", "spotify:track:1", null, artist)
        };

        await updater.RunAsync(new ProgressRecorder(), CancellationToken.None);

        Assert.Equal(1, spotify.CreatePlaylistCalls);
        Assert.Equal("created-playlist-1", settingsStore.Settings.PlaylistId);
        Assert.Equal("created-playlist-1", spotify.ReplacedPlaylistIds.Single());
        Assert.Equal(new[] { "spotify:track:1" }, spotify.ReplacedTrackUris.Single());
    }

    // Verifies recovery when a saved playlist ID is stale but a matching owned playlist still exists.
    [Fact]
    public async Task RunAsync_ReusesPlaylistByNameWhenSavedPlaylistIsMissing()
    {
        SpotifyArtist artist = Artist("artist-1", "Artist One");
        SpotifyAlbum album = Album("album-1", "New Album", Today.AddDays(-1));
        FakeSpotifyGateway spotify = new();
        FakeSettingsStore settingsStore = Settings(new ReleaseSettings
        {
            PlaylistId = "missing-playlist",
            PlaylistName = "Target Playlist",
            ReleaseLookbackDays = 10
        });
        SpotifyReleaseUpdater updater = CreateUpdater(
            spotify,
            settingsStore,
            new FakeReportWriter());

        spotify.FollowedArtists.Add(artist);
        spotify.AddPlaylist(new SpotifyPlaylist("playlist-2", "Target Playlist", spotify.CurrentUser.Id));
        spotify.AlbumsByArtistId[artist.Id] = new List<SpotifyAlbum> { album };
        spotify.TracksByAlbumId[album.Id] = new List<SpotifyTrackItem>
        {
            Track("track-1", "Release Song", "spotify:track:1", null, artist)
        };

        await updater.RunAsync(new ProgressRecorder(), CancellationToken.None);

        Assert.Equal(0, spotify.CreatePlaylistCalls);
        Assert.Equal("playlist-2", settingsStore.Settings.PlaylistId);
        Assert.Equal("playlist-2", spotify.ReplacedPlaylistIds.Single());
    }

    // Verifies that old releases and search results where the followed artist is missing are ignored.
    [Fact]
    public async Task RunAsync_IgnoresOldReleasesAndUnrelatedSearchResults()
    {
        SpotifyArtist artist = Artist("artist-1", "Artist One");
        SpotifyArtist otherArtist = Artist("artist-2", "Other Artist");
        SpotifyAlbum oldAlbum = Album("old-album", "Old Album", Today.AddDays(-40));
        SpotifyAlbum recentAlbum = Album("recent-album", "Recent Album", Today.AddDays(-1));
        FakeSpotifyGateway spotify = new();
        SpotifyReleaseUpdater updater = CreateUpdater(
            spotify,
            Settings(new ReleaseSettings
            {
                PlaylistName = "Target Playlist",
                ReleaseLookbackDays = 10
            }),
            new FakeReportWriter());

        spotify.FollowedArtists.Add(artist);
        spotify.AlbumsByArtistId[artist.Id] = new List<SpotifyAlbum> { oldAlbum };
        spotify.SearchResultsByArtistName[artist.Name] = new List<SpotifyTrackItem>
        {
            Track("old-track", "Old Song", "spotify:track:old", oldAlbum, artist),
            Track("other-track", "Other Song", "spotify:track:other", recentAlbum, otherArtist)
        };

        PlaylistUpdateResult result = await updater.RunAsync(
            new ProgressRecorder(),
            CancellationToken.None);

        Assert.Equal(0, result.UniqueTrackCount);
        Assert.Equal(0, spotify.CreatePlaylistCalls);
        Assert.Empty(spotify.ReplacedPlaylistIds);
        Assert.Empty(spotify.AlbumTrackCalls);
    }

    // Verifies that adapter-level diagnostic messages are forwarded to the UI progress/log channel.
    [Fact]
    public async Task RunAsync_ForwardsProgressToSpotifyDiagnostics()
    {
        FakeSpotifyGateway spotify = new()
        {
            ReportDiagnosticOnFollowedArtistsLoad = true
        };
        SpotifyReleaseUpdater updater = CreateUpdater(
            spotify,
            Settings(new ReleaseSettings()),
            new FakeReportWriter());
        ProgressRecorder progress = new();

        await updater.RunAsync(progress, CancellationToken.None);

        Assert.Contains(
            progress.Items,
            item => item.Message == "Diagnostic message from Spotify adapter.");
    }

    // Verifies that a release exactly on the lookback cutoff date is still included.
    [Fact]
    public async Task RunAsync_IncludesReleaseOnCutoffDate()
    {
        SpotifyArtist artist = Artist("artist-1", "Artist One");
        SpotifyAlbum cutoffAlbum = Album("album-cutoff", "Cutoff Album", Today.AddDays(-10));
        FakeSpotifyGateway spotify = new();
        FakeReportWriter reportWriter = new();
        SpotifyReleaseUpdater updater = CreateUpdater(
            spotify,
            Settings(new ReleaseSettings
            {
                PlaylistName = "Target Playlist",
                ReleaseLookbackDays = 10
            }),
            reportWriter);

        spotify.FollowedArtists.Add(artist);
        spotify.AlbumsByArtistId[artist.Id] = new List<SpotifyAlbum> { cutoffAlbum };
        spotify.TracksByAlbumId[cutoffAlbum.Id] = new List<SpotifyTrackItem>
        {
            Track("track-cutoff", "Cutoff Song", "spotify:track:cutoff", null, artist)
        };

        PlaylistUpdateResult result = await updater.RunAsync(
            new ProgressRecorder(),
            CancellationToken.None);

        Assert.Equal(1, result.ReleaseCount);
        Assert.Equal(1, result.UniqueTrackCount);
        Assert.Equal("spotify:track:cutoff", spotify.ReplacedTrackUris.Single().Single());
        Assert.Equal("Cutoff Song", reportWriter.LastUniqueTracks.Single().Title);
    }

    // Verifies that Spotify albums without a full release date cannot accidentally enter the playlist.
    [Fact]
    public async Task RunAsync_IgnoresReleaseWithoutDate()
    {
        SpotifyArtist artist = Artist("artist-1", "Artist One");
        SpotifyAlbum albumWithoutDate = AlbumWithoutDate("album-no-date", "Album Without Date");
        FakeSpotifyGateway spotify = new();
        SpotifyReleaseUpdater updater = CreateUpdater(
            spotify,
            Settings(new ReleaseSettings
            {
                PlaylistName = "Target Playlist",
                ReleaseLookbackDays = 10
            }),
            new FakeReportWriter());

        spotify.FollowedArtists.Add(artist);
        spotify.AlbumsByArtistId[artist.Id] = new List<SpotifyAlbum> { albumWithoutDate };
        spotify.TracksByAlbumId[albumWithoutDate.Id] = new List<SpotifyTrackItem>
        {
            Track("track-1", "Unknown Date Song", "spotify:track:1", null, artist)
        };

        PlaylistUpdateResult result = await updater.RunAsync(
            new ProgressRecorder(),
            CancellationToken.None);

        Assert.Equal(0, result.ReleaseCount);
        Assert.Equal(0, result.UniqueTrackCount);
        Assert.Empty(spotify.AlbumTrackCalls);
        Assert.Equal(0, spotify.CreatePlaylistCalls);
    }

    // Verifies that duplicate track detection survives casing and whitespace differences.
    [Fact]
    public async Task RunAsync_DeduplicatesTracksCaseInsensitivelyAndTrimsText()
    {
        SpotifyArtist artist = Artist("artist-1", "Artist One");
        SpotifyArtist sameArtistDifferentText = Artist("artist-1", "artist one");
        SpotifyAlbum album = Album("album-1", "New Album", Today.AddDays(-1));
        FakeSpotifyGateway spotify = new();
        FakeReportWriter reportWriter = new();
        SpotifyReleaseUpdater updater = CreateUpdater(
            spotify,
            Settings(new ReleaseSettings
            {
                PlaylistName = "Target Playlist",
                ReleaseLookbackDays = 10
            }),
            reportWriter);

        spotify.FollowedArtists.Add(artist);
        spotify.AlbumsByArtistId[artist.Id] = new List<SpotifyAlbum> { album };
        spotify.TracksByAlbumId[album.Id] = new List<SpotifyTrackItem>
        {
            Track("track-1", "  Release Song  ", "spotify:track:1", null, artist)
        };
        spotify.SearchResultsByArtistName[artist.Name] = new List<SpotifyTrackItem>
        {
            Track("track-2", "release song", "spotify:track:2", album, sameArtistDifferentText)
        };

        PlaylistUpdateResult result = await updater.RunAsync(
            new ProgressRecorder(),
            CancellationToken.None);

        Assert.Equal(2, result.TrackCount);
        Assert.Equal(1, result.UniqueTrackCount);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Single(reportWriter.LastUniqueTracks);
        Assert.Single(reportWriter.LastDuplicateTracks);
    }

    // Verifies that duplicate album IDs are loaded only once even when multiple followed artists return them.
    [Fact]
    public async Task RunAsync_DeduplicatesReleasesByAlbumIdBeforeLoadingTracks()
    {
        SpotifyArtist firstArtist = Artist("artist-1", "Artist One");
        SpotifyArtist secondArtist = Artist("artist-2", "Artist Two");
        SpotifyAlbum sharedAlbum = Album("album-shared", "Shared Album", Today.AddDays(-1));
        FakeSpotifyGateway spotify = new();
        SpotifyReleaseUpdater updater = CreateUpdater(
            spotify,
            Settings(new ReleaseSettings
            {
                PlaylistName = "Target Playlist",
                ReleaseLookbackDays = 10
            }),
            new FakeReportWriter());

        spotify.FollowedArtists.AddRange(new[] { firstArtist, secondArtist });
        spotify.AlbumsByArtistId[firstArtist.Id] = new List<SpotifyAlbum> { sharedAlbum };
        spotify.AlbumsByArtistId[secondArtist.Id] = new List<SpotifyAlbum> { sharedAlbum };
        spotify.TracksByAlbumId[sharedAlbum.Id] = new List<SpotifyTrackItem>
        {
            Track("track-1", "Shared Song", "spotify:track:1", null, firstArtist, secondArtist)
        };

        PlaylistUpdateResult result = await updater.RunAsync(
            new ProgressRecorder(),
            CancellationToken.None);

        Assert.Equal(1, result.ReleaseCount);
        Assert.Equal(new[] { sharedAlbum.Id }, spotify.AlbumTrackCalls);
        Assert.Equal(new[] { "spotify:track:1" }, spotify.ReplacedTrackUris.Single());
    }

    // Verifies that a saved playlist owned by another user is never reused.
    [Fact]
    public async Task RunAsync_DoesNotReuseSavedPlaylistOwnedByAnotherUser()
    {
        SpotifyArtist artist = Artist("artist-1", "Artist One");
        SpotifyAlbum album = Album("album-1", "New Album", Today.AddDays(-1));
        FakeSpotifyGateway spotify = new();
        FakeSettingsStore settingsStore = Settings(new ReleaseSettings
        {
            PlaylistId = "foreign-playlist",
            PlaylistName = "Target Playlist",
            ReleaseLookbackDays = 10
        });
        SpotifyReleaseUpdater updater = CreateUpdater(
            spotify,
            settingsStore,
            new FakeReportWriter());

        spotify.FollowedArtists.Add(artist);
        spotify.AddPlaylist(new SpotifyPlaylist("foreign-playlist", "Target Playlist", "other-user"));
        spotify.AlbumsByArtistId[artist.Id] = new List<SpotifyAlbum> { album };
        spotify.TracksByAlbumId[album.Id] = new List<SpotifyTrackItem>
        {
            Track("track-1", "Release Song", "spotify:track:1", null, artist)
        };

        await updater.RunAsync(new ProgressRecorder(), CancellationToken.None);

        Assert.Equal(1, spotify.CreatePlaylistCalls);
        Assert.Equal("created-playlist-1", settingsStore.Settings.PlaylistId);
        Assert.Equal("created-playlist-1", spotify.ReplacedPlaylistIds.Single());
    }

    // Verifies that an empty run writes a report but does not create an empty playlist.
    [Fact]
    public async Task RunAsync_DoesNotCreatePlaylistWhenNoArtistsAreFollowed()
    {
        FakeSpotifyGateway spotify = new();
        FakeReportWriter reportWriter = new();
        SpotifyReleaseUpdater updater = CreateUpdater(
            spotify,
            Settings(new ReleaseSettings
            {
                PlaylistName = "Target Playlist",
                ReleaseLookbackDays = 10
            }),
            reportWriter);

        PlaylistUpdateResult result = await updater.RunAsync(
            new ProgressRecorder(),
            CancellationToken.None);

        Assert.Equal(0, result.FollowedArtistCount);
        Assert.Equal(0, result.UniqueTrackCount);
        Assert.Equal(0, spotify.CreatePlaylistCalls);
        Assert.Empty(spotify.ReplacedPlaylistIds);
        Assert.Empty(reportWriter.LastUniqueTracks);
    }

    private static SpotifyReleaseUpdater CreateUpdater(
        FakeSpotifyGateway spotify,
        FakeSettingsStore settingsStore,
        FakeReportWriter reportWriter)
    {
        return new SpotifyReleaseUpdater(
            spotify,
            settingsStore,
            reportWriter,
            new FakeReleaseClock(Today));
    }

    private static FakeSettingsStore Settings(ReleaseSettings settings) =>
        new(settings);

    private static SpotifyArtist Artist(string id, string name) =>
        new(id, name);

    private static SpotifyAlbum Album(string id, string name, DateOnly releaseDate) =>
        new(id, name, releaseDate);

    private static SpotifyAlbum AlbumWithoutDate(string id, string name) =>
        new(id, name, null);

    private static SpotifyTrackItem Track(
        string id,
        string title,
        string uri,
        SpotifyAlbum? album,
        params SpotifyArtist[] artists) =>
        new(id, title, artists, uri, $"https://open.spotify.com/track/{id}", album);

    private sealed class FakeSpotifyGateway : ISpotifyGateway, ISpotifyRequestDiagnostics
    {
        public SpotifyUser CurrentUser { get; } = new("user-1", "Test User");
        public List<SpotifyArtist> FollowedArtists { get; } = new();
        public Dictionary<string, List<SpotifyAlbum>> AlbumsByArtistId { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<SpotifyTrackItem>> TracksByAlbumId { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<SpotifyTrackItem>> SearchResultsByArtistName { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, SpotifyPlaylist> PlaylistsById { get; } = new(StringComparer.Ordinal);
        public List<SpotifyPlaylist> UserPlaylists { get; } = new();
        public List<string> AlbumTrackCalls { get; } = new();
        public List<string> ReplacedPlaylistIds { get; } = new();
        public List<IReadOnlyList<string>> ReplacedTrackUris { get; } = new();
        public IProgress<ReleaseProgress>? Progress { get; set; }
        public bool ReportDiagnosticOnFollowedArtistsLoad { get; set; }
        public int CreatePlaylistCalls { get; private set; }

        public void AddPlaylist(SpotifyPlaylist playlist)
        {
            PlaylistsById[playlist.Id] = playlist;
            UserPlaylists.Add(playlist);
        }

        public Task<SpotifyUser> GetCurrentUserAsync(CancellationToken cancellationToken) =>
            Task.FromResult(CurrentUser);

        public Task<IReadOnlyList<SpotifyArtist>> GetFollowedArtistsAsync(
            CancellationToken cancellationToken)
        {
            if (ReportDiagnosticOnFollowedArtistsLoad)
            {
                Progress?.Report(new ReleaseProgress(
                    string.Empty,
                    Message: "Diagnostic message from Spotify adapter."));
            }

            return Task.FromResult<IReadOnlyList<SpotifyArtist>>(FollowedArtists);
        }

        public Task<IReadOnlyList<SpotifyAlbum>> GetArtistAlbumsAsync(
            string artistId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SpotifyAlbum>>(
                AlbumsByArtistId.TryGetValue(artistId, out List<SpotifyAlbum>? albums)
                    ? albums
                    : Array.Empty<SpotifyAlbum>());
        }

        public Task<IReadOnlyList<SpotifyTrackItem>> GetAlbumTracksAsync(
            string albumId,
            CancellationToken cancellationToken)
        {
            AlbumTrackCalls.Add(albumId);

            return Task.FromResult<IReadOnlyList<SpotifyTrackItem>>(
                TracksByAlbumId.TryGetValue(albumId, out List<SpotifyTrackItem>? tracks)
                    ? tracks
                    : Array.Empty<SpotifyTrackItem>());
        }

        public Task<IReadOnlyList<SpotifyTrackItem>> SearchTracksByArtistAsync(
            string artistName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SpotifyTrackItem>>(
                SearchResultsByArtistName.TryGetValue(artistName, out List<SpotifyTrackItem>? tracks)
                    ? tracks
                    : Array.Empty<SpotifyTrackItem>());
        }

        public Task<SpotifyPlaylist?> TryGetPlaylistAsync(
            string playlistId,
            CancellationToken cancellationToken)
        {
            PlaylistsById.TryGetValue(playlistId, out SpotifyPlaylist? playlist);
            return Task.FromResult(playlist);
        }

        public Task<IReadOnlyList<SpotifyPlaylist>> GetCurrentUserPlaylistsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SpotifyPlaylist>>(UserPlaylists);

        public Task<string> CreatePlaylistAsync(
            string name,
            string description,
            CancellationToken cancellationToken)
        {
            CreatePlaylistCalls++;
            string playlistId = $"created-playlist-{CreatePlaylistCalls}";
            AddPlaylist(new SpotifyPlaylist(playlistId, name, CurrentUser.Id));

            return Task.FromResult(playlistId);
        }

        public Task UpdatePlaylistDetailsAsync(
            string playlistId,
            string name,
            string description,
            CancellationToken cancellationToken)
        {
            if (PlaylistsById.TryGetValue(playlistId, out SpotifyPlaylist? playlist))
            {
                SpotifyPlaylist updatedPlaylist = playlist with { Name = name };
                PlaylistsById[playlistId] = updatedPlaylist;
                int index = UserPlaylists.FindIndex(item => item.Id == playlistId);

                if (index >= 0)
                {
                    UserPlaylists[index] = updatedPlaylist;
                }
            }

            return Task.CompletedTask;
        }

        public Task ReplacePlaylistTracksAsync(
            string playlistId,
            IReadOnlyList<string> trackUris,
            CancellationToken cancellationToken)
        {
            ReplacedPlaylistIds.Add(playlistId);
            ReplacedTrackUris.Add(trackUris.ToArray());

            return Task.CompletedTask;
        }

        public Task AddTracksToPlaylistAsync(
            string playlistId,
            IReadOnlyList<string> trackUris,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public FakeSettingsStore(ReleaseSettings settings)
        {
            Settings = Clone(settings);
        }

        public string SettingsPath => "memory-settings.json";
        public ReleaseSettings Settings { get; private set; }

        public ReleaseSettings Load() => Clone(Settings);

        public void Save(ReleaseSettings settings)
        {
            Settings = Clone(settings);
        }

        private static ReleaseSettings Clone(ReleaseSettings settings) =>
            new()
            {
                PlaylistId = settings.PlaylistId,
                PlaylistName = settings.PlaylistName,
                ReleaseLookbackDays = settings.ReleaseLookbackDays,
                CustomSpotifyClientId = settings.CustomSpotifyClientId,
                SpotifyCooldownUntilUtc = settings.SpotifyCooldownUntilUtc,
                SpotifyCooldownClientId = settings.SpotifyCooldownClientId
            };
    }

    private sealed class FakeReportWriter : IReportWriter
    {
        public string ReportPath => "memory-report.html";
        public IReadOnlyList<ReleaseTrack> LastUniqueTracks { get; private set; } =
            Array.Empty<ReleaseTrack>();
        public IReadOnlyList<ReleaseTrack> LastDuplicateTracks { get; private set; } =
            Array.Empty<ReleaseTrack>();

        public string WriteReport(
            IReadOnlyList<ReleaseTrack> uniqueTracks,
            IReadOnlyList<ReleaseTrack> duplicateTracks,
            int releaseLookbackDays)
        {
            LastUniqueTracks = uniqueTracks.ToArray();
            LastDuplicateTracks = duplicateTracks.ToArray();

            return ReportPath;
        }
    }

    private sealed class FakeReleaseClock : IReleaseClock
    {
        public FakeReleaseClock(DateOnly today)
        {
            Today = today;
        }

        public DateOnly Today { get; }
    }

    private sealed class ProgressRecorder : IProgress<ReleaseProgress>
    {
        public List<ReleaseProgress> Items { get; } = new();

        public void Report(ReleaseProgress value)
        {
            Items.Add(value);
        }
    }
}
