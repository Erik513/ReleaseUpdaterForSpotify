using System.Diagnostics;
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
        private readonly UnreleasedSongsForm form;
        private readonly TrackedSongsStore songsStore;
        private readonly UnreleasedSongChecker checker;
        private readonly IUnreleasedSongsReportWriter reportWriter;

        private CancellationTokenSource? runCancellation;

        public UnreleasedSongsController(
            UnreleasedSongsForm form,
            TrackedSongsStore songsStore,
            UnreleasedSongChecker checker,
            IUnreleasedSongsReportWriter reportWriter)
        {
            this.form = form;
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
                form.ShowInfo(
                    AppLocalization.Get(AppStrings.UnreleasedNoSongsMessage),
                    AppLocalization.Get(AppStrings.UnreleasedFormTitle));

                return;
            }

            form.PrepareRunUi();
            runCancellation = new CancellationTokenSource();

            try
            {
                Progress<ReleaseProgress> progress = new(form.ApplyProgress);

                UnreleasedSongsCheckResult result = await checker.RunAsync(
                    validSongs,
                    progress,
                    runCancellation.Token);

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
            }
        }

        public void CancelSearch()
        {
            runCancellation?.Cancel();
        }

        public void OpenReport()
        {
            string title = AppLocalization.Get(AppStrings.UnreleasedFormTitle);

            if (!File.Exists(reportWriter.ReportPath))
            {
                form.ShowInfo(
                    AppLocalization.Get(AppStrings.ControllerNoReportAvailable),
                    title);

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
                    AppLocalization.Get(AppStrings.ControllerCouldNotOpenReportFormat, ex.Message),
                    title);
            }
        }
    }
}
