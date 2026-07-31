using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Core.Abstractions;

public interface ISpotifyTokenStore
{
    SpotifyToken? Load();

    void Save(SpotifyToken token);

    void Clear();
}
