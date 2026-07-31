using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Core.Abstractions;

public interface ISpotifyAuthService
{
    bool HasCachedToken { get; }

    Task<SpotifyUser?> TryGetCurrentUserAsync(CancellationToken cancellationToken);

    Task<SpotifyUser> LoginAsync(CancellationToken cancellationToken);

    Task LogoutAsync(CancellationToken cancellationToken);
}
