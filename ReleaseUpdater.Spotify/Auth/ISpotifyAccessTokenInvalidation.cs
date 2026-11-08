namespace ReleaseUpdater.Spotify.Auth;

/// <summary>
/// For a token provider that can be told "the token you gave me was refused" (Spotify answered 401,
/// say because the access was revoked or the token expired earlier than its date said): the next
/// token it hands out is a freshly refreshed one, not the same one again.
/// </summary>
public interface ISpotifyAccessTokenInvalidation
{
    void MarkAccessTokenRejected();
}
