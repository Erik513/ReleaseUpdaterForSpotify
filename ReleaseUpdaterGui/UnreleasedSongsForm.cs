using System.ComponentModel;
using ErikwnkCore;
using ErikwnkWFUI;
using ErikwnkWFUI.Forms;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Localization;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Core.Services;
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
        private Button btnPaste = null!;
        private Button btnExport = null!;

        public UnreleasedSongsForm(
            TrackedSongsStore songsStore,
            IUnreleasedSongsReportWriter reportWriter,
            ReleaseUpdater.Core.Services.UnreleasedSongChecker checker,
            ISettingsStore settingsStore) : base(
            FormOptions.CreateStandard(
                title: AppLocalization.Get(AppStrings.UnreleasedFormTitle),
                titleTextAlign: ContentAlignment.MiddleCenter,
                backColor: UIStyles.Colors.BackgroundDarkElevated))
        {
            controller = new UnreleasedSongsController(this, songsStore, checker, reportWriter, settingsStore);

            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 640);
            MinimumSize = new Size(460, 420);
            ShowInTaskbar = false;

            BuildUi();
            LoadSongs();
            ApplyColumnOrder(controller.LoadArtistFirst(), keep: false);

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

            SetStatus(result.PossibleCount > 0
                ? AppLocalization.Get(
                    AppStrings.UnreleasedStatusDonePossibleFormat,
                    result.FoundCount,
                    result.CheckedCount,
                    result.PossibleCount)
                : AppLocalization.Get(
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
            btnPaste.Enabled = !busy;
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
                    Artist = song.Artist,
                    // (A file that was edited by hand may hold something that is no web address: that is not kept as a link.)
                    Link = TrackedSongLinks.Normalize(song.Link)
                });
            }
        }

        private IReadOnlyList<TrackedSong> GetCurrentSongs()
        {
            return rows
                .Select(row => new TrackedSong(
                    row.Title?.Trim() ?? string.Empty,
                    row.Artist?.Trim() ?? string.Empty,
                    TrackedSongLinks.Normalize(row.Link)))
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

            // The link of a song: no text in the list (it would make it wide), one small button per row. It is not bound to a property:
            // CellFormatting draws it from the row. A double click opens the link (or asks for one when there is none), Ctrl + double click
            // changes it (a right click is the grid's own, it has a menu).
            dataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = LinkColumnName,
                HeaderText = AppLocalization.Get(AppStrings.UnreleasedColumnLink),
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Width = LinkColumnWidth
            });
            // The header of the column says which clicks do what (the tooltip of the header cell).
            dataGridView.Columns[LinkColumnName]!.HeaderCell.ToolTipText = AppLocalization.Get(AppStrings.UnreleasedLinkHeaderTooltip);
            dataGridView.CellFormatting += DataGridView_LinkCellFormatting;
            dataGridView.CellMouseDoubleClick += DataGridView_LinkCellMouseDoubleClick;
            dataGridView.DataSource = rows;

            // Cast needed since UIStyles.DataGridViews.CreateStandard()
            // hands back the plain System.Windows.Forms.DataGridView type -
            // ShowDeleteRowColumn only exists on ErikwnkWFUI's own subclass.
            ErikwnkWFUI.Controls.DataGridView libraryGrid = (ErikwnkWFUI.Controls.DataGridView)dataGridView;
            libraryGrid.ShowDeleteRowColumn = true;
            libraryGrid.SetColumnReorderable(dataGridView.Columns[LinkColumnName]!.Index, false);

            // The library grid resizes and reorders its columns itself and
            // ignores the standard DataGridViewColumn.Resizable. This form
            // sizes its two columns by hand to fill the width
            // (ApplyFixedColumnWidths), so the user must not drag their
            // widths. The order of Title and Artist can be changed by
            // dragging a header (the delete column stays pinned at the
            // right); the rows are read by column name, not by position.
            libraryGrid.AllowColumnResizing = false;
            libraryGrid.AllowColumnReordering = true;

            // Ctrl+V in the list (not while a cell is being edited: there it pastes into the cell) adds songs from the clipboard.
            dataGridView.KeyDown += (_, e) =>
            {
                if (e.Control && e.KeyCode == Keys.V && !dataGridView.IsCurrentCellInEditMode && btnPaste.Enabled)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    PasteFromClipboard();
                }
            };

            // The order the user gives the two columns by dragging a header is kept for the next time.
            dataGridView.ColumnDisplayIndexChanged += (_, _) =>
            {
                if (!applyingColumnOrder && !IsClosing)
                {
                    controller.SaveArtistFirst(IsArtistFirst());
                }
            };

            dataGridView.Resize += (_, _) => ApplyFixedColumnWidths();

            // The vertical scroll bar appears (and goes) with the number of rows, and takes room from the width without the grid being
            // resized: the columns have to follow, or they are wider than what is left and a horizontal scroll bar appears.
            // (Asked for after the grid has laid itself out: only then does it know whether it shows the bar.)
            dataGridView.RowsAdded += (_, _) => ApplyFixedColumnWidthsAfterLayout();
            dataGridView.RowsRemoved += (_, _) => ApplyFixedColumnWidthsAfterLayout();
            ApplyFixedColumnWidths();

            // Two rows: the progress bar (as high as the buttons beside it) and the buttons, and below them the status line
            // over the whole width - a text that is too long for it moves back and forth instead of being cut.
            TableLayoutPanel bottomPanel = UIStyles.TableLayoutPanels.CreateTransparent(
                columnCount: 2,
                rowCount: 2);
            bottomPanel.Dock = DockStyle.Bottom;
            bottomPanel.Height = 44 + 28;
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 5 * ActionIconButtonSize.Width + 16));
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

            btnPaste = UIStyles.Buttons.CreateSymbol(
                UIStyles.Buttons.Presets.Paste.WithColors(UIStyles.Buttons.Presets.Document.Colors),
                AppLocalization.Get(AppStrings.UnreleasedPaste),
                ActionIconButtonSize);
            btnPaste.Click += (_, _) => PasteFromClipboard();

            btnExport = UIStyles.Buttons.CreateSymbol(
                UIStyles.Buttons.Presets.Upload.WithColors(UIStyles.Buttons.Presets.Document.Colors),
                AppLocalization.Get(AppStrings.UnreleasedExport),
                ActionIconButtonSize);
            btnExport.Click += BtnExport_Click;

            // Right to left: the run / stop button is the rightmost, the report next to it, then import and export.
            buttonPanel.Controls.Add(btnRun.Host);
            buttonPanel.Controls.Add(btnOpenReport);
            buttonPanel.Controls.Add(btnPaste);
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

        private const string LinkColumnName = "colLink";
        private const int LinkColumnWidth = 52;

        private static readonly Font LinkFont = new("Segoe UI Symbol", 10f);

        private bool applyingColumnOrder;

        // The small button of a row: the symbol of a link, bright when the song has one and dim when not; the tooltip says what a click does.
        private void DataGridView_LinkCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count || dataGridView.Columns[e.ColumnIndex].Name != LinkColumnName)
            {
                return;
            }

            string? link = rows[e.RowIndex].Link;
            bool hasLink = !string.IsNullOrWhiteSpace(link);

            e.Value = UIStyles.Symbols.Link;
            e.CellStyle!.Font = LinkFont;
            e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            e.CellStyle.ForeColor = hasLink
                ? ErikwnkWFUI.Styles.UIColors.TextPrimary
                : Blend(ErikwnkWFUI.Styles.UIColors.TextPrimary, e.CellStyle.BackColor, 0.7);
            e.FormattingApplied = true;

            dataGridView[e.ColumnIndex, e.RowIndex].ToolTipText = hasLink
                ? AppLocalization.Get(AppStrings.UnreleasedLinkTooltipOpenFormat, link!.Trim())
                : AppLocalization.Get(AppStrings.UnreleasedLinkTooltipAdd);
        }

        private static Color Blend(Color from, Color to, double toShare) =>
            Color.FromArgb(
                (int)(from.R + ((to.R - from.R) * toShare)),
                (int)(from.G + ((to.G - from.G) * toShare)),
                (int)(from.B + ((to.B - from.B) * toShare)));

        private void DataGridView_LinkCellMouseDoubleClick(object? sender, DataGridViewCellMouseEventArgs e)
        {
            // (Not the header, and not the empty row at the end of the list.)
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count || dataGridView.Columns[e.ColumnIndex].Name != LinkColumnName)
            {
                return;
            }

            TrackedSongRow row = rows[e.RowIndex];

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            // A song without a link is asked for one; with a link, the double click opens it and Ctrl + double click changes it.
            if (string.IsNullOrWhiteSpace(row.Link) || (ModifierKeys & Keys.Control) == Keys.Control)
            {
                AskForLink(row, e.RowIndex);
            }
            else
            {
                controller.OpenLink(row.Link);
            }
        }

        // The window for the link of a song: a web address (the button in it takes an existing link away at once).
        private void AskForLink(TrackedSongRow row, int rowIndex)
        {
            using LinkForm dialog = new(
                AppLocalization.Get(AppStrings.UnreleasedLinkPromptFormat, row.Title?.Trim() ?? string.Empty),
                row.Link);

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            // (The window lets nothing but a web address through; it is kept as the address, with https:// where it was left out.)
            row.Link = dialog.Address;
            dataGridView.InvalidateRow(rowIndex);
        }

        public void ApplyColumnOrder(bool artistFirst)
        {
            ApplyColumnOrder(artistFirst, keep: true);
        }

        private bool IsArtistFirst() =>
            dataGridView.Columns["colArtist"]!.DisplayIndex < dataGridView.Columns["colTitle"]!.DisplayIndex;

        // Puts the Artist column before or behind the Title column (the delete column stays pinned at the right).
        private void ApplyColumnOrder(bool artistFirst, bool keep)
        {
            applyingColumnOrder = true;

            try
            {
                if (IsArtistFirst() != artistFirst)
                {
                    dataGridView.Columns["colArtist"]!.DisplayIndex = artistFirst ? 0 : 1;
                }
            }
            finally
            {
                applyingColumnOrder = false;
            }

            if (keep)
            {
                controller.SaveArtistFirst(artistFirst);
            }
        }

        private bool widthsPending;

        private void ApplyFixedColumnWidthsAfterLayout()
        {
            if (widthsPending || !dataGridView.IsHandleCreated)
            {
                return;
            }

            widthsPending = true;

            dataGridView.BeginInvoke(() =>
            {
                widthsPending = false;
                ApplyFixedColumnWidths();
            });
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

            // The column of the link buttons has a width of its own, too.
            reservedWidth += LinkColumnWidth;
            // (The DisplayRectangle is the width that is left of the client area when a scroll bar is shown.)
            int availableWidth = dataGridView.DisplayRectangle.Width - reservedWidth;

            if (availableWidth <= 0)
            {
                return;
            }

            int firstColumnWidth = availableWidth / 2;
            int secondColumnWidth = availableWidth - firstColumnWidth;

            // (Only a change is set: setting a width lays the grid out again, which comes back here.)
            if (dataGridView.Columns[0].Width != Math.Max(1, firstColumnWidth))
            {
                dataGridView.Columns[0].Width = Math.Max(1, firstColumnWidth);
            }

            if (dataGridView.Columns[1].Width != Math.Max(1, secondColumnWidth))
            {
                dataGridView.Columns[1].Width = Math.Max(1, secondColumnWidth);
            }
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
            ShowSongs(controller.ImportSongs(dialog.FileName, GetCurrentSongs()));
        }

        private void PasteFromClipboard()
        {
            string? text = Clipboard.ContainsText() ? Clipboard.GetText() : null;

            ShowSongs(controller.PasteSongs(text, GetCurrentSongs()));
        }

        // The list after an import or a paste (null: nothing changed).
        private void ShowSongs(IReadOnlyList<TrackedSong>? songs)
        {
            if (songs is null)
            {
                return;
            }

            rows.Clear();

            foreach (TrackedSong song in songs)
            {
                rows.Add(new TrackedSongRow { Title = song.Title, Artist = song.Artist, Link = TrackedSongLinks.Normalize(song.Link) });
            }
        }

        public ImportMode? ChooseImportMode(int currentCount, int importedCount)
        {
            DialogResult answer = Dialogs.AskYesNoCancel(
                this,
                AppLocalization.Get(AppStrings.UnreleasedImportModeFormat, currentCount, importedCount),
                AppLocalization.Get(AppStrings.UnreleasedFormTitle));

            return answer switch
            {
                DialogResult.Yes => ImportMode.Replace,
                DialogResult.No => ImportMode.Add,
                _ => null
            };
        }

        public ImportColumnChoice? ChooseImportColumns(ImportTable table)
        {
            using ImportColumnsForm dialog = new(table);

            return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Choice : null;
        }

        private void BtnExport_Click(object? sender, EventArgs e)
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

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                controller.ExportSongs(GetCurrentSongs(), dialog.FileName);
            }
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

            // Not shown as text (the list stays compact): a small button per row opens it, see the column "colLink".
            public string? Link { get; set; }
        }
    }
}
