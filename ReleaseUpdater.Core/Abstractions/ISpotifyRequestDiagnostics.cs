using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Core.Abstractions;

public interface ISpotifyRequestDiagnostics
{
    IProgress<ReleaseProgress>? Progress { get; set; }
}
