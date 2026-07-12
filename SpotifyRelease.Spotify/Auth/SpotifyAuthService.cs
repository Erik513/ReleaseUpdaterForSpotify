using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpotifyRelease.Core.Abstractions;
using SpotifyRelease.Core.Models;

namespace SpotifyRelease.Spotify.Auth;

public sealed class SpotifyAuthService : ISpotifyAuthService, ISpotifyAccessTokenProvider
{
    public const int RedirectPort = 5543;
    public const string RedirectUri = "http://127.0.0.1:5543/callback";

    private static readonly Uri AccountsBaseUri = new("https://accounts.spotify.com");

    private readonly ISpotifyTokenStore tokenStore;
    private readonly ISpotifyClientIdProvider clientIdProvider;
    private readonly HttpClient httpClient;

    public SpotifyAuthService(
        ISpotifyTokenStore tokenStore,
        HttpClient httpClient,
        ISpotifyClientIdProvider clientIdProvider)
    {
        this.tokenStore = tokenStore;
        this.httpClient = httpClient;
        this.clientIdProvider = clientIdProvider;
    }

    public bool HasCachedToken
    {
        get
        {
            SpotifyToken? token = tokenStore.Load();

            return HasRequiredScopes(token) &&
                TokenBelongsToClient(token, GetConfiguredClientId());
        }
    }

    /// <summary>
    /// Checks the stored token and returns the currently signed-in Spotify user.
    /// </summary>
    public async Task<SpotifyUser?> TryGetCurrentUserAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            string accessToken = await GetAccessTokenAsync(cancellationToken);

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
                return null;
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
                ? null
                : new SpotifyUser(id, displayName);
        }
        catch
        {
            return null;
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
            tokenStore.Clear();
            throw new InvalidOperationException(
                "Spotify did not grant the required permissions. Please sign in again and approve the requested access.");
        }

        tokenStore.Save(token);

        SpotifyUser? user = await TryGetCurrentUserAsync(cancellationToken);

        return user ?? new SpotifyUser(string.Empty, "Spotify");
    }

    public Task LogoutAsync(CancellationToken cancellationToken)
    {
        tokenStore.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns a valid access token and refreshes it when needed.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken)
    {
        SpotifyToken? token = tokenStore.Load();
        string clientId = GetConfiguredClientId();

        if (token is null)
        {
            throw new InvalidOperationException(
                "Please sign in to Spotify first.");
        }

        if (!TokenBelongsToClient(token, clientId))
        {
            tokenStore.Clear();
            throw new InvalidOperationException(
                "The saved Spotify login belongs to another Client ID. Please sign in again.");
        }

        if (!HasRequiredScopes(token))
        {
            tokenStore.Clear();
            throw new InvalidOperationException(
                "Spotify login is missing required permissions. Please sign in again.");
        }

        if (token.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return token.AccessToken;
        }

        SpotifyToken refreshedToken =
            await RefreshTokenAsync(clientId, token, cancellationToken);

        tokenStore.Save(refreshedToken);

        if (!HasRequiredScopes(refreshedToken))
        {
            tokenStore.Clear();
            throw new InvalidOperationException(
                "Spotify login is missing required permissions. Please sign in again.");
        }

        return refreshedToken.AccessToken;
    }

    private string GetConfiguredClientId()
    {
        string clientId = clientIdProvider.CurrentClientId;

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(
                "Spotify client ID is missing.");
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
            throw new InvalidOperationException(
                "Spotify refresh token is missing. Please sign in again.");
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
            throw new InvalidOperationException(
                $"Spotify login failed: {response.StatusCode} {responseText}");
        }

        using JsonDocument json = JsonDocument.Parse(responseText);
        JsonElement root = json.RootElement;

        string accessToken = root.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Spotify access token is missing.");

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
            return string.Equals(
                clientId,
                ReleaseDefaults.SharedSpotifyClientId,
                StringComparison.OrdinalIgnoreCase);
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
            throw new InvalidOperationException("Spotify callback was empty.");
        }

        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
        {
        }

        Dictionary<string, string> query = ParseQueryFromRequestLine(requestLine);

        if (query.TryGetValue("error", out string? error))
        {
            await WriteResponseAsync(
                stream,
                "Login cancelled",
                "Spotify cancelled the login.",
                cancellationToken);

            throw new InvalidOperationException($"Spotify login cancelled: {error}");
        }

        if (!query.TryGetValue("state", out string? state) || state != expectedState)
        {
            await WriteResponseAsync(
                stream,
                "Login failed",
                "The login security state did not match.",
                cancellationToken);

            throw new InvalidOperationException("Spotify callback state is invalid.");
        }

        if (!query.TryGetValue("code", out string? code) || string.IsNullOrWhiteSpace(code))
        {
            await WriteResponseAsync(
                stream,
                "Login failed",
                "Spotify did not return an authorization code.",
                cancellationToken);

            throw new InvalidOperationException("Spotify authorization code is missing.");
        }

        await WriteResponseAsync(
            stream,
            "Login successful",
            "You can close this browser window now.",
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
            throw new InvalidOperationException("Spotify callback is invalid.");
        }

        string target = parts[1];
        int queryStart = target.IndexOf('?');
        string targetPath = queryStart >= 0
            ? target[..queryStart]
            : target;

        if (!string.Equals(targetPath, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Spotify callback path is invalid.");
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
        string body =
            "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">" +
            $"<title>{title}</title></head><body style=\"font-family:Arial,sans-serif;margin:32px;\">" +
            $"<h1>{title}</h1><p>{message}</p></body></html>";

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
