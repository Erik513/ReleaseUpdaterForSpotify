using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SpotifyRelease.Core.Abstractions;
using SpotifyRelease.Core.Models;
using SpotifyRelease.Spotify.Auth;

namespace SpotifyRelease.Spotify.Api;

public sealed class SpotifyApiClient : ISpotifyGateway, ISpotifyRequestDiagnostics
{
    private static readonly Uri ApiBaseUri = new("https://api.spotify.com/v1/");
    private const int MaxRetryAttempts = 5;

    private readonly ISpotifyAccessTokenProvider accessTokenProvider;
    private readonly HttpClient httpClient;
    private readonly SpotifyApiRateLimiter rateLimiter;

    public IProgress<ReleaseProgress>? Progress { get; set; }

    public SpotifyApiClient(
        ISpotifyAccessTokenProvider accessTokenProvider,
        HttpClient httpClient)
    {
        this.accessTokenProvider = accessTokenProvider;
        this.httpClient = httpClient;
        rateLimiter = new SpotifyApiRateLimiter();
    }

    public async Task<SpotifyUser> GetCurrentUserAsync(
        CancellationToken cancellationToken)
    {
        using JsonDocument json =
            await SendJsonAsync(HttpMethod.Get, "me", null, false, cancellationToken);

        JsonElement root = json.RootElement;
        string id = GetRequiredString(root, "id");
        string displayName = root.TryGetProperty("display_name", out JsonElement displayNameElement)
            ? displayNameElement.GetString() ?? id
            : id;

        return new SpotifyUser(id, displayName);
    }

    /// <summary>
    /// Loads all followed artists page by page through Spotify's cursor API.
    /// </summary>
    public async Task<IReadOnlyList<SpotifyArtist>> GetFollowedArtistsAsync(
        CancellationToken cancellationToken)
    {
        List<SpotifyArtist> artists = new();
        string? after = null;

        while (true)
        {
            string path =
                $"me/following?type=artist&limit={ReleaseDefaults.ArtistPageSize}";

            if (!string.IsNullOrWhiteSpace(after))
            {
                path += $"&after={Uri.EscapeDataString(after)}";
            }

            using JsonDocument json =
                await SendJsonAsync(HttpMethod.Get, path, null, false, cancellationToken);

            JsonElement artistsElement = json.RootElement.GetProperty("artists");

            foreach (JsonElement item in artistsElement.GetProperty("items").EnumerateArray())
            {
                artists.Add(ParseArtist(item));
            }

            after = artistsElement
                .GetProperty("cursors")
                .TryGetProperty("after", out JsonElement afterElement)
                    ? afterElement.GetString()
                    : null;

            if (string.IsNullOrWhiteSpace(after))
            {
                break;
            }
        }

        return artists;
    }

    /// <summary>
    /// Loads albums and singles for one artist using the same limit as the Python version.
    /// </summary>
    public async Task<IReadOnlyList<SpotifyAlbum>> GetArtistAlbumsAsync(
        string artistId,
        CancellationToken cancellationToken)
    {
        string path =
            $"artists/{Uri.EscapeDataString(artistId)}/albums" +
            $"?include_groups=album,single&limit={ReleaseDefaults.AlbumPageSize}";

        using JsonDocument json =
            await SendJsonAsync(HttpMethod.Get, path, null, false, cancellationToken);

        List<SpotifyAlbum> albums = new();

        foreach (JsonElement item in json.RootElement.GetProperty("items").EnumerateArray())
        {
            albums.Add(ParseAlbum(item));
        }

        return albums;
    }

    /// <summary>
    /// Loads album tracks and keeps reading pages for longer albums.
    /// </summary>
    public async Task<IReadOnlyList<SpotifyTrackItem>> GetAlbumTracksAsync(
        string albumId,
        CancellationToken cancellationToken)
    {
        List<SpotifyTrackItem> tracks = new();
        int offset = 0;

        while (true)
        {
            string path =
                $"albums/{Uri.EscapeDataString(albumId)}/tracks" +
                $"?limit={ReleaseDefaults.TrackPageSize}&offset={offset}";

            using JsonDocument json =
                await SendJsonAsync(HttpMethod.Get, path, null, false, cancellationToken);

            JsonElement root = json.RootElement;
            JsonElement items = root.GetProperty("items");

            foreach (JsonElement item in items.EnumerateArray())
            {
                tracks.Add(ParseTrack(item, null));
            }

            int itemCount = items.GetArrayLength();
            int total = root.GetProperty("total").GetInt32();
            offset += itemCount;

            if (offset >= total || itemCount == 0)
            {
                break;
            }
        }

        return tracks;
    }

