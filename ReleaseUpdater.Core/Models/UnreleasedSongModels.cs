namespace ReleaseUpdater.Core.Models;

/// <summary>
/// A song the user is tracking as not yet released on Spotify - Title and
/// Artist as entered by hand, so free text rather than a Spotify-verified
/// identity.
/// </summary>
public sealed record TrackedSong(
    string Title,
    string Artist);

/// <summary>
/// The outcome of checking one <see cref="TrackedSong"/> against Spotify.
/// <see cref="Match"/> is only set when <see cref="IsConfidentMatch"/> is
/// true - a search that returned candidates but none confident enough is
/// reported the same as no results at all, per UnreleasedSongChecker's
/// "only surface sure hits" matching policy.
/// </summary>
public sealed record SongMatchResult(
    TrackedSong Song,
    SpotifyTrackItem? Match,
    bool IsConfidentMatch);

public sealed record UnreleasedSongsCheckResult(
    int CheckedCount,
    int FoundCount,
    string ReportPath);
