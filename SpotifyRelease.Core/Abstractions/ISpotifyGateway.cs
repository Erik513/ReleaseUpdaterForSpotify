using SpotifyRelease.Core.Models;

namespace SpotifyRelease.Core.Abstractions;

public interface ISpotifyGateway
{
    Task<SpotifyUser> GetCurrentUserAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SpotifyArtist>> GetFollowedArtistsAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SpotifyAlbum>> GetArtistAlbumsAsync(
        string artistId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SpotifyTrackItem>> GetAlbumTracksAsync(
        string albumId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SpotifyTrackItem>> SearchTracksByArtistAsync(
        string artistName,
        CancellationToken cancellationToken);

    Task<SpotifyPlaylist?> TryGetPlaylistAsync(
        string playlistId,
        CancellationToken cancellationToken);

    Task<string> CreatePlaylistAsync(
        string userId,
        string name,
        string description,
        CancellationToken cancellationToken);

    Task UpdatePlaylistDetailsAsync(
        string playlistId,
        string name,
        string description,
        CancellationToken cancellationToken);

    Task ClearPlaylistAsync(
        string playlistId,
        CancellationToken cancellationToken);

    Task AddTracksToPlaylistAsync(
        string playlistId,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken);
}
