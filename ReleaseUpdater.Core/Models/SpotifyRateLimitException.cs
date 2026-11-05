namespace ReleaseUpdater.Core.Models;

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

    /// <summary>
    /// True when Spotify says the quota of the developer account is used up (<c>"reason": "QUOTA_EXCEEDED"</c> in the 429 body), not
    /// just a burst of requests. The quota is counted per developer account (since July 2026), so waiting a few seconds or using another
    /// Client ID of the same account does not help.
    /// </summary>
    public bool IsQuotaExceeded => IsQuotaExceededResponse(ResponseText);

    public static bool IsQuotaExceededResponse(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return false;
        }

        try
        {
            using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(responseText);

            return json.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                json.RootElement.TryGetProperty("reason", out System.Text.Json.JsonElement reason) &&
                reason.ValueKind == System.Text.Json.JsonValueKind.String &&
                string.Equals(reason.GetString(), "QUOTA_EXCEEDED", StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

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
