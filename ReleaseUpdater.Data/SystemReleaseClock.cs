using ReleaseUpdater.Core.Abstractions;

namespace ReleaseUpdater.Data;

public sealed class SystemReleaseClock : IReleaseClock
{
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
