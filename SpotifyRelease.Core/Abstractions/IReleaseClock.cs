namespace SpotifyRelease.Core.Abstractions;

public interface IReleaseClock
{
    DateOnly Today { get; }
}
