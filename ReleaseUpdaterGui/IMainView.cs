using ReleaseUpdater.Core.Models;

namespace ReleaseUpdaterGui
{
    /// <summary>
    /// What <see cref="MainFormController"/> needs from the main window - and nothing of WinForms, so
    /// the controller can be driven by a stand-in in a test. <see cref="MainForm"/> is the real one.
    /// </summary>
    public interface IMainView
    {
        /// <summary>True once the window is closing or gone; a run that is still ending must leave it alone.</summary>
        bool IsShuttingDown { get; }

        // ---- what the user entered ----
        string PlaylistName { get; }
        string CustomSpotifyClientId { get; }
        ReleaseSettings GetSettings();
        void SetSettings(ReleaseSettings settings);

        // ---- status and output ----
        void SetStatus(string status);
        void SetLoginStatus(string status);
        void SetClientIdStatus(string status, ClientIdStatusKind statusKind);
        void SetUpdateAvailability(bool isAvailable, string tooltip);
        void AppendOutput(string message);

        // ---- sign-in ----
        void SetAuthenticationBusy();
        void SetAuthenticationState(bool isLoggedIn);

        // ---- a run ----
        void PrepareRunUi();
        void ApplyProgress(ReleaseProgress progress);
        void ShowCompleted(PlaylistUpdateResult result);
        void ShowRunFailed(string message);
        void ShowCancelled();
        void StopElapsedTimer();

        // ---- editing the two settings boxes ----

        // A red frame at the box with the reason (it goes when the user types); null takes it away.
        void SetPlaylistNameError(string? message);
        void SetSpotifyClientIdError(string? message);

        // ---- messages and questions ----
        void ShowInfo(string message, string title);
        void ShowWarning(string message, string title);
        void ShowError(string message, string title);
        void ShowToast(string message);

        /// <summary>Asks "start the update now?"; true for yes.</summary>
        bool ConfirmStart();

        /// <summary>Starts the silent check for a newer version of the program (its prompt belongs to the window).</summary>
        void BeginUpdateCheck();
    }
}
