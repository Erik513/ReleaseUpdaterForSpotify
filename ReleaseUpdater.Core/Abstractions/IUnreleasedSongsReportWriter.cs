using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Core.Abstractions;

public interface IUnreleasedSongsReportWriter
{
    string ReportPath { get; }

    string WriteReport(IReadOnlyList<SongMatchResult> results);
}
