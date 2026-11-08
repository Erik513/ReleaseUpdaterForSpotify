using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Data.Storage;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly AppDataPaths paths;

    public JsonSettingsStore(AppDataPaths paths)
    {
        this.paths = paths;
    }

    public string SettingsPath => paths.SettingsPath;

    /// <summary>
    /// Loads app settings from the user profile and normalizes limits.
    /// </summary>
    public ReleaseSettings Load()
    {
        paths.EnsureDirectory();

        return (JsonFile.TryRead<ReleaseSettings>(paths.SettingsPath) ?? new ReleaseSettings())
            .Normalize();
    }

    /// <summary>
    /// Saves app settings only, never access tokens or client secrets.
    /// </summary>
    public void Save(ReleaseSettings settings)
    {
        paths.EnsureDirectory();

        JsonFile.Write(paths.SettingsPath, settings.Normalize());
    }
}
