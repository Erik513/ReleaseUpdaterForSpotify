using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Core.Services;

public static class UpdateVersionParser
{
    /// <summary>
    /// Parses a GitHub release tag (e.g. "v1.2.0") and returns an update result only
    /// when it names a version newer than the currently running one.
    /// </summary>
    public static UpdateCheckResult? TryParseNewerRelease(
        string? tagName,
        string? releaseUrl,
        Version currentVersion)
    {
        if (string.IsNullOrWhiteSpace(tagName) || string.IsNullOrWhiteSpace(releaseUrl))
        {
            return null;
        }

        string versionText = tagName.TrimStart('v', 'V');

        if (!Version.TryParse(versionText, out Version? latestVersion))
        {
            return null;
        }

        Version normalizedCurrent = Normalize(currentVersion);
        Version normalizedLatest = Normalize(latestVersion);

        return normalizedLatest > normalizedCurrent
            ? new UpdateCheckResult(normalizedLatest, releaseUrl)
            : null;
    }

    /// <summary>
    /// Drops any unset trailing components so "1.0.0" and "1.0.0.0" compare as equal
    /// instead of .NET treating a missing Revision as less than zero.
    /// </summary>
    private static Version Normalize(Version version) =>
        new(version.Major, Math.Max(version.Minor, 0), Math.Max(version.Build, 0));
}
