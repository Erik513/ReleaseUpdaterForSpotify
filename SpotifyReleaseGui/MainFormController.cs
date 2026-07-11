using CustomWFUI.Forms;
using SpotifyRelease.Core.Abstractions;
using SpotifyRelease.Core.Models;
using SpotifyRelease.Core.Services;
using System.Diagnostics;

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

        private CancellationTokenSource? runCancellation;

        public MainFormController(
            MainForm form,
            ISettingsStore settingsStore,
            IReportWriter reportWriter,
            ISpotifyAuthService authService,
            SpotifyReleaseUpdater releaseUpdater)
        {
            this.form = form;
            this.settingsStore = settingsStore;
            this.reportWriter = reportWriter;
            this.authService = authService;
            this.releaseUpdater = releaseUpdater;
        }

        public async Task LoadSettingsAsync()
        {
            ReleaseSettings settings = settingsStore.Load();
            form.SetSettings(settings);
            await RefreshLoginStatusAsync();
            form.SetStatus("Ready to run");
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

                form.SetSettings(settingsStore.Load());
                form.ShowCompleted(result);
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
            }
            catch (Exception ex)
            {
                form.ShowError(
                    $"Could not open the report: {ex.Message}",
                    "Report");
            }
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
            SaveSettings(showValidationMessage: false);
        }

        public void SaveSettingsFromUi()
        {
            SaveSettings(showValidationMessage: false);
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

            ReleaseSettings settings = form.GetSettings();
            ReleaseSettings savedSettings = settingsStore.Load();

            if (ShouldKeepSavedPlaylistId(settings, savedSettings))
            {
                settings.PlaylistId = savedSettings.PlaylistId;
                form.SetSettings(settings);
            }

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
    }
}
