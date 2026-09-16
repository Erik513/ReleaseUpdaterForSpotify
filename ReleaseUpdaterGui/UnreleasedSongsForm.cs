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
    public sealed class UnreleasedSongsForm : StyledForm
    {
        private static readonly Size ActionIconButtonSize = new(48, 34);

        private readonly UnreleasedSongsController controller;
        private readonly BindingList<TrackedSongRow> rows = new();

        private DataGridView dataGridView = null!;
        private ProgressBar progressBar = null!;
        private Label lblStatus = null!;
        private Button btnStart = null!;
        private Button btnOpenReport = null!;
        private Button btnCancel = null!;

        public UnreleasedSongsForm(
            TrackedSongsStore songsStore,
            IUnreleasedSongsReportWriter reportWriter,
            ReleaseUpdater.Core.Services.UnreleasedSongChecker checker) : base(
            StyledFormOptions.CreateStandard(
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

        public void ApplyProgress(ReleaseProgress progress)
        {
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

            btnStart.Enabled = false;
            btnCancel.Enabled = true;
            dataGridView.Enabled = false;
        }

        public void ShowCompleted(UnreleasedSongsCheckResult result)
        {
            btnStart.Enabled = true;
            btnCancel.Enabled = false;
            dataGridView.Enabled = true;
            progressBar.Value = 100;

            SetStatus(AppLocalization.Get(
                AppStrings.UnreleasedStatusDoneFormat,
                result.FoundCount,
                result.CheckedCount));
        }

        public void ShowRunFailed(string message)
        {
            btnStart.Enabled = true;
            btnCancel.Enabled = false;
            dataGridView.Enabled = true;

            string title = AppLocalization.Get(AppStrings.MainFormError);
            SetStatus(title);
            ShowError(message, title);
        }

        public void ShowCancelled()
        {
            btnStart.Enabled = true;
            btnCancel.Enabled = false;
            dataGridView.Enabled = true;

            SetStatus(AppLocalization.Get(AppStrings.MainFormCancelled));
        }

        public void ShowInfo(string message, string title)
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Info,
                this,
                MessageBoxSize.Small);
        }

        public void ShowError(string message, string title)
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                this,
                MessageBoxSize.Small);
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

            dataGridView = UIStyles.DataGridViews.CreateStandard();
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
                HeaderText = AppLocalization.Get(AppStrings.UnreleasedColumnTitle),
                Resizable = DataGridViewTriState.False
            });
            dataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colArtist",
                DataPropertyName = nameof(TrackedSongRow.Artist),
                HeaderText = AppLocalization.Get(AppStrings.UnreleasedColumnArtist),
                Resizable = DataGridViewTriState.False
            });
            dataGridView.DataSource = rows;

            // Cast needed since UIStyles.DataGridViews.CreateStandard()
            // hands back the plain System.Windows.Forms.DataGridView type -
            // ShowDeleteRowColumn only exists on ErikwnkWFUI's own subclass.
            ((ErikwnkWFUI.Controls.DataGridView)dataGridView).ShowDeleteRowColumn = true;

            dataGridView.Resize += (_, _) => ApplyFixedColumnWidths();
            ApplyFixedColumnWidths();

            TableLayoutPanel bottomPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 3 * ActionIconButtonSize.Width + 16));
            bottomPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Panel progressPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 4, 8, 4)
            };

            lblStatus = UIStyles.Labels.CreateNormal(AppLocalization.Get(AppStrings.ReadyToRun));
            lblStatus.Dock = DockStyle.Top;
            lblStatus.Height = 20;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;

            progressBar = UIStyles.ProgressBars.CreateGreen();
            progressBar.Dock = DockStyle.Bottom;
            progressBar.Height = 20;
            progressBar.Maximum = 100;
            progressBar.Value = 0;

            progressPanel.Controls.Add(progressBar);
            progressPanel.Controls.Add(lblStatus);

            FlowLayoutPanel buttonPanel = UIStyles.FlowLayoutPanels.CreateStandard();
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.FlowDirection = FlowDirection.RightToLeft;
            buttonPanel.Padding = new Padding(0, 8, 0, 0);

            btnStart = UIStyles.Buttons.CreateGreen(
                string.Empty,
                AppLocalization.Get(AppStrings.UnreleasedStart),
                ActionIconButtonSize);
            SetButtonSymbol(btnStart, "▶", AppLocalization.Get(AppStrings.UnreleasedStart));
            btnStart.Click += BtnStart_Click;

            btnOpenReport = UIStyles.Buttons.CreateStandard(
                string.Empty,
                AppLocalization.Get(AppStrings.UnreleasedOpenReport),
                ActionIconButtonSize);
            SetButtonSymbol(btnOpenReport, "\U0001F4C4", AppLocalization.Get(AppStrings.UnreleasedOpenReport));
            btnOpenReport.Click += BtnOpenReport_Click;

            btnCancel = UIStyles.Buttons.CreateRed(
                string.Empty,
                AppLocalization.Get(AppStrings.UnreleasedCancel),
                ActionIconButtonSize);
            SetButtonSymbol(btnCancel, "■", AppLocalization.Get(AppStrings.UnreleasedCancel));
            btnCancel.Enabled = false;
            btnCancel.Click += BtnCancel_Click;

            buttonPanel.Controls.Add(btnStart);
            buttonPanel.Controls.Add(btnOpenReport);
            buttonPanel.Controls.Add(btnCancel);

            bottomPanel.Controls.Add(progressPanel, 0, 0);
            bottomPanel.Controls.Add(buttonPanel, 1, 0);

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

        private static void SetButtonSymbol(Button button, string symbol, string accessibleName)
        {
            button.Text = symbol;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Font = new Font(button.Font.FontFamily, 10.5f, FontStyle.Regular);
            button.Padding = Padding.Empty;
            button.AccessibleName = accessibleName;
        }

        private async void BtnStart_Click(object? sender, EventArgs e)
        {
            await controller.StartSearchAsync(GetCurrentSongs());
        }

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            controller.CancelSearch();
        }

        private void BtnOpenReport_Click(object? sender, EventArgs e)
        {
            controller.OpenReport();
        }

        private void UnreleasedSongsForm_FormClosing(object? sender, CancelEventArgs e)
        {
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

            controller.SaveSongs(songsToSave);
        }

        private sealed class TrackedSongRow
        {
            public string Title { get; set; } = string.Empty;
            public string Artist { get; set; } = string.Empty;
        }
    }
}
