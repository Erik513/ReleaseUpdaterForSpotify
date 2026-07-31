using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Core.Abstractions;

public interface IReportWriter
{
    string ReportPath { get; }

    string WriteReport(
        IReadOnlyList<ReleaseTrack> uniqueTracks,
        IReadOnlyList<ReleaseTrack> duplicateTracks,
        int releaseLookbackDays);
}
