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

    // ---- artist matching: whole words, not any substring ----

    // Verifies that an artist credited only by a short fragment of the tracked name is not
    // taken for the same artist ("Lex", "A" inside "Alex") - with the same title, that
    // used to come out as a confident match.
    [Theory]
    [InlineData("Alex", "Lex")]
    [InlineData("Alex", "A")]
    [InlineData("Alexander", "Alex")]
    [InlineData("Lex", "Alex")]
    [InlineData("Ke", "Ke$ha")]
    public async Task Artist_PartOfAWord_IsNotAMatch(string trackedArtist, string spotifyArtist)
    {
        SongMatchResult result = await CheckAsync("Same Title", trackedArtist, "Same Title", spotifyArtist);

        Assert.Equal(MatchConfidence.None, result.Confidence);
    }

    // Verifies that whole words still match in both directions, as before ("The Weeknd" and "Weeknd").
    [Theory]
    [InlineData("The Weeknd", "Weeknd")]
    [InlineData("Weeknd", "The Weeknd")]
    [InlineData("Dua Lipa", "Dua Lipa")]
    [InlineData("AC/DC", "ACDC")]
    [InlineData("Florence", "Florence + the Machine")]
    public async Task Artist_WholeWords_AreAMatch(string trackedArtist, string spotifyArtist)
    {
        SongMatchResult result = await CheckAsync("Same Title", trackedArtist, "Same Title", spotifyArtist);

        Assert.Equal(MatchConfidence.Confident, result.Confidence);
    }

    // Verifies that any one of several credited artists is enough.
    [Fact]
    public async Task Artist_AnyCreditedArtist_CanMatch()
    {
        SongMatchResult result = await CheckAsync(
            "Same Title", "Playboi Carti", "Same Title", "The Weeknd", "Playboi Carti");

        Assert.Equal(MatchConfidence.Confident, result.Confidence);
    }

    // ---- title matching: extras and symbols ----

    // Verifies that an extra on Spotify's title that the tracked one lacks (or the other way
    // round) still finds the song - as "Similar", never "Confident", so it gets a manual glance.
    [Theory]
    [InlineData("Song", "Song (feat. Bob)")]
    [InlineData("Song", "Song feat. Bob")]
    [InlineData("Song", "Song ft. Bob")]
    [InlineData("Song", "Song - Remastered 2011")]
    [InlineData("Song", "Song [Live]")]
    [InlineData("Song (feat. Bob)", "Song")]
    [InlineData("Song - Radio Edit", "Song")]
    public async Task Title_WithAnExtra_IsFoundAsSimilar(string trackedTitle, string spotifyTitle)
    {
        SongMatchResult result = await CheckAsync(trackedTitle, "Artist", spotifyTitle, "Artist");

        Assert.Equal(MatchConfidence.Similar, result.Confidence);
    }

    // Verifies that dropping the extras does not make different songs the same: a title that
    // only starts like another ("Dancing with Myself" / "Dancing"), or a different bracketed
    // word, is not a match.
    [Theory]
    [InlineData("Dancing with Myself", "Dancing")]
    [InlineData("Dancing", "Dancing with Myself")]
    [InlineData("Love", "Lovesong (Live)")]
    [InlineData("Hello", "Goodbye (Hello)")]
    public async Task Title_ThatOnlyStartsAlike_IsNotAMatch(string trackedTitle, string spotifyTitle)
    {
        SongMatchResult result = await CheckAsync(trackedTitle, "Artist", spotifyTitle, "Artist");

        Assert.Equal(MatchConfidence.None, result.Confidence);
    }

    // Verifies that a title that is nothing but a bracketed part ("(Untitled)") is compared
    // as it is and can still be found.
    [Fact]
    public async Task Title_ThatIsOnlyBracketed_StillMatchesItself()
    {
        SongMatchResult result = await CheckAsync("(Untitled)", "Artist", "(Untitled)", "Artist");

        Assert.Equal(MatchConfidence.Confident, result.Confidence);
    }

    // Verifies that a title or artist made only of symbols (a band called "!!!") can be
    // matched, instead of silently never being found.
    [Fact]
    public async Task SymbolOnlyNames_CanBeMatched()
    {
        SongMatchResult result = await CheckAsync("Me and Giuliani", "!!!", "Me and Giuliani", "!!!");

        Assert.Equal(MatchConfidence.Confident, result.Confidence);
    }

    // Verifies that two different symbol-only names are still different.
    [Fact]
    public async Task SymbolOnlyNames_ThatDiffer_AreNotAMatch()
    {
        SongMatchResult result = await CheckAsync("Song", "!!!", "Song", "???");

        Assert.Equal(MatchConfidence.None, result.Confidence);
    }

    // Verifies that non-Latin names match themselves (they have no spaces, so the whole name is one word).
    [Fact]
    public async Task NonLatinNames_AreMatched()
    {
        SongMatchResult result = await CheckAsync("ラムのラブソング", "大滝詩一", "ラムのラブソング", "大滝詩一");

        Assert.Equal(MatchConfidence.Confident, result.Confidence);
    }

    // Verifies that a blank tracked title or artist finds nothing (and does not throw).
    [Theory]
    [InlineData("", "Artist")]
    [InlineData("Song", "")]
    [InlineData("   ", "   ")]
    public async Task BlankTrackedSong_FindsNothing(string title, string artist)
    {
        SongMatchResult result = await CheckAsync(title, artist, "Song", "Artist");

        Assert.Equal(MatchConfidence.None, result.Confidence);
    }

    // Verifies that, among several candidates, the exact one wins over a closer-looking-but-
    // extended one regardless of the order Spotify returned them in.
    [Fact]
    public async Task BestCandidate_IsTheExactOne_InAnyOrder()
    {
        foreach (bool exactFirst in new[] { true, false })
        {
            FakeGateway gateway = new();
            SpotifyTrackItem exact = Track("exact", "Song", "Artist");
            SpotifyTrackItem extended = Track("extended", "Song (feat. Bob)", "Artist");

            foreach (SpotifyTrackItem item in exactFirst ? new[] { exact, extended } : new[] { extended, exact })
            {
                gateway.AddResult("Song", "Artist", item);
            }

            FakeReportWriter writer = new();
            await new UnreleasedSongChecker(gateway, writer).RunAsync(
                new[] { new TrackedSong("Song", "Artist") }, null, CancellationToken.None);

            Assert.Equal("exact", writer.LastResults[0].Match?.Id);
            Assert.Equal(MatchConfidence.Confident, writer.LastResults[0].Confidence);
        }
    }

    // Verifies that a cancelled check stops with an OperationCanceledException and writes no report.
    [Fact]
    public async Task RunAsync_WhenCancelled_Throws_AndWritesNoReport()
    {
        FakeReportWriter writer = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new UnreleasedSongChecker(new FakeGateway(), writer).RunAsync(
                new[] { new TrackedSong("Song", "Artist") }, null, cancellation.Token));

        Assert.Empty(writer.LastResults);
    }

    private static SpotifyTrackItem Track(string id, string title, params string[] artists) =>
        new(
            id,
            title,
            artists.Select((name, index) => new SpotifyArtist("artist-" + index, name)).ToArray(),
            "spotify:track:" + id,
            "https://open.spotify.com/track/" + id,
            null);

    // One tracked song against one Spotify candidate; returns what the checker decided.
    private static async Task<SongMatchResult> CheckAsync(
        string trackedTitle,
        string trackedArtist,
        string spotifyTitle,
        params string[] spotifyArtists)
    {
        FakeGateway gateway = new();
        gateway.AddResult(trackedTitle, trackedArtist, Track("candidate", spotifyTitle, spotifyArtists));

        FakeReportWriter writer = new();
        await new UnreleasedSongChecker(gateway, writer).RunAsync(
            new[] { new TrackedSong(trackedTitle, trackedArtist) },
            null,
            CancellationToken.None);

        return Assert.Single(writer.LastResults);
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
