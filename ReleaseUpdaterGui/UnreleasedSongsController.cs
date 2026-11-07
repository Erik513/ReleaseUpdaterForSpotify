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
        private readonly ISettingsStore settingsStore;

        private CancellationTokenSource? runCancellation;

        // The order of the columns of the table that is being imported (artist first or not), when it says something: the user chose
        // the columns, or the header named them. Applied to the list once the songs are in it.
        private bool? orderOfImportedTable;

        public UnreleasedSongsController(
            IUnreleasedSongsView view,
            TrackedSongsStore songsStore,
            UnreleasedSongChecker checker,
            IUnreleasedSongsReportWriter reportWriter,
            ISettingsStore settingsStore)
        {
            this.view = view;
            this.songsStore = songsStore;
            this.checker = checker;
            this.reportWriter = reportWriter;
            this.settingsStore = settingsStore;
        }

        public bool IsRunInProgress => runCancellation is not null;

        public IReadOnlyList<TrackedSong> LoadSongs() => songsStore.Load();

        /// <summary>Whether the user put the Artist column before the Title column (kept in the settings file); false when unknown.</summary>
        public bool LoadArtistFirst()
        {
            try
            {
                return settingsStore.Load().UnreleasedArtistFirst;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Keeps the order of the columns. A preference is not worth a message when the file cannot be written: the order is still
        /// as the user made it for as long as the window is open.
        /// </summary>
        public void SaveArtistFirst(bool artistFirst)
        {
            try
            {
                ReleaseSettings settings = settingsStore.Load();

                if (settings.UnreleasedArtistFirst == artistFirst)
                {
                    return;
                }

                settings.UnreleasedArtistFirst = artistFirst;
                settingsStore.Save(settings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // (see above)
            }
        }

        public void SaveSongs(IReadOnlyList<TrackedSong> songs) => songsStore.Save(songs);

        /// <summary>
        /// Writes the songs to <paramref name="path"/> in the format of its ending (.xlsx, .docx, .csv, else JSON). Tells the user what
        /// happened; false when nothing was written.
        /// </summary>
        public bool ExportSongs(IReadOnlyList<TrackedSong> songs, string path)
        {
            string title = AppLocalization.Get(AppStrings.UnreleasedFormTitle);
            List<TrackedSong> toExport = songs
                .Where(song => song.HasContent)
                .Select(song => new TrackedSong(song.Title.Trim(), song.Artist.Trim(), TrackedSongLinks.Normalize(song.Link)))
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
            orderOfImportedTable = null;

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
        public IReadOnlyList<TrackedSong>? PasteSongs(string? clipboardText, IReadOnlyList<TrackedSong> current, string? clipboardHtml = null)
        {
            orderOfImportedTable = null;

            // A table from Word or Excel comes as HTML too, and only that knows the address behind a hyperlink (the text has the words).
            ImportTable? htmlTable = TrackedSongsTransfer.ParseClipboardHtml(clipboardHtml);

            if (htmlTable is null && string.IsNullOrWhiteSpace(clipboardText))
            {
                view.ShowInfo(
                    AppLocalization.Get(AppStrings.UnreleasedPasteNothing),
                    AppLocalization.Get(AppStrings.UnreleasedFormTitle));

                return null;
            }

            return Merge(
                ReadChosenColumns(htmlTable ?? TrackedSongsTransfer.ParseClipboardText(clipboardText!)),
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
            bool chosenByUser = false;

            // A table that is just what the app writes (title, artist and perhaps link, named so) needs no question.
            if (table.ColumnCount <= 2 || table.HasRecognizedHeader())
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
                chosenByUser = true;
            }

            // The list shows the columns in the order the table had them - when the table says so (a choice, or a header that names
            // them); a table of two columns without a header says nothing, and leaves the order the user has.
            if (chosenByUser || choice.HasHeader)
            {
                orderOfImportedTable = choice.ArtistColumn < choice.TitleColumn;
            }

            return TrackedSongsTransfer.SongsFromTable(table, choice);
        }

        // Puts the imported songs into the list and says what happened; null when there was nothing to add or the user cancelled.
        // An empty list (or only the blank row of the grid) simply becomes the imported songs, in the order of the file. With songs in it,
        // the user decides: add the new ones behind them, or replace the list by the imported songs, in the order of the file.
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

            List<TrackedSong> filled = current
                .Where(song => song.HasContent)
                .ToList();
            ImportMode mode = ImportMode.Replace;

            if (filled.Count > 0)
            {
                ImportMode? answer = view.ChooseImportMode(filled.Count, import.Songs.Count);

                if (answer is null)
                {
                    return null;
                }

                mode = answer.Value;
            }

            // (Replace: the imported songs alone, without a twin inside the file; Add: behind what is in the list.)
            TrackedSongsMerge merge = TrackedSongsTransfer.Merge(
                mode == ImportMode.Replace ? Array.Empty<TrackedSong>() : current,
                import.Songs);
            string status = AppLocalization.Get(
                mode == ImportMode.Replace && filled.Count > 0 ? AppStrings.UnreleasedImportReplacedFormat : AppStrings.UnreleasedImportDoneFormat,
                merge.AddedCount,
                merge.DuplicateCount,
                import.SkippedCount);

            // A link that is no web address is not taken over: the user is told, so that nothing seems lost without a word.
            if (import.SkippedLinkCount > 0)
            {
                status += " " + AppLocalization.Get(AppStrings.UnreleasedImportLinksSkippedFormat, import.SkippedLinkCount);
            }

            view.SetStatus(status);

            if (orderOfImportedTable is bool artistFirst)
            {
                view.ApplyColumnOrder(artistFirst);
            }

            return merge.Songs;
        }

        public async Task StartSearchAsync(IReadOnlyList<TrackedSong> songs)
        {
            // (One run at a time.)
            if (IsRunInProgress)
            {
                return;
            }

            List<TrackedSong> validSongs = songs
                .Where(song => song.IsComplete)
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

        /// <summary>Opens the link of a song in the browser; tells the user when it is no web address or cannot be opened.</summary>
        public void OpenLink(string? link)
        {
            string title = AppLocalization.Get(AppStrings.UnreleasedFormTitle);

            switch (LinkLauncher.Open(link, out string? error))
            {
                case LinkOpenResult.NotAWebAddress:
                    view.ShowError(AppLocalization.Get(AppStrings.UnreleasedLinkInvalid), title);
                    break;

                case LinkOpenResult.Failed:
                    view.ShowError(AppLocalization.Get(AppStrings.UnreleasedLinkCouldNotOpenFormat, error ?? string.Empty), title);
                    break;
            }
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
