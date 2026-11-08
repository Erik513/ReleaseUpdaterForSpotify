using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using ErikwnkCore;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Localization;
using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Spotify.Auth;

public sealed class SpotifyAuthService : ISpotifyAuthService, ISpotifyAccessTokenProvider, ISpotifyAccessTokenInvalidation
{
    public const int RedirectPort = 5543;
    public const string RedirectUri = "http://127.0.0.1:5543/callback";

    private static readonly Uri AccountsBaseUri = new("https://accounts.spotify.com");

    private readonly ISpotifyTokenStore tokenStore;
    private readonly ISpotifyClientIdProvider clientIdProvider;
    private readonly HttpClient httpClient;
    private readonly SemaphoreSlim tokenRefreshGate = new(1, 1);

    public SpotifyAuthService(
        ISpotifyTokenStore tokenStore,
        HttpClient httpClient,
        ISpotifyClientIdProvider clientIdProvider)
    {
        this.tokenStore = tokenStore;
        this.httpClient = httpClient;
        this.clientIdProvider = clientIdProvider;
    }

    /// <summary>
    /// Checks the stored token and asks Spotify who the signed-in user is. "Not signed in" is only
    /// said when it is true (no token, a token that is no use, access that Spotify refuses); when
    /// Spotify cannot be reached or has a problem the answer is "unavailable", so a bad moment of the
    /// network does not look like a sign-out.
    /// </summary>
    public async Task<SpotifySignInCheck> CheckSignInAsync(
        CancellationToken cancellationToken)
    {
        string accessToken;

        try
        {
            accessToken = await GetAccessTokenAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // No token, the token of another client, missing permissions, a refresh that Spotify refused.
            return new SpotifySignInCheck(SpotifySignInState.NotSignedIn);
        }
        catch
        {
            // The refresh could not even be tried (no network, timeout).
            return new SpotifySignInCheck(SpotifySignInState.Unavailable);
        }

        try
        {
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                "https://api.spotify.com/v1/me");

            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer",
                    accessToken);

            using HttpResponseMessage response =
                await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                int status = (int)response.StatusCode;

