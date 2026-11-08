using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Spotify.Auth;

namespace ReleaseUpdater.Core.Tests;

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

    // Verifies that the settings file is read once while it stays as it is, and again after it was saved.
    [Fact]
    public void CurrentClientId_ReadsTheFileOnlyWhenItHasChanged()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ClientIdProviderTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            string path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, "{}");
            CountingSettingsStore store = new(path, new ReleaseSettings { CustomSpotifyClientId = CustomClientId });
            SettingsSpotifyClientIdProvider provider = new(store);

            Assert.Equal(CustomClientId, provider.CurrentClientId);
            Assert.Equal(CustomClientId, provider.CurrentClientId);
            Assert.Equal(CustomClientId, provider.CurrentClientId);
            Assert.Equal(1, store.LoadCount);

            const string other = "fedcba9876543210fedcba9876543210";
            store.Settings = new ReleaseSettings { CustomSpotifyClientId = other };
            File.WriteAllText(path, "{ \"changed\": true }");

            Assert.Equal(other, provider.CurrentClientId);
            Assert.Equal(2, store.LoadCount);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    // Verifies that a settings file that is not there is never cached (a fake store, a first start).
    [Fact]
    public void CurrentClientId_IsNotCachedWithoutAFile()
    {
        CountingSettingsStore store = new("does-not-exist.json", new ReleaseSettings { CustomSpotifyClientId = CustomClientId });
        SettingsSpotifyClientIdProvider provider = new(store);

        _ = provider.CurrentClientId;
        _ = provider.CurrentClientId;

        Assert.Equal(2, store.LoadCount);
    }

    private sealed class CountingSettingsStore : ISettingsStore
    {
        public CountingSettingsStore(string path, ReleaseSettings settings)
        {
            SettingsPath = path;
            Settings = settings;
        }

        public string SettingsPath { get; }
        public ReleaseSettings Settings { get; set; }
        public int LoadCount { get; private set; }

        public ReleaseSettings Load()
        {
            LoadCount++;
            return Settings;
        }

        public void Save(ReleaseSettings settings)
        {
            Settings = settings;
        }
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
