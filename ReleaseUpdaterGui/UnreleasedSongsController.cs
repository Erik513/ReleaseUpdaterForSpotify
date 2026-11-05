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
