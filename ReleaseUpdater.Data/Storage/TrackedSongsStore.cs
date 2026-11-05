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
    private readonly AppDataPaths paths;

    public TrackedSongsStore(AppDataPaths paths)
    {
        this.paths = paths;
    }

    public IReadOnlyList<TrackedSong> Load()
    {
        paths.EnsureDirectory();

        return JsonFile.TryRead<List<TrackedSong>>(paths.UnreleasedSongsPath)
            ?? (IReadOnlyList<TrackedSong>)Array.Empty<TrackedSong>();
    }

    public void Save(IReadOnlyList<TrackedSong> songs)
    {
        paths.EnsureDirectory();

        JsonFile.Write(paths.UnreleasedSongsPath, songs);
    }
}
