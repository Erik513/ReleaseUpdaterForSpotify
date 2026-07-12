using SpotifyRelease.Core.Models;

namespace SpotifyRelease.Spotify.Auth;

public static class SpotifyAppOptions
{
    // Public OAuth client ID for this desktop app. Do not add a client secret here.
    public static string ClientId => ReleaseDefaults.SharedSpotifyClientId;
}
