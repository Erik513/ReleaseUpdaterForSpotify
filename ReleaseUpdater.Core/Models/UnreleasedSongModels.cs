namespace ReleaseUpdater.Core.Models;

/// <summary>
/// A song the user is tracking as not yet released on Spotify - Title and
/// Artist as entered by hand, so free text rather than a Spotify-verified
/// identity. The link is optional, also free text: where the user found the
/// song (see TrackedSongLinks for what is done before it is opened).
/// </summary>
public sealed record TrackedSong(
    string Title,
    string Artist,
    string? Link = null)
{
    /// <summary>True when there is something in the title or the artist (the blank row at the end of the list has nothing).</summary>
    public bool HasContent => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Artist);

    /// <summary>True when both title and artist are filled in: only such a song can be searched for.</summary>
    public bool IsComplete => !string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(Artist);
}

/// <summary>
/// How closely a <see cref="SongMatchResult"/>'s <see cref="SongMatchResult.Match"/>
/// lines up with the tracked title/artist, after fuzzy comparison - see
/// UnreleasedSongChecker for the actual scoring. Ordered low to high, so
/// callers picking the best of several candidates can compare with &gt;.
/// </summary>
public enum MatchConfidence
{
    None,

    /// <summary>
    /// Only one of title and artist fits (the other is wrong or missing): not counted as found, but worth a look by hand. Shown in
    /// its own section of the report.
    /// </summary>
    Partial,
    Similar,
    Confident
}

/// <summary>What is special about how a song was matched (shown in the report, so that the user knows what to look at).</summary>
public enum MatchHint
{
    None,

    /// <summary>The title and the artist of the entry are the other way round: the title is the artist of the track, and the other way round.</summary>
    SwappedFields,

    /// <summary>A partial match: the title fits, the artist does not.</summary>
    TitleOnly,

    /// <summary>A partial match: the artist fits, the title does not (or only loosely).</summary>
    ArtistOnly
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
    MatchConfidence Confidence,
    MatchHint Hint = MatchHint.None)
{
    /// <summary>A match that is believed (<see cref="MatchConfidence.Similar"/> or better).</summary>
    public bool IsFound => Confidence >= MatchConfidence.Similar;

    /// <summary>Only a hint (<see cref="MatchConfidence.Partial"/>): not found, but something comes close.</summary>
    public bool IsPossible => Confidence == MatchConfidence.Partial;
}

public sealed record UnreleasedSongsCheckResult(
    int CheckedCount,
    int FoundCount,
    string ReportPath,
    int PossibleCount = 0);
