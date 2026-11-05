using System.Net;
using System.Text;
using ErikwnkCore;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Localization;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Spotify.Auth;

namespace ReleaseUpdater.Core.Tests;

// A run makes hundreds of Spotify calls and each one asks for the access token: the token is
// kept in memory instead of being read from its file every time - and must never outlive a
// sign-out, a change of the Client ID or a token without the required permissions.
public sealed class SpotifyAuthTokenCacheTests
{
    private static SpotifyToken ValidToken(string accessToken = "access", string clientId = "client-1") => new()
    {
        AccessToken = accessToken,
        RefreshToken = "refresh",
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
        Scope = ReleaseDefaults.SpotifyScope,
        ClientId = clientId
    };

    // Verifies that a token that is still valid is read from the store once, not once per call.
    [Fact]
    public async Task ValidToken_IsReadFromTheStoreOnlyOnce()
    {
        CountingTokenStore store = new(ValidToken());
        SpotifyAuthService service = new(store, new HttpClient(new RefreshHandler()), new ClientId("client-1"));

        for (int i = 0; i < 25; i++)
        {
            Assert.Equal("access", await service.GetAccessTokenAsync(CancellationToken.None));
        }

        Assert.Equal(1, store.LoadCount);
    }

    // Verifies that signing out empties the copy in memory too: the next call must say "sign in",
    // not hand out the old token.
    [Fact]
    public async Task AfterSignOut_TheOldTokenIsNotHandedOut()
    {
        CountingTokenStore store = new(ValidToken());
        SpotifyAuthService service = new(store, new HttpClient(new RefreshHandler()), new ClientId("client-1"));
        await service.GetAccessTokenAsync(CancellationToken.None);

        await service.LogoutAsync(CancellationToken.None);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAccessTokenAsync(CancellationToken.None));
        Assert.Equal(AppLocalization.Get(AppStrings.SpotifySignInFirst), exception.Message);
        Assert.Null(store.Load());
    }

    // Verifies that a changed Client ID is noticed even though the token is in memory: the token
    // of the old client is refused and removed.
    [Fact]
    public async Task AfterTheClientIdChanged_TheTokenOfTheOldClientIsRefused_AndRemoved()
    {
        CountingTokenStore store = new(ValidToken(clientId: "client-1"));
        ClientId clientId = new("client-1");
        SpotifyAuthService service = new(store, new HttpClient(new RefreshHandler()), clientId);
        await service.GetAccessTokenAsync(CancellationToken.None);

        clientId.Current = "client-2";

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAccessTokenAsync(CancellationToken.None));
        Assert.Equal(AppLocalization.Get(AppStrings.SpotifyLoginOtherClientId), exception.Message);
        Assert.Null(store.Load());

        // ... and it stays refused: switching back does not bring the removed token back.
        clientId.Current = "client-1";
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAccessTokenAsync(CancellationToken.None));
    }

    // Verifies the client ID comparison ignores case (Spotify shows client IDs in lower case,
    // but a pasted one may not be).
    [Fact]
    public async Task ClientId_IsComparedWithoutRegardToCase()
    {
        CountingTokenStore store = new(ValidToken(clientId: "abc123"));
        SpotifyAuthService service = new(store, new HttpClient(new RefreshHandler()), new ClientId("ABC123"));

        Assert.Equal("access", await service.GetAccessTokenAsync(CancellationToken.None));
    }

    // Verifies that a token whose permissions are missing is refused and removed, not kept.
    [Fact]
    public async Task TokenWithoutTheRequiredPermissions_IsRefused_AndRemoved()
    {
        SpotifyToken token = ValidToken();
        token.Scope = "user-follow-read";
        CountingTokenStore store = new(token);
        SpotifyAuthService service = new(store, new HttpClient(new RefreshHandler()), new ClientId("client-1"));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAccessTokenAsync(CancellationToken.None));

        Assert.Equal(AppLocalization.Get(AppStrings.SpotifyLoginMissingPermissions), exception.Message);
        Assert.Null(store.Load());
    }

    // Verifies that without any stored token the user is asked to sign in (and nothing is kept in memory).
    [Fact]
    public async Task WithoutAToken_TheUserIsAskedToSignIn()
    {
        CountingTokenStore store = new(null);
        SpotifyAuthService service = new(store, new HttpClient(new RefreshHandler()), new ClientId("client-1"));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAccessTokenAsync(CancellationToken.None));

        Assert.Equal(AppLocalization.Get(AppStrings.SpotifySignInFirst), exception.Message);

        // Signing in later (the store now has a token) is found.
        store.Save(ValidToken());
        Assert.Equal("access", await service.GetAccessTokenAsync(CancellationToken.None));
    }

    // Verifies that an expired token is refreshed once, and that the refreshed one is then served
    // from memory without reading the file or calling Spotify again.
    [Fact]
    public async Task ExpiredToken_IsRefreshedOnce_ThenServedFromMemory()
    {
        SpotifyToken expired = ValidToken("old");
        expired.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        CountingTokenStore store = new(expired);
        RefreshHandler handler = new();
        SpotifyAuthService service = new(store, new HttpClient(handler), new ClientId("client-1"));

        for (int i = 0; i < 10; i++)
        {
            Assert.Equal("refreshed", await service.GetAccessTokenAsync(CancellationToken.None));
        }

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal("client-1", store.Load()!.ClientId);
    }

    // Verifies that a refresh that returns no permissions signs the user out instead of keeping a useless token.
    [Fact]
    public async Task Refresh_WithoutThePermissions_RemovesTheToken()
    {
        SpotifyToken expired = ValidToken("old");
        expired.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        CountingTokenStore store = new(expired);
        SpotifyAuthService service = new(
            store,
            new HttpClient(new RefreshHandler(scope: "user-follow-read")),
            new ClientId("client-1"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAccessTokenAsync(CancellationToken.None));

        Assert.Null(store.Load());
    }

    // Verifies that a refresh Spotify refuses (revoked access) is reported with the localized
    // prefix and what Spotify said, and keeps nothing in memory.
    [Fact]
    public async Task Refresh_RefusedBySpotify_ReportsTheStatusAndTheAnswer()
    {
        SpotifyToken expired = ValidToken("old");
        expired.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        CountingTokenStore store = new(expired);
        SpotifyAuthService service = new(
            store,
            new HttpClient(new RefreshHandler(failWith: HttpStatusCode.BadRequest, body: "{\"error\":\"invalid_grant\"}")),
            new ClientId("client-1"));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAccessTokenAsync(CancellationToken.None));

        Assert.Equal(
            AppLocalization.Get(AppStrings.SpotifyLoginFailedFormat, HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}"),
            exception.Message);
        Assert.Contains("invalid_grant", exception.Message);
    }

    // Verifies that a token Spotify refused (401) is refreshed at once even though its date says it
    // is still good, once - and the new one is then served from memory.
    [Fact]
    public async Task ARefusedToken_IsRefreshedAtOnce_EvenIfItsDateIsGood()
    {
        CountingTokenStore store = new(ValidToken("stale"));
        RefreshHandler handler = new();
        SpotifyAuthService service = new(store, new HttpClient(handler), new ClientId("client-1"));
        Assert.Equal("stale", await service.GetAccessTokenAsync(CancellationToken.None));

        service.MarkAccessTokenRejected();

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal("refreshed", await service.GetAccessTokenAsync(CancellationToken.None));
        }

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("refreshed", store.Load()!.AccessToken);
    }

    // Verifies that a refresh that fails does not forget that the token was refused: the next call
    // tries again (and does not hand the refused token out).
    [Fact]
    public async Task ARefusedToken_IsNotHandedOutAgain_WhenTheRefreshFailed()
    {
        CountingTokenStore store = new(ValidToken("stale"));
        FlakyRefreshHandler handler = new();
        SpotifyAuthService service = new(store, new HttpClient(handler), new ClientId("client-1"));
        await service.GetAccessTokenAsync(CancellationToken.None);
        service.MarkAccessTokenRejected();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAccessTokenAsync(CancellationToken.None));
        string second = await service.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("refreshed", second);
        Assert.Equal(2, handler.RequestCount);
    }

    // Verifies that a refusal with nothing to refresh with (no refresh token) asks the user to sign in
    // instead of looping on the refused token.
    [Fact]
    public async Task ARefusedToken_WithoutARefreshToken_AsksToSignInAgain()
    {
        SpotifyToken token = ValidToken();
        token.RefreshToken = null;
        SpotifyAuthService service = new(new CountingTokenStore(token), new HttpClient(new RefreshHandler()), new ClientId("client-1"));
        await service.GetAccessTokenAsync(CancellationToken.None);

        service.MarkAccessTokenRejected();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAccessTokenAsync(CancellationToken.None));
        Assert.Equal(AppLocalization.Get(AppStrings.SpotifyRefreshTokenMissing), exception.Message);
    }

    // Verifies that a refresh without a refresh token says so instead of calling Spotify.
    [Fact]
    public async Task Refresh_WithoutARefreshToken_AsksToSignInAgain()
    {
        SpotifyToken expired = ValidToken("old");
        expired.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        expired.RefreshToken = null;
        RefreshHandler handler = new();
        SpotifyAuthService service = new(new CountingTokenStore(expired), new HttpClient(handler), new ClientId("client-1"));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAccessTokenAsync(CancellationToken.None));

        Assert.Equal(AppLocalization.Get(AppStrings.SpotifyRefreshTokenMissing), exception.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    // Verifies that a missing Client ID is reported before anything else.
    [Fact]
    public async Task MissingClientId_IsReported()
    {
        SpotifyAuthService service = new(
            new CountingTokenStore(ValidToken()),
            new HttpClient(new RefreshHandler()),
            new ClientId("   "));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAccessTokenAsync(CancellationToken.None));

        Assert.Equal(AppLocalization.Get(AppStrings.SpotifyClientIdMissing), exception.Message);
    }

    // Verifies that the error texts of this service have a German version (read from the table,
    // not by switching the language: that is global and would disturb tests running at the same time).
    [Fact]
    public void ErrorTexts_HaveAGermanVersion()
    {
        System.Reflection.FieldInfo german = typeof(AppStrings).GetField(
            "German",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Dictionary<string, string> table = (Dictionary<string, string>)german.GetValue(null)!;

        Assert.Equal("Bitte melde dich zuerst bei Spotify an.", table[AppStrings.SpotifySignInFirst]);
        Assert.Contains("Client-ID", table[AppStrings.SpotifyLoginOtherClientId]);
    }

    private sealed class ClientId : ISpotifyClientIdProvider
    {
        public ClientId(string current)
        {
            Current = current;
        }

        public string Current { get; set; }

        public string CurrentClientId => Current;
    }

    private sealed class CountingTokenStore : ISpotifyTokenStore
    {
        private SpotifyToken? current;

        public CountingTokenStore(SpotifyToken? initial)
        {
            current = initial;
        }

        public int LoadCount { get; private set; }

        public int SaveCount { get; private set; }

        public SpotifyToken? Load()
        {
            LoadCount++;
            return current;
        }

        public void Save(SpotifyToken token)
        {
            SaveCount++;
            current = token;
        }

        public void Clear() => current = null;
    }

    // The first refresh is refused by Spotify, the second works.
    private sealed class FlakyRefreshHandler : HttpMessageHandler
    {
        private int requestCount;

        public int RequestCount => Volatile.Read(ref requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bool first = Interlocked.Increment(ref requestCount) == 1;
            string json = first
                ? "{\"error\":\"temporarily_unavailable\"}"
                : "{\"access_token\":\"refreshed\",\"expires_in\":3600,\"refresh_token\":\"refresh\",\"scope\":\"" + ReleaseDefaults.SpotifyScope + "\"}";

            return Task.FromResult(new HttpResponseMessage(first ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class RefreshHandler : HttpMessageHandler
    {
        private readonly string scope;
        private readonly HttpStatusCode status;
        private readonly string? body;
        private int requestCount;

        public RefreshHandler(string? scope = null, HttpStatusCode failWith = HttpStatusCode.OK, string? body = null)
        {
            this.scope = scope ?? ReleaseDefaults.SpotifyScope;
            status = failWith;
            this.body = body;
        }

        public int RequestCount => Volatile.Read(ref requestCount);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);

            string json = body ?? "{\"access_token\":\"refreshed\",\"expires_in\":3600,\"refresh_token\":\"refresh\",\"scope\":\"" + scope + "\"}";

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
