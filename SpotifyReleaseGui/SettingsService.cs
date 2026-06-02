using System.Text.Json;

namespace SpotifyReleaseGui
{
    public static class SettingsService
    {
        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(AppPaths.ConfigPath))
                {
                    return new AppSettings();
                }

                string json = File.ReadAllText(AppPaths.ConfigPath);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return new AppSettings();
                }

                JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;

                AppSettings settings = new AppSettings();

                if (root.TryGetProperty("playlist_id", out JsonElement playlistId))
                {
                    settings.PlaylistId = playlistId.GetString() ?? string.Empty;
                }

                if (root.TryGetProperty("playlist_name", out JsonElement playlistName))
                {
                    settings.PlaylistName =
                        playlistName.GetString()
                        ?? "[Followed Artists - New Releases]";
                }

                if (root.TryGetProperty("release_lookback_days", out JsonElement days))
                {
                    settings.ReleaseLookbackDays = days.GetInt32();
                }

                document.Dispose();

                return settings;
            }
            catch
            {
                return new AppSettings();
            }
        }

        public static void Save(AppSettings settings)
        {
            if (!Directory.Exists(AppPaths.BackendPath))
            {
                Directory.CreateDirectory(AppPaths.BackendPath);
            }

            Dictionary<string, object> config = new Dictionary<string, object>();

            if (!string.IsNullOrWhiteSpace(settings.PlaylistId))
            {
                config["playlist_id"] = settings.PlaylistId;
            }

            config["playlist_name"] = settings.PlaylistName;
            config["release_lookback_days"] = settings.ReleaseLookbackDays;

            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(AppPaths.ConfigPath, json);
        }
    }
}