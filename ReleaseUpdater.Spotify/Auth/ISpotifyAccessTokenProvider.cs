namespace ReleaseUpdater.Spotify.Auth;

public interface ISpotifyAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}
