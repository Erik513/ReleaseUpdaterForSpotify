using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Spotify.Api;
using ReleaseUpdater.Spotify.Auth;

namespace ReleaseUpdater.Core.Tests;

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

    // Verifies that the albums of an artist are requested one group at a time (album, single): Spotify lists one group newest first, so
    // each can be read only as far as needed. "appears_on" is NOT requested: Spotify lists it in no order of date, so only the whole list
    // (up to 46 pages for one artist) could be read.
    [Fact]
    public async Task GetArtistAlbumsAsync_RequestsAlbumAndSingleOnTheirOwn_AndNotAppearsOn()
    {
        StubHttpMessageHandler handler = new();
        SpotifyApiClient client = CreateClient(handler);
        handler.EnqueueJson("""{ "items": [], "total": 0 }""");
        handler.EnqueueJson("""{ "items": [], "total": 0 }""");

        await client.GetArtistAlbumsAsync("artist-1", null, CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "/v1/artists/artist-1/albums?include_groups=album&limit=10&offset=0",
                "/v1/artists/artist-1/albums?include_groups=single&limit=10&offset=0"
            },
            handler.Requests.Select(request => request.PathAndQuery));
    }

    // A page of the albums of an artist as Spotify sends it: one album per date, "total" for the whole list.
    private static string AlbumPage(int total, params string[] dates) =>
        "{ \"total\": " + total + ", \"items\": [" +
        string.Join(",", dates.Select((date, index) => "{ \"id\": \"album-" + date + "-" + index + "\", \"name\": \"A\", \"release_date\": \"" + date + "\" }")) +
        "] }";

    // Verifies that a compilation (a sampler such as "2010 European Club Hits": old songs, new date) is not taken as a release of new
    // songs, while the album next to it is.
    [Fact]
    public async Task GetArtistAlbumsAsync_LeavesCompilationsOut()
    {
        StubHttpMessageHandler handler = new();
        SpotifyApiClient client = CreateClient(handler);
        handler.EnqueueJson("""{ "items": [], "total": 0 }""");
        handler.EnqueueJson("""
            {
              "total": 2,
              "items": [
                { "id": "sampler", "name": "2010 European Club Hits", "album_type": "compilation", "release_date": "2026-11-08" },
                { "id": "feature", "name": "A Single", "album_type": "single", "release_date": "2026-11-07" }
              ]
            }
            """);

        IReadOnlyList<SpotifyAlbum> albums =
            await client.GetArtistAlbumsAsync("artist-1", new DateOnly(2026, 11, 1), CancellationToken.None);

        Assert.Equal(new[] { "feature" }, albums.Select(album => album.Id));
    }

    // Verifies that each group is read newest first only as far as the lookback reaches: once a whole page is older than the first wanted
    // day (with a margin), the pages behind it are not requested - a group with hundreds of entries costs 1 or 2 requests, not 10.
    [Fact]
    public async Task GetArtistAlbumsAsync_StopsWhenAWholePageIsOlderThanWanted()
    {
        StubHttpMessageHandler handler = new();
        SpotifyApiClient client = CreateClient(handler);
        handler.EnqueueJson(AlbumPage(1, "2026-11-05"));                    // album: one release, done
        handler.EnqueueJson(AlbumPage(100, "2026-11-04", "2026-10-20"));    // single: wanted, so the next page is read
        handler.EnqueueJson(AlbumPage(100, "2026-08-02", "2026-07-15"));    // single: all older than the day minus the margin: stop

        IReadOnlyList<SpotifyAlbum> albums =
            await client.GetArtistAlbumsAsync("artist-1", new DateOnly(2026, 11, 1), CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(5, albums.Count);
    }

    // Verifies that nothing is cut when no first wanted day is given (every page of every group).
    [Fact]
    public async Task GetArtistAlbumsAsync_WithoutALimitDate_ReadsEveryPage()
    {
        StubHttpMessageHandler handler = new();
        SpotifyApiClient client = CreateClient(handler);
        handler.EnqueueJson(AlbumPage(3, "2026-08-02", "2026-07-15"));
        handler.EnqueueJson(AlbumPage(3, "2026-06-01"));
        handler.EnqueueJson(AlbumPage(0));

        await client.GetArtistAlbumsAsync("artist-1", null, CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
    }

    // Verifies that a group that is not in the order newest first is read to the end (a later date after an earlier one: do not trust
    // the order, take every page).
    [Fact]
    public async Task GetArtistAlbumsAsync_ReadsEveryPage_WhenTheOrderIsNotNewestFirst()
    {
        StubHttpMessageHandler handler = new();
        SpotifyApiClient client = CreateClient(handler);
        handler.EnqueueJson(AlbumPage(3, "2026-03-01", "2026-09-01"));    // later after earlier: not newest first
        handler.EnqueueJson(AlbumPage(3, "2026-11-05"));
        handler.EnqueueJson(AlbumPage(0));

        IReadOnlyList<SpotifyAlbum> albums =
            await client.GetArtistAlbumsAsync("artist-1", new DateOnly(2026, 11, 1), CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains(albums, album => album.ReleaseDate == new DateOnly(2026, 11, 5));
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

    // Verifies that Spotify 429 remains actionable after all retry attempts are exhausted.
    [Fact]
    public async Task GetCurrentUserAsync_ThrowsRateLimitExceptionWhenRetriesAreExhausted()
    {
        StubHttpMessageHandler handler = new();
        SpotifyApiClient client = CreateClient(handler);

        for (int index = 0; index < 5; index++)
        {
            handler.EnqueueJson(
                """{ "error": { "status": 429, "message": "Too many requests" } }""",
                HttpStatusCode.TooManyRequests,
                retryAfter: TimeSpan.Zero);
        }

        SpotifyRateLimitException exception =
            await Assert.ThrowsAsync<SpotifyRateLimitException>(
                () => client.GetCurrentUserAsync(CancellationToken.None));

        Assert.Equal(5, handler.Requests.Count);
        Assert.Equal("GET", exception.Method);
        Assert.Equal("me", exception.Path);
    }

    // Verifies that a long Retry-After surfaces as a cooldown immediately instead of
    // silently retrying for minutes with no feedback.
    [Fact]
    public async Task GetCurrentUserAsync_ThrowsRateLimitExceptionImmediately_WhenRetryAfterIsLong()
    {
        StubHttpMessageHandler handler = new();
        SpotifyApiClient client = CreateClient(handler);
        TimeSpan longRetryAfter = TimeSpan.FromSeconds(45);

        handler.EnqueueJson(
            """{ "error": { "status": 429, "message": "Too many requests" } }""",
            HttpStatusCode.TooManyRequests,
            retryAfter: longRetryAfter);

        SpotifyRateLimitException exception =
            await Assert.ThrowsAsync<SpotifyRateLimitException>(
                () => client.GetCurrentUserAsync(CancellationToken.None));

        Assert.Single(handler.Requests);
        Assert.Equal(longRetryAfter, exception.RetryAfter);
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
            request => Assert.Equal("/v1/playlists/playlist-1/items", request.PathAndQuery));
        Assert.Equal(new[] { 100, 100, 5 }, handler.Requests.Select(GetUriCount));
        Assert.Equal("Bearer token", handler.Requests[0].Authorization);
    }

    // Verifies the search query is restricted to the lookback window's release
    // year(s), so an artist's older, more popular tracks can't crowd a recent
    // collaboration credit out of Spotify's relevance-ranked top results.
    [Fact]
    public async Task SearchTracksByArtistAsync_FiltersBySingleYear()
    {
        StubHttpMessageHandler handler = new();
        SpotifyApiClient client = CreateClient(handler);
        handler.EnqueueJson("""{ "tracks": { "items": [] } }""");

        await client.SearchTracksByArtistAsync(
            "Test Artist",
            2026,
            2026,
            CancellationToken.None);

        Assert.Equal(
            "/v1/search?q=artist%3A%22Test%20Artist%22%20year%3A2026&type=track&limit=10",
            handler.Requests.Single().PathAndQuery);
    }

    // Verifies a lookback window spanning a year boundary produces a Spotify year range.
    [Fact]
    public async Task SearchTracksByArtistAsync_FiltersByYearRangeAcrossYearBoundary()
    {
        StubHttpMessageHandler handler = new();
        SpotifyApiClient client = CreateClient(handler);
        handler.EnqueueJson("""{ "tracks": { "items": [] } }""");

        await client.SearchTracksByArtistAsync(
            "Test Artist",
            2025,
            2026,
            CancellationToken.None);

        Assert.Equal(
            "/v1/search?q=artist%3A%22Test%20Artist%22%20year%3A2025-2026&type=track&limit=10",
            handler.Requests.Single().PathAndQuery);
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