                // The token is refused (4xx) - or Spotify is the one with the problem (429, 5xx, timeout).
                return status is >= 400 and < 500 and not 408 and not 429
                    ? new SpotifySignInCheck(SpotifySignInState.NotSignedIn)
                    : new SpotifySignInCheck(SpotifySignInState.Unavailable);
            }

            await using Stream stream =
                await response.Content.ReadAsStreamAsync(cancellationToken);

            using JsonDocument json =
                await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            JsonElement root = json.RootElement;

            string id = root.GetProperty("id").GetString() ?? string.Empty;
            string displayName = root.TryGetProperty("display_name", out JsonElement displayNameElement)
                ? displayNameElement.GetString() ?? id
                : id;

            return string.IsNullOrWhiteSpace(id)
                ? new SpotifySignInCheck(SpotifySignInState.NotSignedIn)
                : new SpotifySignInCheck(SpotifySignInState.SignedIn, new SpotifyUser(id, displayName));
        }
        catch
        {
            return new SpotifySignInCheck(SpotifySignInState.Unavailable);
        }
    }

    /// <summary>
    /// Starts the browser login and exchanges the PKCE code for tokens.
    /// </summary>
    public async Task<SpotifyUser> LoginAsync(
        CancellationToken cancellationToken)
    {
        string clientId = GetConfiguredClientId();
        string codeVerifier = CreateCodeVerifier();
        string codeChallenge = CreateCodeChallenge(codeVerifier);
        string state = CreateState();

        using LoopbackCallbackListener listener =
            new(RedirectPort, "/callback");

        Task<AuthorizationCallback> callbackTask =
            listener.WaitForCallbackAsync(state, cancellationToken);

        OpenBrowser(BuildAuthorizeUri(clientId, codeChallenge, state));

        AuthorizationCallback callback =
            await callbackTask.ConfigureAwait(false);

        SpotifyToken token = await ExchangeCodeAsync(
            clientId,
            callback.Code,
            codeVerifier,
            cancellationToken);

        if (!HasRequiredScopes(token))
        {
            ClearToken();
            throw new InvalidOperationException(AppLocalization.Get(AppStrings.SpotifyPermissionsNotGranted));
        }

        SaveToken(token);

        SpotifySignInCheck check = await CheckSignInAsync(cancellationToken);

        return check.User ?? new SpotifyUser(string.Empty, "Spotify");
    }

    public Task LogoutAsync(CancellationToken cancellationToken)
    {
        ClearToken();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns a valid access token and refreshes it when needed.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken)
    {
        string clientId = GetConfiguredClientId();

        // The usual case (hundreds of API calls in one run, one token): answered from memory,
        // without reading the token file each time. Only a token that was checked when it was
        // stored is ever kept here (see StoreInMemory).
        SpotifyToken? cached = cachedToken;

        if (!refreshRequested &&
            cached is not null &&
            TokenBelongsToClient(cached, clientId) &&
            cached.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return cached.AccessToken;
        }

        SpotifyToken token = ValidateToken(tokenStore.Load(), clientId);

        if (!refreshRequested && token.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            StoreInMemory(token);
            return token.AccessToken;
        }

        // Concurrent Spotify calls can all see an expiring token at once. Only one
        // of them should refresh it; the rest wait, then re-check the now-current
        // token instead of racing to refresh (and save) it themselves.
        await tokenRefreshGate.WaitAsync(cancellationToken);

        try
        {
            token = ValidateToken(tokenStore.Load(), clientId);

            if (!refreshRequested && token.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                StoreInMemory(token);
                return token.AccessToken;
            }

            SpotifyToken refreshedToken =
                await RefreshTokenAsync(clientId, token, cancellationToken);

            tokenStore.Save(refreshedToken);

            if (!HasRequiredScopes(refreshedToken))
            {
                ClearToken();
                throw new InvalidOperationException(AppLocalization.Get(AppStrings.SpotifyLoginMissingPermissions));
            }

            StoreInMemory(refreshedToken);
            refreshRequested = false;
            return refreshedToken.AccessToken;
        }
        finally
        {
            tokenRefreshGate.Release();
        }
    }

    // Set when Spotify refused the token (401): the next call refreshes it whatever its date says.
    // Stays set until a refresh has worked, so a refresh that failed is tried again by the next call.
    private volatile bool refreshRequested;

    /// <summary>Tells the service that Spotify refused the token it handed out last (see <see cref="ISpotifyAccessTokenInvalidation"/>).</summary>
    public void MarkAccessTokenRejected()
    {
        refreshRequested = true;
        cachedToken = null;
    }

    // Set only for a token that passed ValidateToken (right client, required scopes) or was just
    // received from Spotify, so reading it back needs no second check of the scopes.
    private volatile SpotifyToken? cachedToken;

    private void StoreInMemory(SpotifyToken token)
    {
        cachedToken = token;
    }

    private void SaveToken(SpotifyToken token)
    {
        tokenStore.Save(token);
        cachedToken = token;
    }

    // Every removal goes through here, so the copy in memory never outlives the file (a sign-out,
    // a token of another client, a missing permission).
    private void ClearToken()
    {
        cachedToken = null;
        tokenStore.Clear();
    }

    private SpotifyToken ValidateToken(SpotifyToken? token, string clientId)
    {
        if (token is null)
        {
            throw new InvalidOperationException(AppLocalization.Get(AppStrings.SpotifySignInFirst));
        }

        if (!TokenBelongsToClient(token, clientId))
        {
            ClearToken();
            throw new InvalidOperationException(AppLocalization.Get(AppStrings.SpotifyLoginOtherClientId));
        }

        if (!HasRequiredScopes(token))
        {
            ClearToken();
            throw new InvalidOperationException(AppLocalization.Get(AppStrings.SpotifyLoginMissingPermissions));
        }

        return token;
    }

    private string GetConfiguredClientId()
    {
        string clientId = clientIdProvider.CurrentClientId;

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(AppLocalization.Get(AppStrings.SpotifyClientIdMissing));
        }

        return clientId.Trim();
    }

    private static Uri BuildAuthorizeUri(
        string clientId,
        string codeChallenge,
        string state)
    {
        Dictionary<string, string> parameters = new()
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["scope"] = ReleaseDefaults.SpotifyScope,
            ["redirect_uri"] = RedirectUri,
            ["state"] = state,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = codeChallenge
        };

        string query = string.Join(
            "&",
            parameters.Select(
                pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        return new Uri($"{AccountsBaseUri}/authorize?{query}");
    }

    private async Task<SpotifyToken> ExchangeCodeAsync(
        string clientId,
        string code,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> form = new()
        {
            ["client_id"] = clientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = codeVerifier
        };

        SpotifyToken token =
            await SendTokenRequestAsync(form, null, null, cancellationToken);
        token.ClientId = clientId;

        return token;
    }

    private async Task<SpotifyToken> RefreshTokenAsync(
        string clientId,
        SpotifyToken currentToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentToken.RefreshToken))
        {
            throw new InvalidOperationException(AppLocalization.Get(AppStrings.SpotifyRefreshTokenMissing));
        }

        Dictionary<string, string> form = new()
        {
            ["client_id"] = clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = currentToken.RefreshToken
        };

        SpotifyToken refreshedToken = await SendTokenRequestAsync(
            form,
            currentToken.RefreshToken,
            currentToken.Scope,
            cancellationToken);
        refreshedToken.ClientId = clientId;

        return refreshedToken;
    }

    private async Task<SpotifyToken> SendTokenRequestAsync(
        Dictionary<string, string> form,
        string? existingRefreshToken,
        string? existingScope,
        CancellationToken cancellationToken)
    {
        using FormUrlEncodedContent content = new(form);

        using HttpResponseMessage response = await httpClient.PostAsync(
            new Uri(AccountsBaseUri, "/api/token"),
            content,
            cancellationToken);

        string responseText =
            await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string failure = AppLocalization.Get(AppStrings.SpotifyLoginFailedFormat, response.StatusCode, responseText);

            // Spotify having a problem (5xx, 429) is not the same as refusing the sign-in (4xx): the
            // first is an HttpRequestException, so a check of the sign-in says "unavailable" and keeps the token.
            if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                throw new HttpRequestException(failure, null, response.StatusCode);
            }

            throw new InvalidOperationException(failure);
        }

        using JsonDocument json = JsonDocument.Parse(responseText);
        JsonElement root = json.RootElement;

        string accessToken = root.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException(AppLocalization.Get(AppStrings.SpotifyAccessTokenMissing));

        int expiresInSeconds = root.GetProperty("expires_in").GetInt32();

        string? refreshToken = root.TryGetProperty("refresh_token", out JsonElement refreshTokenElement)
            ? refreshTokenElement.GetString()
            : existingRefreshToken;

        return new SpotifyToken
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds),
            Scope = root.TryGetProperty("scope", out JsonElement scopeElement)
                ? scopeElement.GetString() ?? string.Empty
                : existingScope ?? string.Empty
        };
    }

    private static bool HasRequiredScopes(SpotifyToken? token)
    {
        if (token is null)
        {
            return false;
        }

        HashSet<string> grantedScopes = token.Scope
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ReleaseDefaults.SpotifyScope
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(grantedScopes.Contains);
    }

    private static bool TokenBelongsToClient(
        SpotifyToken? token,
        string clientId)
    {
        if (token is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(token.ClientId))
        {
            return false;
        }

        return string.Equals(
            token.ClientId,
            clientId,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void OpenBrowser(Uri authorizationUri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = authorizationUri.ToString(),
            UseShellExecute = true
        });
    }

    private static string CreateCodeVerifier()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(64);
        return Base64UrlEncode(bytes);
    }

    private static string CreateCodeChallenge(string codeVerifier)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(codeVerifier);
        byte[] hash = SHA256.HashData(bytes);
        return Base64UrlEncode(hash);
    }

    private static string CreateState()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

