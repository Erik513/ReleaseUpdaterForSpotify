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
/// How closely a <see cref="SongMatchResult"/>'s <see cref="SongMatchResult.Match"/>
/// lines up with the tracked title/artist, after fuzzy comparison - see
/// UnreleasedSongChecker for the actual scoring. Ordered low to high, so
/// callers picking the best of several candidates can compare with &gt;.
/// </summary>
public enum MatchConfidence
{
    None,
    Similar,
    Confident
}

/// <summary>
/// The outcome of checking one <see cref="TrackedSong"/> against Spotify.
/// <see cref="Match"/> is only set when <see cref="Confidence"/> is above
/// <see cref="MatchConfidence.None"/> - a search that returned candidates
/// but none similar enough is reported the same as no results at all, per
/// UnreleasedSongChecker's matching policy.
/// </summary>
public sealed record SongMatchResult(
    TrackedSong Song,
    SpotifyTrackItem? Match,
    MatchConfidence Confidence)
{
    public bool IsFound => Confidence != MatchConfidence.None;
}

public sealed record UnreleasedSongsCheckResult(
    int CheckedCount,
    int FoundCount,
    string ReportPath);
