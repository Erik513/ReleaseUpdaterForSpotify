using System.Net;
using System.Net.Sockets;
using System.Text;
using ReleaseUpdater.Spotify.Auth;

namespace ReleaseUpdater.Core.Tests;

public sealed class LoopbackCallbackListenerTests
{
    private const string State = "expected-state";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // Verifies the normal case: Spotify redirects back with the right state and a code.
    [Fact]
    public async Task ValidCallback_ReturnsTheCode_AndShowsASuccessPage()
    {
        using LoopbackCallbackListener listener = new(0, "/callback");
        Task<AuthorizationCallback> callback = Start(listener);

        string response = await SendAsync(listener.Port, $"GET /callback?code=abc123&state={State} HTTP/1.1");

        Assert.Equal("abc123", (await callback).Code);
        Assert.StartsWith("HTTP/1.1 200 OK", response);
        Assert.Contains("Login successful", response);
    }

    // Verifies that the code is URL-decoded: "+" is a space, "%2B" is a plus, as in a
    // normal query string.
    [Fact]
    public async Task ValidCallback_DecodesEscapedCharactersInTheCode()
    {
        using LoopbackCallbackListener listener = new(0, "/callback");
        Task<AuthorizationCallback> callback = Start(listener);

        await SendAsync(listener.Port, $"GET /callback?code=a%2Bb+c%2Fd&state={State} HTTP/1.1");

        Assert.Equal("a+b c/d", (await callback).Code);
    }

    // Verifies that the order of the query parameters does not matter.
    [Fact]
    public async Task ValidCallback_AcceptsTheParametersInAnyOrder()
    {
        using LoopbackCallbackListener listener = new(0, "/callback");
        Task<AuthorizationCallback> callback = Start(listener);

        await SendAsync(listener.Port, $"GET /callback?state={State}&code=xyz HTTP/1.1");

        Assert.Equal("xyz", (await callback).Code);
    }

    // Verifies that the path is compared without regard to case, like the rest of the
    // callback handling.
    [Fact]
    public async Task ValidCallback_AcceptsThePathInAnyCase()
    {
        using LoopbackCallbackListener listener = new(0, "/callback");
        Task<AuthorizationCallback> callback = Start(listener);

        await SendAsync(listener.Port, $"GET /CallBack?code=abc&state={State} HTTP/1.1");

        Assert.Equal("abc", (await callback).Code);
    }

