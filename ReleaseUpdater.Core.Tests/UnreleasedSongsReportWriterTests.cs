using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Data.Reports;
using ReleaseUpdater.Data.Storage;

namespace ReleaseUpdater.Core.Tests;

public sealed class UnreleasedSongsReportWriterTests
{
    // Verifies that found and not-found songs land in their own sections with correct counts.
    [Fact]
    public void WriteReport_SplitsFoundAndNotFoundIntoSeparateSections()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            UnreleasedSongsReportWriter writer = new(new AppDataPaths(directory));

            SongMatchResult found = new(
                new TrackedSong("Blinding Lights", "The Weeknd"),
                new SpotifyTrackItem(
                    "track-1",
                    "Blinding Lights",
                    new[] { new SpotifyArtist("artist-1", "The Weeknd") },
                    "spotify:track:1",
                    "https://open.spotify.com/track/1",
                    new SpotifyAlbum("album-1", "After Hours", new DateOnly(2020, 3, 20))),
                MatchConfidence.Confident);

            SongMatchResult notFound = new(
                new TrackedSong("<Unreleased> & Song", "Some Artist"),
                null,
                MatchConfidence.None);

            string reportPath = writer.WriteReport(new[] { found, notFound });
            string html = File.ReadAllText(reportPath);

            Assert.Contains("<h2>Found on Spotify <span>1</span></h2>", html);
            Assert.Contains("<h2>Not found <span>1</span></h2>", html);
            Assert.Contains(
                "<a class=\"track-link\" href=\"https://open.spotify.com/track/1\" target=\"_blank\" rel=\"noopener\">Blinding Lights</a>",
                html);
            Assert.Contains("&lt;Unreleased&gt; &amp; Song", html);
            Assert.DoesNotContain("<Unreleased> & Song", html);
            Assert.Contains("<span class=\"source-badge confidence-confident\">Confident</span>", html);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // Verifies that hints get a section of their own with the entry of the user next to the track, and are not counted as found;
    // that a swapped match says so; and that without hints there is no such section.
    [Fact]
    public void WriteReport_ShowsPossibleMatchesInTheirOwnSection()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            UnreleasedSongsReportWriter writer = new(new AppDataPaths(directory));
            SpotifyTrackItem track = new(
                "t",
                "Sammy In My Room",
                new[] { new SpotifyArtist("a", "Somebody Else") },
                "spotify:track:t",
                "https://open.spotify.com/track/t",
                null);

            SongMatchResult swapped = new(new TrackedSong("The Weeknd", "Blinding Lights"), track, MatchConfidence.Similar, MatchHint.SwappedFields);
            SongMatchResult titleOnly = new(new TrackedSong("Sammy In My Room", "David <Mackay>"), track, MatchConfidence.Partial, MatchHint.TitleOnly);
            SongMatchResult artistOnly = new(new TrackedSong("Time And Trust", "Naomi Sharon"), track, MatchConfidence.Partial, MatchHint.ArtistOnly);
            SongMatchResult nothing = new(new TrackedSong("Nothing", "Nobody"), null, MatchConfidence.None);

            string html = File.ReadAllText(writer.WriteReport(new[] { swapped, titleOnly, artistOnly, nothing }));

            Assert.Contains("<h2>Found on Spotify <span>1</span></h2>", html);
            Assert.Contains("<h2>Possible matches <span>2</span></h2>", html);
            Assert.Contains("<h2>Not found <span>1</span></h2>", html);
            Assert.Contains("Similar - title and artist swapped", html);
            Assert.Contains("Title matches", html);
            Assert.Contains("Artist matches", html);
            Assert.Contains("Your entry: Sammy In My Room - David &lt;Mackay&gt;", html);
            Assert.DoesNotContain("David <Mackay>", html);

            string without = File.ReadAllText(writer.WriteReport(new[] { nothing }));

            Assert.DoesNotContain("Possible matches", without);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "SpotifyReleaseTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        return directory;
    }
}
