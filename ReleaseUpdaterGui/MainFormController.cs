using ErikwnkCore;
using ErikwnkWFUI.Forms;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Localization;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Core.Services;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace ReleaseUpdaterGui
{
    public class MainFormController
    {
        private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan UpdateCheckTimeout = TimeSpan.FromSeconds(8);

        private readonly MainForm form;
        private readonly ISettingsStore settingsStore;
        private readonly IReportWriter reportWriter;
        private readonly ISpotifyAuthService authService;
        private readonly SpotifyReleaseUpdater releaseUpdater;
        private readonly AppUpdater appUpdater;

        private CancellationTokenSource? runCancellation;

        public MainFormController(
            MainForm form,
            ISettingsStore settingsStore,
            IReportWriter reportWriter,
            ISpotifyAuthService authService,
            SpotifyReleaseUpdater releaseUpdater,
            AppUpdater appUpdater)
        {
            this.form = form;
            this.settingsStore = settingsStore;
            this.reportWriter = reportWriter;
            this.authService = authService;
            this.releaseUpdater = releaseUpdater;
            this.appUpdater = appUpdater;
        }

        public async Task LoadSettingsAsync()
        {
            ReleaseSettings settings = settingsStore.Load();
            form.SetSettings(settings);
            await RefreshLoginStatusAsync();
            form.SetStatus(AppLocalization.Get(AppStrings.ReadyToRun));
            ApplyRunAvailability(updateStatus: true);

            Version currentVersion =
                Assembly.GetExecutingAssembly().GetName().Version
                    ?? new Version(0, 0, 0);

            _ = appUpdater.CheckForUpdateAsync(currentVersion, UpdateCheckTimeout, form);
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

            using CancellationTokenSource loginCancellation = new(LoginTimeout);

            try
            {
                form.SetAuthenticationBusy();
                form.SetStatus(AppLocalization.Get(AppStrings.ControllerWaitingForLogin));
                form.SetLoginStatus(AppLocalization.Get(AppStrings.ControllerBrowserLoginInProgress));

                SpotifyUser user = await authService.LoginAsync(
                    loginCancellation.Token);

                form.SetLoginStatus(AppLocalization.Get(AppStrings.ControllerSignedInFormat, user.DisplayName));
                form.SetAuthenticationState(isLoggedIn: true);
                form.SetStatus(AppLocalization.Get(AppStrings.ControllerSignedIn));

                ToastForm.ShowToast(AppLocalization.Get(AppStrings.ControllerToastLoginSuccess), form);
            }
            catch (Exception ex) when (IsLoginCancellation(ex, loginCancellation.Token))
            {
                await RefreshLoginStatusAsync();
                form.SetStatus(AppLocalization.Get(AppStrings.ControllerLoginCancelledStatus));
                ToastForm.ShowToast(AppLocalization.Get(AppStrings.ControllerToastLoginCancelled), form);
            }
            catch (Exception ex)
            {
                await RefreshLoginStatusAsync();
                form.SetStatus(AppLocalization.Get(AppStrings.ControllerLoginFailed));
                form.ShowError(ex.Message, AppLocalization.Get(AppStrings.ControllerSpotifyLoginTitle));
            }
        }

        public async Task LogoutAsync()
        {
            try
            {
                form.SetAuthenticationBusy();
                await authService.LogoutAsync(CancellationToken.None);
                form.SetLoginStatus(AppLocalization.Get(AppStrings.NotSignedIn));
                form.SetAuthenticationState(isLoggedIn: false);
                form.SetStatus(AppLocalization.Get(AppStrings.ControllerSignedOut));
                ToastForm.ShowToast(AppLocalization.Get(AppStrings.ControllerToastSignedOut), form);
            }
            catch (Exception ex)
            {
                await RefreshLoginStatusAsync();
                form.SetStatus(AppLocalization.Get(AppStrings.ControllerLogoutFailed));
                form.ShowError(ex.Message, AppLocalization.Get(AppStrings.ControllerSpotifyLogoutTitle));
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

            if (!ConfirmStart())
            {
                return;
            }

            form.PrepareRunUi();
            runCancellation = new CancellationTokenSource();

            try
            {
                Progress<ReleaseProgress> progress = new(form.ApplyProgress);

                PlaylistUpdateResult result = await releaseUpdater.RunAsync(
                    progress,
                    runCancellation.Token);

                if (!IsShuttingDown)
                {
                    form.ShowCompleted(result);
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
                    form.ShowRunFailed(
                        AppLocalization.Get(AppStrings.ControllerTestModeRateLimit));
                    return;
                }

                ReleaseSettings settings = settingsStore.Load().Normalize();
                SpotifyRunPolicy.MarkRateLimitCooldown(
                    settings,
                    DateTimeOffset.UtcNow,
                    ex.RetryAfter);

                try
                {
                    settingsStore.Save(settings);
                }
                catch (Exception saveException) when (IsFileAccessException(saveException))
                {
                    // The cooldown is still shown below; it just will not survive a restart.
                    form.AppendOutput(AppLocalization.Get(
                        AppStrings.ControllerSettingsSaveFailedFormat,
                        saveException.Message));
                }

                form.SetSettings(settings);

                string message = CreateCooldownMessage(settings);
                form.ShowRunFailed(message);
            }
            catch (OperationCanceledException)
            {
                if (!IsShuttingDown)
                {
                    form.ShowCancelled();
                }
            }
            catch (Exception ex)
            {
                if (!IsShuttingDown)
                {
                    form.ShowRunFailed(ex.Message);
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
                    form.StopElapsedTimer();
                    await RefreshLoginStatusAsync();
                    ApplyRunAvailability(updateStatus: false);
                }
            }
        }

        private bool IsShuttingDown => form.IsClosing || form.IsDisposed;

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

            form.SetStatus(AppLocalization.Get(AppStrings.ControllerCancelling));
            ToastForm.ShowToast(AppLocalization.Get(AppStrings.ControllerToastCancelling), form);
        }

        /// <summary>
        /// Asks the user before the app closes mid-run, since the playlist may be left
        /// only partially updated if the run is interrupted now.
        /// </summary>
        public bool ConfirmCloseWhileRunning()
        {
            DialogResult result = MessageBox.Show(
                AppLocalization.Get(AppStrings.ControllerConfirmCloseMessage),
                AppLocalization.Get(AppStrings.ControllerConfirmCloseTitle),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                form,
                FormSize.Small);

            return result == DialogResult.Yes;
        }

        public void OpenReport()
        {
            string reportTitle = AppLocalization.Get(AppStrings.Report);

            if (!File.Exists(reportWriter.ReportPath))
            {
                form.ShowInfo(
                    AppLocalization.Get(AppStrings.ControllerNoReportAvailable),
                    reportTitle);

                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = reportWriter.ReportPath,
                    UseShellExecute = true
                });
                ToastForm.ShowToast(AppLocalization.Get(AppStrings.ControllerToastOpeningReport), form);
            }
            catch (Exception ex)
            {
                form.ShowError(
                    AppLocalization.Get(AppStrings.ControllerCouldNotOpenReportFormat, ex.Message),
                    reportTitle);
            }
        }

        public void OpenSpotifyClientIdHelp()
        {
            using SpotifyClientIdHelpForm helpForm = new();
            helpForm.ShowDialog(form);
        }

        public void TogglePlaylistNameEdit()
        {
            if (form.IsPlaylistNameReadOnly)
            {
                form.EnablePlaylistNameEditing();
                return;
            }

            if (!ValidatePlaylistName(showMessage: true))
            {
                form.FocusPlaylistName();
                return;
            }

            form.DisablePlaylistNameEditing();
            if (SaveSettings(showValidationMessage: false))
            {
                ApplyRunAvailability(updateStatus: false);
                ToastForm.ShowToast(AppLocalization.Get(AppStrings.ControllerToastPlaylistNameSaved), form);
            }
        }

        public void ToggleSpotifyClientIdEdit()
        {
            if (form.IsSpotifyClientIdReadOnly)
            {
                form.EnableSpotifyClientIdEditing();
                return;
            }

            if (!ValidateSpotifyClientId(showMessage: true))
            {
                form.FocusSpotifyClientId();
                return;
            }

            form.DisableSpotifyClientIdEditing();
            if (SaveSettings(showValidationMessage: false))
            {
                ApplyRunAvailability(updateStatus: false);
                ToastForm.ShowToast(AppLocalization.Get(AppStrings.ControllerToastClientIdSaved), form);
            }
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
            SpotifyUser? user = await authService.TryGetCurrentUserAsync(
                CancellationToken.None);

            form.SetLoginStatus(
                user is null
                    ? AppLocalization.Get(AppStrings.NotSignedIn)
                    : AppLocalization.Get(AppStrings.ControllerSignedInFormat, user.DisplayName));
            form.SetAuthenticationState(user is not null);
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

            ReleaseSettings settings = form.GetSettings();
            ReleaseSettings savedSettings = settingsStore.Load();

            if (ShouldKeepSavedPlaylistId(settings, savedSettings))
            {
                settings.PlaylistId = savedSettings.PlaylistId;
                form.SetSettings(settings);
            }

            settings.SpotifyCooldownUntilUtc = savedSettings.SpotifyCooldownUntilUtc;
            settings.SpotifyCooldownClientId = savedSettings.SpotifyCooldownClientId;

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
                form.SetStatus(message);

                if (showValidationMessage)
                {
                    form.ShowError(message, AppLocalization.Get(AppStrings.MainFormError));
                }

                return false;
            }

            form.SetStatus(AppLocalization.Get(AppStrings.ControllerSettingsSaved));

            return true;
        }

        private bool ValidatePlaylistName(bool showMessage)
        {
            bool isValid = PlaylistValidator.IsValidPlaylistName(
                form.PlaylistName,
                out string errorMessage);

            if (!isValid && showMessage)
            {
                form.ShowWarning(errorMessage, AppLocalization.Get(AppStrings.ControllerInvalidPlaylistNameTitle));
            }

            return isValid;
        }

        private bool ValidateSpotifyClientId(bool showMessage)
        {
            string clientId = form.CustomSpotifyClientId;

            if (string.IsNullOrWhiteSpace(clientId))
            {
                return true;
            }

            bool isValid = clientId.Length == 32 &&
                clientId.All(Uri.IsHexDigit);

            if (!isValid && showMessage)
            {
                form.ShowWarning(
                    AppLocalization.Get(AppStrings.ControllerInvalidClientIdMessage),
                    AppLocalization.Get(AppStrings.ControllerInvalidClientIdTitle));
            }

            return isValid;
        }

        private bool EnsureSpotifyClientIdConfigured(bool showMessage)
        {
            ReleaseSettings settings = form.GetSettings();

            if (settings.HasSpotifyClientId)
            {
                return true;
            }

            string message = AppLocalization.Get(AppStrings.ControllerClientIdRequiredForLoginMessage);
            string title = AppLocalization.Get(AppStrings.SpotifyClientIdRequired);
            form.SetStatus(title);
            form.SetUpdateAvailability(false, message);
            ApplyClientIdStatus(settings);

            if (showMessage)
            {
                form.ShowInfo(message, title);
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
                form.SetStatus(title);
                form.SetUpdateAvailability(false, message);

                if (showMessage)
                {
                    form.ShowInfo(message, title);
                }

                return false;
            }

            if (SpotifyRunPolicy.IsCooldownActive(settings, now))
            {
                string message = CreateCooldownMessage(settings);
                form.SetStatus(AppLocalization.Get(AppStrings.ControllerCooldownActive));
                form.SetUpdateAvailability(false, message);

                if (showMessage)
                {
                    form.ShowInfo(message, AppLocalization.Get(AppStrings.ControllerCooldownTitle));
                }

                return false;
            }

            form.SetUpdateAvailability(true, AppLocalization.Get(AppStrings.UpdatePlaylist));
            return true;
        }

        private void ApplyRunAvailability(bool updateStatus)
        {
            ReleaseSettings settings = settingsStore.Load().Normalize();
            ApplyClientIdStatus(settings);

            if (!settings.HasSpotifyClientId)
            {
                string message = AppLocalization.Get(AppStrings.ControllerClientIdRequiredTooltip);
                form.SetUpdateAvailability(false, message);

                if (updateStatus)
                {
                    form.SetStatus(AppLocalization.Get(AppStrings.SpotifyClientIdRequired));
                }

                return;
            }

            if (SpotifyRunPolicy.IsCooldownActive(settings, DateTimeOffset.UtcNow))
            {
                string message = CreateCooldownMessage(settings);
                form.SetUpdateAvailability(false, message);

                if (updateStatus)
                {
                    form.SetStatus(AppLocalization.Get(AppStrings.ControllerCooldownActive));
                }

                return;
            }

            form.SetUpdateAvailability(true, AppLocalization.Get(AppStrings.UpdatePlaylist));

            if (updateStatus)
            {
                form.SetStatus(AppLocalization.Get(AppStrings.ReadyToRun));
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

            form.SetClientIdStatus(message, kind);
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

        private static string FormatDate(DateOnly value)
        {
            return value.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);
        }

        private static string FormatLocalTime(DateTimeOffset value)
        {
            return value
                .ToLocalTime()
                .ToString("HH:mm", CultureInfo.CurrentCulture);
        }

        private bool ConfirmStart()
        {
            DialogResult result = MessageBox.Show(
                AppLocalization.Get(AppStrings.ControllerConfirmStartMessage),
                AppLocalization.Get(AppStrings.UpdatePlaylist),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                form,
                FormSize.Medium);

            return result == DialogResult.Yes;
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
