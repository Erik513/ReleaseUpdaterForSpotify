using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Core.Services;

namespace ReleaseUpdater.Core.Tests;

public sealed class UnreleasedSongCheckerTests
{
    // Verifies that a title+artist match (after normalization) is reported as found.
    [Fact]
    public async Task RunAsync_ReportsConfidentMatchAsFound()
    {
        FakeGateway gateway = new();
        gateway.AddResult(
            "Blinding Lights",
            "The Weeknd",
            new SpotifyTrackItem(
                "track-1",
                "Blinding Lights",
                new[] { new SpotifyArtist("artist-1", "The Weeknd") },
                "spotify:track:1",
                "https://open.spotify.com/track/1",
                new SpotifyAlbum("album-1", "After Hours", new DateOnly(2020, 3, 20))));

        FakeReportWriter reportWriter = new();
        UnreleasedSongChecker checker = new(gateway, reportWriter);

        UnreleasedSongsCheckResult result = await checker.RunAsync(
            new[] { new TrackedSong("Blinding Lights", "The Weeknd") },
            null,
            CancellationToken.None);

        Assert.Equal(1, result.CheckedCount);
        Assert.Equal(1, result.FoundCount);
        Assert.Single(reportWriter.LastResults);
        Assert.Equal(MatchConfidence.Confident, reportWriter.LastResults[0].Confidence);
        Assert.Equal("track-1", reportWriter.LastResults[0].Match?.Id);
    }

    // Verifies that a typo in the tracked title/artist still surfaces a
    // match - just as "Similar" rather than "Confident" - instead of being
    // discarded the way an exact-match-only policy would.
    [Fact]
    public async Task RunAsync_MatchesDespiteTyposAsSimilar()
    {
        FakeGateway gateway = new();
        gateway.AddResult(
            "Blinding Lightz",
            "The Weeknf",
            new SpotifyTrackItem(
                "track-typo",
                "Blinding Lights",
                new[] { new SpotifyArtist("artist-1", "The Weeknd") },
                "spotify:track:typo",
                "https://open.spotify.com/track/typo",
                null));

        FakeReportWriter reportWriter = new();
        UnreleasedSongChecker checker = new(gateway, reportWriter);

        UnreleasedSongsCheckResult result = await checker.RunAsync(
            new[] { new TrackedSong("Blinding Lightz", "The Weeknf") },
            null,
            CancellationToken.None);

        Assert.Equal(1, result.FoundCount);
        Assert.Equal(MatchConfidence.Similar, reportWriter.LastResults[0].Confidence);
        Assert.Equal("track-typo", reportWriter.LastResults[0].Match?.Id);
    }

    // Verifies that a title/artist too different from any candidate still
    // isn't matched - fuzzy tolerance has a limit.
    [Fact]
    public async Task RunAsync_StillRejectsUnrelatedTitles()
    {
        FakeGateway gateway = new();
        gateway.AddResult(
            "Completely Different Song",
            "Totally Different Artist",
            new SpotifyTrackItem(
                "track-unrelated",
                "Blinding Lights",
                new[] { new SpotifyArtist("artist-1", "The Weeknd") },
                "spotify:track:unrelated",
                "https://open.spotify.com/track/unrelated",
                null));

        FakeReportWriter reportWriter = new();
        UnreleasedSongChecker checker = new(gateway, reportWriter);

        UnreleasedSongsCheckResult result = await checker.RunAsync(
            new[] { new TrackedSong("Completely Different Song", "Totally Different Artist") },
            null,
            CancellationToken.None);

        Assert.Equal(0, result.FoundCount);
        Assert.Equal(MatchConfidence.None, reportWriter.LastResults[0].Confidence);
    }

    // Verifies that punctuation/casing differences don't block an otherwise-correct match.
    [Fact]
    public async Task RunAsync_MatchesDespiteCasingAndPunctuationDifferences()
    {
        FakeGateway gateway = new();
        gateway.AddResult(
            "don't stop me now",
            "queen",
            new SpotifyTrackItem(
                "track-2",
                "Don't Stop Me Now",
                new[] { new SpotifyArtist("artist-2", "Queen") },
                "spotify:track:2",
                "https://open.spotify.com/track/2",
                null));

        FakeReportWriter reportWriter = new();
        UnreleasedSongChecker checker = new(gateway, reportWriter);

        UnreleasedSongsCheckResult result = await checker.RunAsync(
            new[] { new TrackedSong("don't stop me now", "queen") },
            null,
            CancellationToken.None);

        Assert.Equal(1, result.FoundCount);
    }

