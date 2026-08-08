using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Data.Reports;
using ReleaseUpdater.Data.Storage;

namespace ReleaseUpdater.Core.Tests;

public sealed class HtmlReportWriterTests
{
    // Verifies that report dates are displayed for humans while sort data stays machine-friendly.
    [Fact]
    public void WriteReport_UsesReadableDatesAndSortableDateValues()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            HtmlReportWriter writer = new(new AppDataPaths(directory));
            ReleaseTrack track = Track(
                "Release Song",
                new[] { new SpotifyArtist("artist-1", "Artist One") },
                "Album One",
                ReleaseTrackSource.ArtistAlbums,
                new DateOnly(2026, 7, 11));

            string reportPath = writer.WriteReport(
                new[] { track },
                Array.Empty<ReleaseTrack>(),
                10);

            string html = File.ReadAllText(reportPath);

            Assert.Contains("11/07/2026", html);
            Assert.Contains("data-sort=\"2026-07-11\"", html);
            Assert.Contains("Release", html);
            Assert.Contains("https://open.spotify.com/artist/artist-1", html);
            Assert.DoesNotContain("Added from Releases", html);
            Assert.DoesNotContain("Added from song search", html);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // Verifies that user-controlled track text is HTML-encoded before it enters the report.
    [Fact]
    public void WriteReport_EncodesTrackTextAndShowsSongSearchSource()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            HtmlReportWriter writer = new(new AppDataPaths(directory));
            ReleaseTrack track = Track(
                "<Song & Title>",
                new[] { new SpotifyArtist("artist-1", "Artist <One>") },
                "Album & Friends",
                ReleaseTrackSource.Search,
                new DateOnly(2026, 7, 10));

            string reportPath = writer.WriteReport(
                new[] { track },
                Array.Empty<ReleaseTrack>(),
                10);

            string html = File.ReadAllText(reportPath);

            Assert.Contains("&lt;Song &amp; Title&gt;", html);
            Assert.Contains("Artist &lt;One&gt;", html);
            Assert.Contains("Album &amp; Friends", html);
            Assert.Contains("Song search", html);
            Assert.DoesNotContain("<Song & Title>", html);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ReleaseTrack Track(
        string title,
        IReadOnlyList<SpotifyArtist> artists,
        string album,
        ReleaseTrackSource source,
        DateOnly releaseDate) =>
        new(
            title,
            artists,
            "spotify:track:1",
            "https://open.spotify.com/track/1",
            releaseDate,
            album,
            source,
            true);

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
