namespace ReleaseUpdater.Core.Models;

public sealed record SpotifyUser(
    string Id,
    string DisplayName);

public sealed record SpotifyArtist(
    string Id,
    string Name);

public sealed record SpotifyAlbum(
    string Id,
    string Name,
    DateOnly? ReleaseDate,
    string? ImageUrl = null,
    string? AlbumType = null)
{
    /// <summary>True for a compilation (a sampler or "best of" that gathers songs that are mostly older than the compilation itself).</summary>
    public bool IsCompilation => string.Equals(AlbumType, "compilation", StringComparison.OrdinalIgnoreCase);
}

public sealed record SpotifyTrackItem(
    string Id,
    string Title,
    IReadOnlyList<SpotifyArtist> Artists,
    string Uri,
    string SpotifyUrl,
    SpotifyAlbum? Album);

public sealed record SpotifyPlaylist(
    string Id,
    string Name,
    string OwnerId);
