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
        // The columns are read by name, not by position (the user can drag the headers).
        private const string TitleColumnName = "colTitle";
        private const string ArtistColumnName = "colArtist";
        private const string LinkColumnName = "colLink";
        private const int LinkColumnWidth = 52;

        private static readonly Size ActionIconButtonSize = new(48, 34);
        private static readonly Font LinkFont = new("Segoe UI Symbol", 10f);

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

        // True while the code (not the user) moves the columns: that is not saved as the user's choice.
        private bool applyingColumnOrder;

        // The list as it was before the last import or paste (Ctrl+Z brings it back); null when there is nothing to undo.
        private IReadOnlyList<TrackedSong>? undoSnapshot;

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
                Name = TitleColumnName,
                DataPropertyName = nameof(TrackedSongRow.Title),
                HeaderText = AppLocalization.Get(AppStrings.UnreleasedColumnTitle),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 50
            });
            dataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = ArtistColumnName,
                DataPropertyName = nameof(TrackedSongRow.Artist),
                HeaderText = AppLocalization.Get(AppStrings.UnreleasedColumnArtist),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 50
            });

            // The link of a song: no text in the list (it would make it wide), one small button per row. It is bound to the link, but its text is
            // never shown: CellPainting draws the symbol - the VALUE of the cell is the link itself, so that copying the cell copies the address and not a
            // symbol. A double click opens the link (or asks for one when there is none), Ctrl + double click changes it (a right click is
            // the grid's own, it has a menu).
            dataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = LinkColumnName,
                DataPropertyName = nameof(TrackedSongRow.Link),
                HeaderText = AppLocalization.Get(AppStrings.UnreleasedColumnLink),
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Width = LinkColumnWidth
            });
            // The header of the column says which clicks do what (the tooltip of the header cell).
            dataGridView.Columns[LinkColumnName]!.HeaderCell.ToolTipText = AppLocalization.Get(AppStrings.UnreleasedLinkHeaderTooltip);
            dataGridView.CellFormatting += UpdateLinkTooltip;
            dataGridView.CellPainting += DataGridView_LinkCellPainting;
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
            // splits the width between Title and Artist with AutoSizeMode.Fill
            // (FillWeight 50 each; the grid keeps them right when a scroll bar
            // comes and goes), so the user must not drag their widths. The order of Title and Artist can be changed by
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
                else if (e.Control && e.KeyCode == Keys.Z && !dataGridView.IsCurrentCellInEditMode && btnPaste.Enabled && undoSnapshot is not null)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    UndoImport();
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

        }

        // The tooltip of the small button of a row says what a double click does (the text of the cell is not touched: it is the link).
        private void UpdateLinkTooltip(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (!IsSongRow(e.RowIndex) || dataGridView.Columns[e.ColumnIndex].Name != LinkColumnName)
            {
                return;
            }

            string? link = rows[e.RowIndex].Link;
            bool hasLink = !string.IsNullOrWhiteSpace(link);

            dataGridView[e.ColumnIndex, e.RowIndex].ToolTipText = hasLink
                ? AppLocalization.Get(AppStrings.UnreleasedLinkTooltipOpenFormat, link!.Trim())
                : AppLocalization.Get(AppStrings.UnreleasedLinkTooltipAdd);
        }

        // The small button of a row: the symbol of a link, bright when the song has one and dim when not. The cell is painted without its text
        // (the address) and the symbol is drawn over it, so the cell keeps the link as its value.
        private void DataGridView_LinkCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            if (!IsSongRow(e.RowIndex) || e.ColumnIndex < 0 || dataGridView.Columns[e.ColumnIndex].Name != LinkColumnName)
            {
                return;
            }

            e.Paint(e.ClipBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);

            bool hasLink = !string.IsNullOrWhiteSpace(rows[e.RowIndex].Link);
            // (The text color of the cell, not a fixed one: the grid dims it while a search runs, and the symbol dims with it.)
            Color text = e.CellStyle!.ForeColor;
            Color color = hasLink ? text : Blend(text, e.CellStyle.BackColor, 0.7);

            TextRenderer.DrawText(
                e.Graphics!,
                UIStyles.Symbols.Link,
                LinkFont,
                e.CellBounds,
                color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            e.Handled = true;
        }

        // A row that is a song: not the header, not the empty row at the end of the list - also not while that row is being filled: the list has
        // an item for it then, but it is still the row "new" of the grid - and one with a title or an artist in it. Only such a row has a link.
        private bool IsSongRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= rows.Count || rowIndex >= dataGridView.Rows.Count || dataGridView.Rows[rowIndex].IsNewRow)
            {
                return false;
            }

            TrackedSongRow row = rows[rowIndex];

            return !string.IsNullOrWhiteSpace(row.Title) || !string.IsNullOrWhiteSpace(row.Artist);
        }

        private static Color Blend(Color from, Color to, double toShare) =>
            Color.FromArgb(
                (int)(from.R + ((to.R - from.R) * toShare)),
                (int)(from.G + ((to.G - from.G) * toShare)),
                (int)(from.B + ((to.B - from.B) * toShare)));

        private void DataGridView_LinkCellMouseDoubleClick(object? sender, DataGridViewCellMouseEventArgs e)
        {
            // (Not the header, and not the empty row at the end of the list.)
            if (!IsSongRow(e.RowIndex) || dataGridView.Columns[e.ColumnIndex].Name != LinkColumnName)
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
            SaveQuietly();
        }

        public void ApplyColumnOrder(bool artistFirst)
        {
            ApplyColumnOrder(artistFirst, keep: true);
        }

        private bool IsArtistFirst() =>
            dataGridView.Columns[ArtistColumnName]!.DisplayIndex < dataGridView.Columns[TitleColumnName]!.DisplayIndex;

        // Puts the Artist column before or behind the Title column (the delete column stays pinned at the right).
        private void ApplyColumnOrder(bool artistFirst, bool keep)
        {
            applyingColumnOrder = true;

            try
            {
                if (IsArtistFirst() != artistFirst)
                {
                    dataGridView.Columns[ArtistColumnName]!.DisplayIndex = artistFirst ? 0 : 1;
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
            string? text;
            string? html = null;

            try
            {
                text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
                html = Clipboard.ContainsText(TextDataFormat.Html) ? Clipboard.GetText(TextDataFormat.Html) : null;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // Another program (Excel, Word) holds the clipboard at this moment.
                SetStatus(AppLocalization.Get(AppStrings.UnreleasedClipboardBusy));

                return;
            }

            ShowSongs(controller.PasteSongs(text, GetCurrentSongs(), html));
        }

        // The list after an import or a paste (null: nothing changed).
        private void ShowSongs(IReadOnlyList<TrackedSong>? songs)
        {
            if (songs is null)
            {
                return;
            }

            undoSnapshot = GetCurrentSongs().Where(song => song.Title.Length > 0 || song.Artist.Length > 0).ToList();
            SetStatus(lblStatus.Text + AppLocalization.Get(AppStrings.UnreleasedUndoHint));
            LoadRows(songs);
            SaveQuietly();
        }

        // Puts the list as it was before the last import or paste.
        private void UndoImport()
        {
            if (undoSnapshot is null)
            {
                return;
            }

            LoadRows(undoSnapshot);
            undoSnapshot = null;
            SaveQuietly();
            SetStatus(AppLocalization.Get(AppStrings.UnreleasedUndone));
        }

        private void LoadRows(IReadOnlyList<TrackedSong> songs)
        {
            rows.Clear();

            foreach (TrackedSong song in songs)
            {
                rows.Add(new TrackedSongRow { Title = song.Title, Artist = song.Artist, Link = TrackedSongLinks.Normalize(song.Link) });
            }

            // (The grid selects the first cell when the list is filled; after an import nothing should look selected.)
            dataGridView.CurrentCell = null;
            dataGridView.ClearSelection();
        }

        // Keeps the list at once after an import or a link change, so that a crash does not lose it. A failure is not worth a message here:
        // closing the window tries again and tells the user then.
        private void SaveQuietly()
        {
            try
            {
                controller.SaveSongs(GetCurrentSongs().Where(song => song.Title.Length > 0 || song.Artist.Length > 0).ToList());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // (see above)
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
