using ErikwnkCore;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Localization;

namespace ReleaseUpdater.Spotify.Auth;

public sealed class SettingsSpotifyClientIdProvider : ISpotifyClientIdProvider
{
    private readonly ISettingsStore settingsStore;
    private readonly object gate = new();

    // The Client ID is asked for with every API request. It is read from the settings file again only when the
    // file has changed (time of the last write and length), so a save in the app - or an edit by hand - is seen at
    // once and a request does not read and parse the file. A file that is not there is never cached.
    private string? cachedClientId;
    private (DateTime LastWriteUtc, long Length)? cachedStamp;

    public SettingsSpotifyClientIdProvider(ISettingsStore settingsStore)
    {
        this.settingsStore = settingsStore;
    }

    public string CurrentClientId
    {
        get
        {
            lock (gate)
            {
                // (The stamp is taken before the file is read: a write during the read makes the next call read again.)
                (DateTime, long)? stamp = ReadStamp();

                if (cachedClientId != null && stamp != null && stamp == cachedStamp)
                {
                    return cachedClientId;
                }

                string clientId = settingsStore
                    .Load()
                    .Normalize()
                    .EffectiveSpotifyClientId;

                if (string.IsNullOrWhiteSpace(clientId))
                {
                    cachedClientId = null;
                    cachedStamp = null;

                    throw new InvalidOperationException(AppLocalization.Get(AppStrings.SpotifyClientIdNotSaved));
                }

                cachedClientId = clientId;
                cachedStamp = stamp;

                return clientId;
            }
        }
    }

    private (DateTime, long)? ReadStamp()
    {
        try
        {
            FileInfo file = new(settingsStore.SettingsPath);

            return file.Exists ? (file.LastWriteTimeUtc, file.Length) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
