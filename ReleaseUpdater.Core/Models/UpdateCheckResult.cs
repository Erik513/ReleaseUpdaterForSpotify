namespace ReleaseUpdater.Core.Models;

public sealed record UpdateCheckResult(
    Version LatestVersion,
    string ReleaseUrl,
    string? DownloadUrl);
