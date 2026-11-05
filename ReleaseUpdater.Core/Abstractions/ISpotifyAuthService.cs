using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Core.Abstractions;

public interface ISpotifyAuthService
{
    /// <summary>
    /// Checks the stored token against Spotify. A network problem is <see cref="SpotifySignInState.Unavailable"/>,
    /// not "signed out": the user stays signed in and is told Spotify could not be reached.
    /// </summary>
    Task<SpotifySignInCheck> CheckSignInAsync(CancellationToken cancellationToken);

    Task<SpotifyUser> LoginAsync(CancellationToken cancellationToken);

    Task LogoutAsync(CancellationToken cancellationToken);
}
