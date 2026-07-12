using CustomWFUI.Forms;
using SpotifyRelease.Core.Abstractions;
using SpotifyRelease.Core.Models;
using SpotifyRelease.Core.Services;
using System.Diagnostics;
using System.Globalization;

namespace SpotifyReleaseGui
{
    public class MainFormController
    {
        private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);

        private readonly MainForm form;
        private readonly ISettingsStore settingsStore;
        private readonly IReportWriter reportWriter;
        private readonly ISpotifyAuthService authService;
        private readonly SpotifyReleaseUpdater releaseUpdater;
        private readonly IReleaseClock releaseClock;

        private CancellationTokenSource? runCancellation;

        public MainFormController(
            MainForm form,
            ISettingsStore settingsStore,
            IReportWriter reportWriter,
            ISpotifyAuthService authService,
            SpotifyReleaseUpdater releaseUpdater,
            IReleaseClock releaseClock)
        {
            this.form = form;
            this.settingsStore = settingsStore;
            this.reportWriter = reportWriter;
            this.authService = authService;
            this.releaseUpdater = releaseUpdater;
            this.releaseClock = releaseClock;
        }

        public async Task LoadSettingsAsync()
        {
            ReleaseSettings settings = settingsStore.Load();
            form.SetSettings(settings);
            await RefreshLoginStatusAsync();
            form.SetStatus("Ready to run");
            ApplyRunAvailability(updateStatus: true);
        }

        public async Task LoginAsync()
        {
            if (!SaveSettings(showValidationMessage: true))
            {
                return;
            }

            try
            {
                using CancellationTokenSource loginCancellation =
                    new(LoginTimeout);

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
            catch (Exception ex) when (IsLoginCancellation(ex))
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

                ReleaseSettings settings = settingsStore.Load().Normalize();
                SpotifyRunPolicy.MarkSuccessfulSharedClientRun(
                    settings,
                    releaseClock.Today);
                settingsStore.Save(settings);
                form.SetSettings(settings);
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

            settings.SharedClientLastRunDate = savedSettings.SharedClientLastRunDate;
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
                    "Spotify Client ID must be 32 hexadecimal characters. Leave it empty to use the shared app.",
                    "Invalid Spotify Client ID");
            }

            return isValid;
        }

        private bool EnsureRunIsAllowed(bool showMessage)
        {
            ReleaseSettings settings = settingsStore.Load().Normalize();
            ApplySharedClientStatus(settings);
            DateTimeOffset now = DateTimeOffset.UtcNow;

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

            if (SpotifyRunPolicy.IsDailyLimitReached(settings, releaseClock.Today))
            {
                string message =
                    "The shared Spotify app can be used once per day. Add your own Spotify Client ID to run again today.";
                form.SetStatus("Shared app daily limit reached");
                form.SetUpdateAvailability(false, message);

                if (showMessage)
                {
                    form.ShowInfo(message, "Daily limit");
                }

                return false;
            }

            form.SetUpdateAvailability(true, "Update playlist");
            return true;
        }

        private void ApplyRunAvailability(bool updateStatus)
        {
            ReleaseSettings settings = settingsStore.Load().Normalize();
            ApplySharedClientStatus(settings);

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

            if (SpotifyRunPolicy.IsDailyLimitReached(settings, releaseClock.Today))
            {
                string message =
                    "The shared Spotify app can be used once per day. Add your own Spotify Client ID to run again today.";
                form.SetUpdateAvailability(false, message);

                if (updateStatus)
                {
                    form.SetStatus("Shared app daily limit reached");
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

        private void ApplySharedClientStatus(ReleaseSettings settings)
        {
            (string message, SharedClientStatusKind kind) =
                CreateSharedClientStatus(settings);

            form.SetSharedClientStatus(message, kind);
        }

        private (string Message, SharedClientStatusKind Kind)
            CreateSharedClientStatus(ReleaseSettings settings)
        {
            ReleaseSettings normalized = settings.Normalize();

            if (IsLogicTestModeEnabled())
            {
                return (
                    "Test mode: standard Client ID is used. Daily limit is ignored while testing.",
                    SharedClientStatusKind.TestMode);
            }

            if (SpotifyRunPolicy.IsCooldownActive(
                normalized,
                DateTimeOffset.UtcNow,
                ignoreCooldown: false))
            {
                return (
                    CreateCooldownMessage(normalized),
                    SharedClientStatusKind.Cooldown);
            }

            if (!normalized.UsesSharedSpotifyClientId)
            {
                return (
                    "Custom Client ID active. The standard daily limit does not apply.",
                    SharedClientStatusKind.Custom);
            }

            if (SpotifyRunPolicy.IsDailyLimitReached(
                normalized,
                releaseClock.Today,
                ignoreSharedLimit: false))
            {
                DateOnly nextRunDate = releaseClock.Today.AddDays(1);
                return (
                    "Standard Client ID used today. Next shared run: " +
                    $"{FormatDate(nextRunDate)}.",
                    SharedClientStatusKind.Blocked);
            }

            return (
                "Standard Client ID available today. A custom Client ID removes the daily limit.",
                SharedClientStatusKind.Available);
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

        private static bool IsLoginCancellation(Exception exception)
        {
            return exception is OperationCanceledException ||
                exception.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("abgebrochen", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLogicTestModeEnabled()
        {
            return ReleaseDefaults.LogicTestModeEnabled;
        }
    }
}
