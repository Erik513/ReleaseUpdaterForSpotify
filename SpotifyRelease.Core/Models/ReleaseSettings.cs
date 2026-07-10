namespace SpotifyRelease.Core.Models;

public sealed class ReleaseSettings
{
    public string? PlaylistId { get; set; }
    public string PlaylistName { get; set; } = ReleaseDefaults.PlaylistName;
    public int ReleaseLookbackDays { get; set; } = ReleaseDefaults.ReleaseLookbackDays;

    public ReleaseSettings Normalize()
    {
        PlaylistName = string.IsNullOrWhiteSpace(PlaylistName)
            ? ReleaseDefaults.PlaylistName
            : PlaylistName.Trim();

        ReleaseLookbackDays = Math.Clamp(
            ReleaseLookbackDays,
            ReleaseDefaults.MinReleaseLookbackDays,
            ReleaseDefaults.MaxReleaseLookbackDays);

        PlaylistId = PlaylistId?.Trim();

        if (string.IsNullOrWhiteSpace(PlaylistId))
        {
            PlaylistId = null;
        }

        return this;
    }
}