    /// <summary>
    /// Searches tracks by artist name to catch additional songs from Spotify search.
    /// </summary>
    public async Task<IReadOnlyList<SpotifyTrackItem>> SearchTracksByArtistAsync(
        string artistName,
        CancellationToken cancellationToken)
    {
        string query = Uri.EscapeDataString($"artist:\"{artistName}\"");
        string path =
            $"search?q={query}&type=track&limit={ReleaseDefaults.SearchResultLimit}";

        using JsonDocument json =
            await SendJsonAsync(HttpMethod.Get, path, null, false, cancellationToken);

        List<SpotifyTrackItem> tracks = new();

        foreach (JsonElement item in json.RootElement
            .GetProperty("tracks")
            .GetProperty("items")
            .EnumerateArray())
        {
            SpotifyAlbum album = ParseAlbum(item.GetProperty("album"));
            tracks.Add(ParseTrack(item, album));
        }

        return tracks;
    }

    public async Task<SpotifyPlaylist?> TryGetPlaylistAsync(
        string playlistId,
        CancellationToken cancellationToken)
    {
        using JsonDocument? json = await SendJsonOrNullAsync(
            HttpMethod.Get,
            $"playlists/{Uri.EscapeDataString(playlistId)}",
            null,
            cancellationToken);

        if (json is null)
        {
            return null;
        }

        return ParsePlaylist(json.RootElement);
    }

    /// <summary>
    /// Loads playlists that Spotify currently lists for the signed-in user.
    /// </summary>
    public async Task<IReadOnlyList<SpotifyPlaylist>> GetCurrentUserPlaylistsAsync(
        CancellationToken cancellationToken)
    {
        List<SpotifyPlaylist> playlists = new();
        int offset = 0;

        while (true)
        {
            string path =
                $"me/playlists?limit={ReleaseDefaults.PlaylistPageSize}&offset={offset}";

            using JsonDocument json =
                await SendJsonAsync(HttpMethod.Get, path, null, false, cancellationToken);

            JsonElement root = json.RootElement;
            JsonElement items = root.GetProperty("items");

            foreach (JsonElement item in items.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                playlists.Add(ParsePlaylist(item));
            }

            int itemCount = items.GetArrayLength();
            int total = root.GetProperty("total").GetInt32();
            offset += itemCount;

            if (itemCount == 0 || offset >= total)
            {
                return playlists;
            }
        }
    }

    public async Task<string> CreatePlaylistAsync(
        string name,
        string description,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            name,
            @public = true,
            description
        };

        using JsonDocument json = await SendJsonAsync(
            HttpMethod.Post,
            "me/playlists",
            body,
            false,
            cancellationToken);

