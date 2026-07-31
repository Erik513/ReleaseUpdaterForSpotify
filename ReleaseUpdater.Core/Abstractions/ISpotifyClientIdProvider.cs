namespace ReleaseUpdater.Core.Abstractions;

public interface ISpotifyClientIdProvider
{
    string CurrentClientId { get; }
}
