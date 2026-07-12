namespace SpotifyRelease.Core.Models;

public sealed class SpotifyRateLimitException : InvalidOperationException
{
    public SpotifyRateLimitException(
        string method,
        string path,
        TimeSpan retryAfter,
        string responseText)
        : base(CreateMessage(method, path, retryAfter, responseText))
    {
        Method = method;
        Path = path;
        RetryAfter = retryAfter;
        ResponseText = responseText;
    }

    public string Method { get; }
    public string Path { get; }
    public TimeSpan RetryAfter { get; }
    public string ResponseText { get; }

    private static string CreateMessage(
        string method,
        string path,
        TimeSpan retryAfter,
        string responseText)
    {
        string retryText = retryAfter > TimeSpan.Zero
            ? $" Try again after {Math.Ceiling(retryAfter.TotalSeconds)} seconds."
            : string.Empty;

        return $"Spotify rate limit reached on {method} /{path}.{retryText} {responseText}".Trim();
    }
}
