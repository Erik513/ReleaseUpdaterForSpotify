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
