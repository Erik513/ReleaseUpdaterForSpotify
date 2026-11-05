using ErikwnkCore;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Localization;

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
                throw new InvalidOperationException(AppLocalization.Get(AppStrings.SpotifyClientIdNotSaved));
            }

            return clientId;
        }
    }
}
