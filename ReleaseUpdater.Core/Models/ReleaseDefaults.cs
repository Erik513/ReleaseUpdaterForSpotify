namespace ReleaseUpdater.Core.Models;

public static class ReleaseDefaults
{
    public const string PlaylistName = "[Followed Artists - New Releases]";
    public const int ReleaseLookbackDays = 10;
    public const int MinReleaseLookbackDays = 1;
    public const int MaxReleaseLookbackDays = 20;

    public const int ArtistPageSize = 50;
    public const int AlbumPageSize = 10;
    public const int TrackPageSize = 50;
    public const int SearchResultLimit = 10;
    public const int PlaylistBatchSize = 100;
    public const int PlaylistPageSize = 50;
    public const int SpotifyRobustRequestBudget = 90;
    public const int SpotifyRobustWindowSeconds = 30;
    public const int SpotifyRobustModeDecaySeconds = 60;
    public const int SpotifyRateLimitCooldownMinutes = 10;
    // Keep this enabled only while testing; disable it before distributing the app.
    public const bool LogicTestModeEnabled = false;
    public const int LogicTestArtistLimit = 10;
    public const int LogicTestReleaseLimit = 10;

    // playlist-modify-public/private are both requested because changing an existing
    // playlist's details (including flipping it to private) requires the scope
    // matching its current visibility, not just the target one. playlist-read-private
    // is required for GET /me/playlists to list the app's own playlist at all, since
    // it's created private - without it, the reuse-by-ID and reuse-by-name checks below
    // would never find it, and a new playlist would be created on every run.
    public const string SpotifyScope =
        "user-follow-read playlist-modify-public playlist-modify-private playlist-read-private";
}
