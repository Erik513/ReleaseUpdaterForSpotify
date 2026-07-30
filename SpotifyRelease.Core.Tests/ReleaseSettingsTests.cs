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
        Assert.False(settings.HasSpotifyClientId);
        Assert.Equal(string.Empty, settings.EffectiveSpotifyClientId);
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
        Assert.True(settings.HasSpotifyClientId);
        Assert.Equal("0123456789abcdef0123456789abcdef", settings.EffectiveSpotifyClientId);
    }

    // Verifies that empty Client ID text is removed instead of becoming an invalid configured value.
    [Fact]
    public void Normalize_RemovesEmptySpotifyClientId()
    {
        ReleaseSettings settings = new()
        {
            CustomSpotifyClientId = "   "
        };

        settings.Normalize();

        Assert.Null(settings.CustomSpotifyClientId);
        Assert.False(settings.HasSpotifyClientId);
        Assert.Equal(string.Empty, settings.EffectiveSpotifyClientId);
    }
}
