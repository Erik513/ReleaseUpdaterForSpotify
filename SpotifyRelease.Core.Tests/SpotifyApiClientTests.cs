using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SpotifyRelease.Core.Models;
using SpotifyRelease.Spotify.Api;
using SpotifyRelease.Spotify.Auth;

namespace SpotifyRelease.Core.Tests;

public sealed class SpotifyApiClientTests
{
    // Verifies that followed artists are loaded across Spotify cursor pages.
    [Fact]
    public async Task GetFollowedArtistsAsync_LoadsAllCursorPages()
    {
        StubHttpMessageHandler handler = new();
        SpotifyApiClient client = CreateClient(handler);
        handler.EnqueueJson("""
            {
              "artists": {
                "items": [{ "id": "artist-1", "name": "Artist One" }],
                "cursors": { "after": "cursor-1" }
              }
            }
            """);
        handler.EnqueueJson("""
            {
              "artists": {
                "items": [{ "id": "artist-2", "name": "Artist Two" }],
                "cursors": { "after": null }
              }
            }
            """);

        IReadOnlyList<SpotifyArtist> artists =
            await client.GetFollowedArtistsAsync(CancellationToken.None);

        Assert.Equal(new[] { "artist-1", "artist-2" }, artists.Select(artist => artist.Id));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(
            "/v1/me/following?type=artist&limit=50",
            handler.Requests[0].PathAndQuery);
        Assert.Equal(
            "/v1/me/following?type=artist&limit=50&after=cursor-1",
            handler.Requests[1].PathAndQuery);
    }

    // Verifies that album tracks are loaded page by page until Spotify's total count is reached.
    [Fact]
    public async Task GetAlbumTracksAsync_LoadsAllOffsetPages()
    {
        StubHttpMessageHandler handler = new();
        SpotifyApiClient client = CreateClient(handler);
        handler.EnqueueJson("""
            {
              "items": [
                {
                  "id": "track-1",
                  "name": "Song One",
                  "uri": "spotify:track:1",
                  "artists": [{ "id": "artist-1", "name": "Artist One" }],
                  "external_urls": { "spotify": "https://open.spotify.com/track/1" }
                }
              ],
              "total": 2
            }
            """);
        handler.EnqueueJson("""
            {
              "items": [
                {
                  "id": "track-2",
                  "name": "Song Two",
                  "uri": "spotify:track:2",
                  "artists": [{ "id": "artist-1", "name": "Artist One" }],
                  "external_urls": { "spotify": "https://open.spotify.com/track/2" }
                }
              ],
              "total": 2
            }
            """);

        IReadOnlyList<SpotifyTrackItem> tracks =
            await client.GetAlbumTracksAsync("album-1", CancellationToken.None);

        Assert.Equal(new[] { "track-1", "track-2" }, tracks.Select(track => track.Id));
        Assert.Equal(
            new[]
            {
                "/v1/albums/album-1/tracks?limit=50&offset=0",
                "/v1/albums/album-1/tracks?limit=50&offset=1"
            },
            handler.Requests.Select(request => request.PathAndQuery));
    }

    // Verifies that current-user playlists are loaded across offset pages and null items are skipped.
    [Fact]
    public async Task GetCurrentUserPlaylistsAsync_LoadsPagesAndSkipsNullItems()
    {
        StubHttpMessageHandler handler = new();
        SpotifyApiClient client = CreateClient(handler);
        handler.EnqueueJson("""
            {
              "items": [
                { "id": "playlist-1", "name": "First", "owner": { "id": "user-1" } },
                null
              ],
              "total": 3
            }
            """);
        handler.EnqueueJson("""
            {
              "items": [
                { "id": "playlist-2", "name": "Second", "owner": { "id": "user-1" } }
              ],
              "total": 3
            }
            """);

        IReadOnlyList<SpotifyPlaylist> playlists =
            await client.GetCurrentUserPlaylistsAsync(CancellationToken.None);

        Assert.Equal(new[] { "playlist-1", "playlist-2" }, playlists.Select(playlist => playlist.Id));
        Assert.Equal(
            new[]
            {
                "/v1/me/playlists?limit=50&offset=0",
                "/v1/me/playlists?limit=50&offset=2"
            },
            handler.Requests.Select(request => request.PathAndQuery));
    }

    // Verifies that a missing playlist is treated as null instead of failing the update flow.
    [Fact]
    public async Task TryGetPlaylistAsync_ReturnsNullForNotFound()
    {
        StubHttpMessageHandler handler = new();
        SpotifyApiClient client = CreateClient(handler);
        handler.EnqueueJson("{}", HttpStatusCode.NotFound);

        SpotifyPlaylist? playlist =
            await client.TryGetPlaylistAsync("missing-playlist", CancellationToken.None);

        Assert.Null(playlist);
        Assert.Equal("/v1/playlists/missing-playlist", handler.Requests.Single().PathAndQuery);
    }

