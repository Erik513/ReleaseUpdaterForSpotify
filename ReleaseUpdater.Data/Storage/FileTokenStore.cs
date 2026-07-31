using System.Text.Json;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Data.Storage;

public sealed class FileTokenStore : ISpotifyTokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

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

        try
        {
            if (!File.Exists(paths.TokenPath))
            {
                return null;
            }

            string json = File.ReadAllText(paths.TokenPath);
            return JsonSerializer.Deserialize<SpotifyToken>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(SpotifyToken token)
    {
        paths.EnsureDirectory();

        string json = JsonSerializer.Serialize(token, JsonOptions);
        AtomicFile.WriteAllText(paths.TokenPath, json);
    }

    public void Clear()
    {
        if (File.Exists(paths.TokenPath))
        {
            File.Delete(paths.TokenPath);
        }
    }
}
