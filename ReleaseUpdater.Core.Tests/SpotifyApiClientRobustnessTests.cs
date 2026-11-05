using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Spotify.Api;
using ReleaseUpdater.Spotify.Auth;

namespace ReleaseUpdater.Core.Tests;

// How the API client behaves when Spotify says "no" in ways that are not the caller's fault:
// a token that was refused (401), and server errors for requests that must not be repeated blindly.
public sealed class SpotifyApiClientRobustnessTests
{
    private const string UserJson = """{ "id": "user-1", "display_name": "Test User" }""";

    // ---- 401 ----

    // Verifies that a 401 gets one new token and the same request is sent again with it.
    [Fact]
    public async Task Unauthorized_GetsAFreshToken_AndTheRequestIsRepeatedOnce()
    {
        RotatingTokenProvider tokens = new();
        StubHandler handler = new();
        handler.Enqueue(HttpStatusCode.Unauthorized, """{ "error": { "status": 401, "message": "The access token expired" } }""");
        handler.Enqueue(HttpStatusCode.OK, UserJson);

        SpotifyUser user = await new SpotifyApiClient(tokens, new HttpClient(handler)).GetCurrentUserAsync(CancellationToken.None);

        Assert.Equal("user-1", user.Id);
        Assert.Equal(1, tokens.Rejections);
        Assert.Equal(new[] { "Bearer old", "Bearer new" }, handler.Requests.Select(request => request.Authorization));
    }

