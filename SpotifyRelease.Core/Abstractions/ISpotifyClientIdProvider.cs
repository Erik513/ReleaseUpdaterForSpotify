namespace SpotifyRelease.Core.Abstractions;

public interface ISpotifyClientIdProvider
{
    string CurrentClientId { get; }
}
