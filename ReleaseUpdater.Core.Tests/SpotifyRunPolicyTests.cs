using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Core.Services;

namespace ReleaseUpdater.Core.Tests;

public sealed class SpotifyRunPolicyTests
{
    private const string ClientId = "0123456789abcdef0123456789abcdef";
    private const string OtherClientId = "abcdef0123456789abcdef0123456789";

    // Verifies that Spotify cooldowns apply only to the Client ID that actually hit the limit.
    [Fact]
    public void IsCooldownActive_MatchesCooldownToConfiguredClientId()
    {
        DateTimeOffset now = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        ReleaseSettings settings = new()
        {
            CustomSpotifyClientId = ClientId,
            SpotifyCooldownClientId = ClientId,
            SpotifyCooldownUntilUtc = now.AddMinutes(5)
        };

        Assert.True(SpotifyRunPolicy.IsCooldownActive(
            settings,
            now,
            ignoreCooldown: false));

        settings.CustomSpotifyClientId = OtherClientId;

        Assert.False(SpotifyRunPolicy.IsCooldownActive(
            settings,
            now,
            ignoreCooldown: false));
    }

    // Verifies that a saved cooldown cannot block a user before a Client ID has been configured.
    [Fact]
    public void IsCooldownActive_IgnoresCooldownWhenClientIdIsMissing()
    {
        DateTimeOffset now = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        ReleaseSettings settings = new()
        {
            SpotifyCooldownClientId = ClientId,
            SpotifyCooldownUntilUtc = now.AddMinutes(5)
        };

        Assert.False(SpotifyRunPolicy.IsCooldownActive(
            settings,
            now,
            ignoreCooldown: false));
    }

    // Verifies that expired cooldown timestamps do not keep blocking updates.
    [Fact]
    public void IsCooldownActive_IgnoresExpiredCooldown()
    {
        DateTimeOffset now = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        ReleaseSettings settings = new()
        {
            CustomSpotifyClientId = ClientId,
            SpotifyCooldownClientId = ClientId,
            SpotifyCooldownUntilUtc = now.AddMinutes(-1)
        };

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
            CustomSpotifyClientId = ClientId,
            SpotifyCooldownClientId = ClientId,
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
        ReleaseSettings settings = new()
        {
            CustomSpotifyClientId = ClientId
        };

        SpotifyRunPolicy.MarkRateLimitCooldown(
            settings,
            now,
            TimeSpan.FromSeconds(2),
            ignoreCooldown: false);

        Assert.Equal(ClientId, settings.SpotifyCooldownClientId);
        Assert.Equal(
            now.AddMinutes(ReleaseDefaults.SpotifyRateLimitCooldownMinutes),
            settings.SpotifyCooldownUntilUtc);
    }

    // Verifies that a long Spotify Retry-After value is respected.
    [Fact]
    public void MarkRateLimitCooldown_UsesRetryAfterWhenItIsLongerThanMinimum()
    {
        DateTimeOffset now = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        ReleaseSettings settings = new()
        {
            CustomSpotifyClientId = ClientId
        };

        SpotifyRunPolicy.MarkRateLimitCooldown(
            settings,
            now,
            TimeSpan.FromMinutes(30),
            ignoreCooldown: false);

        Assert.Equal(ClientId, settings.SpotifyCooldownClientId);
        Assert.Equal(now.AddMinutes(30), settings.SpotifyCooldownUntilUtc);
    }

    // Verifies that no cooldown marker is written when there is no configured Client ID.
    [Fact]
    public void MarkRateLimitCooldown_DoesNotStoreCooldownWithoutClientId()
    {
        DateTimeOffset now = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        ReleaseSettings settings = new();

        SpotifyRunPolicy.MarkRateLimitCooldown(
            settings,
            now,
            TimeSpan.FromMinutes(30),
            ignoreCooldown: false);

        Assert.Null(settings.SpotifyCooldownClientId);
        Assert.Null(settings.SpotifyCooldownUntilUtc);
    }

    // Verifies that test mode does not store a rate-limit cooldown.
    [Fact]
    public void MarkRateLimitCooldown_DoesNotStoreCooldownInTestMode()
    {
        DateTimeOffset now = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        ReleaseSettings settings = new()
        {
            CustomSpotifyClientId = ClientId
        };

        SpotifyRunPolicy.MarkRateLimitCooldown(
            settings,
            now,
            TimeSpan.FromMinutes(30),
            ignoreCooldown: true);

        Assert.Null(settings.SpotifyCooldownClientId);
        Assert.Null(settings.SpotifyCooldownUntilUtc);
    }
}
