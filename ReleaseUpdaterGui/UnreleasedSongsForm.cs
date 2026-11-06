using System.ComponentModel;
using ErikwnkCore;
using ErikwnkWFUI;
using ErikwnkWFUI.Forms;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Localization;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Data.Storage;

namespace ReleaseUpdaterGui
{
    public sealed class UnreleasedSongsForm : AppForm, IUnreleasedSongsView
    {
        private static readonly Size ActionIconButtonSize = new(48, 34);

        private readonly UnreleasedSongsController controller;
        private readonly BindingList<TrackedSongRow> rows = new();

        private DataGridView dataGridView = null!;
        private ProgressBar progressBar = null!;
        private Label lblStatus = null!;
        private SymbolToggle btnRun = null!;
        private Button btnOpenReport = null!;
        private Button btnImport = null!;
        private Button btnExport = null!;

        public UnreleasedSongsForm(
            TrackedSongsStore songsStore,
            IUnreleasedSongsReportWriter reportWriter,
            ReleaseUpdater.Core.Services.UnreleasedSongChecker checker) : base(
            FormOptions.CreateStandard(
                title: AppLocalization.Get(AppStrings.UnreleasedFormTitle),
                titleTextAlign: ContentAlignment.MiddleCenter,
                backColor: UIStyles.Colors.BackgroundDarkElevated))
        {
            controller = new UnreleasedSongsController(this, songsStore, checker, reportWriter);

            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 640);
            MinimumSize = new Size(460, 420);
            ShowInTaskbar = false;

            BuildUi();
            LoadSongs();

            FormClosing += UnreleasedSongsForm_FormClosing;
        }

        public void SetStatus(string status)
        {
            lblStatus.Text = status;
        }

        /// <summary>True once the window is closing; a search that is still ending must leave it alone.</summary>
        public bool IsClosing { get; private set; }

        public bool IsShuttingDown => IsClosing || IsDisposed;

        public void ApplyProgress(ReleaseProgress progress)
        {
            if (IsClosing || IsDisposed)
            {
                return;
            }

            if (progress.Current.HasValue && progress.Total.HasValue)
            {
                progressBar.Value = Math.Min(100, Math.Max(0, progress.Percent ?? 0));
            }

            if (!string.IsNullOrWhiteSpace(progress.Message))
            {
                lblStatus.Text = progress.Message;
            }
        }

        public void PrepareRunUi()
        {
            progressBar.Value = 0;
            SetStatus(AppLocalization.Get(AppStrings.UnreleasedSearching));

            SetBusy(true);
        }

        public void ShowCompleted(UnreleasedSongsCheckResult result)
        {
            SetBusy(false);
            progressBar.Value = 100;

            SetStatus(AppLocalization.Get(
                AppStrings.UnreleasedStatusDoneFormat,
                result.FoundCount,
                result.CheckedCount));
        }

        public void ShowRunFailed(string message)
        {
            SetBusy(false);

            string title = AppLocalization.Get(AppStrings.MainFormError);
            SetStatus(title);
            ShowError(message, title);
        }

        public void ShowCancelled()
        {
            SetBusy(false);

            SetStatus(AppLocalization.Get(AppStrings.MainFormCancelled));
        }

        // While a search runs, the list and everything that changes it or starts another search is locked.
        private void SetBusy(bool busy)
        {
            // One button: Play starts the search, and while it runs the same place is Stop (which stays usable).
            btnRun.ShowSecond(busy);
            btnRun.Enabled = true;
            btnImport.Enabled = !busy;
            btnExport.Enabled = !busy;
            dataGridView.Enabled = !busy;
        }

        public void ShowInfo(string message, string title)
        {
            Dialogs.Info(this, message, title);
        }

        public void ShowError(string message, string title)
        {
            Dialogs.Error(this, message, title);
        }

        private void LoadSongs()
        {
            foreach (TrackedSong song in controller.LoadSongs())
            {
                rows.Add(new TrackedSongRow
                {
                    Title = song.Title,
                    Artist = song.Artist
                });
            }
        }

        private IReadOnlyList<TrackedSong> GetCurrentSongs()
        {
            return rows
                .Select(row => new TrackedSong(row.Title?.Trim() ?? string.Empty, row.Artist?.Trim() ?? string.Empty))
                .ToList();
        }

