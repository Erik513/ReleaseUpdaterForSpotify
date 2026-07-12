using SpotifyRelease.Core.Abstractions;
using SpotifyRelease.Core.Models;

namespace SpotifyRelease.Spotify.Auth;

public sealed class SettingsSpotifyClientIdProvider : ISpotifyClientIdProvider
{
    private readonly ISettingsStore settingsStore;
    private readonly bool useSharedClientInTestMode;

    public SettingsSpotifyClientIdProvider(
        ISettingsStore settingsStore,
        bool useSharedClientInTestMode = ReleaseDefaults.LogicTestModeEnabled)
    {
        this.settingsStore = settingsStore;
        this.useSharedClientInTestMode = useSharedClientInTestMode;
    }

    public string CurrentClientId
    {
        get
        {
            if (useSharedClientInTestMode)
            {
                return ReleaseDefaults.SharedSpotifyClientId;
            }

            string clientId = settingsStore
                .Load()
                .Normalize()
                .EffectiveSpotifyClientId;

            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new InvalidOperationException("Spotify client ID is missing.");
            }

            return clientId;
        }
    }

    public bool UsesSharedClientId =>
        useSharedClientInTestMode ||
        settingsStore.Load().Normalize().UsesSharedSpotifyClientId;
}
