using System.Text.Json;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Data.Storage;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

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

        try
        {
            if (!File.Exists(paths.SettingsPath))
            {
                return CreateDefaultSettings();
            }

            string json = File.ReadAllText(paths.SettingsPath);

            if (string.IsNullOrWhiteSpace(json))
            {
                return CreateDefaultSettings();
            }

            ReleaseSettings? settings =
                JsonSerializer.Deserialize<ReleaseSettings>(json, JsonOptions);

            return (settings ?? CreateDefaultSettings()).Normalize();
        }
        catch
        {
            return CreateDefaultSettings();
        }
    }

    /// <summary>
    /// Saves app settings only, never access tokens or client secrets.
    /// </summary>
    public void Save(ReleaseSettings settings)
    {
        paths.EnsureDirectory();

        ReleaseSettings normalized = settings.Normalize();
        string json = JsonSerializer.Serialize(normalized, JsonOptions);
        AtomicFile.WriteAllText(paths.SettingsPath, json);
    }

    private static ReleaseSettings CreateDefaultSettings()
    {
        return new ReleaseSettings().Normalize();
    }
}
