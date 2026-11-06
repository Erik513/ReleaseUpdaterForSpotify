using ErikwnkCore;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Localization;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Core.Services;
using System.Globalization;

namespace ReleaseUpdaterGui
{
    public class MainFormController
    {
        private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);

        private readonly IMainView view;
        private readonly ISettingsStore settingsStore;
        private readonly IReportWriter reportWriter;
        private readonly ISpotifyAuthService authService;
        private readonly SpotifyReleaseUpdater releaseUpdater;

        private CancellationTokenSource? runCancellation;
        private CancellationTokenSource? loginCancellation;

        public MainFormController(
            IMainView view,
            ISettingsStore settingsStore,
            IReportWriter reportWriter,
            ISpotifyAuthService authService,
            SpotifyReleaseUpdater releaseUpdater)
        {
            this.view = view;
            this.settingsStore = settingsStore;
            this.reportWriter = reportWriter;
            this.authService = authService;
            this.releaseUpdater = releaseUpdater;
        }

        public async Task LoadSettingsAsync()
        {
            ReleaseSettings settings = settingsStore.Load();
            view.SetSettings(settings);
            await RefreshLoginStatusAsync();
            view.SetStatus(AppLocalization.Get(AppStrings.ReadyToRun));
            ApplyRunAvailability(updateStatus: true);

            view.BeginUpdateCheck();
        }

        public async Task LoginAsync()
        {
            if (!SaveSettings(showValidationMessage: true))
            {
                return;
            }

            if (!EnsureSpotifyClientIdConfigured(showMessage: true))
            {
                return;
            }

            using CancellationTokenSource cancellation = new(LoginTimeout);
            loginCancellation = cancellation;

            try
            {
                view.SetAuthenticationBusy();
                view.SetStatus(AppLocalization.Get(AppStrings.ControllerWaitingForLogin));
                view.SetLoginStatus(AppLocalization.Get(AppStrings.ControllerBrowserLoginInProgress));

                SpotifyUser user = await authService.LoginAsync(
                    cancellation.Token);

                if (IsShuttingDown)
                {
                    return;
                }

                view.SetLoginStatus(AppLocalization.Get(AppStrings.ControllerSignedInFormat, user.DisplayName));
                view.SetAuthenticationState(isLoggedIn: true);
                view.SetStatus(AppLocalization.Get(AppStrings.ControllerSignedIn));

                view.ShowToast(AppLocalization.Get(AppStrings.ControllerToastLoginSuccess));
            }
            catch (Exception ex) when (IsLoginCancellation(ex, cancellation.Token))
            {
                if (IsShuttingDown)
                {
                    return;
                }

                await RefreshLoginStatusAsync();
                view.SetStatus(AppLocalization.Get(AppStrings.ControllerLoginCancelledStatus));
                view.ShowToast(AppLocalization.Get(AppStrings.ControllerToastLoginCancelled));
            }
            catch (Exception ex)
            {
                if (IsShuttingDown)
                {
                    return;
                }

                await RefreshLoginStatusAsync();
                view.SetStatus(AppLocalization.Get(AppStrings.ControllerLoginFailed));
                view.ShowError(ex.Message, AppLocalization.Get(AppStrings.ControllerSpotifyLoginTitle));
            }
            finally
            {
                loginCancellation = null;
            }
        }

        public bool IsLoginInProgress => loginCancellation is not null;

        /// <summary>
        /// Ends a sign-in that is waiting for the browser (the user closed the tab, changed their mind):
        /// the wait stops and the port of the callback is free again. Nothing happens when none is running.
        /// </summary>
        public void CancelLogin()
        {
            loginCancellation?.Cancel();
        }

        public async Task LogoutAsync()
        {
            try
            {
                view.SetAuthenticationBusy();
                await authService.LogoutAsync(CancellationToken.None);
                view.SetLoginStatus(AppLocalization.Get(AppStrings.NotSignedIn));
                view.SetAuthenticationState(isLoggedIn: false);
                view.SetStatus(AppLocalization.Get(AppStrings.ControllerSignedOut));
                view.ShowToast(AppLocalization.Get(AppStrings.ControllerToastSignedOut));
            }
            catch (Exception ex)
            {
                await RefreshLoginStatusAsync();
                view.SetStatus(AppLocalization.Get(AppStrings.ControllerLogoutFailed));
                view.ShowError(ex.Message, AppLocalization.Get(AppStrings.ControllerSpotifyLogoutTitle));
            }
        }