    // Verifies that a candidate with the right title but a different artist is not
    // treated as a match - the matching policy requires both to line up.
    [Fact]
    public async Task RunAsync_DoesNotMatchWhenOnlyTitleAgrees()
    {
        FakeGateway gateway = new();
        gateway.AddResult(
            "Levitating",
            "Dua Lipa",
            new SpotifyTrackItem(
                "track-3",
                "Levitating",
                new[] { new SpotifyArtist("artist-3", "Some Cover Band") },
                "spotify:track:3",
                "https://open.spotify.com/track/3",
                null));

        FakeReportWriter reportWriter = new();
        UnreleasedSongChecker checker = new(gateway, reportWriter);

        UnreleasedSongsCheckResult result = await checker.RunAsync(
            new[] { new TrackedSong("Levitating", "Dua Lipa") },
            null,
            CancellationToken.None);

        Assert.Equal(0, result.FoundCount);
        Assert.Equal(MatchConfidence.None, reportWriter.LastResults[0].Confidence);
        Assert.Null(reportWriter.LastResults[0].Match);
    }

    // Verifies that no search results at all is reported as not found, not an error.
    [Fact]
    public async Task RunAsync_ReportsNotFoundWhenSearchReturnsNoResults()
    {
        FakeGateway gateway = new();
        FakeReportWriter reportWriter = new();
        UnreleasedSongChecker checker = new(gateway, reportWriter);

        UnreleasedSongsCheckResult result = await checker.RunAsync(
            new[] { new TrackedSong("Some Unreleased Track", "Some Artist") },
            null,
            CancellationToken.None);

        Assert.Equal(1, result.CheckedCount);
        Assert.Equal(0, result.FoundCount);
    }

    private sealed class FakeReportWriter : IUnreleasedSongsReportWriter
    {
        public string ReportPath => "memory-unreleased-report.html";
        public IReadOnlyList<SongMatchResult> LastResults { get; private set; } =
            Array.Empty<SongMatchResult>();

        public string WriteReport(IReadOnlyList<SongMatchResult> results)
        {
            LastResults = results.ToArray();
            return ReportPath;
        }
    }

    private sealed class FakeGateway : ISpotifyGateway
    {
        private readonly Dictionary<string, List<SpotifyTrackItem>> resultsByKey =
            new(StringComparer.Ordinal);

        public void AddResult(string title, string artist, SpotifyTrackItem track)
        {
            string key = title + "|" + artist;

            if (!resultsByKey.TryGetValue(key, out List<SpotifyTrackItem>? tracks))
            {
                tracks = new List<SpotifyTrackItem>();
                resultsByKey[key] = tracks;
            }

            tracks.Add(track);
        }

        public Task<IReadOnlyList<SpotifyTrackItem>> SearchTrackByTitleArtistAsync(
            string title,
            string artist,
            CancellationToken cancellationToken)
        {
            string key = title + "|" + artist;

            return Task.FromResult<IReadOnlyList<SpotifyTrackItem>>(
                resultsByKey.TryGetValue(key, out List<SpotifyTrackItem>? tracks)
                    ? tracks
                    : Array.Empty<SpotifyTrackItem>());
        }

        public Task<SpotifyUser> GetCurrentUserAsync(CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<SpotifyArtist>> GetFollowedArtistsAsync(
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<SpotifyAlbum>> GetArtistAlbumsAsync(
            string artistId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<SpotifyTrackItem>> GetAlbumTracksAsync(
            string albumId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<SpotifyTrackItem>> SearchTracksByArtistAsync(
            string artistName,
            int fromYear,
            int toYear,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<SpotifyPlaylist?> TryGetPlaylistAsync(
            string playlistId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<SpotifyPlaylist>> GetCurrentUserPlaylistsAsync(
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<string> CreatePlaylistAsync(
            string name,
            string description,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task UpdatePlaylistDetailsAsync(
            string playlistId,
            string name,
            string description,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task ReplacePlaylistTracksAsync(
            string playlistId,
            IReadOnlyList<string> trackUris,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task AddTracksToPlaylistAsync(
            string playlistId,
            IReadOnlyList<string> trackUris,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