    // Verifies that a second 401 is a real "no": it fails with the details instead of looping.
    [Fact]
    public async Task Unauthorized_Twice_Fails_AfterOneRepeat()
    {
        RotatingTokenProvider tokens = new();
        StubHandler handler = new();
        handler.Enqueue(HttpStatusCode.Unauthorized, "{}");
        handler.Enqueue(HttpStatusCode.Unauthorized, "{}");

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SpotifyApiClient(tokens, new HttpClient(handler)).GetCurrentUserAsync(CancellationToken.None));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(1, tokens.Rejections);
        Assert.Contains("401", exception.Message);
    }

    // Verifies that a token provider that cannot refresh gets no second try (nothing would change).
    [Fact]
    public async Task Unauthorized_WithAProviderThatCannotRefresh_FailsAtOnce()
    {
        StubHandler handler = new();
        handler.Enqueue(HttpStatusCode.Unauthorized, "{}");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SpotifyApiClient(new FixedTokenProvider(), new HttpClient(handler)).GetCurrentUserAsync(CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    // Verifies that every request has its own one repeat (an earlier 401 does not use it up for later ones).
    [Fact]
    public async Task Unauthorized_EachRequestHasItsOwnRepeat()
    {
        RotatingTokenProvider tokens = new();
        StubHandler handler = new();
        handler.Enqueue(HttpStatusCode.Unauthorized, "{}");
        handler.Enqueue(HttpStatusCode.OK, UserJson);
        handler.Enqueue(HttpStatusCode.Unauthorized, "{}");
        handler.Enqueue(HttpStatusCode.OK, UserJson);
        SpotifyApiClient client = new(tokens, new HttpClient(handler));

        await client.GetCurrentUserAsync(CancellationToken.None);
        await client.GetCurrentUserAsync(CancellationToken.None);

        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(2, tokens.Rejections);
    }

    // Verifies that a 403 (not allowlisted) is not mistaken for a token problem: no refresh, one request.
    [Fact]
    public async Task Forbidden_IsNotRetried_AndDoesNotRefreshTheToken()
    {
        RotatingTokenProvider tokens = new();
        StubHandler handler = new();
        handler.Enqueue(HttpStatusCode.Forbidden, "{}");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SpotifyApiClient(tokens, new HttpClient(handler)).GetCurrentUserAsync(CancellationToken.None));

        Assert.Single(handler.Requests);
        Assert.Equal(0, tokens.Rejections);
    }

    // ---- 5xx and requests that must not be repeated ----

    // Verifies that a POST is not sent again after a server error: it may have worked, and a second
    // one would add the tracks twice.
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task AddingTracks_IsNotRepeated_AfterAServerError(HttpStatusCode status)
    {
        StubHandler handler = new();
        handler.Enqueue(status, "{}");
        handler.Enqueue(HttpStatusCode.OK, "{}");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SpotifyApiClient(new FixedTokenProvider(), new HttpClient(handler))
                .AddTracksToPlaylistAsync("playlist-1", new[] { "spotify:track:1" }, CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    // Verifies the same for creating a playlist (a second POST would make a second playlist).
    [Fact]
    public async Task CreatingAPlaylist_IsNotRepeated_AfterAServerError()
    {
        StubHandler handler = new();
        handler.Enqueue(HttpStatusCode.BadGateway, "{}");
        handler.Enqueue(HttpStatusCode.OK, """{ "id": "new-playlist" }""");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SpotifyApiClient(new FixedTokenProvider(), new HttpClient(handler))
                .CreatePlaylistAsync("name", "description", CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    // Verifies that a 429 is still repeated for a POST: Spotify refused it before doing anything.
    [Fact]
    public async Task AddingTracks_IsRepeated_AfterATooManyRequestsAnswer()
    {
        StubHandler handler = new();
        handler.Enqueue(HttpStatusCode.TooManyRequests, "{}", retryAfter: TimeSpan.Zero);
        handler.Enqueue(HttpStatusCode.OK, "{}");

        await new SpotifyApiClient(new FixedTokenProvider(), new HttpClient(handler))
            .AddTracksToPlaylistAsync("playlist-1", new[] { "spotify:track:1" }, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    // Verifies that requests that can safely be repeated (GET, PUT) still are after a server error.
    [Fact]
    public async Task ReadingAndReplacing_AreRepeated_AfterAServerError()
    {
        StubHandler handler = new();
        handler.Enqueue(HttpStatusCode.InternalServerError, "{}", retryAfter: TimeSpan.Zero);
        handler.Enqueue(HttpStatusCode.OK, "{}");
        handler.Enqueue(HttpStatusCode.BadGateway, "{}", retryAfter: TimeSpan.Zero);
        handler.Enqueue(HttpStatusCode.OK, UserJson);
        SpotifyApiClient client = new(new FixedTokenProvider(), new HttpClient(handler));

        await client.ReplacePlaylistTracksAsync("playlist-1", new[] { "spotify:track:1" }, CancellationToken.None);
        await client.GetCurrentUserAsync(CancellationToken.None);

        Assert.Equal(new[] { HttpMethod.Put, HttpMethod.Put, HttpMethod.Get, HttpMethod.Get }, handler.Requests.Select(request => request.Method));
    }

    // ---- unusual answers ----

    // Verifies that a followed-artists page without cursors (or with "cursors": null) ends the list.
    [Theory]
    [InlineData("""{ "artists": { "items": [{ "id": "a1", "name": "One" }] } }""")]
    [InlineData("""{ "artists": { "items": [{ "id": "a1", "name": "One" }], "cursors": null } }""")]
    [InlineData("""{ "artists": { "items": [{ "id": "a1", "name": "One" }], "cursors": {} } }""")]
    [InlineData("""{ "artists": { "items": [{ "id": "a1", "name": "One" }], "cursors": { "after": null } } }""")]
    [InlineData("""{ "artists": { "items": [{ "id": "a1", "name": "One" }], "cursors": { "after": "" } } }""")]
    public async Task FollowedArtists_WithoutACursor_EndTheList(string page)
    {
        StubHandler handler = new();
        handler.Enqueue(HttpStatusCode.OK, page);

        IReadOnlyList<SpotifyArtist> artists = await new SpotifyApiClient(new FixedTokenProvider(), new HttpClient(handler))
            .GetFollowedArtistsAsync(CancellationToken.None);

        Assert.Equal("a1", Assert.Single(artists).Id);
        Assert.Single(handler.Requests);
    }

    private sealed class FixedTokenProvider : ISpotifyAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult("fixed");
    }

    // Hands out "old" until it is told the token was refused, then "new".
    private sealed class RotatingTokenProvider : ISpotifyAccessTokenProvider, ISpotifyAccessTokenInvalidation
    {
        private bool rejected;

        public int Rejections { get; private set; }

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            Task.FromResult(rejected ? "new" : "old");

        public void MarkAccessTokenRejected()
        {
            Rejections++;
            rejected = true;
        }
    }

    private sealed record Captured(HttpMethod Method, string? Authorization);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Json, TimeSpan? RetryAfter)> responses = new();

        public List<Captured> Requests { get; } = new();

        public void Enqueue(HttpStatusCode status, string json, TimeSpan? retryAfter = null) =>
            responses.Enqueue((status, json, retryAfter));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Captured(request.Method, request.Headers.Authorization?.ToString()));

            if (responses.Count == 0)
            {
                throw new InvalidOperationException("No fake HTTP response was queued.");
            }

            (HttpStatusCode status, string json, TimeSpan? retryAfter) = responses.Dequeue();
            HttpResponseMessage response = new(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            if (retryAfter is not null)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
            }

            return Task.FromResult(response);
        }
    }
}
