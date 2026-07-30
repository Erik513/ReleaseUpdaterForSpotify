using SpotifyRelease.Core.Abstractions;
using SpotifyRelease.Core.Models;
using SpotifyRelease.Spotify.Auth;

namespace SpotifyRelease.Core.Tests;

public sealed class SettingsSpotifyClientIdProviderTests
{
    private const string CustomClientId = "0123456789abcdef0123456789abcdef";

    // Verifies that the saved Spotify Client ID is used for OAuth.
    [Fact]
    public void CurrentClientId_UsesConfiguredClientId()
    {
        FakeSettingsStore settingsStore = new(new ReleaseSettings
        {
            CustomSpotifyClientId = CustomClientId
        });
        SettingsSpotifyClientIdProvider provider = new(settingsStore);

        Assert.Equal(CustomClientId, provider.CurrentClientId);
    }

    // Verifies that the provider trims and normalizes the saved Client ID before use.
    [Fact]
    public void CurrentClientId_NormalizesConfiguredClientId()
    {
        FakeSettingsStore settingsStore = new(new ReleaseSettings
        {
            CustomSpotifyClientId = "  0123456789ABCDEF0123456789ABCDEF  "
        });
        SettingsSpotifyClientIdProvider provider = new(settingsStore);

        Assert.Equal(CustomClientId, provider.CurrentClientId);
    }

    // Verifies that OAuth cannot start until the user has configured a Spotify Client ID.
    [Fact]
    public void CurrentClientId_ThrowsWhenClientIdIsMissing()
    {
        FakeSettingsStore settingsStore = new(new ReleaseSettings());
        SettingsSpotifyClientIdProvider provider = new(settingsStore);

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => provider.CurrentClientId);

        Assert.Contains("Spotify Client ID", exception.Message);
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
