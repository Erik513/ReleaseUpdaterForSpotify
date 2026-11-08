namespace ReleaseUpdater.Core.Models;

public enum SpotifySignInState
{
    /// <summary>A valid token, and Spotify named the user.</summary>
    SignedIn,

    /// <summary>No token, or one that is not usable (another Client ID, missing permissions, access revoked).</summary>
    NotSignedIn,

    /// <summary>Could not find out: no network, a timeout, or Spotify itself had a problem. The token may well be fine.</summary>
    Unavailable
}

/// <summary>The answer to "is the user signed in?" - which, unlike a plain yes/no, can also be "could not find out".</summary>
public sealed record SpotifySignInCheck(SpotifySignInState State, SpotifyUser? User = null);
