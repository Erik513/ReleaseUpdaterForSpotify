namespace SpotifyRelease.Core.Models;

public sealed class ReleaseSettings
{
    public string? PlaylistId { get; set; }
    public string PlaylistName { get; set; } = ReleaseDefaults.PlaylistName;
    public int ReleaseLookbackDays { get; set; } = ReleaseDefaults.ReleaseLookbackDays;
    public string? CustomSpotifyClientId { get; set; }
    public DateOnly? SharedClientLastRunDate { get; set; }
    public DateTimeOffset? SpotifyCooldownUntilUtc { get; set; }
    public string? SpotifyCooldownClientId { get; set; }

    public bool UsesSharedSpotifyClientId =>
        string.IsNullOrWhiteSpace(CustomSpotifyClientId);

    public string EffectiveSpotifyClientId =>
        UsesSharedSpotifyClientId
            ? ReleaseDefaults.SharedSpotifyClientId
            : CustomSpotifyClientId!;

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

        CustomSpotifyClientId = CustomSpotifyClientId?.Trim();

        if (string.IsNullOrWhiteSpace(CustomSpotifyClientId))
        {
            CustomSpotifyClientId = null;
        }
        else
        {
            CustomSpotifyClientId = CustomSpotifyClientId.ToLowerInvariant();
        }

        SpotifyCooldownClientId = SpotifyCooldownClientId?.Trim();

        if (string.IsNullOrWhiteSpace(SpotifyCooldownClientId))
        {
            SpotifyCooldownClientId = null;
        }

        return this;
    }
}
