using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Core.Abstractions;

public interface ISettingsStore
{
    string SettingsPath { get; }

    ReleaseSettings Load();

    void Save(ReleaseSettings settings);
}
