namespace ReleaseUpdater.Core.Models;

public sealed class ReleaseSettings
{
    public string? PlaylistId { get; set; }
    public string PlaylistName { get; set; } = ReleaseDefaults.PlaylistName;
    // Empty / null: the description is made by the app (see SpotifyReleaseUpdater). Spotify takes up to 300 characters.
    public string? PlaylistDescription { get; set; }
    public int ReleaseLookbackDays { get; set; } = ReleaseDefaults.ReleaseLookbackDays;
    public string? CustomSpotifyClientId { get; set; }
    public DateTimeOffset? SpotifyCooldownUntilUtc { get; set; }
    public string? SpotifyCooldownClientId { get; set; }

    // Plain string, not ErikwnkCore.AppLanguage - Core has no UI/library
    // dependency, and this is the only two values MainForm's language
    // picker ever writes here, so a stray/unrecognized value (an old
    // settings file, manual edit) just falls back to English in Normalize.
    public string Language { get; set; } = "English";

    public bool HasSpotifyClientId =>
        !string.IsNullOrWhiteSpace(CustomSpotifyClientId);

    public string EffectiveSpotifyClientId =>
        CustomSpotifyClientId ?? string.Empty;

    public ReleaseSettings Normalize()
    {
        PlaylistName = string.IsNullOrWhiteSpace(PlaylistName)
            ? ReleaseDefaults.PlaylistName
            : PlaylistName.Trim();

        PlaylistDescription = PlaylistDescription?.Trim();

        if (string.IsNullOrEmpty(PlaylistDescription))
        {
            PlaylistDescription = null;
        }
        else if (PlaylistDescription.Length > ReleaseDefaults.MaxPlaylistDescriptionLength)
        {
            PlaylistDescription = PlaylistDescription.Substring(0, ReleaseDefaults.MaxPlaylistDescriptionLength).TrimEnd();
        }

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

        Language = string.Equals(Language, "German", StringComparison.OrdinalIgnoreCase)
            ? "German"
            : "English";

        return this;
    }
}
