using SpotifyRelease.Core.Models;

namespace SpotifyRelease.Core.Services;

public static class SpotifyRunPolicy
{
    public static bool IsCooldownActive(
        ReleaseSettings settings,
        DateTimeOffset now,
        bool ignoreCooldown = ReleaseDefaults.LogicTestModeEnabled)
    {
        if (ignoreCooldown)
        {
            return false;
        }

        ReleaseSettings normalized = settings.Normalize();

        if (!normalized.HasSpotifyClientId)
        {
            return false;
        }

        return normalized.SpotifyCooldownUntilUtc is DateTimeOffset cooldownUntil &&
            cooldownUntil > now.ToUniversalTime() &&
            string.Equals(
                normalized.SpotifyCooldownClientId,
                normalized.EffectiveSpotifyClientId,
                StringComparison.OrdinalIgnoreCase);
    }

    public static DateTimeOffset GetCooldownUntil(
        ReleaseSettings settings,
        DateTimeOffset now,
        TimeSpan retryAfter)
    {
        TimeSpan minimumCooldown =
            TimeSpan.FromMinutes(ReleaseDefaults.SpotifyRateLimitCooldownMinutes);
        TimeSpan cooldown = retryAfter > minimumCooldown
            ? retryAfter
            : minimumCooldown;

        return now.ToUniversalTime().Add(cooldown);
    }

    public static void MarkRateLimitCooldown(
        ReleaseSettings settings,
        DateTimeOffset now,
        TimeSpan retryAfter,
        bool ignoreCooldown = ReleaseDefaults.LogicTestModeEnabled)
    {
        if (ignoreCooldown)
        {
            return;
        }

        ReleaseSettings normalized = settings.Normalize();

        if (!normalized.HasSpotifyClientId)
        {
            return;
        }

        normalized.SpotifyCooldownClientId = normalized.EffectiveSpotifyClientId;
        normalized.SpotifyCooldownUntilUtc = GetCooldownUntil(
            normalized,
            now,
            retryAfter);
    }
}
