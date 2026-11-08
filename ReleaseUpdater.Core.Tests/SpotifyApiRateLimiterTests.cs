using System.Diagnostics;
using ReleaseUpdater.Spotify.Api;

namespace ReleaseUpdater.Core.Tests;

public sealed class SpotifyApiRateLimiterTests
{
    // Verifies that requests within the budget of the window are not held back, and that the one behind it waits until the window has
    // moved on - also without robust mode (the limit of Spotify is not run into first).
    [Fact]
    public async Task WaitForSlotAsync_HoldsBackTheRequestBehindTheBudget_EvenInNormalMode()
    {
        SpotifyApiRateLimiter limiter = new(
            robustRequestBudget: 1,
            robustWindow: TimeSpan.FromMilliseconds(300),
            robustModeDecay: TimeSpan.FromSeconds(10),
            requestBudget: 3);

        Stopwatch watch = Stopwatch.StartNew();

        for (int index = 0; index < 3; index++)
        {
            await limiter.WaitForSlotAsync(CancellationToken.None);
        }

        TimeSpan withinBudget = watch.Elapsed;

        await limiter.WaitForSlotAsync(CancellationToken.None);

        Assert.True(withinBudget < TimeSpan.FromMilliseconds(150), "three requests within the budget wait for nothing: " + withinBudget);
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(250), "the fourth waits for the window: " + watch.Elapsed);
    }

    // Verifies that without a normal budget (the older constructor) only robust mode holds requests back, as before.
    [Fact]
    public async Task WaitForSlotAsync_WithoutANormalBudget_NeverWaitsBeforeRobustMode()
    {
        SpotifyApiRateLimiter limiter = new(1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));
        Stopwatch watch = Stopwatch.StartNew();

        for (int index = 0; index < 20; index++)
        {
            await limiter.WaitForSlotAsync(CancellationToken.None);
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), "no waiting: " + watch.Elapsed);
    }

    // Verifies that robust mode uses its own, smaller budget.
    [Fact]
    public async Task WaitForSlotAsync_InRobustMode_UsesTheSmallerBudget()
    {
        SpotifyApiRateLimiter limiter = new(
            robustRequestBudget: 1,
            robustWindow: TimeSpan.FromMilliseconds(300),
            robustModeDecay: TimeSpan.FromSeconds(10),
            requestBudget: 50);

        limiter.EnableRobustMode();
        Stopwatch watch = Stopwatch.StartNew();

        await limiter.WaitForSlotAsync(CancellationToken.None);
        await limiter.WaitForSlotAsync(CancellationToken.None);

        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(250), "the second waits: " + watch.Elapsed);
    }
}
