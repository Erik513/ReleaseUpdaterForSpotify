namespace ReleaseUpdaterGui.Tests;

public sealed class PlaylistValidatorTests
{
    // Verifies the names that are accepted.
    [Theory]
    [InlineData("My releases")]
    [InlineData("[Followed Artists - New Releases]")]
    [InlineData("x")]
    [InlineData("Neue Songs üäö – 東京 🎵")]
    public void ValidNames_AreAccepted(string name)
    {
        Assert.True(PlaylistValidator.IsValidPlaylistName(name, out string message));
        Assert.Equal(string.Empty, message);
    }

    // Verifies that Spotify's limit of 100 characters is checked on the trimmed name: 100 is fine, 101 is not.
    [Fact]
    public void Length_IsCheckedAtOneHundred_OnTheTrimmedName()
    {
        Assert.True(PlaylistValidator.IsValidPlaylistName(new string('a', 100), out _));
        Assert.True(PlaylistValidator.IsValidPlaylistName("   " + new string('a', 100) + "   ", out _));
        Assert.False(PlaylistValidator.IsValidPlaylistName(new string('a', 101), out string message));
        Assert.NotEqual(string.Empty, message);
    }

    // Verifies that an empty or blank name is refused, with a message.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void BlankNames_AreRefused(string name)
    {
        Assert.False(PlaylistValidator.IsValidPlaylistName(name, out string message));
        Assert.NotEqual(string.Empty, message);
    }

    // Verifies that control characters (tab, line break, NUL, escape) inside a name are refused.
    [Theory]
    [InlineData("a\tb")]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    [InlineData("a\0b")]
    [InlineData("a\u001bb")]
    public void ControlCharacters_AreRefused(string name)
    {
        Assert.False(PlaylistValidator.IsValidPlaylistName(name, out string message));
        Assert.NotEqual(string.Empty, message);
    }

    // Verifies that the three refusals have three different messages (so the user is told which problem it is).
    [Fact]
    public void TheThreeProblems_HaveDifferentMessages()
    {
        PlaylistValidator.IsValidPlaylistName("", out string blank);
        PlaylistValidator.IsValidPlaylistName(new string('a', 101), out string tooLong);
        PlaylistValidator.IsValidPlaylistName("a\tb", out string control);

        Assert.Equal(3, new[] { blank, tooLong, control }.Distinct().Count());
    }
}
