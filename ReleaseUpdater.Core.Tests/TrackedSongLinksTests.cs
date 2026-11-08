using ReleaseUpdater.Core.Services;

namespace ReleaseUpdater.Core.Tests;

public sealed class TrackedSongLinksTests
{
    // Verifies which texts are an address that may be opened, and how they are written then.
    [Theory]
    [InlineData("https://example.com/watch?v=1", "https://example.com/watch?v=1")]
    [InlineData("http://example.com", "http://example.com/")]
    [InlineData("  https://example.com/a  ", "https://example.com/a")]
    [InlineData("www.example.com/x", "https://www.example.com/x")]
    [InlineData("example.com/x", "https://example.com/x")]
    [InlineData("example.com:8080/x", "https://example.com:8080/x")]
    [InlineData("HTTPS://EXAMPLE.COM/Path", "https://example.com/Path")]
    public void Normalize_AcceptsWebAddresses(string text, string expected)
    {
        Assert.Equal(expected, TrackedSongLinks.Normalize(text));
    }

    // Verifies that everything else is refused: other schemes (nothing but a web page may be opened from a hand-typed list), empty
    // text, text with spaces or control characters, a name without a dot.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("ftp://example.com/x")]
    [InlineData("C:\\Windows\\notepad.exe")]
    [InlineData("\\\\server\\share")]
    [InlineData("two words.com")]
    [InlineData("localhost")]
    [InlineData("https://")]
    [InlineData("https://nohost")]
    [InlineData("see the video")]
    public void Normalize_RefusesWhatIsNoWebAddress(string? text)
    {
        Assert.Null(TrackedSongLinks.Normalize(text));
    }

    // Verifies what counts as "looks like a link" when a column of a table is searched.
    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("HTTP://EXAMPLE.COM", true)]
    [InlineData("www.example.com", true)]
    [InlineData("example.com", false)]
    [InlineData("a note", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeLink_NeedsASchemeOrWww(string? text, bool expected)
    {
        Assert.Equal(expected, TrackedSongLinks.LooksLikeLink(text));
    }
}