        private void BuildUi()
        {
            Panel mainPanel = UIStyles.Panels.CreateElevated();
            mainPanel.BackColor = UIStyles.Colors.BackgroundLight;
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = new Padding(16);

            dataGridView = UIStyles.DataGridViews.CreatePrimary();
            dataGridView.Dock = DockStyle.Fill;
            dataGridView.AutoGenerateColumns = false;
            dataGridView.AllowUserToAddRows = true;
            dataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            dataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dataGridView.ColumnHeadersHeight = 32;
            dataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colTitle",
                DataPropertyName = nameof(TrackedSongRow.Title),
                HeaderText = AppLocalization.Get(AppStrings.UnreleasedColumnTitle)
            });
            dataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colArtist",
                DataPropertyName = nameof(TrackedSongRow.Artist),
                HeaderText = AppLocalization.Get(AppStrings.UnreleasedColumnArtist)
            });
            dataGridView.DataSource = rows;

            // Cast needed since UIStyles.DataGridViews.CreateStandard()
            // hands back the plain System.Windows.Forms.DataGridView type -
            // ShowDeleteRowColumn only exists on ErikwnkWFUI's own subclass.
            ErikwnkWFUI.Controls.DataGridView libraryGrid = (ErikwnkWFUI.Controls.DataGridView)dataGridView;
            libraryGrid.ShowDeleteRowColumn = true;

            // The library grid resizes and reorders its columns itself and
            // ignores the standard DataGridViewColumn.Resizable. This form
            // sizes its two columns by hand to fill the width
            // (ApplyFixedColumnWidths), so the user must not drag them.
            libraryGrid.AllowColumnResizing = false;
            libraryGrid.AllowColumnReordering = false;

            dataGridView.Resize += (_, _) => ApplyFixedColumnWidths();
            ApplyFixedColumnWidths();

            TableLayoutPanel bottomPanel = UIStyles.TableLayoutPanels.CreateTransparent(
                columnCount: 2,

            // Two rows: the progress bar (as high as the buttons beside it) and the buttons, and below them the status line
            // over the whole width - a text that is too long for it moves back and forth instead of being cut.
                rowCount: 2);
            bottomPanel.Dock = DockStyle.Bottom;
            bottomPanel.Height = 44 + 28;
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 4 * ActionIconButtonSize.Width + 16));
            bottomPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            bottomPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            lblStatus = UIStyles.Labels.CreateStandard(
                AppLocalization.Get(AppStrings.ReadyToRun),
                scroll: ErikwnkWFUI.Controls.LabelScroll.BackAndForth(pauseOnHover: true));
            lblStatus.Dock = DockStyle.Fill;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;

            progressBar = UIStyles.ProgressBars.CreateGreen();
            progressBar.Dock = DockStyle.Fill;
            progressBar.Margin = new Padding(0, 8, 8, 2);
            progressBar.Maximum = 100;
            progressBar.Value = 0;

            FlowLayoutPanel buttonPanel = UIStyles.FlowLayoutPanels.CreateTransparent();
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.FlowDirection = FlowDirection.RightToLeft;
            buttonPanel.Padding = new Padding(0, 8, 0, 0);

            btnRun = new SymbolToggle(
                UIStyles.Buttons.Presets.Play,
                UIStyles.Buttons.Presets.Stop,
                AppLocalization.Get(AppStrings.UnreleasedStart),
                AppLocalization.Get(AppStrings.UnreleasedCancel),
                ActionIconButtonSize,
                BtnRun_Click,
                useAccent: false);

            btnOpenReport = UIStyles.Buttons.CreateSymbol(
                UIStyles.Buttons.Presets.Document,
                AppLocalization.Get(AppStrings.UnreleasedOpenReport),
                ActionIconButtonSize);
            btnOpenReport.Click += BtnOpenReport_Click;

            // Import and export are quiet buttons: the dark gray of the report button, not the blue of their presets.
            btnImport = UIStyles.Buttons.CreateSymbol(
                UIStyles.Buttons.Presets.Download.WithColors(UIStyles.Buttons.Presets.Document.Colors),
                AppLocalization.Get(AppStrings.UnreleasedImport),
                ActionIconButtonSize);
            btnImport.Click += BtnImport_Click;

            btnExport = UIStyles.Buttons.CreateSymbol(
                UIStyles.Buttons.Presets.Upload.WithColors(UIStyles.Buttons.Presets.Document.Colors),
                AppLocalization.Get(AppStrings.UnreleasedExport),
                ActionIconButtonSize);
            btnExport.Click += BtnExport_Click;

            // Right to left: the run / stop button is the rightmost, the report next to it, then import and export.
            buttonPanel.Controls.Add(btnRun.Host);
            buttonPanel.Controls.Add(btnOpenReport);
            buttonPanel.Controls.Add(btnImport);
            buttonPanel.Controls.Add(btnExport);

            bottomPanel.Controls.Add(progressBar, 0, 0);
            bottomPanel.Controls.Add(buttonPanel, 1, 0);
            bottomPanel.Controls.Add(lblStatus, 0, 1);
            bottomPanel.SetColumnSpan(lblStatus, 2);

            mainPanel.Controls.Add(dataGridView);
            mainPanel.Controls.Add(bottomPanel);

            ContentPanel.Controls.Add(mainPanel);

            ApplyFixedColumnWidths();
        }

        // AutoSizeColumnsMode.Fill (the usual way to split a grid's width
        // between columns) left the column headers nearly invisible in this
        // control, so the widths are recalculated by hand instead - a fixed
        // 50/50 split, kept in sync with the grid's own size via its
        // Resize event.
        private void ApplyFixedColumnWidths()
        {
            if (dataGridView.Columns.Count < 2)
            {
                return;
            }

            // The delete-row column manages its own fixed width - only the
            // Title/Artist split below needs to leave room for it.
            int reservedWidth = ((ErikwnkWFUI.Controls.DataGridView)dataGridView).ShowDeleteRowColumn
                ? ErikwnkWFUI.Controls.DataGridView.DeleteRowColumnWidth
                : 0;
            int availableWidth = dataGridView.ClientSize.Width - reservedWidth;

            if (availableWidth <= 0)
            {
                return;
            }

            int firstColumnWidth = availableWidth / 2;
            int secondColumnWidth = availableWidth - firstColumnWidth;

            dataGridView.Columns[0].Width = Math.Max(1, firstColumnWidth);
            dataGridView.Columns[1].Width = Math.Max(1, secondColumnWidth);
        }

        // Play starts the search; while it runs the same button is Stop.
        private async void BtnRun_Click(object? sender, EventArgs e)
        {
            if (btnRun.IsSecondShown)
            {
                controller.CancelSearch();
                return;
            }

            await controller.StartSearchAsync(GetCurrentSongs());
        }

        private void BtnImport_Click(object? sender, EventArgs e)
        {
            using OpenFileDialog dialog = new()
            {
                Title = AppLocalization.Get(AppStrings.UnreleasedImport),
                Filter = GetFileFilter(includeCombined: true),
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            // (Rows that are still being typed are part of the list, so they are kept.)
            IReadOnlyList<TrackedSong>? merged = controller.ImportSongs(dialog.FileName, GetCurrentSongs());

            if (merged is null)
            {
                return;
            }

            rows.Clear();

            foreach (TrackedSong song in merged)
            {
                rows.Add(new TrackedSongRow { Title = song.Title, Artist = song.Artist });
            }
        }

        private void BtnExport_Click(object? sender, EventArgs e)
        {
            using SaveFileDialog dialog = new()
            {
                Title = AppLocalization.Get(AppStrings.UnreleasedExport),
                Filter = GetFileFilter(includeCombined: false),
                DefaultExt = "xlsx",
                AddExtension = true,
                FileName = "unreleased-songs",
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                controller.ExportSongs(GetCurrentSongs(), dialog.FileName);
            }
        }

        // Excel first (the default of a save), then Word, CSV and JSON; an open dialog can also show all of them at once.
        private static string GetFileFilter(bool includeCombined)
        {
            string songs = AppLocalization.Get(AppStrings.UnreleasedFileFilterSongs);
            string filter = "Excel (*.xlsx)|*.xlsx|Word (*.docx)|*.docx|CSV (*.csv)|*.csv|JSON (*.json)|*.json";

            return includeCombined
                ? songs + " (*.xlsx;*.docx;*.csv;*.json)|*.xlsx;*.docx;*.csv;*.json|" + filter
                : filter;
        }

        private void BtnOpenReport_Click(object? sender, EventArgs e)
        {
            controller.OpenReport();
        }

        private void UnreleasedSongsForm_FormClosing(object? sender, CancelEventArgs e)
        {
            IsClosing = true;

            if (controller.IsRunInProgress)
            {
                controller.CancelSearch();
            }

            // Skip rows where both fields are still empty - the DataGridView's
            // native "type here to add a row" placeholder always leaves one of
            // these at the end, and there's nothing useful to persist from it.
            IReadOnlyList<TrackedSong> songsToSave = GetCurrentSongs()
                .Where(song => song.Title.Length > 0 || song.Artist.Length > 0)
                .ToList();

            try
            {
                controller.SaveSongs(songsToSave);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The window still closes, but the user is told the list was not kept.
                ShowError(
                    AppLocalization.Get(AppStrings.UnreleasedSaveFailedFormat, ex.Message),
                    AppLocalization.Get(AppStrings.UnreleasedFormTitle));
            }
        }

        private sealed class TrackedSongRow
        {
            public string Title { get; set; } = string.Empty;
            public string Artist { get; set; } = string.Empty;
        }
    }
}