    // Verifies that the user pressing "Cancel" on Spotify's page (error=access_denied) ends
    // the login with an error that names the reason, and tells the browser about it.
    [Fact]
    public async Task ErrorParameter_FailsTheLogin_AndShowsACancelledPage()
    {
        using LoopbackCallbackListener listener = new(0, "/callback");
        Task<AuthorizationCallback> callback = Start(listener);

        string response = await SendAsync(listener.Port, $"GET /callback?error=access_denied&state={State} HTTP/1.1");

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() => callback);
        Assert.Contains("access_denied", exception.Message);
        Assert.Contains("Login cancelled", response);
    }

    // Verifies the security check: a callback whose state is not the one that was sent
    // (a forged or stale redirect) must never be accepted, even with a code.
    [Fact]
    public async Task WrongState_IsRejected()
    {
        using LoopbackCallbackListener listener = new(0, "/callback");
        Task<AuthorizationCallback> callback = Start(listener);

        string response = await SendAsync(listener.Port, "GET /callback?code=abc&state=somebody-else HTTP/1.1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => callback);
        Assert.Contains("Login failed", response);
    }

    // Verifies that a callback without any state is rejected too, not treated as "no state to check".
    [Fact]
    public async Task MissingState_IsRejected()
    {
        using LoopbackCallbackListener listener = new(0, "/callback");
        Task<AuthorizationCallback> callback = Start(listener);

        await SendAsync(listener.Port, "GET /callback?code=abc HTTP/1.1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => callback);
    }

    // Verifies that the state is compared exactly, not case-insensitively.
    [Fact]
    public async Task State_IsComparedCaseSensitively()
    {
        using LoopbackCallbackListener listener = new(0, "/callback");
        Task<AuthorizationCallback> callback = Start(listener);

        await SendAsync(listener.Port, $"GET /callback?code=abc&state={State.ToUpperInvariant()} HTTP/1.1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => callback);
    }

    // Verifies that a callback without a code (missing, or present but empty) is an error
    // with a page for the user, not a successful login with an empty code.
    [Theory]
    [InlineData("GET /callback?state=expected-state HTTP/1.1")]
    [InlineData("GET /callback?code=&state=expected-state HTTP/1.1")]
    [InlineData("GET /callback?code=%20&state=expected-state HTTP/1.1")]
    public async Task MissingOrBlankCode_IsRejected(string requestLine)
    {
        using LoopbackCallbackListener listener = new(0, "/callback");
        Task<AuthorizationCallback> callback = Start(listener);

        string response = await SendAsync(listener.Port, requestLine);

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() => callback);
        Assert.Contains("code", exception.Message);
        Assert.Contains("Login failed", response);
    }

    // Verifies that a request to another path (anything but the registered redirect) is not
    // taken for the callback.
    [Fact]
    public async Task WrongPath_IsRejected()
    {
        using LoopbackCallbackListener listener = new(0, "/callback");
        Task<AuthorizationCallback> callback = Start(listener);

        await SendAsync(listener.Port, $"GET /other?code=abc&state={State} HTTP/1.1");

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() => callback);
        Assert.Contains("path", exception.Message);
    }

    // Verifies that a path that only starts with the registered one is not accepted.
    [Fact]
    public async Task PathWithATail_IsRejected()
    {
        using LoopbackCallbackListener listener = new(0, "/callback");
        Task<AuthorizationCallback> callback = Start(listener);

        await SendAsync(listener.Port, $"GET /callback/evil?code=abc&state={State} HTTP/1.1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => callback);
    }

    // Verifies that a connection that closes without sending anything (a port scanner, a
    // browser that gave up) ends the wait with a clear error instead of hanging.
    [Fact]
    public async Task EmptyConnection_FailsWithAnError()
    {
        using LoopbackCallbackListener listener = new(0, "/callback");
        Task<AuthorizationCallback> callback = Start(listener);

        await SendAsync(listener.Port, string.Empty);

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() => callback);
        Assert.Contains("empty", exception.Message);
    }

    // Verifies that a request line that is not HTTP at all is an error, not a crash of another kind.
    [Fact]
    public async Task GarbageRequestLine_FailsWithAnError()
    {
        using LoopbackCallbackListener listener = new(0, "/callback");
        Task<AuthorizationCallback> callback = Start(listener);

        await SendAsync(listener.Port, "GARBAGE", addBlankLine: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => callback);
    }

    // Verifies that a query part without "=" is ignored instead of breaking the parsing of the rest.
    [Fact]
    public async Task QueryPartWithoutAValue_IsIgnored()
    {
        using LoopbackCallbackListener listener = new(0, "/callback");
        Task<AuthorizationCallback> callback = Start(listener);

        await SendAsync(listener.Port, $"GET /callback?junk&code=abc&state={State} HTTP/1.1");

        Assert.Equal("abc", (await callback).Code);
    }

    // Verifies that cancelling the login (the user closes the wait, or the five minutes run
    // out) ends the wait with an OperationCanceledException so the caller can tell it from a failure.
    [Fact]
    public async Task Cancellation_EndsTheWait_WithAnOperationCanceledException()
    {
        using LoopbackCallbackListener listener = new(0, "/callback");
        using CancellationTokenSource cancellation = new();
        Task<AuthorizationCallback> callback = listener.WaitForCallbackAsync(State, cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => callback.WaitAsync(Timeout));
    }

    private static Task<AuthorizationCallback> Start(LoopbackCallbackListener listener) =>
        listener.WaitForCallbackAsync(State, CancellationToken.None);

    // Sends one request the way a browser would and returns everything the listener answered
    // (empty when it closed the connection without an answer).
    private static async Task<string> SendAsync(int port, string requestLine, bool addBlankLine = false)
    {
        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);

        using NetworkStream stream = client.GetStream();

        if (requestLine.Length > 0)
        {
            string request = requestLine + "\r\nHost: 127.0.0.1\r\n\r\n";

            if (addBlankLine)
            {
                request = requestLine + "\r\n\r\n";
            }

            byte[] bytes = Encoding.ASCII.GetBytes(request);
            await stream.WriteAsync(bytes);
        }
        else
        {
            // Nothing to send: say so, so that the listener's read ends.
            client.Client.Shutdown(SocketShutdown.Send);
        }

        using StreamReader reader = new(stream, Encoding.UTF8);

        return await reader.ReadToEndAsync().WaitAsync(Timeout);
    }
}