internal sealed record AuthorizationCallback(string Code);

internal sealed class LoopbackCallbackListener : IDisposable
{
    private readonly TcpListener listener;
    private readonly string expectedPath;

    public LoopbackCallbackListener(int port, string expectedPath)
    {
        this.expectedPath = expectedPath;
        listener = new TcpListener(System.Net.IPAddress.Loopback, port);
        listener.Start();
    }

    /// <summary>The port actually listened on (differs from the requested one when that was 0).</summary>
    public int Port => ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

    public async Task<AuthorizationCallback> WaitForCallbackAsync(
        string expectedState,
        CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration registration =
            cancellationToken.Register(listener.Stop);

        using TcpClient client =
            await listener.AcceptTcpClientAsync(cancellationToken);

        await using NetworkStream stream = client.GetStream();
        using StreamReader reader = new(stream, Encoding.ASCII, leaveOpen: true);

        string? requestLine = await reader.ReadLineAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(requestLine))
        {
            throw new InvalidOperationException(AppLocalization.Get(AppStrings.SpotifyCallbackEmpty));
        }

        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
        {
        }

        Dictionary<string, string> query = ParseQueryFromRequestLine(requestLine);

        if (query.TryGetValue("error", out string? error))
        {
            await WriteResponseAsync(
                stream,
                AppLocalization.Get(AppStrings.SpotifyPageLoginCancelledTitle),
                AppLocalization.Get(AppStrings.SpotifyPageLoginCancelledMessage),
                cancellationToken);

            throw new InvalidOperationException(AppLocalization.Get(AppStrings.SpotifyLoginCancelledFormat, error));
        }

