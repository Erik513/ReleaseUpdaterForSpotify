using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Data.Storage;

namespace ReleaseUpdater.Core.Tests;

public sealed class TrackedSongsStoreTests
{
    // Verifies that saved songs survive a round trip through the JSON file.
    [Fact]
    public void SaveThenLoad_RoundTripsTrackedSongs()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            TrackedSongsStore store = new(new AppDataPaths(directory));
            TrackedSong[] songs =
            {
                new("Blinding Lights", "The Weeknd"),
                new("Don't Stop Me Now", "Queen")
            };

            store.Save(songs);
            IReadOnlyList<TrackedSong> loaded = store.Load();

            Assert.Equal(songs, loaded);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // Verifies that loading before anything was ever saved returns an empty list
    // instead of throwing.
    [Fact]
    public void Load_WithNoFileYet_ReturnsEmptyList()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            TrackedSongsStore store = new(new AppDataPaths(directory));

            Assert.Empty(store.Load());
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