        public async Task StartUpdateAsync()
        {
            if (!SaveSettings(showValidationMessage: true))
            {
                return;
            }

            if (!EnsureSpotifyClientIdConfigured(showMessage: true))
            {
                return;
            }

            if (!EnsureRunIsAllowed(showMessage: true))
            {
                return;
            }

            if (!view.ConfirmStart())
            {
                return;
            }

            view.PrepareRunUi();
            runCancellation = new CancellationTokenSource();

            try
            {
                Progress<ReleaseProgress> progress = new(view.ApplyProgress);

                PlaylistUpdateResult result = await releaseUpdater.RunAsync(
                    progress,
                    runCancellation.Token);

                if (!IsShuttingDown)
                {
                    view.ShowCompleted(result);
                }
            }
            catch (SpotifyRateLimitException ex)
            {
                if (IsShuttingDown)
                {
                    return;
                }

                if (IsLogicTestModeEnabled())
                {
                    view.ShowRunFailed(
                        AppLocalization.Get(AppStrings.ControllerTestModeRateLimit));
                    return;
                }

                ReleaseSettings settings = settingsStore.Load().Normalize();
                SpotifyRunPolicy.MarkRateLimitCooldown(
                    settings,
                    DateTimeOffset.UtcNow,
                    ex.RetryAfter,
                    quotaExceeded: ex.IsQuotaExceeded);

                try
                {
                    settingsStore.Save(settings);
                }
                catch (Exception saveException) when (IsFileAccessException(saveException))
                {
                    // The cooldown is still shown below; it just will not survive a restart.
                    view.AppendOutput(AppLocalization.Get(
                        AppStrings.ControllerSettingsSaveFailedFormat,
                        saveException.Message));
                }

                view.SetSettings(settings);

                string message = CreateCooldownMessage(settings);
                view.ShowRunFailed(message);
            }
            catch (OperationCanceledException)
            {
                if (!IsShuttingDown)
                {
                    view.ShowCancelled();
                }
            }
            catch (Exception ex)
            {
                if (!IsShuttingDown)
                {
                    view.ShowRunFailed(ex.Message);
                }
            }
            finally
            {
                runCancellation?.Dispose();
                runCancellation = null;

                // When the window is closing, its controls and HTTP clients are being
                // torn down - touching them (or the network) here would only fail.
                if (!IsShuttingDown)
                {
                    view.StopElapsedTimer();
                    await RefreshLoginStatusAsync();
                    ApplyRunAvailability(updateStatus: false);
                }
            }
        }

        private bool IsShuttingDown => view.IsShuttingDown;

        private static bool IsFileAccessException(Exception exception) =>
            exception is IOException or UnauthorizedAccessException;

        public bool IsRunInProgress => runCancellation is not null;

        public void CancelUpdate()
        {
            if (runCancellation is null)
            {
                return;
            }

            runCancellation?.Cancel();

            if (IsShuttingDown)
            {
                return;
            }

            view.SetStatus(AppLocalization.Get(AppStrings.ControllerCancelling));
            view.ShowToast(AppLocalization.Get(AppStrings.ControllerToastCancelling));
        }

        public void OpenReport()
        {
            string reportTitle = AppLocalization.Get(AppStrings.Report);

            switch (ReportLauncher.Open(reportWriter.ReportPath, out string? error))
            {
                case ReportOpenResult.Missing:
                    view.ShowInfo(
                        AppLocalization.Get(AppStrings.ControllerNoReportAvailable),
                        reportTitle);
                    break;

                case ReportOpenResult.Opened:
                    view.ShowToast(AppLocalization.Get(AppStrings.ControllerToastOpeningReport));
                    break;

                default:
                    view.ShowError(
                        AppLocalization.Get(AppStrings.ControllerCouldNotOpenReportFormat, error ?? string.Empty),
                        reportTitle);
                    break;
            }
        }