        if (!query.TryGetValue("state", out string? state) || state != expectedState)
        {
            await WriteResponseAsync(
                stream,
                AppLocalization.Get(AppStrings.SpotifyPageLoginFailedTitle),
                AppLocalization.Get(AppStrings.SpotifyPageStateMismatchMessage),
                cancellationToken);

            throw new InvalidOperationException(AppLocalization.Get(AppStrings.SpotifyCallbackStateInvalid));
        }

        if (!query.TryGetValue("code", out string? code) || string.IsNullOrWhiteSpace(code))
        {
            await WriteResponseAsync(
                stream,
                AppLocalization.Get(AppStrings.SpotifyPageLoginFailedTitle),
                AppLocalization.Get(AppStrings.SpotifyPageCodeMissingMessage),
                cancellationToken);

            throw new InvalidOperationException(AppLocalization.Get(AppStrings.SpotifyCallbackCodeMissing));
        }

        await WriteResponseAsync(
            stream,
            AppLocalization.Get(AppStrings.SpotifyPageLoginSuccessfulTitle),
            AppLocalization.Get(AppStrings.SpotifyPageLoginSuccessfulMessage),
            cancellationToken);

        return new AuthorizationCallback(code);
    }

    public void Dispose()
    {
        listener.Stop();
    }

    private Dictionary<string, string> ParseQueryFromRequestLine(
        string requestLine)
    {
        string[] parts = requestLine.Split(' ');

        if (parts.Length < 2)
        {
            throw new InvalidOperationException(AppLocalization.Get(AppStrings.SpotifyCallbackInvalid));
        }

        string target = parts[1];
        int queryStart = target.IndexOf('?');
        string targetPath = queryStart >= 0
            ? target[..queryStart]
            : target;

        if (!string.Equals(targetPath, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(AppLocalization.Get(AppStrings.SpotifyCallbackPathInvalid));
        }

        if (queryStart < 0 || queryStart == target.Length - 1)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        string query = target[(queryStart + 1)..];
        Dictionary<string, string> values = new(StringComparer.Ordinal);

        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] keyValue = pair.Split('=', 2);

            if (keyValue.Length != 2)
            {
                continue;
            }

            values[Uri.UnescapeDataString(keyValue[0])] =
                Uri.UnescapeDataString(keyValue[1].Replace('+', ' '));
        }

        return values;
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        string language = AppLocalization.Language == AppLanguage.German ? "de" : "en";
        string safeTitle = System.Net.WebUtility.HtmlEncode(title);
        string safeMessage = System.Net.WebUtility.HtmlEncode(message);

        string body =
            $"<!doctype html><html lang=\"{language}\"><head><meta charset=\"utf-8\">" +
            $"<title>{safeTitle}</title></head><body style=\"font-family:Arial,sans-serif;margin:32px;\">" +
            $"<h1>{safeTitle}</h1><p>{safeMessage}</p></body></html>";

        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        string headers =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";

        byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
        await stream.WriteAsync(headerBytes, cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
    }
}
