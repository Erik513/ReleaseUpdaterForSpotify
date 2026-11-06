namespace ReleaseUpdater.Core.Services;

/// <summary>
/// The link of a tracked song (where the user found the unreleased song: a video, a set, a post). It is free text that the user typed or
/// pasted, so before it is opened or put into a report as a link it is checked: only http and https, with a host.
/// </summary>
public static class TrackedSongLinks
{
    /// <summary>
    /// The address to open for what the user wrote: "https://..." as it is, "www.example.com/x" and "example.com/x" with https in front;
    /// null for anything else (empty, other schemes such as file: or javascript:, text with spaces, no host).
    /// </summary>
    public static string? Normalize(string? text)
    {
        string trimmed = text?.Trim() ?? string.Empty;

        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace) || trimmed.Any(char.IsControl))
        {
            return null;
        }

        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            // (A scheme without "//" - "javascript:alert(1)", "mailto:x" - is no address either: a colon before the first slash.)
            int colon = trimmed.IndexOf(':');
            int slash = trimmed.IndexOf('/');

            if (colon >= 0 && (slash < 0 || colon < slash) && !IsPort(trimmed, colon))
            {
                return null;
            }

            if (!trimmed.Contains('.', StringComparison.Ordinal))
            {
                return null;
            }

            trimmed = "https://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host) ||
            !uri.Host.Contains('.', StringComparison.Ordinal))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    /// <summary>Whether a cell of a table looks like a link (begins with http://, https:// or www.): used to find the column of the links.</summary>
    public static bool LooksLikeLink(string? text)
    {
        string trimmed = text?.Trim() ?? string.Empty;

        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
    }

    // "example.com:8080/x" - the colon is a port, not a scheme.
    private static bool IsPort(string text, int colon)
    {
        int end = colon + 1;

        while (end < text.Length && char.IsDigit(text[end]))
        {
            end++;
        }

        return end > colon + 1 && (end == text.Length || text[end] == '/');
    }
}
