using SpotifyRelease.Core.Models;
using SpotifyRelease.Core.Services;

namespace SpotifyRelease.Core.Tests;

public sealed class SpotifyRunPolicyTests
{
    private static readonly DateOnly Today = new(2026, 7, 11);

    // Verifies that the bundled shared Client ID can run only once per local day.
    [Fact]
    public void IsDailyLimitReached_BlocksSharedClientAfterSuccessfulRunToday()
    {
        ReleaseSettings settings = new()
        {
            SharedClientLastRunDate = Today
        };

        Assert.True(SpotifyRunPolicy.IsDailyLimitReached(
            settings,
            Today,
            ignoreSharedLimit: false));
    }

    // Verifies that the daily shared-client limit does not apply to a user-provided Client ID.
    [Fact]
    public void IsDailyLimitReached_DoesNotBlockCustomClientId()
    {
        ReleaseSettings settings = new()
        {
            CustomSpotifyClientId = "0123456789abcdef0123456789abcdef",
            SharedClientLastRunDate = Today
        };

        Assert.False(SpotifyRunPolicy.IsDailyLimitReached(
            settings,
            Today,
            ignoreSharedLimit: false));
    }

    // Verifies that the app owner's own entry of the bundled Client ID is treated as custom.
    [Fact]
    public void IsDailyLimitReached_DoesNotBlockCustomClientIdThatMatchesSharedClientId()
    {
        ReleaseSettings settings = new()
        {
            CustomSpotifyClientId = ReleaseDefaults.SharedSpotifyClientId,
            SharedClientLastRunDate = Today
        };

        Assert.False(SpotifyRunPolicy.IsDailyLimitReached(
            settings,
            Today,
            ignoreSharedLimit: false));
    }

    // Verifies that test mode can run repeatedly even when the shared Client ID ran today.
    [Fact]
    public void IsDailyLimitReached_IgnoresSharedDailyLimitInTestMode()
    {
        ReleaseSettings settings = new()
        {
            SharedClientLastRunDate = Today
        };

        Assert.False(SpotifyRunPolicy.IsDailyLimitReached(
            settings,
            Today,
            ignoreSharedLimit: true));
    }

    // Verifies that Spotify cooldowns apply only to the Client ID that actually hit the limit.
    [Fact]
    public void IsCooldownActive_MatchesCooldownToEffectiveClientId()
    {
        DateTimeOffset now = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        ReleaseSettings settings = new()
        {
            SpotifyCooldownClientId = ReleaseDefaults.SharedSpotifyClientId,
            SpotifyCooldownUntilUtc = now.AddMinutes(5)
        };

        Assert.True(SpotifyRunPolicy.IsCooldownActive(
            settings,
            now,
            ignoreCooldown: false));

        settings.CustomSpotifyClientId = "0123456789abcdef0123456789abcdef";

        Assert.False(SpotifyRunPolicy.IsCooldownActive(
            settings,
            now,
            ignoreCooldown: false));
    }

    // Verifies that test mode ignores a stored Spotify cooldown.
    [Fact]
    public void IsCooldownActive_IgnoresCooldownInTestMode()
    {
        DateTimeOffset now = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        ReleaseSettings settings = new()
        {
            SpotifyCooldownClientId = ReleaseDefaults.SharedSpotifyClientId,
            SpotifyCooldownUntilUtc = now.AddMinutes(5)
        };

        Assert.False(SpotifyRunPolicy.IsCooldownActive(
            settings,
            now,
            ignoreCooldown: true));
    }

    // Verifies that a short Retry-After still creates a user-friendly minimum cooldown.
    [Fact]
    public void MarkRateLimitCooldown_UsesMinimumCooldownWhenRetryAfterIsShort()
    {
        DateTimeOffset now = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        ReleaseSettings settings = new();

        SpotifyRunPolicy.MarkRateLimitCooldown(
            settings,
            now,
            TimeSpan.FromSeconds(2),
            ignoreCooldown: false);

        Assert.Equal(ReleaseDefaults.SharedSpotifyClientId, settings.SpotifyCooldownClientId);
        Assert.Equal(
            now.AddMinutes(ReleaseDefaults.SpotifyRateLimitCooldownMinutes),
            settings.SpotifyCooldownUntilUtc);
    }

    // Verifies that test mode does not write a daily shared-client run marker.
    [Fact]
    public void MarkSuccessfulSharedClientRun_DoesNotStoreDailyRunInTestMode()
    {
        ReleaseSettings settings = new();

        SpotifyRunPolicy.MarkSuccessfulSharedClientRun(
            settings,
            Today,
            ignoreSharedLimit: true);

        Assert.Null(settings.SharedClientLastRunDate);
    }

    // Verifies that a custom Client ID matching the bundled ID does not get marked as a shared run.
    [Fact]
    public void MarkSuccessfulSharedClientRun_DoesNotStoreDailyRunForCustomSharedClientId()
    {
        ReleaseSettings settings = new()
        {
            CustomSpotifyClientId = ReleaseDefaults.SharedSpotifyClientId
        };

        SpotifyRunPolicy.MarkSuccessfulSharedClientRun(
            settings,
            Today,
            ignoreSharedLimit: false);

        Assert.Null(settings.SharedClientLastRunDate);
    }

    // Verifies that test mode does not store a rate-limit cooldown.
    [Fact]
    public void MarkRateLimitCooldown_DoesNotStoreCooldownInTestMode()
    {
        DateTimeOffset now = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        ReleaseSettings settings = new();

        SpotifyRunPolicy.MarkRateLimitCooldown(
            settings,
            now,
            TimeSpan.FromMinutes(30),
            ignoreCooldown: true);

        Assert.Null(settings.SpotifyCooldownClientId);
        Assert.Null(settings.SpotifyCooldownUntilUtc);
    }
}
