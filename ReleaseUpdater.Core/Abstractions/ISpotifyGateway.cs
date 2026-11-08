using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Core.Abstractions;

public interface ISpotifyGateway
{
    Task<SpotifyUser> GetCurrentUserAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SpotifyArtist>> GetFollowedArtistsAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// The albums, singles and appearances of an artist. Spotify lists them newest first, 10 per request: when
    /// <paramref name="releasedSince"/> is given, the paging stops once a whole page is older than that day (the older ones are not
    /// wanted, and an artist can have hundreds of appearances), so only the newest pages are requested.
    /// </summary>
    Task<IReadOnlyList<SpotifyAlbum>> GetArtistAlbumsAsync(
        string artistId,
        DateOnly? releasedSince,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SpotifyTrackItem>> GetAlbumTracksAsync(
        string albumId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SpotifyTrackItem>> SearchTracksByArtistAsync(
        string artistName,
        int fromYear,
        int toYear,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SpotifyTrackItem>> SearchTrackByTitleArtistAsync(
        string title,
        string artist,
        CancellationToken cancellationToken);

    /// <summary>
    /// A search of free text (no title: / artist: fields): finds a track whatever the order of the words, and the spelling that Spotify
    /// is lenient about. The tracks come as the search ranks them.
    /// </summary>
    Task<IReadOnlyList<SpotifyTrackItem>> SearchTracksAsync(
        string query,
        CancellationToken cancellationToken);

    Task<SpotifyPlaylist?> TryGetPlaylistAsync(
        string playlistId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SpotifyPlaylist>> GetCurrentUserPlaylistsAsync(
        CancellationToken cancellationToken);

    Task<string> CreatePlaylistAsync(
        string name,
        string description,
        CancellationToken cancellationToken);

    Task UpdatePlaylistDetailsAsync(
        string playlistId,
        string name,
        string description,
        CancellationToken cancellationToken);

    Task ReplacePlaylistTracksAsync(
        string playlistId,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken);

    Task AddTracksToPlaylistAsync(
        string playlistId,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken);
}