        // The two boxes (playlist name, Client ID) are lockable text boxes of the library: the pencil unlocks, and the check mark
        // or Enter asks "may this be accepted?" first (EditAccepting) - the box stays open, with the reason in its frame, when not -
        // and then tells that it was accepted (EditAccepted). The window passes those on:

        /// <summary>Whether the playlist name in the box may be accepted; when not, the box shows why and stays open.</summary>
        public bool CanAcceptPlaylistName() => ValidatePlaylistName(showMessage: true);

        /// <summary>The playlist name was accepted (the box is locked): it is saved.</summary>
        public void PlaylistNameAccepted()
        {
            if (SaveSettings(showValidationMessage: false))
            {
                ApplyRunAvailability(updateStatus: false);
                view.ShowToast(AppLocalization.Get(AppStrings.ControllerToastPlaylistNameSaved));
            }
        }

        /// <summary>Whether the Client ID in the box may be accepted; when not, the box shows why and stays open.</summary>
        public bool CanAcceptSpotifyClientId() => ValidateSpotifyClientId(showMessage: true);

        /// <summary>The Client ID was accepted (the box is locked): it is saved.</summary>
        public void SpotifyClientIdAccepted()
        {
            if (SaveSettings(showValidationMessage: false))
            {
                ApplyRunAvailability(updateStatus: false);
                view.ShowToast(AppLocalization.Get(AppStrings.ControllerToastClientIdSaved));
            }
        }

        /// <summary>
        /// A box was unlocked or locked (or an editing was cancelled): an update cannot be started while one is open, so the run button
        /// follows. The text that is shown again after Escape is the saved one, so a message about it goes.
        /// </summary>
        public void EditingChanged()
        {
            ApplyRunAvailability(updateStatus: false);
        }

        public void SaveSettingsFromUi()
        {
            if (SaveSettings(showValidationMessage: false))
            {
                ApplyRunAvailability(updateStatus: false);
            }
        }

        public void RefreshRunAvailability()
        {
            if (runCancellation is not null)
            {
                return;
            }

            ApplyRunAvailability(updateStatus: false);
        }

        /// <summary>
        /// Re-derives every piece of dynamic UI text this controller owns
        /// (login status, client ID status, run availability) in the newly
        /// selected language - called by MainForm after a language switch,
        /// alongside its own refresh of static labels/tooltips.
        /// </summary>
        public async Task RefreshLocalizedUiAsync()
        {
            await RefreshLoginStatusAsync();
            ApplyRunAvailability(updateStatus: runCancellation is null);
        }

        private async Task RefreshLoginStatusAsync()
        {
            SpotifySignInCheck check = await authService.CheckSignInAsync(
                CancellationToken.None);

            switch (check.State)
            {
                case SpotifySignInState.SignedIn:
                    view.SetLoginStatus(AppLocalization.Get(
                        AppStrings.ControllerSignedInFormat,
                        check.User?.DisplayName ?? string.Empty));
                    view.SetAuthenticationState(true);
                    break;

                case SpotifySignInState.Unavailable:
                    // A token is there and Spotify could not be asked: still signed in (so signing out
                    // stays possible), with a note, instead of a sign-out that did not happen.
                    view.SetLoginStatus(AppLocalization.Get(AppStrings.ControllerSignedInUnverified));
                    view.SetAuthenticationState(true);
                    break;

                default:
                    view.SetLoginStatus(AppLocalization.Get(AppStrings.NotSignedIn));
                    view.SetAuthenticationState(false);
                    break;
            }
        }

