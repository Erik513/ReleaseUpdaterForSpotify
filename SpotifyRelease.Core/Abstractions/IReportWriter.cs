using SpotifyRelease.Core.Models;

namespace SpotifyRelease.Core.Abstractions;

public interface IReportWriter
{
    string ReportPath { get; }

    string WriteReport(
        IReadOnlyList<ReleaseTrack> uniqueTracks,
        IReadOnlyList<ReleaseTrack> duplicateTracks,
        int releaseLookbackDays);
}
