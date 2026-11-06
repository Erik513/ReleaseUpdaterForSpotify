using ErikwnkCore;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Localization;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Core.Services;
using ReleaseUpdater.Data.Storage;

namespace ReleaseUpdaterGui
{
    public sealed class UnreleasedSongsController
    {
        private readonly IUnreleasedSongsView view;
        private readonly TrackedSongsStore songsStore;
        private readonly UnreleasedSongChecker checker;
        private readonly IUnreleasedSongsReportWriter reportWriter;

        private CancellationTokenSource? runCancellation;

        public UnreleasedSongsController(
            IUnreleasedSongsView view,
            TrackedSongsStore songsStore,
            UnreleasedSongChecker checker,
            IUnreleasedSongsReportWriter reportWriter)
        {
            this.view = view;
            this.songsStore = songsStore;
            this.checker = checker;
            this.reportWriter = reportWriter;
        }

        public bool IsRunInProgress => runCancellation is not null;

        public IReadOnlyList<TrackedSong> LoadSongs() => songsStore.Load();

        public void SaveSongs(IReadOnlyList<TrackedSong> songs) => songsStore.Save(songs);

        /// <summary>
        /// Writes the songs to <paramref name="path"/> in the format of its ending (.xlsx, .docx, .csv, else JSON). Tells the user what
        /// happened; false when nothing was written.
        /// </summary>
        public bool ExportSongs(IReadOnlyList<TrackedSong> songs, string path)
        {
            string title = AppLocalization.Get(AppStrings.UnreleasedFormTitle);
            List<TrackedSong> toExport = songs
                .Where(song => !string.IsNullOrWhiteSpace(song.Title) || !string.IsNullOrWhiteSpace(song.Artist))
                .Select(song => new TrackedSong(song.Title.Trim(), song.Artist.Trim()))
                .ToList();

            if (toExport.Count == 0)
            {
                view.ShowInfo(AppLocalization.Get(AppStrings.UnreleasedNothingToExport), title);

                return false;
            }

            try
            {
                File.WriteAllBytes(path, TrackedSongsTransfer.Write(toExport, TrackedSongsTransfer.FormatForPath(path)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                view.ShowError(AppLocalization.Get(AppStrings.UnreleasedExportFailedFormat, ex.Message), title);

                return false;
            }

            view.SetStatus(AppLocalization.Get(AppStrings.UnreleasedExportDoneFormat, toExport.Count));

            return true;
        }

        /// <summary>
        /// Reads songs from <paramref name="path"/> and returns <paramref name="current"/> with the new ones behind it (the songs that are in
        /// it already are not added again). Null, after telling the user why, when the file could not be used; the list is not touched then.
        /// </summary>
        public IReadOnlyList<TrackedSong>? ImportSongs(string path, IReadOnlyList<TrackedSong> current)
        {
            string title = AppLocalization.Get(AppStrings.UnreleasedFormTitle);
            TrackedSongsFormat format = TrackedSongsTransfer.FormatForPath(path);
            TrackedSongsImport? import;

            try
            {
                byte[] content = File.ReadAllBytes(path);

                // A table (CSV, Excel, Word) may have more columns than the two that are needed: the user chooses them.
                // JSON has its own names for them.
                import = TrackedSongsTransfer.IsTable(format)
                    ? ReadChosenColumns(TrackedSongsTransfer.ReadTable(content, format))
                    : TrackedSongsTransfer.Read(content, format);
            }
            catch (FormatException)
            {
                view.ShowError(AppLocalization.Get(AppStrings.UnreleasedImportInvalidFile), title);

                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                view.ShowError(AppLocalization.Get(AppStrings.UnreleasedImportFailedFormat, ex.Message), title);

                return null;
            }

            return Merge(import, current, AppStrings.UnreleasedImportNothingFound);
        }

        /// <summary>
        /// Reads songs from what is on the clipboard (a table copied from Excel or Word: tabs between the cells, a line for a row) and
        /// returns <paramref name="current"/> with the new ones behind it. Null, after telling the user why, when there is nothing to add or
        /// the user cancels the choice of the columns.
        /// </summary>
        public IReadOnlyList<TrackedSong>? PasteSongs(string? clipboardText, IReadOnlyList<TrackedSong> current)
        {
            if (string.IsNullOrWhiteSpace(clipboardText))
            {
                view.ShowInfo(
                    AppLocalization.Get(AppStrings.UnreleasedPasteNothing),
                    AppLocalization.Get(AppStrings.UnreleasedFormTitle));

                return null;
            }

            return Merge(
                ReadChosenColumns(TrackedSongsTransfer.ParseClipboardText(clipboardText)),
                current,
                AppStrings.UnreleasedPasteNoSongs);
        }

        // The songs of a table: its two columns when it has no more than two, else the ones the user chooses (null: cancelled).
        private TrackedSongsImport? ReadChosenColumns(ImportTable table)
        {
            if (table.IsEmpty)
            {
                return new TrackedSongsImport(Array.Empty<TrackedSong>(), 0);
            }

            ImportColumnChoice choice;

            if (table.ColumnCount <= 2)
            {
                choice = table.GuessColumns();
            }
            else
            {
                ImportColumnChoice? chosen = view.ChooseImportColumns(table);

                if (chosen is null)
                {
                    return null;
                }

                choice = chosen;
            }

            return TrackedSongsTransfer.SongsFromTable(table, choice);
        }

        // Adds the songs that are new to the list and says what happened; null when there was nothing to add.
        private IReadOnlyList<TrackedSong>? Merge(TrackedSongsImport? import, IReadOnlyList<TrackedSong> current, string noSongsMessage)
        {
            if (import is null)
            {
                return null;
            }

            if (import.Songs.Count == 0)
            {
                view.ShowInfo(AppLocalization.Get(noSongsMessage), AppLocalization.Get(AppStrings.UnreleasedFormTitle));

                return null;
            }

            TrackedSongsMerge merge = TrackedSongsTransfer.Merge(current, import.Songs);
            view.SetStatus(AppLocalization.Get(
                AppStrings.UnreleasedImportDoneFormat,
                merge.AddedCount,
                merge.DuplicateCount,
                import.SkippedCount));

            return merge.Songs;
        }

        public async Task StartSearchAsync(IReadOnlyList<TrackedSong> songs)
        {
            List<TrackedSong> validSongs = songs
                .Where(song =>
                    !string.IsNullOrWhiteSpace(song.Title) &&
                    !string.IsNullOrWhiteSpace(song.Artist))
                .ToList();

            if (validSongs.Count == 0)
            {
                view.ShowInfo(
                    AppLocalization.Get(AppStrings.UnreleasedNoSongsMessage),
                    AppLocalization.Get(AppStrings.UnreleasedFormTitle));

                return;
            }

            view.PrepareRunUi();
            runCancellation = new CancellationTokenSource();

            try
            {
                Progress<ReleaseProgress> progress = new(view.ApplyProgress);

                UnreleasedSongsCheckResult result = await checker.RunAsync(
                    validSongs,
                    progress,
                    runCancellation.Token);

                if (!IsShuttingDown)
                {
                    view.ShowCompleted(result);
                }
            }
            catch (OperationCanceledException)
            {
                if (!IsShuttingDown)
                {
                    view.ShowCancelled();
                }
            }
            catch (SpotifyRateLimitException ex)
            {
                // The main window keeps a cooldown for this; here the user only needs to know to wait.
                if (!IsShuttingDown)
                {
                    int minutes = Math.Max(1, (int)Math.Ceiling(ex.RetryAfter.TotalMinutes));
                    view.ShowRunFailed(AppLocalization.Get(AppStrings.UnreleasedRateLimitedFormat, minutes));
                }
            }
            catch (Exception ex)
            {
                // (A message box owned by a window that is already closed would throw.)
                if (!IsShuttingDown)
                {
                    view.ShowRunFailed(ex.Message);
                }
            }
            finally
            {
                runCancellation?.Dispose();
                runCancellation = null;
            }
        }

        private bool IsShuttingDown => view.IsShuttingDown;

        public void CancelSearch()
        {
            runCancellation?.Cancel();
        }

        public void OpenReport()
        {
            string title = AppLocalization.Get(AppStrings.UnreleasedFormTitle);

            switch (ReportLauncher.Open(reportWriter.ReportPath, out string? error))
            {
                case ReportOpenResult.Missing:
                    view.ShowInfo(
                        AppLocalization.Get(AppStrings.ControllerNoReportAvailable),
                        title);
                    break;

                case ReportOpenResult.Failed:
                    view.ShowError(
                        AppLocalization.Get(AppStrings.ControllerCouldNotOpenReportFormat, error ?? string.Empty),
                        title);
                    break;
            }
        }
    }
}