        private bool SaveSettings(bool showValidationMessage)
        {
            if (!ValidatePlaylistName(showValidationMessage))
            {
                return false;
            }

            if (!ValidateSpotifyClientId(showValidationMessage))
            {
                return false;
            }

            ReleaseSettings settings = view.GetSettings();
            ReleaseSettings savedSettings = settingsStore.Load();

            if (ShouldKeepSavedPlaylistId(settings, savedSettings))
            {
                settings.PlaylistId = savedSettings.PlaylistId;
                view.SetSettings(settings);
            }

            settings.SpotifyCooldownUntilUtc = savedSettings.SpotifyCooldownUntilUtc;
            settings.SpotifyCooldownClientId = savedSettings.SpotifyCooldownClientId;
            settings.SpotifyCooldownAccountWide = savedSettings.SpotifyCooldownAccountWide;
            settings.UnreleasedArtistFirst = savedSettings.UnreleasedArtistFirst;

            try
            {
                settingsStore.Save(settings);
            }
            catch (Exception ex) when (IsFileAccessException(ex))
            {
                // A full disk or a locked file must not crash the window: say so and
                // report "not saved" so callers do not carry on as if it had worked.
                string message = AppLocalization.Get(
                    AppStrings.ControllerSettingsSaveFailedFormat,
                    ex.Message);
                view.SetStatus(message);

                if (showValidationMessage)
                {
                    view.ShowError(message, AppLocalization.Get(AppStrings.MainFormError));
                }

                return false;
            }

            view.SetStatus(AppLocalization.Get(AppStrings.ControllerSettingsSaved));

            return true;
        }

        private bool ValidatePlaylistName(bool showMessage)
        {
            bool isValid = PlaylistValidator.IsValidPlaylistName(
                view.PlaylistName,
                out string errorMessage);

            if (isValid)
            {
                view.SetPlaylistNameError(null);
            }
            else if (showMessage)
            {
                // A red frame with the reason at the box (it goes when the user types), and the same text in the status line.
                view.SetPlaylistNameError(errorMessage);
                view.SetStatus(errorMessage);
            }

            return isValid;
        }

        private bool ValidateSpotifyClientId(bool showMessage)
        {
            string clientId = view.CustomSpotifyClientId;

            if (string.IsNullOrWhiteSpace(clientId))
            {
                return true;
            }

            bool isValid = clientId.Length == 32 &&
                clientId.All(Uri.IsHexDigit);

            if (isValid)
            {
                view.SetSpotifyClientIdError(null);
            }
            else if (showMessage)
            {
                // A red frame with the reason at the box (it goes when the user types), and the same text in the status line.
                string message = AppLocalization.Get(AppStrings.ControllerInvalidClientIdMessage);
                view.SetSpotifyClientIdError(message);
                view.SetStatus(message);
            }

            return isValid;
        }

        private bool EnsureSpotifyClientIdConfigured(bool showMessage)
        {
            ReleaseSettings settings = view.GetSettings();

            if (settings.HasSpotifyClientId)
            {
                return true;
            }

            string message = AppLocalization.Get(AppStrings.ControllerClientIdRequiredForLoginMessage);
            string title = AppLocalization.Get(AppStrings.SpotifyClientIdRequired);
            view.SetStatus(title);
            view.SetUpdateAvailability(false, message);
            ApplyClientIdStatus(settings);

            if (showMessage)
            {
                view.ShowInfo(message, title);
            }

            return false;
        }

        private bool EnsureRunIsAllowed(bool showMessage)
        {
            ReleaseSettings settings = settingsStore.Load().Normalize();
            ApplyClientIdStatus(settings);
            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (!settings.HasSpotifyClientId)
            {
                string message = AppLocalization.Get(AppStrings.ControllerClientIdRequiredForUpdateMessage);
                string title = AppLocalization.Get(AppStrings.SpotifyClientIdRequired);
                view.SetStatus(title);
                view.SetUpdateAvailability(false, message);

                if (showMessage)
                {
                    view.ShowInfo(message, title);
                }

                return false;
            }

            if (SpotifyRunPolicy.IsCooldownActive(settings, now))
            {
                string message = CreateCooldownMessage(settings);
                view.SetStatus(AppLocalization.Get(AppStrings.ControllerCooldownActive));
                view.SetUpdateAvailability(false, message);

                if (showMessage)
                {
                    view.ShowInfo(message, AppLocalization.Get(AppStrings.ControllerCooldownTitle));
                }

                return false;
            }

            view.SetUpdateAvailability(true, AppLocalization.Get(AppStrings.UpdatePlaylist));
            return true;
        }

