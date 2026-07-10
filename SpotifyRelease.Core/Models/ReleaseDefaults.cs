namespace SpotifyRelease.Core.Models;

public static class ReleaseDefaults
{
    public const string PlaylistName = "[Followed Artists - New Releases]";
    public const int ReleaseLookbackDays = 10;
    public const int MinReleaseLookbackDays = 1;
    public const int MaxReleaseLookbackDays = 20;

    public const int ArtistPageSize = 50;
    public const int AlbumPageSize = 50;
    public const int TrackPageSize = 50;
    public const int SearchResultLimit = 20;
    public const int PlaylistBatchSize = 100;
    public const bool LogicTestModeEnabled = false;
    public const int LogicTestArtistLimit = 10;
    public const int LogicTestReleaseLimit = 5;

    public const string SpotifyScope =
        "user-follow-read playlist-modify-private playlist-modify-public";
}
