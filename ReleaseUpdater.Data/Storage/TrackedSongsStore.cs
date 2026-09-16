using System.Text.Json;
using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Data.Storage;

/// <summary>
/// Persists the user's hand-entered list of not-yet-released songs across
/// sessions - a separate file from settings.json, since this list is its
/// own kind of data (tracked songs, not app configuration) with its own
/// lifecycle (edited in the unreleased-songs window, saved when it closes).
/// </summary>
public sealed class TrackedSongsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly AppDataPaths paths;

    public TrackedSongsStore(AppDataPaths paths)
    {
        this.paths = paths;
    }

    public IReadOnlyList<TrackedSong> Load()
    {
        paths.EnsureDirectory();

        try
        {
            if (!File.Exists(paths.UnreleasedSongsPath))
            {
                return Array.Empty<TrackedSong>();
            }

            string json = File.ReadAllText(paths.UnreleasedSongsPath);

            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<TrackedSong>();
            }

            List<TrackedSong>? songs =
                JsonSerializer.Deserialize<List<TrackedSong>>(json, JsonOptions);

            return songs ?? new List<TrackedSong>();
        }
        catch
        {
            return Array.Empty<TrackedSong>();
        }
    }

    public void Save(IReadOnlyList<TrackedSong> songs)
    {
        paths.EnsureDirectory();

        string json = JsonSerializer.Serialize(songs, JsonOptions);
        AtomicFile.WriteAllText(paths.UnreleasedSongsPath, json);
    }
}
