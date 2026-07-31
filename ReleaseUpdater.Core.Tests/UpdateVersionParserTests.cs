using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Core.Services;

namespace ReleaseUpdater.Core.Tests;

public sealed class UpdateVersionParserTests
{
    [Fact]
    public void TryParseNewerRelease_ReturnsResult_WhenTagIsNewer()
    {
        UpdateCheckResult? result = UpdateVersionParser.TryParseNewerRelease(
            "v1.2.0",
            "https://example.com/releases/v1.2.0",
            new Version(1, 1, 0));

        Assert.NotNull(result);
        Assert.Equal(new Version(1, 2, 0), result!.LatestVersion);
        Assert.Equal("https://example.com/releases/v1.2.0", result.ReleaseUrl);
    }

    [Fact]
    public void TryParseNewerRelease_StripsLeadingVRegardlessOfCase()
    {
        UpdateCheckResult? result = UpdateVersionParser.TryParseNewerRelease(
            "V2.0.0",
            "https://example.com/releases/v2.0.0",
            new Version(1, 0, 0));

        Assert.NotNull(result);
        Assert.Equal(new Version(2, 0, 0), result!.LatestVersion);
    }

    [Fact]
    public void TryParseNewerRelease_ReturnsNull_WhenTagIsSameVersion()
    {
        UpdateCheckResult? result = UpdateVersionParser.TryParseNewerRelease(
            "v1.0.0",
            "https://example.com/releases/v1.0.0",
            new Version(1, 0, 0));

        Assert.Null(result);
    }

    // The assembly's four-part version (with an implicit zero Revision) must compare
    // equal to a three-part release tag, not "newer", or every launch would nag.
    [Fact]
    public void TryParseNewerRelease_ReturnsNull_WhenFourPartCurrentVersionMatchesTag()
    {
        UpdateCheckResult? result = UpdateVersionParser.TryParseNewerRelease(
            "v1.0.0",
            "https://example.com/releases/v1.0.0",
            new Version(1, 0, 0, 0));

        Assert.Null(result);
    }

    [Fact]
    public void TryParseNewerRelease_ReturnsNull_WhenTagIsOlder()
    {
        UpdateCheckResult? result = UpdateVersionParser.TryParseNewerRelease(
            "v0.9.0",
            "https://example.com/releases/v0.9.0",
            new Version(1, 0, 0));

        Assert.Null(result);
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseNewerRelease_ReturnsNull_ForUnparsableOrMissingTag(string? tagName)
    {
        UpdateCheckResult? result = UpdateVersionParser.TryParseNewerRelease(
            tagName,
            "https://example.com/releases/latest",
            new Version(1, 0, 0));

        Assert.Null(result);
    }

    [Fact]
    public void TryParseNewerRelease_ReturnsNull_WhenReleaseUrlIsMissing()
    {
        UpdateCheckResult? result = UpdateVersionParser.TryParseNewerRelease(
            "v2.0.0",
            null,
            new Version(1, 0, 0));

        Assert.Null(result);
    }
}
