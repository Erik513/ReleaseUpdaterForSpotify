using System.Net;
using System.Text;
using System.Threading;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Spotify.Auth;

namespace ReleaseUpdater.Core.Tests;

public sealed class SpotifyAuthServiceTests
{
    // Verifies that concurrent callers racing against an expiring token trigger only
    // one refresh, since concurrent Spotify calls (introduced for faster playlist
    // updates) can all see an expiring token at the same time.
    [Fact]
    public async Task GetAccessTokenAsync_RefreshesOnlyOnce_WhenCalledConcurrently()
    {
        FakeTokenStore tokenStore = new(new SpotifyToken
        {
            AccessToken = "expiring-token",
            RefreshToken = "refresh-token",
            ExpiresAtUtc = DateTimeOffset.UtcNow,
            Scope = ReleaseDefaults.SpotifyScope,
            ClientId = "client-1"
        });
        DelayedRefreshHandler handler = new(TimeSpan.FromMilliseconds(30));
        SpotifyAuthService authService = new(
            tokenStore,
            new HttpClient(handler),
            new FakeClientIdProvider("client-1"));

        Task<string>[] tasks = Enumerable.Range(0, 10)
            .Select(_ => authService.GetAccessTokenAsync(CancellationToken.None))
            .ToArray();

        string[] accessTokens = await Task.WhenAll(tasks);

        Assert.Equal(1, handler.RefreshRequestCount);
        Assert.All(accessTokens, token => Assert.Equal("refreshed-token", token));
        Assert.Equal(1, tokenStore.SaveCount);
    }

    private sealed class FakeClientIdProvider : ISpotifyClientIdProvider
    {
        public FakeClientIdProvider(string clientId)
        {
            CurrentClientId = clientId;
        }

        public string CurrentClientId { get; }
    }

    private sealed class FakeTokenStore : ISpotifyTokenStore
    {
        private SpotifyToken? current;

        public FakeTokenStore(SpotifyToken initial)
        {
            current = initial;
        }

        public int SaveCount { get; private set; }

        public SpotifyToken? Load() => current;

        public void Save(SpotifyToken token)
        {
            SaveCount++;
            current = token;
        }

        public void Clear() => current = null;
    }

    private sealed class DelayedRefreshHandler : HttpMessageHandler
    {
        private readonly TimeSpan refreshDelay;
        private int refreshRequestCount;

        public DelayedRefreshHandler(TimeSpan refreshDelay)
        {
            this.refreshDelay = refreshDelay;
        }

        public int RefreshRequestCount => refreshRequestCount;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref refreshRequestCount);

            // A real await point so concurrent callers genuinely interleave instead
            // of running one after another inside a single synchronous call chain.
            await Task.Delay(refreshDelay, cancellationToken);

            string json = """
                {
                  "access_token": "refreshed-token",
                  "expires_in": 3600,
                  "refresh_token": "refresh-token",
                  "scope": "SCOPE_PLACEHOLDER"
                }
                """.Replace("SCOPE_PLACEHOLDER", ReleaseDefaults.SpotifyScope);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
