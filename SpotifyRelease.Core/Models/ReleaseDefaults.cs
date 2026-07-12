namespace SpotifyRelease.Core.Models;

public static class ReleaseDefaults
{
    private static readonly byte[] EncodedSharedSpotifyClientId =
    [
        109, 63, 56, 62, 111, 107, 99, 104,
        107, 111, 99, 56, 110, 104, 109, 57,
        99, 108, 108, 105, 111, 57, 108, 63,
        62, 59, 109, 110, 108, 63, 108, 104
    ];

    private static readonly Lazy<string> DecodedSharedSpotifyClientId =
        new(DecodeSharedSpotifyClientId);

    public static string SharedSpotifyClientId =>
        DecodedSharedSpotifyClientId.Value;

    public const string PlaylistName = "[Followed Artists - New Releases]";
    public const int ReleaseLookbackDays = 10;
    public const int MinReleaseLookbackDays = 1;
    public const int MaxReleaseLookbackDays = 20;

    public const int ArtistPageSize = 50;
    public const int AlbumPageSize = 50;
    public const int TrackPageSize = 50;
    public const int SearchResultLimit = 20;
    public const int PlaylistBatchSize = 100;
    public const int PlaylistPageSize = 50;
    public const int SpotifyRobustRequestBudget = 90;
    public const int SpotifyRobustWindowSeconds = 30;
    public const int SpotifyRateLimitCooldownMinutes = 10;
    // Keep this enabled only while testing; disable it before distributing the app.
    public const bool LogicTestModeEnabled = false;
    public const int LogicTestArtistLimit = 10;
    public const int LogicTestReleaseLimit = 5;

    public const string SpotifyScope =
        "user-follow-read playlist-modify-public";

    private static string DecodeSharedSpotifyClientId()
    {
        char[] decoded = new char[EncodedSharedSpotifyClientId.Length];

        for (int index = 0; index < EncodedSharedSpotifyClientId.Length; index++)
        {
            decoded[index] = (char)(EncodedSharedSpotifyClientId[index] ^ 0x5A);
        }

        return new string(decoded);
    }
}
