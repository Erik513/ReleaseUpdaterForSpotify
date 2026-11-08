using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Data.Storage;

public sealed class FileTokenStore : ISpotifyTokenStore
{
    private readonly AppDataPaths paths;

    public FileTokenStore(AppDataPaths paths)
    {
        this.paths = paths;
    }

    /// <summary>
    /// Loads the cached OAuth token so users don't have to sign in again on every start.
    /// </summary>
    public SpotifyToken? Load()
    {
        paths.EnsureDirectory();

        return JsonFile.TryRead<SpotifyToken>(paths.TokenPath);
    }

    public void Save(SpotifyToken token)
    {
        paths.EnsureDirectory();

        JsonFile.Write(paths.TokenPath, token);
    }

    public void Clear()
    {
        if (File.Exists(paths.TokenPath))
        {
            File.Delete(paths.TokenPath);
        }
    }
}
