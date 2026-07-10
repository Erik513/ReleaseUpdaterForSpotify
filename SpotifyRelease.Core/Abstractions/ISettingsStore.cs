using SpotifyRelease.Core.Models;

namespace SpotifyRelease.Core.Abstractions;

public interface ISettingsStore
{
    string SettingsPath { get; }

    ReleaseSettings Load();

    void Save(ReleaseSettings settings);
}
