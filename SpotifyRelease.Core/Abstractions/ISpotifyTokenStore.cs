using SpotifyRelease.Core.Models;

namespace SpotifyRelease.Core.Abstractions;

public interface ISpotifyTokenStore
{
    SpotifyToken? Load();

    void Save(SpotifyToken token);

    void Clear();
}
