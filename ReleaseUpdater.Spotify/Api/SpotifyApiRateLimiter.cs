using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Spotify.Api;

internal sealed class SpotifyApiRateLimiter
{
    private readonly object modeLock = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Queue<DateTimeOffset> requestStarts = new();
    private readonly int robustRequestBudget;
    private readonly TimeSpan robustWindow;
    private readonly TimeSpan robustModeDecay;
    private DateTimeOffset? robustModeExpiresAt;

    public SpotifyApiRateLimiter()
        : this(
            ReleaseDefaults.SpotifyRobustRequestBudget,
            TimeSpan.FromSeconds(ReleaseDefaults.SpotifyRobustWindowSeconds),
            TimeSpan.FromSeconds(ReleaseDefaults.SpotifyRobustModeDecaySeconds))
    {
    }

    public SpotifyApiRateLimiter(
        int robustRequestBudget,
        TimeSpan robustWindow,
        TimeSpan robustModeDecay)
    {
        if (robustRequestBudget <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(robustRequestBudget),
                "Request budget must be greater than zero.");
        }

        if (robustWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(robustWindow),
                "Rate-limit window must be greater than zero.");
        }

        if (robustModeDecay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(robustModeDecay),
                "Robust-mode decay must be greater than zero.");
        }

        this.robustRequestBudget = robustRequestBudget;
        this.robustWindow = robustWindow;
        this.robustModeDecay = robustModeDecay;
    }

    /// <summary>
    /// Enters (or extends) robust mode. Every trip refreshes the decay window, so
    /// throttling persists while errors keep happening but lapses on its own once
    /// Spotify has been quiet for a while - it's not a one-way switch for the rest
    /// of the run.
    /// </summary>
    public bool EnableRobustMode()
    {
        lock (modeLock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool wasActive = IsRobustModeActive(now);
            robustModeExpiresAt = now + robustModeDecay;
            return !wasActive;
        }
    }

    /// <summary>
    /// Counts every request, but only waits after robust mode was enabled.
    /// </summary>
    public async Task WaitForSlotAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan delay;

            await gate.WaitAsync(cancellationToken);

            try
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                RemoveExpiredRequests(now);

                if (!IsRobustMode() || requestStarts.Count < robustRequestBudget)
                {
                    requestStarts.Enqueue(now);
                    return;
                }

                DateTimeOffset oldestRequest = requestStarts.Peek();
                delay = robustWindow - (now - oldestRequest);
            }
            finally
            {
                gate.Release();
            }

            if (delay <= TimeSpan.Zero)
            {
                continue;
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    private bool IsRobustMode()
    {
        lock (modeLock)
        {
            return IsRobustModeActive(DateTimeOffset.UtcNow);
        }
    }

    private bool IsRobustModeActive(DateTimeOffset now)
    {
        return robustModeExpiresAt is DateTimeOffset expiresAt && now < expiresAt;
    }

    private void RemoveExpiredRequests(DateTimeOffset now)
    {
        while (requestStarts.Count > 0 &&
            now - requestStarts.Peek() >= robustWindow)
        {
            requestStarts.Dequeue();
        }
    }
}
