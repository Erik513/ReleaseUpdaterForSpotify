namespace SpotifyRelease.Core.Models;

public sealed class SpotifyToken
{
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string Scope { get; set; } = string.Empty;
}
