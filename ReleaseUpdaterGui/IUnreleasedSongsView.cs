using ReleaseUpdater.Core.Models;

namespace ReleaseUpdaterGui
{
    /// <summary>
    /// What <see cref="UnreleasedSongsController"/> needs from the unreleased-songs window (see
    /// <see cref="IMainView"/> for why it is an interface).
    /// </summary>
    public interface IUnreleasedSongsView
    {
        /// <summary>True once the window is closing or gone; a search that is still ending must leave it alone.</summary>
        bool IsShuttingDown { get; }

        void SetStatus(string status);
        void PrepareRunUi();
        void ApplyProgress(ReleaseProgress progress);
        void ShowCompleted(UnreleasedSongsCheckResult result);
        void ShowCancelled();
        void ShowRunFailed(string message);
        void ShowInfo(string message, string title);
        void ShowError(string message, string title);
    }
}