        private void ApplyRunAvailability(bool updateStatus)
        {
            ReleaseSettings settings = settingsStore.Load().Normalize();
            ApplyClientIdStatus(settings);

            if (!settings.HasSpotifyClientId)
            {
                string message = AppLocalization.Get(AppStrings.ControllerClientIdRequiredTooltip);
                view.SetUpdateAvailability(false, message);

                if (updateStatus)
                {
                    view.SetStatus(AppLocalization.Get(AppStrings.SpotifyClientIdRequired));
                }

                return;
            }

            if (SpotifyRunPolicy.IsCooldownActive(settings, DateTimeOffset.UtcNow))
            {
                string message = CreateCooldownMessage(settings);
                view.SetUpdateAvailability(false, message);

                if (updateStatus)
                {
                    view.SetStatus(AppLocalization.Get(AppStrings.ControllerCooldownActive));
                }

                return;
            }

            view.SetUpdateAvailability(true, AppLocalization.Get(AppStrings.UpdatePlaylist));

            if (updateStatus)
            {
                view.SetStatus(AppLocalization.Get(AppStrings.ReadyToRun));
            }
        }

        private static string CreateCooldownMessage(ReleaseSettings settings)
        {
            DateTimeOffset cooldownUntil =
                settings.SpotifyCooldownUntilUtc ?? DateTimeOffset.UtcNow;

            return AppLocalization.Get(
                AppStrings.ControllerCooldownMessageFormat,
                FormatLocalTime(cooldownUntil));
        }

        private void ApplyClientIdStatus(ReleaseSettings settings)
        {
            (string message, ClientIdStatusKind kind) =
                CreateClientIdStatus(settings);

            view.SetClientIdStatus(message, kind);
        }

        private (string Message, ClientIdStatusKind Kind)
            CreateClientIdStatus(ReleaseSettings settings)
        {
            ReleaseSettings normalized = settings.Normalize();

            if (!normalized.HasSpotifyClientId)
            {
                return (
                    AppLocalization.Get(AppStrings.ControllerClientIdStatusMissing),
                    ClientIdStatusKind.Missing);
            }

            if (SpotifyRunPolicy.IsCooldownActive(
                normalized,
                DateTimeOffset.UtcNow,
                ignoreCooldown: false))
            {
                return (
                    CreateCooldownMessage(normalized),
                    ClientIdStatusKind.Cooldown);
            }

            if (IsLogicTestModeEnabled())
            {
                return (
                    AppLocalization.Get(AppStrings.ControllerClientIdStatusTestMode),
                    ClientIdStatusKind.TestMode);
            }

            return (
                AppLocalization.Get(AppStrings.ControllerClientIdStatusConfigured),
                ClientIdStatusKind.Configured);
        }

        private static string FormatLocalTime(DateTimeOffset value)
        {
            return value
                .ToLocalTime()
                .ToString("HH:mm", CultureInfo.CurrentCulture);
        }

        private static bool ShouldKeepSavedPlaylistId(
            ReleaseSettings uiSettings,
            ReleaseSettings savedSettings)
        {
            return string.IsNullOrWhiteSpace(uiSettings.PlaylistId) &&
                !string.IsNullOrWhiteSpace(savedSettings.PlaylistId) &&
                string.Equals(
                    uiSettings.PlaylistName,
                    savedSettings.PlaylistName,
                    StringComparison.Ordinal);
        }

        /// <summary>
        /// Treats teardown exceptions (e.g. from stopping the loopback listener) as a
        /// cancellation whenever the login token itself was cancelled, regardless of the
        /// OS locale of the underlying exception message.
        /// </summary>
        private static bool IsLoginCancellation(
            Exception exception,
            CancellationToken cancellationToken)
        {
            return exception is OperationCanceledException ||
                cancellationToken.IsCancellationRequested;
        }

        private static bool IsLogicTestModeEnabled()
        {
            return ReleaseDefaults.LogicTestModeEnabled;
        }
    }
}