        return GetRequiredString(json.RootElement, "id");
    }

    public Task UpdatePlaylistDetailsAsync(
        string playlistId,
        string name,
        string description,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            name,
            description
        };

        return SendWithoutBodyAsync(
            HttpMethod.Put,
            $"playlists/{Uri.EscapeDataString(playlistId)}",
            body,
            cancellationToken);
    }

    /// <summary>
    /// Replaces the playlist contents first, then appends remaining tracks in Spotify-sized batches.
    /// </summary>
    public async Task ReplacePlaylistTracksAsync(
        string playlistId,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken)
    {
        string[] firstChunk = trackUris
            .Take(ReleaseDefaults.PlaylistBatchSize)
            .ToArray();

        var body = new
        {
            uris = firstChunk
        };

        await SendWithoutBodyAsync(
            HttpMethod.Put,
            $"playlists/{Uri.EscapeDataString(playlistId)}/tracks",
            body,
            cancellationToken);

        if (trackUris.Count <= firstChunk.Length)
        {
            return;
        }

        IReadOnlyList<string> remainingTrackUris = trackUris
            .Skip(firstChunk.Length)
            .ToList();

        await AddTracksToPlaylistAsync(
            playlistId,
            remainingTrackUris,
            cancellationToken);
    }

    /// <summary>
    /// Spotify accepts up to 100 playlist URIs per request, so tracks are batched.
    /// </summary>
    public async Task AddTracksToPlaylistAsync(
        string playlistId,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken)
    {
        for (int index = 0; index < trackUris.Count; index += ReleaseDefaults.PlaylistBatchSize)
        {
            string[] chunk = trackUris
                .Skip(index)
                .Take(ReleaseDefaults.PlaylistBatchSize)
                .ToArray();

            var body = new
            {
                uris = chunk
            };

            await SendWithoutBodyAsync(
                HttpMethod.Post,
                $"playlists/{Uri.EscapeDataString(playlistId)}/tracks",
                body,
                cancellationToken);
        }
    }

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string path,
        object? body,
        bool allowNotFound,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(
            method,
            path,
            body,
            allowNotFound,
            cancellationToken);

        await using Stream stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        return await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
    }

    private async Task<JsonDocument?> SendJsonOrNullAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(
            method,
            path,
            body,
            true,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await using Stream stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        return await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
    }

    private async Task SendWithoutBodyAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(
            method,
            path,
            body,
            false,
            cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        bool allowNotFound,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            string accessToken =
                await accessTokenProvider.GetAccessTokenAsync(cancellationToken);

            using HttpRequestMessage request =
                new(method, new Uri(ApiBaseUri, path));

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);

            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            await rateLimiter.WaitForSlotAsync(cancellationToken);

            HttpResponseMessage response =
                await httpClient.SendAsync(request, cancellationToken);

            if (IsRetryable(response.StatusCode) && attempt < MaxRetryAttempts)
            {
                EnableRobustModeWhenNeeded(response.StatusCode, attempt);
                TimeSpan retryDelay = GetRetryDelay(response, attempt);
                response.Dispose();
                await Task.Delay(retryDelay, cancellationToken);
                continue;
            }

            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return response;
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            string responseText =
                await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                TimeSpan retryDelay = GetRetryDelay(response, attempt);
                response.Dispose();

                throw new SpotifyRateLimitException(
                    method.Method,
                    path,
                    retryDelay,
                    responseText);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                response.Dispose();

                throw new InvalidOperationException(
                    "Spotify denied access for this account. If you use the shared app, your account may not be allowlisted. Add your own Spotify Client ID or ask the developer to allowlist your account.");
            }

            response.Dispose();

            throw new InvalidOperationException(
                $"Spotify API error after {attempt} attempt(s) on {method.Method} /{path}: {(int)response.StatusCode} {response.StatusCode} {responseText}");
        }

        throw new InvalidOperationException("Spotify API request failed after all retry attempts.");
    }

    private static bool IsRetryable(HttpStatusCode statusCode)
    {
        int statusCodeNumber = (int)statusCode;

        return statusCode == HttpStatusCode.TooManyRequests ||
            statusCodeNumber is >= 500 and <= 599;
    }

    private void EnableRobustModeWhenNeeded(HttpStatusCode statusCode, int attempt)
    {
        bool shouldSwitch =
            statusCode == HttpStatusCode.TooManyRequests ||
            attempt >= 2;

        if (!shouldSwitch || !rateLimiter.EnableRobustMode())
        {
            return;
        }

        string message = statusCode == HttpStatusCode.TooManyRequests
            ? "Spotify request limit reached. Switching to robust request mode."
            : "Spotify returned repeated temporary errors. Switching to robust request mode.";

        Progress?.Report(new ReleaseProgress(string.Empty, Message: message));
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
        {
            return delta;
        }

        int delaySeconds = Math.Min(30, attempt * attempt * 2);
        return TimeSpan.FromSeconds(delaySeconds);
    }

    private static SpotifyArtist ParseArtist(JsonElement item) =>
        new(
            GetRequiredString(item, "id"),
            GetRequiredString(item, "name"));

    private static SpotifyAlbum ParseAlbum(JsonElement item) =>
        new(
            GetRequiredString(item, "id"),
            GetRequiredString(item, "name"),
            TryParseReleaseDate(item));

    private static SpotifyTrackItem ParseTrack(
        JsonElement item,
        SpotifyAlbum? album)
    {
        return new SpotifyTrackItem(
            GetRequiredString(item, "id"),
            GetRequiredString(item, "name"),
            ParseArtists(item.GetProperty("artists")),
            GetRequiredString(item, "uri"),
            GetSpotifyUrl(item),
            album);
    }

    private static IReadOnlyList<SpotifyArtist> ParseArtists(JsonElement artistsElement)
    {
        List<SpotifyArtist> artists = new();

        foreach (JsonElement artistElement in artistsElement.EnumerateArray())
        {
            artists.Add(ParseArtist(artistElement));
        }

        return artists;
    }

    private static DateOnly? TryParseReleaseDate(JsonElement item)
    {
        string? releaseDateText = item.TryGetProperty("release_date", out JsonElement releaseDateElement)
            ? releaseDateElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(releaseDateText) || releaseDateText.Length != 10)
        {
            return null;
        }

        return DateOnly.TryParseExact(
            releaseDateText,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateOnly date)
                ? date
                : null;
    }

    private static string GetSpotifyUrl(JsonElement item)
    {
        if (item.TryGetProperty("external_urls", out JsonElement externalUrls)
            && externalUrls.TryGetProperty("spotify", out JsonElement spotifyUrl))
        {
            return spotifyUrl.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static SpotifyPlaylist ParsePlaylist(JsonElement item) =>
        new(
            GetRequiredString(item, "id"),
            GetRequiredString(item, "name"),
            GetRequiredString(item.GetProperty("owner"), "id"));

    private static string GetRequiredString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out JsonElement element))
        {
            throw new InvalidOperationException(
                $"Spotify response does not contain '{propertyName}'.");
        }

        return element.GetString()
            ?? throw new InvalidOperationException(
                $"Spotify response does not contain text for '{propertyName}'.");
    }
}
