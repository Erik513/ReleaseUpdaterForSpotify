using ReleaseUpdater.Core.Localization;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Core.Services;
using ErikwnkCore;

namespace ReleaseUpdaterGui.Tests;

public sealed class MainFormControllerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly FakeMainView view = new() { CustomSpotifyClientId = Texts.ValidClientId };
    private readonly FakeSettingsStore store = new();
    private readonly FakeAuthService auth = new();
    private readonly FakeGateway gateway = new();
    private readonly FakeReportWriter reportWriter = new();

    private MainFormController CreateController() =>
        new(view, store, reportWriter, auth, new SpotifyReleaseUpdater(gateway, store, reportWriter, new FakeClock()));

    private static string Text(string key, params object[] args) => AppLocalization.Get(key, args);

    // ---- starting a run ----

    // Verifies that without a saved Client ID the run does not start and the user is told why.
    [Fact]
    public async Task StartUpdate_WithoutAClientId_ExplainsAndDoesNotRun()
    {
        view.CustomSpotifyClientId = string.Empty;
        store.Stored.CustomSpotifyClientId = null;

        await CreateController().StartUpdateAsync();

        Assert.True(view.Has("ShowInfo:"));
        Assert.False(view.Has("PrepareRunUi"));
        Assert.False(view.Has("ConfirmStart"));
    }

    // Verifies that answering "no" to "start the update?" starts nothing.
    [Fact]
    public async Task StartUpdate_WhenNotConfirmed_DoesNotRun()
    {
        view.ConfirmStartAnswer = false;

        await CreateController().StartUpdateAsync();

        Assert.True(view.Has("ConfirmStart"));
        Assert.False(view.Has("PrepareRunUi"));
    }

    // Verifies that an active rate-limit cooldown stops the run before it asks anything.
    [Fact]
    public async Task StartUpdate_DuringACooldown_ExplainsAndDoesNotRun()
    {
        store.Stored.SpotifyCooldownUntilUtc = DateTimeOffset.UtcNow.AddMinutes(30);
        store.Stored.SpotifyCooldownClientId = Texts.ValidClientId;

        await CreateController().StartUpdateAsync();

        Assert.True(view.Has("ShowInfo:"));
        Assert.False(view.Has("ConfirmStart"));
        Assert.False(view.Has("PrepareRunUi"));
    }

    // Verifies that a run that fails shows the error and puts the window back in order
    // (the timer stops, the sign-in is looked at again).
    [Fact]
    public async Task StartUpdate_WhenTheRunFails_ShowsTheError_AndRestoresTheWindow()
    {
        gateway.OnFollowedArtists = _ => throw new InvalidOperationException("boom");

        await CreateController().StartUpdateAsync();

        Assert.True(view.Has("ShowRunFailed:boom"));
        Assert.True(view.Has("StopElapsedTimer"));
        Assert.True(view.Has("SetAuthenticationState:"));
        Assert.False(view.Has("ShowCompleted"));
    }

    // Verifies that a run that fails with a rate limit starts a cooldown that is saved.
    [Fact]
    public async Task StartUpdate_WhenRateLimited_SavesACooldown()
    {
        gateway.OnFollowedArtists = _ => throw new SpotifyRateLimitException("GET", "me/following", TimeSpan.FromMinutes(20), "slow down");

        await CreateController().StartUpdateAsync();

        Assert.NotNull(store.Stored.SpotifyCooldownUntilUtc);
        Assert.True(store.Stored.SpotifyCooldownUntilUtc > DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.Equal(Texts.ValidClientId, store.Stored.SpotifyCooldownClientId);
        Assert.True(view.Has("ShowRunFailed:"));
    }

    // Verifies that a cooldown that cannot be saved (disk problem) is no reason to lose the
    // message: the run still ends cleanly and the user hears about the save problem.
    [Fact]
    public async Task StartUpdate_WhenRateLimited_AndTheSaveFails_StillEndsCleanly()
    {
        // (The disk fails only once the run is on its way: the first save, before the run, still works.)
        gateway.OnFollowedArtists = _ =>
        {
            store.SaveFailure = new IOException("disk full");
            throw new SpotifyRateLimitException("GET", "me/following", TimeSpan.FromMinutes(20), "slow down");
        };
        MainFormController controller = CreateController();

        await controller.StartUpdateAsync();

        Assert.True(view.Has("ShowRunFailed:"));
        Assert.True(view.Has("StopElapsedTimer"));
        Assert.Contains(view.Calls, call => call.StartsWith("AppendOutput:", StringComparison.Ordinal) && call.Contains("disk full"));
        Assert.False(controller.IsRunInProgress);
    }

    // Verifies the run in progress flag: true while it runs, false before and after.
    [Fact]
    public async Task IsRunInProgress_IsTrueOnlyWhileTheRunLasts()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.OnFollowedArtists = async _ =>
        {
            started.SetResult();
            await release.Task;
            return Array.Empty<ReleaseUpdater.Core.Models.SpotifyArtist>();
        };
        MainFormController controller = CreateController();
        Assert.False(controller.IsRunInProgress);

        Task run = controller.StartUpdateAsync();
        await started.Task.WaitAsync(Timeout);
        Assert.True(controller.IsRunInProgress);

        release.SetException(new InvalidOperationException("stop"));
        await run.WaitAsync(Timeout);
        Assert.False(controller.IsRunInProgress);
    }

    // ---- cancelling ----

    // Verifies that cancelling a running update ends it as "cancelled" (not as a failure),
    // with the notices for the user.
    [Fact]
    public async Task CancelUpdate_EndsTheRunAsCancelled()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.OnFollowedArtists = async cancellationToken =>
        {
            started.SetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
            return Array.Empty<ReleaseUpdater.Core.Models.SpotifyArtist>();
        };
        MainFormController controller = CreateController();

        Task run = controller.StartUpdateAsync();
        await started.Task.WaitAsync(Timeout);
        controller.CancelUpdate();
        await run.WaitAsync(Timeout);

        Assert.True(view.Has("ShowCancelled"));
        Assert.False(view.Has("ShowRunFailed"));
        Assert.True(view.Has("ShowToast:" + Text(AppStrings.ControllerToastCancelling)));
    }

    // Verifies that cancelling when nothing runs does nothing at all.
    [Fact]
    public void CancelUpdate_WithoutARun_DoesNothing()
    {
        CreateController().CancelUpdate();

        Assert.Empty(view.Calls);
    }

    // Verifies the close-during-a-run case: once the window is shutting down, a run that is
    // still ending - cancelled, failed or even finished - must not touch the window or the
    // network any more (its controls and HTTP clients are being disposed).
    [Theory]
    [InlineData("fail")]
    [InlineData("cancel")]
    [InlineData("ratelimit")]
    [InlineData("finish")]
    public async Task WhenTheWindowIsShuttingDown_ARunThatEndsTouchesNothing(string ending)
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.OnFollowedArtists = async cancellationToken =>
        {
            started.SetResult();
            await release.Task;

            return ending switch
            {
                "fail" => throw new InvalidOperationException("boom"),
                "cancel" => throw new OperationCanceledException(),
                "ratelimit" => throw new SpotifyRateLimitException("GET", "x", TimeSpan.FromMinutes(5), "slow"),
                _ => Array.Empty<ReleaseUpdater.Core.Models.SpotifyArtist>()
            };
        };
        MainFormController controller = CreateController();

        Task run = controller.StartUpdateAsync();
        await started.Task.WaitAsync(Timeout);

        view.IsShuttingDown = true;
        int callsBefore = view.Calls.Count;
        release.SetResult();
        await run.WaitAsync(Timeout);

        Assert.Equal(callsBefore, view.Calls.Count);
        Assert.False(controller.IsRunInProgress);
    }

    // Verifies that cancelling because the window closes shows no "cancelling" status or toast on the closing window.
    [Fact]
    public async Task CancelUpdate_WhenShuttingDown_ShowsNothing()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.OnFollowedArtists = async cancellationToken =>
        {
            started.SetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
            return Array.Empty<ReleaseUpdater.Core.Models.SpotifyArtist>();
        };
        MainFormController controller = CreateController();
        Task run = controller.StartUpdateAsync();
        await started.Task.WaitAsync(Timeout);

        view.IsShuttingDown = true;
        int callsBefore = view.Calls.Count;
        controller.CancelUpdate();
        await run.WaitAsync(Timeout);

        Assert.Equal(callsBefore, view.Calls.Count);
    }

    // ---- saving the settings ----

    // Verifies that a settings file that cannot be written is reported in the status line,
    // not with a dialog (this save happens on every change of a box) and not with a crash.
    [Fact]
    public void SaveSettingsFromUi_WhenTheSaveFails_ShowsTheStatus_NotADialog()
    {
        store.SaveFailure = new IOException("disk full");

        CreateController().SaveSettingsFromUi();

        Assert.Contains(view.Calls, call => call.StartsWith("SetStatus:", StringComparison.Ordinal) && call.Contains("disk full"));
        Assert.False(view.Has("ShowError"));
    }

    // Verifies that where the user acts on purpose (sign in), the same failure gets a dialog -
    // and the sign-in does not start as if the settings had been kept.
    [Fact]
    public async Task Login_WhenTheSaveFails_ShowsADialog_AndDoesNotSignIn()
    {
        store.SaveFailure = new UnauthorizedAccessException("access denied");
        bool loginCalled = false;
        auth.OnLogin = _ =>
        {
            loginCalled = true;
            return Task.FromResult(new SpotifyUser("id", "Name"));
        };

        await CreateController().LoginAsync();

        Assert.True(view.Has("ShowError:"));
        Assert.False(loginCalled);
    }

    // Verifies that saving keeps the cooldown that is in the file (the window does not know about it).
    [Fact]
    public void SaveSettingsFromUi_KeepsTheSavedCooldown()
    {
        DateTimeOffset until = DateTimeOffset.UtcNow.AddHours(1);
        store.Stored.SpotifyCooldownUntilUtc = until;
        store.Stored.SpotifyCooldownClientId = Texts.ValidClientId;

        CreateController().SaveSettingsFromUi();

        Assert.Equal(until, store.Stored.SpotifyCooldownUntilUtc);
        Assert.Equal(Texts.ValidClientId, store.Stored.SpotifyCooldownClientId);
    }

    // Verifies that a playlist that was found earlier is kept when the window has no ID but the same name.
    [Fact]
    public void SaveSettingsFromUi_KeepsTheSavedPlaylistId_ForTheSameName()
    {
        store.Stored.PlaylistName = ReleaseDefaults.PlaylistName;
        store.Stored.PlaylistId = "playlist-1";
        view.PlaylistId = null;

        CreateController().SaveSettingsFromUi();

        Assert.Equal("playlist-1", store.Stored.PlaylistId);
    }

    // Verifies that a renamed playlist does not keep the old playlist's ID (it would update the wrong playlist).
    [Fact]
    public void SaveSettingsFromUi_DropsTheSavedPlaylistId_WhenTheNameChanged()
    {
        store.Stored.PlaylistName = "Old name";
        store.Stored.PlaylistId = "playlist-1";
        view.PlaylistName = "New name";
        view.PlaylistId = null;

        CreateController().SaveSettingsFromUi();

        Assert.Null(store.Stored.PlaylistId);
    }

    // ---- checks of the entries ----

    // Verifies that a playlist name that is too long stops a run with a warning.
    [Fact]
    public async Task StartUpdate_WithAnInvalidPlaylistName_MarksTheBoxInsteadOfAskingWithADialog()
    {
        view.PlaylistName = new string('x', 101);

        await CreateController().StartUpdateAsync();

        Assert.False(string.IsNullOrEmpty(view.PlaylistNameError));
        Assert.False(view.Has("ShowWarning:"));
        Assert.False(view.Has("ConfirmStart"));
        Assert.Equal(0, store.SaveCount);
    }

    // Verifies which Client IDs are accepted: none (blank) or exactly 32 hexadecimal characters.
    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("0123456789abcdef0123456789abcdef", true)]
    [InlineData("0123456789ABCDEF0123456789ABCDEF", true)]
    [InlineData("0123456789abcdef0123456789abcde", false)]
    [InlineData("0123456789abcdef0123456789abcdef0", false)]
    [InlineData("0123456789abcdef0123456789abcdeg", false)]
    [InlineData("not a client id", false)]
    public void ClientIdEntry_IsCheckedWhenSaved(string clientId, bool accepted)
    {
        view.CustomSpotifyClientId = clientId;
        view.IsSpotifyClientIdReadOnly = false;

        CreateController().ToggleSpotifyClientIdEdit();

        Assert.Equal(accepted, view.Has("DisableSpotifyClientIdEditing"));
        Assert.Equal(!accepted, view.Has("ShowWarning:"));
        Assert.Equal(!accepted, view.Has("FocusSpotifyClientId"));
    }

    // ---- the two edit buttons ----

    // Verifies that the first press on the pencil only unlocks the box.
    [Fact]
    public void TogglePlaylistNameEdit_WhenLocked_Unlocks()
    {
        view.IsPlaylistNameReadOnly = true;

        CreateController().TogglePlaylistNameEdit();

        Assert.True(view.Has("EnablePlaylistNameEditing"));
        Assert.Equal(0, store.SaveCount);
    }

    // Verifies that pressing the check mark with a valid name locks the box again and saves it.
    [Fact]
    public void TogglePlaylistNameEdit_WhenEditingWithAValidName_LocksAndSaves()
    {
        view.IsPlaylistNameReadOnly = false;
        view.PlaylistName = "My releases";

        CreateController().TogglePlaylistNameEdit();

        Assert.True(view.Has("DisablePlaylistNameEditing"));
        Assert.Equal("My releases", store.Stored.PlaylistName);
        Assert.True(view.Has("ShowToast:"));
    }

    // Verifies that with an invalid name the box stays open for the user to correct it.
    [Fact]
    public void TogglePlaylistNameEdit_WhenEditingWithAnInvalidName_StaysOpen()
    {
        view.IsPlaylistNameReadOnly = false;
        view.PlaylistName = "   ";

        CreateController().TogglePlaylistNameEdit();

        Assert.False(string.IsNullOrEmpty(view.PlaylistNameError));
        Assert.False(view.Has("ShowWarning:"));
        Assert.True(view.Has("FocusPlaylistName"));
        Assert.False(view.Has("DisablePlaylistNameEditing"));
        Assert.Equal(0, store.SaveCount);
    }

    // Verifies that a box that locks again but cannot be saved says so (the text stays as typed).
    [Fact]
    public void TogglePlaylistNameEdit_WhenTheSaveFails_ShowsTheStatus_AndNoSavedToast()
    {
        view.IsPlaylistNameReadOnly = false;
        view.PlaylistName = "My releases";
        store.SaveFailure = new IOException("disk full");

        CreateController().TogglePlaylistNameEdit();

        Assert.Contains(view.Calls, call => call.StartsWith("SetStatus:", StringComparison.Ordinal) && call.Contains("disk full"));
        Assert.False(view.Has("ShowToast:"));
    }

    // ---- sign in and out ----

    // Verifies the normal sign-in: the window shows the user, and the user is told.
    [Fact]
    public async Task Login_Success_ShowsTheUser()
    {
        auth.OnLogin = _ => Task.FromResult(new SpotifyUser("id", "Erik"));

        await CreateController().LoginAsync();

        Assert.True(view.Has("SetAuthenticationBusy"));
        Assert.Contains(view.Calls, call => call == "SetLoginStatus:" + Text(AppStrings.ControllerSignedInFormat, "Erik"));
        Assert.True(view.Has("SetAuthenticationState:True"));
        Assert.True(view.Has("ShowToast:" + Text(AppStrings.ControllerToastLoginSuccess)));
    }

    // Verifies that a cancelled sign-in (closed browser, timeout) is a quiet "cancelled", not an error dialog.
    [Fact]
    public async Task Login_Cancelled_IsQuiet()
    {
        auth.OnLogin = _ => throw new OperationCanceledException();

        await CreateController().LoginAsync();

        Assert.True(view.Has("ShowToast:" + Text(AppStrings.ControllerToastLoginCancelled)));
        Assert.False(view.Has("ShowError"));
    }

    // Verifies that a failed sign-in shows what went wrong.
    [Fact]
    public async Task Login_Failure_ShowsTheError()
    {
        auth.OnLogin = _ => throw new InvalidOperationException("denied");

        await CreateController().LoginAsync();

        Assert.True(view.Has("ShowError:denied"));
        Assert.True(view.Has("SetAuthenticationState:False"));
    }

    // Verifies that a callback torn down because the sign-in token was cancelled counts as a
    // cancellation even if the exception itself is of another kind (a listener that was stopped).
    [Fact]
    public async Task Login_AnyFailureAfterTheTimeout_IsTreatedAsCancelled()
    {
        // The 5 minute timeout cannot be waited for; an OperationCanceledException subclass stands in for it.
        auth.OnLogin = _ => throw new TaskCanceledException();

        await CreateController().LoginAsync();

        Assert.False(view.Has("ShowError"));
    }

    // Verifies that pressing the sign-in button again while the browser is awaited ends the sign-in:
    // quiet "cancelled" notices, no error dialog, and the controller is free for the next sign-in.
    [Fact]
    public async Task CancelLogin_EndsAWaitingSignIn_Quietly()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        auth.OnLogin = async cancellationToken =>
        {
            started.SetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
            return new SpotifyUser("id", "never");
        };
        MainFormController controller = CreateController();
        Assert.False(controller.IsLoginInProgress);

        Task login = controller.LoginAsync();
        await started.Task.WaitAsync(Timeout);
        Assert.True(controller.IsLoginInProgress);
        controller.CancelLogin();
        await login.WaitAsync(Timeout);

        Assert.False(controller.IsLoginInProgress);
        Assert.True(view.Has("ShowToast:" + Text(AppStrings.ControllerToastLoginCancelled)));
        Assert.True(view.Has("SetStatus:" + Text(AppStrings.ControllerLoginCancelledStatus)));
        Assert.False(view.Has("ShowError"));
        Assert.False(view.Has("SetAuthenticationState:True"));
    }

    // Verifies that cancelling when no sign-in waits does nothing.
    [Fact]
    public void CancelLogin_WithoutASignIn_DoesNothing()
    {
        CreateController().CancelLogin();

        Assert.Empty(view.Calls);
    }

    // Verifies that, after a cancelled sign-in, a new one can be started and works.
    [Fact]
    public async Task Login_CanBeStartedAgain_AfterACancellation()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        auth.OnLogin = async cancellationToken =>
        {
            started.TrySetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
            return new SpotifyUser("id", "never");
        };
        MainFormController controller = CreateController();
        Task first = controller.LoginAsync();
        await started.Task.WaitAsync(Timeout);
        controller.CancelLogin();
        await first.WaitAsync(Timeout);

        auth.OnLogin = _ => Task.FromResult(new SpotifyUser("id", "Erik"));
        await controller.LoginAsync();

        Assert.True(view.Has("SetAuthenticationState:True"));
    }

    // Verifies the close-during-sign-in case: with the window shutting down, a sign-in that ends
    // (cancelled by the closing, failed, or even succeeded) touches nothing.
    [Theory]
    [InlineData("cancel")]
    [InlineData("fail")]
    [InlineData("succeed")]
    public async Task WhenTheWindowIsShuttingDown_ASignInThatEndsTouchesNothing(string ending)
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        auth.OnLogin = async cancellationToken =>
        {
            started.SetResult();
            await release.Task;

            return ending switch
            {
                "cancel" => throw new OperationCanceledException(),
                "fail" => throw new InvalidOperationException("boom"),
                _ => new SpotifyUser("id", "Erik")
            };
        };
        MainFormController controller = CreateController();

        Task login = controller.LoginAsync();
        await started.Task.WaitAsync(Timeout);
        view.IsShuttingDown = true;
        int callsBefore = view.Calls.Count;
        release.SetResult();
        await login.WaitAsync(Timeout);

        Assert.Equal(callsBefore, view.Calls.Count);
        Assert.False(controller.IsLoginInProgress);
    }

    // Verifies that a start-up where Spotify cannot be reached does not look like a sign-out: the
    // user stays signed in (so signing out stays possible), with a note.
    [Fact]
    public async Task LoadSettings_WhenSpotifyCannotBeReached_KeepsTheUserSignedIn_WithANote()
    {
        auth.Unavailable = true;

        await CreateController().LoadSettingsAsync();

        Assert.True(view.Has("SetLoginStatus:" + Text(AppStrings.ControllerSignedInUnverified)));
        Assert.True(view.Has("SetAuthenticationState:True"));
        Assert.False(view.Has("SetAuthenticationState:False"));
    }

    // Verifies that a sign-in that Spotify really does not know any more shows as signed out.
    [Fact]
    public async Task LoadSettings_WhenNotSignedIn_ShowsSignedOut()
    {
        await CreateController().LoadSettingsAsync();

        Assert.True(view.Has("SetLoginStatus:" + Text(AppStrings.NotSignedIn)));
        Assert.True(view.Has("SetAuthenticationState:False"));
    }

    // Verifies sign-out.
    [Fact]
    public async Task Logout_SignsOut_AndShowsIt()
    {
        auth.CurrentUser = new SpotifyUser("id", "Erik");

        await CreateController().LogoutAsync();

        Assert.Equal(1, auth.LogoutCount);
        Assert.True(view.Has("SetAuthenticationState:False"));
        Assert.True(view.Has("ShowToast:" + Text(AppStrings.ControllerToastSignedOut)));
    }

    // ---- start-up, report ----

    // Verifies the start-up: the saved settings are shown, the sign-in is looked at and the update check begins.
    [Fact]
    public async Task LoadSettings_ShowsTheSettings_AndBeginsTheUpdateCheck()
    {
        store.Stored.PlaylistName = "Saved name";
        auth.CurrentUser = new SpotifyUser("id", "Erik");

        await CreateController().LoadSettingsAsync();

        Assert.Equal("Saved name", view.LastSetSettings!.PlaylistName);
        Assert.True(view.Has("SetAuthenticationState:True"));
        Assert.True(view.Has("BeginUpdateCheck"));
    }

    // Verifies that, without a Client ID, the start-up says so in the status.
    [Fact]
    public async Task LoadSettings_WithoutAClientId_ShowsThatItIsRequired()
    {
        store.Stored.CustomSpotifyClientId = null;

        await CreateController().LoadSettingsAsync();

        Assert.True(view.Has("SetClientIdStatus:" + ClientIdStatusKind.Missing));
        Assert.True(view.Has("SetUpdateAvailability:False"));
    }

    // Verifies that a cooldown shows as such at start-up and blocks the update button.
    [Fact]
    public async Task LoadSettings_DuringACooldown_ShowsItAndBlocksTheButton()
    {
        store.Stored.SpotifyCooldownUntilUtc = DateTimeOffset.UtcNow.AddMinutes(30);
        store.Stored.SpotifyCooldownClientId = Texts.ValidClientId;

        await CreateController().LoadSettingsAsync();

        Assert.True(view.Has("SetClientIdStatus:" + ClientIdStatusKind.Cooldown));
        Assert.True(view.Has("SetUpdateAvailability:False"));
    }

    // Verifies that "open report" without a report file says so instead of failing.
    [Fact]
    public void OpenReport_WithoutAReport_SaysSo()
    {
        CreateController().OpenReport();

        Assert.True(view.Has("ShowInfo:" + Text(AppStrings.ControllerNoReportAvailable)));
        Assert.False(view.Has("ShowToast"));
    }
}
