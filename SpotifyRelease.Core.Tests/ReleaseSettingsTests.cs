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
}
