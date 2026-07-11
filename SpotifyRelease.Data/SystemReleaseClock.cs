using SpotifyRelease.Core.Abstractions;

namespace SpotifyRelease.Data;

public sealed class SystemReleaseClock : IReleaseClock
{
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
