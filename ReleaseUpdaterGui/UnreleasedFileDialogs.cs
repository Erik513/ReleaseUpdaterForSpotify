using ErikwnkCore;
using ReleaseUpdater.Core.Localization;

namespace ReleaseUpdaterGui
{
    /// <summary>The file dialogs of the list of unreleased songs: which file to import, where to export to.</summary>
    internal static class UnreleasedFileDialogs
    {
        /// <summary>The file the user picks to import songs from; null when cancelled.</summary>
        public static string? AskImportPath(IWin32Window owner)
        {
            using OpenFileDialog dialog = new()
            {
                Title = AppLocalization.Get(AppStrings.UnreleasedImport),
                Filter = GetFileFilter(includeCombined: true),
                CheckFileExists = true
            };

            return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
        }

        /// <summary>The file the user picks to export the songs to (Word is the default); null when cancelled.</summary>
        public static string? AskExportPath(IWin32Window owner)
        {
            using SaveFileDialog dialog = new()
            {
                Title = AppLocalization.Get(AppStrings.UnreleasedExport),
                Filter = GetFileFilter(includeCombined: false),
                DefaultExt = "docx",
                AddExtension = true,
                FileName = "unreleased-songs",
                OverwritePrompt = true
            };

            return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
        }

        // Word first (the default of a save), then Excel, CSV and JSON; an open dialog can also show all of them at once.
        private static string GetFileFilter(bool includeCombined)
        {
            string songs = AppLocalization.Get(AppStrings.UnreleasedFileFilterSongs);
            string filter = "Word (*.docx)|*.docx|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|JSON (*.json)|*.json";

            return includeCombined
                ? songs + " (*.docx;*.xlsx;*.csv;*.json)|*.docx;*.xlsx;*.csv;*.json|" + filter
                : filter;
        }
    }
}
