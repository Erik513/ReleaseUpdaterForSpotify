namespace ReleaseUpdater.Core.Abstractions;

public interface IReleaseClock
{
    DateOnly Today { get; }
}
