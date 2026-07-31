using CustomWFUI.Forms;
using ReleaseUpdater.Core.Abstractions;
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
        private readonly GitHubUpdateChecker updateChecker;

        private CancellationTokenSource? runCancellation;

        public MainFormController(
            MainForm form,
            ISettingsStore settingsStore,
            IReportWriter reportWriter,
            ISpotifyAuthService authService,
            SpotifyReleaseUpdater releaseUpdater,
            GitHubUpdateChecker updateChecker)
        {
            this.form = form;
            this.settingsStore = settingsStore;
            this.reportWriter = reportWriter;
            this.authService = authService;
            this.releaseUpdater = releaseUpdater;
            this.updateChecker = updateChecker;
        }

        public async Task LoadSettingsAsync()
        {
            ReleaseSettings settings = settingsStore.Load();
            form.SetSettings(settings);
            await RefreshLoginStatusAsync();
            form.SetStatus("Ready to run");
            ApplyRunAvailability(updateStatus: true);

            _ = CheckForUpdateAsync();
        }

        /// <summary>
        /// Best-effort, non-blocking check against GitHub's latest release. Never
        /// surfaces errors to the user; a failed or slow check just means no prompt.
        /// </summary>
        private async Task CheckForUpdateAsync()
        {
            try
            {
                using CancellationTokenSource timeout = new(UpdateCheckTimeout);

                Version currentVersion =
                    Assembly.GetExecutingAssembly().GetName().Version
                        ?? new Version(0, 0, 0);

                UpdateCheckResult? result = await updateChecker.CheckForUpdateAsync(
                    currentVersion,
                    timeout.Token);

                if (result is null)
                {
                    return;
                }

                string displayedCurrentVersion =
                    $"{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}";

                if (UpdatePrompt.ShowUpdateAvailable(
                    displayedCurrentVersion,
                    result.LatestVersion.ToString(),
                    form))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = result.ReleaseUrl,
                        UseShellExecute = true
                    });
                }
            }
            catch
            {
            }
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
                form.SetStatus("Waiting for Spotify login...");
                form.SetLoginStatus("Browser login in progress...");

                SpotifyUser user = await authService.LoginAsync(
                    loginCancellation.Token);

                form.SetLoginStatus($"Signed in: {user.DisplayName}");
                form.SetAuthenticationState(isLoggedIn: true);
                form.SetStatus("Signed in");

                ToastForm.ShowToast("Spotify login successful.", form);
            }
            catch (Exception ex) when (IsLoginCancellation(ex, loginCancellation.Token))
            {
                await RefreshLoginStatusAsync();
                form.SetStatus("Login cancelled or timed out");
                ToastForm.ShowToast("Spotify login cancelled or timed out.", form);
            }
            catch (Exception ex)
            {
                await RefreshLoginStatusAsync();
                form.SetStatus("Login failed");
                form.ShowError(ex.Message, "Spotify Login");
            }
        }

        public async Task LogoutAsync()
        {
            try
            {
                form.SetAuthenticationBusy();
                await authService.LogoutAsync(CancellationToken.None);
                form.SetLoginStatus("Not signed in");
                form.SetAuthenticationState(isLoggedIn: false);
                form.SetStatus("Signed out");
                ToastForm.ShowToast("Signed out from Spotify.", form);
            }
            catch (Exception ex)
            {
                await RefreshLoginStatusAsync();
                form.SetStatus("Logout failed");
                form.ShowError(ex.Message, "Spotify Logout");
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

                form.ShowCompleted(result);
            }
            catch (SpotifyRateLimitException ex)
            {
                if (IsLogicTestModeEnabled())
                {
                    form.ShowRunFailed(
                        "Spotify request limit reached. Test mode ignores saved cooldowns, so you can try again later without changing settings.");
                    return;
                }

                ReleaseSettings settings = settingsStore.Load().Normalize();
                SpotifyRunPolicy.MarkRateLimitCooldown(
                    settings,
                    DateTimeOffset.UtcNow,
                    ex.RetryAfter);
                settingsStore.Save(settings);
                form.SetSettings(settings);

                string message = CreateCooldownMessage(settings);
                form.ShowRunFailed(message);
            }
            catch (OperationCanceledException)
            {
                form.ShowCancelled();
            }
            catch (Exception ex)
            {
                form.ShowRunFailed(ex.Message);
            }
            finally
            {
                runCancellation?.Dispose();
                runCancellation = null;
                await RefreshLoginStatusAsync();
                ApplyRunAvailability(updateStatus: false);
            }
        }

        public bool IsRunInProgress => runCancellation is not null;

        public void CancelUpdate()
        {
            if (runCancellation is null)
            {
                return;
            }

            runCancellation?.Cancel();
            form.SetStatus("Cancelling...");
            ToastForm.ShowToast("Cancelling update...", form);
        }

        /// <summary>
        /// Asks the user before the app closes mid-run, since the playlist may be left
        /// only partially updated if the run is interrupted now.
        /// </summary>
        public bool ConfirmCloseWhileRunning()
        {
            DialogResult result = CustomMessageBox.Show(
                "An update is still running. The playlist may end up only partially " +
                "updated if you close now.\n\nClose anyway?",
                "Update in progress",
                CustomMessageBoxButtons.YesNo,
                CustomMessageBoxIcon.Warning,
                form,
                CustomMessageBoxSize.Small);

            return result == DialogResult.Yes;
        }

        public void OpenReport()
        {
            if (!File.Exists(reportWriter.ReportPath))
            {
                form.ShowInfo(
                    "No report is available yet.",
                    "Report");

                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = reportWriter.ReportPath,
                    UseShellExecute = true
                });
                ToastForm.ShowToast("Opening HTML report.", form);
            }
            catch (Exception ex)
            {
                form.ShowError(
                    $"Could not open the report: {ex.Message}",
                    "Report");
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
                ToastForm.ShowToast("Playlist name saved.", form);
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
                ToastForm.ShowToast("Spotify Client ID saved.", form);
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

        private async Task RefreshLoginStatusAsync()
        {
            SpotifyUser? user = await authService.TryGetCurrentUserAsync(
                CancellationToken.None);

            form.SetLoginStatus(
                user is null
                    ? "Not signed in"
                    : $"Signed in: {user.DisplayName}");
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

            settingsStore.Save(settings);
            form.SetStatus("Settings saved");

            return true;
        }

        private bool ValidatePlaylistName(bool showMessage)
        {
            bool isValid = PlaylistValidator.IsValidPlaylistName(
                form.PlaylistName,
                out string errorMessage);

            if (!isValid && showMessage)
            {
                form.ShowWarning(errorMessage, "Invalid playlist name");
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
                    "Spotify Client ID must be 32 hexadecimal characters. Do not enter a Client Secret.",
                    "Invalid Spotify Client ID");
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

            string message =
                "Please enter and save your Spotify Client ID before signing in or updating the playlist.";
            form.SetStatus("Spotify Client ID required");
            form.SetUpdateAvailability(false, message);
            ApplyClientIdStatus(settings);

            if (showMessage)
            {
                form.ShowInfo(message, "Spotify Client ID required");
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
                string message =
                    "Please enter and save your Spotify Client ID before updating the playlist.";
                form.SetStatus("Spotify Client ID required");
                form.SetUpdateAvailability(false, message);

                if (showMessage)
                {
                    form.ShowInfo(message, "Spotify Client ID required");
                }

                return false;
            }

            if (SpotifyRunPolicy.IsCooldownActive(settings, now))
            {
                string message = CreateCooldownMessage(settings);
                form.SetStatus("Spotify cooldown active");
                form.SetUpdateAvailability(false, message);

                if (showMessage)
                {
                    form.ShowInfo(message, "Spotify cooldown");
                }

                return false;
            }

            form.SetUpdateAvailability(true, "Update playlist");
            return true;
        }

        private void ApplyRunAvailability(bool updateStatus)
        {
            ReleaseSettings settings = settingsStore.Load().Normalize();
            ApplyClientIdStatus(settings);

            if (!settings.HasSpotifyClientId)
            {
                string message =
                    "Enter and save your Spotify Client ID before updating the playlist.";
                form.SetUpdateAvailability(false, message);

                if (updateStatus)
                {
                    form.SetStatus("Spotify Client ID required");
                }

                return;
            }

            if (SpotifyRunPolicy.IsCooldownActive(settings, DateTimeOffset.UtcNow))
            {
                string message = CreateCooldownMessage(settings);
                form.SetUpdateAvailability(false, message);

                if (updateStatus)
                {
                    form.SetStatus("Spotify cooldown active");
                }

                return;
            }

            form.SetUpdateAvailability(true, "Update playlist");

            if (updateStatus)
            {
                form.SetStatus("Ready to run");
            }
        }

        private static string CreateCooldownMessage(ReleaseSettings settings)
        {
            DateTimeOffset cooldownUntil =
                settings.SpotifyCooldownUntilUtc ?? DateTimeOffset.UtcNow;

            return "Spotify request limit reached. Updates are paused until " +
                $"{FormatLocalTime(cooldownUntil)}.";
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
                    "Spotify Client ID required. Click for setup help.",
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
                    "Test mode active. Your configured Client ID will be used with limited requests.",
                    ClientIdStatusKind.TestMode);
            }

            return (
                "Spotify Client ID configured. Click for setup help.",
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
            DialogResult result = CustomMessageBox.Show(
                "The playlist will be replaced with the found songs.\n\nContinue?",
                "Update playlist",
                CustomMessageBoxButtons.YesNo,
                CustomMessageBoxIcon.Question,
                form,
                CustomMessageBoxSize.Small);

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
