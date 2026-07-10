namespace SpotifyRelease.Core.Models;

public enum ReleaseTrackSource
{
    ArtistAlbums,
    Search
}

public sealed record ReleaseCandidate(
    string ArtistName,
    string AlbumName,
    DateOnly ReleaseDate,
    string AlbumId);

public sealed record ReleaseTrack(
    string Title,
    string Artists,
    string Uri,
    string SpotifyUrl,
    DateOnly ReleaseDate,
    string Album,
    ReleaseTrackSource Source,
    bool IsFollowedArtistTrack);

public sealed record ReleaseProgress(
    string Status,
    int? Current = null,
    int? Total = null,
    string? Message = null)
{
    public int? Percent =>
        Current.HasValue && Total.HasValue && Total.Value > 0
            ? Current.Value * 100 / Total.Value
            : null;
}

public sealed record PlaylistUpdateResult(
    int FollowedArtistCount,
    int ReleaseCount,
    int TrackCount,
    int UniqueTrackCount,
    int DuplicateCount,
    string ReportPath);
