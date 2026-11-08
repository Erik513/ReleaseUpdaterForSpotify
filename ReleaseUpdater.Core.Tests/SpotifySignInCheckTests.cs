using System.Net;
using System.Text;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Spotify.Auth;

namespace ReleaseUpdater.Core.Tests;

// "Is the user signed in?" has three answers: yes, no, and "could not find out" - a moment without
// network must not look like a sign-out (and must not throw away a good token).
public sealed class SpotifySignInCheckTests
{
    private const string MeJson = """{ "id": "user-1", "display_name": "Erik" }""";

    private static SpotifyToken ValidToken() => new()
    {
        AccessToken = "access",
        RefreshToken = "refresh",
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
        Scope = ReleaseDefaults.SpotifyScope,
        ClientId = "client-1"
    };

    private static SpotifyToken ExpiredToken()
    {
        SpotifyToken token = ValidToken();
        token.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);

        return token;
    }

    private static async Task<(SpotifySignInCheck Check, MemoryStore Store)> CheckAsync(
        SpotifyToken? token,
        RoutingHandler handler,
        string clientId = "client-1")
    {
        MemoryStore store = new(token);
        SpotifyAuthService service = new(store, new HttpClient(handler), new FixedClientId(clientId));

        return (await service.CheckSignInAsync(CancellationToken.None), store);
    }

    // Verifies the normal case: a good token and Spotify names the user.
    [Fact]
    public async Task ValidToken_AndSpotifyAnswers_IsSignedIn()
    {
        RoutingHandler handler = new() { Me = Respond(HttpStatusCode.OK, MeJson) };

        (SpotifySignInCheck check, _) = await CheckAsync(ValidToken(), handler);

        Assert.Equal(SpotifySignInState.SignedIn, check.State);
        Assert.Equal("Erik", check.User!.DisplayName);
        Assert.Equal("user-1", check.User.Id);
        Assert.Equal("Bearer access", handler.LastMeAuthorization);
    }

    // Verifies that the user's id stands in when Spotify gives no display name.
    [Fact]
    public async Task WithoutADisplayName_TheIdIsUsed()
    {
        RoutingHandler handler = new() { Me = Respond(HttpStatusCode.OK, """{ "id": "user-1" }""") };

        (SpotifySignInCheck check, _) = await CheckAsync(ValidToken(), handler);

        Assert.Equal("user-1", check.User!.DisplayName);
    }

    // Verifies that no token at all is "not signed in" - and Spotify is not even asked.
    [Fact]
    public async Task WithoutAToken_IsNotSignedIn_AndNothingIsAsked()
    {
        RoutingHandler handler = new();

        (SpotifySignInCheck check, _) = await CheckAsync(null, handler);

        Assert.Equal(SpotifySignInState.NotSignedIn, check.State);
        Assert.Equal(0, handler.RequestCount);
    }

    // Verifies that a token of another Client ID, or one without the required permissions, is "not signed in".
    [Fact]
    public async Task ATokenOfAnotherClient_IsNotSignedIn()
    {
        (SpotifySignInCheck check, _) = await CheckAsync(ValidToken(), new RoutingHandler(), clientId: "client-2");

        Assert.Equal(SpotifySignInState.NotSignedIn, check.State);
    }

    [Fact]
    public async Task ATokenWithoutPermissions_IsNotSignedIn()
    {
        SpotifyToken token = ValidToken();
        token.Scope = "user-follow-read";

        (SpotifySignInCheck check, _) = await CheckAsync(token, new RoutingHandler());

        Assert.Equal(SpotifySignInState.NotSignedIn, check.State);
    }

    // Verifies that Spotify refusing the token (4xx other than "try later") is "not signed in".
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task WhenSpotifyRefusesTheToken_IsNotSignedIn(HttpStatusCode status)
    {
        RoutingHandler handler = new() { Me = Respond(status, "{}") };

        (SpotifySignInCheck check, _) = await CheckAsync(ValidToken(), handler);

        Assert.Equal(SpotifySignInState.NotSignedIn, check.State);
    }

    // Verifies that Spotify having a problem or asking to wait is "could not find out", and that the
    // token is kept.
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task WhenSpotifyHasAProblem_IsUnavailable_AndTheTokenIsKept(HttpStatusCode status)
    {
        RoutingHandler handler = new() { Me = Respond(status, "{}") };

        (SpotifySignInCheck check, MemoryStore store) = await CheckAsync(ValidToken(), handler);

        Assert.Equal(SpotifySignInState.Unavailable, check.State);
        Assert.NotNull(store.Load());
    }

    // Verifies that a network failure while asking is "could not find out", and the token stays.
    [Fact]
    public async Task WhenTheNetworkFails_IsUnavailable_AndTheTokenIsKept()
    {
        RoutingHandler handler = new() { Me = _ => throw new HttpRequestException("no route to host") };

        (SpotifySignInCheck check, MemoryStore store) = await CheckAsync(ValidToken(), handler);

        Assert.Equal(SpotifySignInState.Unavailable, check.State);
        Assert.NotNull(store.Load());
    }

    // Verifies that a timeout (the HTTP client cancels the request) is "could not find out" as well.
    [Fact]
    public async Task WhenTheRequestTimesOut_IsUnavailable()
    {
        RoutingHandler handler = new() { Me = _ => throw new TaskCanceledException("timeout") };

        (SpotifySignInCheck check, _) = await CheckAsync(ValidToken(), handler);

        Assert.Equal(SpotifySignInState.Unavailable, check.State);
    }

    // Verifies that an answer that is not what Spotify sends (garbage, no id) is "could not find out",
    // not a sign-out - and that an empty id is "not signed in".
    [Theory]
    [InlineData("not json", SpotifySignInState.Unavailable)]
    [InlineData("{}", SpotifySignInState.Unavailable)]
    [InlineData("""{ "id": null }""", SpotifySignInState.NotSignedIn)]
    [InlineData("""{ "id": "  " }""", SpotifySignInState.NotSignedIn)]
    public async Task UnexpectedAnswers_AreHandled(string body, SpotifySignInState expected)
    {
        RoutingHandler handler = new() { Me = Respond(HttpStatusCode.OK, body) };

        (SpotifySignInCheck check, _) = await CheckAsync(ValidToken(), handler);

        Assert.Equal(expected, check.State);
    }

    // ---- an expired token has to be refreshed first ----

    // Verifies that an expired token is refreshed and the user is then found.
    [Fact]
    public async Task ExpiredToken_IsRefreshed_ThenSignedIn()
    {
        RoutingHandler handler = new()
        {
            Token = Respond(HttpStatusCode.OK, "{\"access_token\":\"fresh\",\"expires_in\":3600,\"refresh_token\":\"refresh\",\"scope\":\"" + ReleaseDefaults.SpotifyScope + "\"}"),
            Me = Respond(HttpStatusCode.OK, MeJson)
        };

        (SpotifySignInCheck check, _) = await CheckAsync(ExpiredToken(), handler);

        Assert.Equal(SpotifySignInState.SignedIn, check.State);
        Assert.Equal("Bearer fresh", handler.LastMeAuthorization);
    }

    // Verifies that a refresh that Spotify refuses (access revoked: invalid_grant) is "not signed in".
    [Fact]
    public async Task WhenTheRefreshIsRefused_IsNotSignedIn()
    {
        RoutingHandler handler = new() { Token = Respond(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }""") };

        (SpotifySignInCheck check, _) = await CheckAsync(ExpiredToken(), handler);

        Assert.Equal(SpotifySignInState.NotSignedIn, check.State);
    }

    // Verifies that a refresh that fails because Spotify has a problem (5xx, 429) is "could not find
    // out" and does not throw away the token.
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task WhenTheRefreshHitsAProblemAtSpotify_IsUnavailable_AndTheTokenIsKept(HttpStatusCode status)
    {
        RoutingHandler handler = new() { Token = Respond(status, "{}") };

        (SpotifySignInCheck check, MemoryStore store) = await CheckAsync(ExpiredToken(), handler);

        Assert.Equal(SpotifySignInState.Unavailable, check.State);
        Assert.NotNull(store.Load());
    }

    // Verifies that a refresh that cannot be sent at all (no network) is "could not find out" too.
    [Fact]
    public async Task WhenTheRefreshHasNoNetwork_IsUnavailable_AndTheTokenIsKept()
    {
        RoutingHandler handler = new() { Token = _ => throw new HttpRequestException("no network") };

        (SpotifySignInCheck check, MemoryStore store) = await CheckAsync(ExpiredToken(), handler);

        Assert.Equal(SpotifySignInState.Unavailable, check.State);
        Assert.Equal("access", store.Load()!.AccessToken);
    }

    // Verifies the whole point for a user: a check that could not be made, repeated when the network is
    // back, finds the user signed in - nothing was lost in between.
    [Fact]
    public async Task AfterAFailedCheck_TheNextOneStillFindsTheUser()
    {
        bool down = true;
        RoutingHandler handler = new()
        {
            Me = request => down ? throw new HttpRequestException("offline") : Respond(HttpStatusCode.OK, MeJson)(request)
        };
        MemoryStore store = new(ValidToken());
        SpotifyAuthService service = new(store, new HttpClient(handler), new FixedClientId("client-1"));

        SpotifySignInCheck first = await service.CheckSignInAsync(CancellationToken.None);
        down = false;
        SpotifySignInCheck second = await service.CheckSignInAsync(CancellationToken.None);

        Assert.Equal(SpotifySignInState.Unavailable, first.State);
        Assert.Equal(SpotifySignInState.SignedIn, second.State);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Respond(HttpStatusCode status, string json) =>
        _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class FixedClientId : ISpotifyClientIdProvider
    {
        public FixedClientId(string clientId)
        {
            CurrentClientId = clientId;
        }

        public string CurrentClientId { get; }
    }

    private sealed class MemoryStore : ISpotifyTokenStore
    {
        private SpotifyToken? current;

        public MemoryStore(SpotifyToken? initial)
        {
            current = initial;
        }

        public SpotifyToken? Load() => current;

        public void Save(SpotifyToken token) => current = token;

        public void Clear() => current = null;
    }

    // Answers /api/token and /v1/me with what a test sets (the default is a server error).
    private sealed class RoutingHandler : HttpMessageHandler
    {
        private int requestCount;

        public Func<HttpRequestMessage, HttpResponseMessage> Token { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        public Func<HttpRequestMessage, HttpResponseMessage> Me { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        public int RequestCount => Volatile.Read(ref requestCount);

        public string? LastMeAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);

            if (request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
            {
                return Task.FromResult(Token(request));
            }

            LastMeAuthorization = request.Headers.Authorization?.ToString();

            return Task.FromResult(Me(request));
        }
    }
}
