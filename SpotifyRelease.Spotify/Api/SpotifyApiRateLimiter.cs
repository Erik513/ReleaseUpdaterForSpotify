using SpotifyRelease.Core.Models;

namespace SpotifyRelease.Spotify.Api;

internal sealed class SpotifyApiRateLimiter
{
    private readonly object modeLock = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Queue<DateTimeOffset> requestStarts = new();
    private readonly int robustRequestBudget;
    private readonly TimeSpan robustWindow;
    private bool isRobustMode;

    public SpotifyApiRateLimiter()
        : this(
            ReleaseDefaults.SpotifyRobustRequestBudget,
            TimeSpan.FromSeconds(ReleaseDefaults.SpotifyRobustWindowSeconds))
    {
    }

    public SpotifyApiRateLimiter(int robustRequestBudget, TimeSpan robustWindow)
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

        this.robustRequestBudget = robustRequestBudget;
        this.robustWindow = robustWindow;
    }

    public bool EnableRobustMode()
    {
        lock (modeLock)
        {
            if (isRobustMode)
            {
                return false;
            }

            isRobustMode = true;
            return true;
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
            return isRobustMode;
        }
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
