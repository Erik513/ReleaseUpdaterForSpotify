using SpotifyRelease.Core.Models;

namespace SpotifyRelease.Core.Abstractions;

public interface ISpotifyRequestDiagnostics
{
    IProgress<ReleaseProgress>? Progress { get; set; }
}