    // Verifies that 429 responses are retried, switch to robust mode, and report a log message.
    [Fact]
    public async Task GetCurrentUserAsync_RetriesTooManyRequestsAndReportsRobustMode()
    {
        StubHttpMessageHandler handler = new();
        SpotifyApiClient client = CreateClient(handler);
        ProgressRecorder progress = new();
        client.Progress = progress;
        handler.EnqueueJson(
            """{ "error": { "status": 429, "message": "Too many requests" } }""",
            HttpStatusCode.TooManyRequests,
            retryAfter: TimeSpan.Zero);
        handler.EnqueueJson("""{ "id": "user-1", "display_name": "Test User" }""");

        SpotifyUser user = await client.GetCurrentUserAsync(CancellationToken.None);

        Assert.Equal("user-1", user.Id);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains(
            progress.Items,
            item => item.Message == "Spotify request limit reached. Switching to robust request mode.");
    }

    // Verifies that repeated temporary Spotify errors switch to robust mode before retrying again.
    [Fact]
    public async Task GetCurrentUserAsync_RetriesRepeatedServerErrorsAndReportsRobustMode()
    {
        StubHttpMessageHandler handler = new();
        SpotifyApiClient client = CreateClient(handler);
        ProgressRecorder progress = new();
        client.Progress = progress;
        handler.EnqueueJson(
            """{ "error": { "status": 500, "message": "Temporary" } }""",
            HttpStatusCode.InternalServerError,
            retryAfter: TimeSpan.Zero);
        handler.EnqueueJson(
            """{ "error": { "status": 502, "message": "Temporary" } }""",
            HttpStatusCode.BadGateway,
            retryAfter: TimeSpan.Zero);
        handler.EnqueueJson("""{ "id": "user-1", "display_name": "Test User" }""");

        SpotifyUser user = await client.GetCurrentUserAsync(CancellationToken.None);

        Assert.Equal("Test User", user.DisplayName);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains(
            progress.Items,
            item => item.Message == "Spotify returned repeated temporary errors. Switching to robust request mode.");
    }

    // Verifies that non-retryable Spotify errors fail immediately with endpoint details.
    [Fact]
    public async Task GetCurrentUserAsync_DoesNotRetryBadRequest()
    {
        StubHttpMessageHandler handler = new();
        SpotifyApiClient client = CreateClient(handler);
        handler.EnqueueJson(
            """{ "error": { "status": 400, "message": "Bad request" } }""",
            HttpStatusCode.BadRequest);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetCurrentUserAsync(CancellationToken.None));

        Assert.Single(handler.Requests);
        Assert.Contains("GET /me", exception.Message);
        Assert.Contains("400 BadRequest", exception.Message);
    }

    // Verifies that playlist replacement uses Spotify's 100-track batch size.
    [Fact]
    public async Task ReplacePlaylistTracksAsync_SendsPutThenPostBatchesOfOneHundred()
    {
        StubHttpMessageHandler handler = new();
        SpotifyApiClient client = CreateClient(handler);
        List<string> trackUris = Enumerable
            .Range(1, 205)
            .Select(index => $"spotify:track:{index}")
            .ToList();
        handler.EnqueueJson("{}");
        handler.EnqueueJson("{}");
        handler.EnqueueJson("{}");

        await client.ReplacePlaylistTracksAsync(
            "playlist-1",
            trackUris,
            CancellationToken.None);

        Assert.Equal(
            new[] { HttpMethod.Put, HttpMethod.Post, HttpMethod.Post },
            handler.Requests.Select(request => request.Method));
        Assert.All(
            handler.Requests,
            request => Assert.Equal("/v1/playlists/playlist-1/tracks", request.PathAndQuery));
        Assert.Equal(new[] { 100, 100, 5 }, handler.Requests.Select(GetUriCount));
        Assert.Equal("Bearer token", handler.Requests[0].Authorization);
    }

    private static SpotifyApiClient CreateClient(StubHttpMessageHandler handler) =>
        new(new FakeTokenProvider(), new HttpClient(handler));

    private static int GetUriCount(CapturedRequest request)
    {
        using JsonDocument json = JsonDocument.Parse(request.Body ?? "{}");
        return json.RootElement.GetProperty("uris").GetArrayLength();
    }

    private sealed class FakeTokenProvider : ISpotifyAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            Task.FromResult("token");
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> responses = new();

        public List<CapturedRequest> Requests { get; } = new();

        public void EnqueueJson(
            string json,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            TimeSpan? retryAfter = null)
        {
            responses.Enqueue(_ =>
            {
                HttpResponseMessage response = new(statusCode)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                if (retryAfter is not null)
                {
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
                }

                return response;
            });
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string? body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri?.PathAndQuery ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                body));

            if (responses.Count == 0)
            {
                throw new InvalidOperationException("No fake HTTP response was queued.");
            }

            return responses.Dequeue()(request);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string PathAndQuery,
        string? Authorization,
        string? Body);

    private sealed class ProgressRecorder : IProgress<ReleaseProgress>
    {
        public List<ReleaseProgress> Items { get; } = new();

        public void Report(ReleaseProgress value)
        {
            Items.Add(value);
        }
    }
}
