using ReleaseUpdater.Core.Abstractions;

namespace ReleaseUpdater.Spotify.Auth;

public sealed class SettingsSpotifyClientIdProvider : ISpotifyClientIdProvider
{
    private readonly ISettingsStore settingsStore;

    public SettingsSpotifyClientIdProvider(ISettingsStore settingsStore)
    {
        this.settingsStore = settingsStore;
    }

    public string CurrentClientId
    {
        get
        {
            string clientId = settingsStore
                .Load()
                .Normalize()
                .EffectiveSpotifyClientId;

            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new InvalidOperationException(
                    "Please enter and save your Spotify Client ID first.");
            }

            return clientId;
        }
    }
}
