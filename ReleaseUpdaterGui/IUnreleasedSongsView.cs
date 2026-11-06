using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Core.Services;

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

        /// <summary>
        /// Asks the user which columns of an imported table are the title and the artist; null when the user cancels. (Only asked when the
        /// table has more than two columns.)
        /// </summary>
        ImportColumnChoice? ChooseImportColumns(ImportTable table);

        /// <summary>
        /// Puts the Artist column before or behind the Title column, as the imported table had them (and keeps that order for the next
        /// time, like an order the user made by dragging a header).
        /// </summary>
        void ApplyColumnOrder(bool artistFirst);
        void PrepareRunUi();
        void ApplyProgress(ReleaseProgress progress);
        void ShowCompleted(UnreleasedSongsCheckResult result);
        void ShowCancelled();
        void ShowRunFailed(string message);
        void ShowInfo(string message, string title);
        void ShowError(string message, string title);
    }
}
