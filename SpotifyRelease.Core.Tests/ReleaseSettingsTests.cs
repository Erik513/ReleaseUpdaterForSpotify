using SpotifyRelease.Core.Models;

namespace SpotifyRelease.Core.Tests;

public sealed class ReleaseSettingsTests
{
    // Verifies that stored text values are cleaned up and too-large lookback values are capped.
    [Fact]
    public void Normalize_TrimsValuesAndClampsLookbackDays()
    {
        ReleaseSettings settings = new()
        {
            PlaylistId = "  playlist-1  ",
            PlaylistName = "  My playlist  ",
            ReleaseLookbackDays = 999
        };

        settings.Normalize();

        Assert.Equal("playlist-1", settings.PlaylistId);
        Assert.Equal("My playlist", settings.PlaylistName);
        Assert.Equal(ReleaseDefaults.MaxReleaseLookbackDays, settings.ReleaseLookbackDays);
        Assert.True(settings.UsesSharedSpotifyClientId);
    }

    // Verifies that empty settings fall back to safe defaults.
    [Fact]
    public void Normalize_UsesDefaultPlaylistNameAndRemovesEmptyPlaylistId()
    {
        ReleaseSettings settings = new()
        {
            PlaylistId = "   ",
            PlaylistName = "   ",
            ReleaseLookbackDays = -10
        };

        settings.Normalize();

        Assert.Null(settings.PlaylistId);
        Assert.Equal(ReleaseDefaults.PlaylistName, settings.PlaylistName);
        Assert.Equal(ReleaseDefaults.MinReleaseLookbackDays, settings.ReleaseLookbackDays);
    }

    // Verifies that a custom Spotify Client ID is trimmed and becomes the effective OAuth client.
    [Fact]
    public void Normalize_UsesTrimmedCustomSpotifyClientId()
    {
        ReleaseSettings settings = new()
        {
            CustomSpotifyClientId = "  0123456789abcdef0123456789abcdef  "
        };

        settings.Normalize();

        Assert.Equal("0123456789abcdef0123456789abcdef", settings.CustomSpotifyClientId);
        Assert.False(settings.UsesSharedSpotifyClientId);
        Assert.Equal("0123456789abcdef0123456789abcdef", settings.EffectiveSpotifyClientId);
    }

    // Verifies that the app owner can enter the bundled Client ID as a saved custom value.
    [Fact]
    public void Normalize_KeepsCustomClientIdWhenItMatchesSharedClientId()
    {
        ReleaseSettings settings = new()
        {
            CustomSpotifyClientId = ReleaseDefaults.SharedSpotifyClientId.ToUpperInvariant()
        };

        settings.Normalize();

        Assert.Equal(ReleaseDefaults.SharedSpotifyClientId, settings.CustomSpotifyClientId);
        Assert.False(settings.UsesSharedSpotifyClientId);
        Assert.Equal(ReleaseDefaults.SharedSpotifyClientId, settings.EffectiveSpotifyClientId);
    }
}
