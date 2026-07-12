using SpotifyRelease.Core.Abstractions;
using SpotifyRelease.Core.Models;
using SpotifyRelease.Spotify.Auth;

namespace SpotifyRelease.Core.Tests;

public sealed class SettingsSpotifyClientIdProviderTests
{
    private const string CustomClientId = "0123456789abcdef0123456789abcdef";

    // Verifies that test mode always uses the bundled shared Spotify Client ID.
    [Fact]
    public void CurrentClientId_UsesSharedClientIdWhenTestModeForcesSharedClient()
    {
        FakeSettingsStore settingsStore = new(new ReleaseSettings
        {
            CustomSpotifyClientId = CustomClientId
        });
        SettingsSpotifyClientIdProvider provider = new(
            settingsStore,
            useSharedClientInTestMode: true);

        Assert.Equal(ReleaseDefaults.SharedSpotifyClientId, provider.CurrentClientId);
        Assert.True(provider.UsesSharedClientId);
    }

    // Verifies that normal mode still uses a saved custom Spotify Client ID.
    [Fact]
    public void CurrentClientId_UsesConfiguredClientIdWhenTestModeDoesNotForceSharedClient()
    {
        FakeSettingsStore settingsStore = new(new ReleaseSettings
        {
            CustomSpotifyClientId = CustomClientId
        });
        SettingsSpotifyClientIdProvider provider = new(
            settingsStore,
            useSharedClientInTestMode: false);

        Assert.Equal(CustomClientId, provider.CurrentClientId);
        Assert.False(provider.UsesSharedClientId);
    }

    // Verifies that a saved custom value matching the bundled Client ID still disables the local shared limit.
    [Fact]
    public void UsesSharedClientId_IsFalseWhenCustomClientIdMatchesBundledClientId()
    {
        FakeSettingsStore settingsStore = new(new ReleaseSettings
        {
            CustomSpotifyClientId = ReleaseDefaults.SharedSpotifyClientId
        });
        SettingsSpotifyClientIdProvider provider = new(
            settingsStore,
            useSharedClientInTestMode: false);

        Assert.Equal(ReleaseDefaults.SharedSpotifyClientId, provider.CurrentClientId);
        Assert.False(provider.UsesSharedClientId);
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        private ReleaseSettings settings;

        public FakeSettingsStore(ReleaseSettings settings)
        {
            this.settings = settings;
        }

        public string SettingsPath => "settings.json";

        public ReleaseSettings Load()
        {
            return settings;
        }

        public void Save(ReleaseSettings settings)
        {
            this.settings = settings;
        }
    }
}
