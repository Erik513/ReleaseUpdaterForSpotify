using ErikwnkCore;
using ErikwnkWFUI;
using ErikwnkWFUI.Forms;
using ReleaseUpdater.Core.Localization;
using ReleaseUpdater.Core.Services;

namespace ReleaseUpdaterGui
{
    /// <summary>
    /// Asks which columns of an imported table are the title and the artist (a table from Excel or Word often has a number or a note in
    /// front of them). It starts with the proposal of <see cref="ImportTable.GuessColumns"/> and shows the first rows, with the chosen
    /// columns marked in their headers.
    /// </summary>
    public sealed class ImportColumnsForm : AppForm
    {
        private const int PreviewRows = 8;
        private const int SampleLength = 28;

        private readonly ImportTable table;
        private readonly ComboBox cmbTitle;
        private readonly ComboBox cmbArtist;
        private readonly ComboBox cmbLink;
        private readonly CheckBox chkHeader;
        private readonly DataGridView preview;
        private readonly Label lblHint;
        private readonly Button btnOk;
        private bool updating;

        public ImportColumnsForm(ImportTable table) : base(
            FormOptions.CreateDialog(
                title: AppLocalization.Get(AppStrings.ImportColumnsTitle),
                titleTextAlign: ContentAlignment.MiddleCenter,
                backColor: UIStyles.Colors.BackgroundDarkElevated))
        {
            this.table = table;

            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(640, 460);
            MinimumSize = new Size(560, 400);

            TableLayoutPanel layout = UIStyles.TableLayoutPanels.CreateStandard(
                columnCount: 1,
                rowCount: 5,
                colors: UIStyles.Colors.BackgroundLight);
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(16);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            Label intro = UIStyles.Labels.CreateStandard(
                AppLocalization.Get(AppStrings.ImportColumnsIntroFormat, table.ColumnCount));
            intro.Dock = DockStyle.Fill;

            cmbTitle = UIStyles.ComboBoxes.CreatePrimary();
            cmbArtist = UIStyles.ComboBoxes.CreatePrimary();
            cmbTitle.Dock = DockStyle.Fill;
            cmbArtist.Dock = DockStyle.Fill;
            cmbTitle.AccessibleName = AppLocalization.Get(AppStrings.ImportColumnsTitleColumn);
            cmbArtist.AccessibleName = AppLocalization.Get(AppStrings.ImportColumnsArtistColumn);

            // The column of the links is optional: the first entry is "none".
            cmbLink = UIStyles.ComboBoxes.CreatePrimary();
            cmbLink.Dock = DockStyle.Fill;
            cmbLink.AccessibleName = AppLocalization.Get(AppStrings.ImportColumnsLinkColumn);

            TableLayoutPanel choices = UIStyles.TableLayoutPanels.CreateTransparent(columnCount: 4, rowCount: 2);
            choices.Dock = DockStyle.Fill;
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            choices.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            choices.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            choices.Controls.Add(CreateCaption(AppStrings.ImportColumnsTitleColumn), 0, 0);
            choices.Controls.Add(cmbTitle, 1, 0);
            choices.Controls.Add(CreateCaption(AppStrings.ImportColumnsArtistColumn), 2, 0);
            choices.Controls.Add(cmbArtist, 3, 0);
            choices.Controls.Add(CreateCaption(AppStrings.ImportColumnsLinkColumn), 0, 1);
            choices.Controls.Add(cmbLink, 1, 1);

            TableLayoutPanel options = UIStyles.TableLayoutPanels.CreateTransparent(columnCount: 2, rowCount: 1);
            options.Dock = DockStyle.Fill;
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            chkHeader = UIStyles.CheckBoxes.CreatePrimary(AppLocalization.Get(AppStrings.ImportColumnsHasHeader));
            chkHeader.Dock = DockStyle.Fill;

            lblHint = UIStyles.Labels.CreateStandard(string.Empty, UIStyles.Colors.Sets.Red);
            lblHint.Dock = DockStyle.Fill;
            lblHint.TextAlign = ContentAlignment.MiddleLeft;

            options.Controls.Add(chkHeader, 0, 0);
            options.Controls.Add(lblHint, 1, 0);

            preview = UIStyles.DataGridViews.CreateReadOnlyPrimary();
            preview.Dock = DockStyle.Fill;
            preview.AutoGenerateColumns = false;
            preview.AllowUserToAddRows = false;
            preview.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            preview.ColumnHeadersHeight = 32;
            preview.TabStop = false;
            preview.Resize += (_, _) => FitPreviewColumns();

            // A preview is nothing to select in: the selection looks like the same color as the cells (no blue cell in the corner).
            preview.DefaultCellStyle.SelectionBackColor = preview.DefaultCellStyle.BackColor;
            preview.DefaultCellStyle.SelectionForeColor = preview.DefaultCellStyle.ForeColor;

            FlowLayoutPanel buttons = UIStyles.FlowLayoutPanels.CreateTransparent();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;

            Button btnCancel = UIStyles.Buttons.CreateStandard(
                AppLocalization.Get(AppStrings.ImportColumnsCancel),
                AppLocalization.Get(AppStrings.ImportColumnsCancel),
                new Size(110, 32),
                true);
            btnCancel.Margin = new Padding(8, 6, 0, 6);
            btnCancel.DialogResult = DialogResult.Cancel;

            btnOk = UIStyles.Buttons.CreatePrimary(
                AppLocalization.Get(AppStrings.ImportColumnsOk),
                AppLocalization.Get(AppStrings.ImportColumnsOk),
                new Size(130, 32),
                true);
            btnOk.Margin = new Padding(0, 6, 0, 6);
            btnOk.DialogResult = DialogResult.OK;

            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnOk);
            CancelButton = btnCancel;
            AcceptButton = btnOk;

            layout.Controls.Add(intro, 0, 0);
            layout.Controls.Add(choices, 0, 1);
            layout.Controls.Add(options, 0, 2);
            layout.Controls.Add(preview, 0, 3);
            layout.Controls.Add(buttons, 0, 4);
            ContentPanel.Controls.Add(layout);

            ImportColumnChoice guess = table.GuessColumns();
            updating = true;
            chkHeader.Checked = guess.HasHeader;
            FillColumnLists(keepSelection: false);
            cmbTitle.SelectedIndex = Math.Min(guess.TitleColumn, cmbTitle.Items.Count - 1);
            cmbArtist.SelectedIndex = Math.Min(guess.ArtistColumn, cmbArtist.Items.Count - 1);
            cmbLink.SelectedIndex = guess.LinkColumn is int linkColumn ? Math.Min(linkColumn + 1, cmbLink.Items.Count - 1) : 0;
            updating = false;

            cmbTitle.SelectedIndexChanged += (_, _) => RefreshPreview();
            cmbArtist.SelectedIndexChanged += (_, _) => RefreshPreview();
            cmbLink.SelectedIndexChanged += (_, _) => RefreshPreview();
            chkHeader.CheckedChanged += (_, _) =>
            {
                updating = true;
                FillColumnLists(keepSelection: true);
                updating = false;
                RefreshPreview();
            };

            RefreshPreview();
        }

        /// <summary>The columns the user chose (valid after OK).</summary>
        public ImportColumnChoice Choice =>
            new(Math.Max(0, cmbTitle.SelectedIndex), Math.Max(0, cmbArtist.SelectedIndex), chkHeader.Checked, LinkColumn);

        // The column of the links (the first entry of its list is "none").
        private int? LinkColumn => cmbLink.SelectedIndex > 0 ? cmbLink.SelectedIndex - 1 : null;

        private Label CreateCaption(string key)
        {
            Label label = UIStyles.Labels.CreateStandard(AppLocalization.Get(key));
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;

            return label;
        }

        // "Column 3: Some text" - the first row (or, with a header, the name of the column) tells what is in it.
        private void FillColumnLists(bool keepSelection)
        {
            int title = cmbTitle.SelectedIndex;
            int artist = cmbArtist.SelectedIndex;
            int link = cmbLink.SelectedIndex;
            List<object> items = new();

            for (int column = 0; column < Math.Max(2, table.ColumnCount); column++)
            {
                string sample = table.Cell(0, column).Trim();
                sample = sample.Length > SampleLength ? sample.Substring(0, SampleLength) + "…" : sample;
                string name = AppLocalization.Get(AppStrings.ImportColumnsColumnFormat, column + 1);
                items.Add(sample.Length == 0 ? name : name + ": " + sample);
            }

            UIStyles.ComboBoxes.ReplaceItems(cmbTitle, items, keepSelection: false);
            UIStyles.ComboBoxes.ReplaceItems(cmbArtist, items, keepSelection: false);
            UIStyles.ComboBoxes.ReplaceItems(
                cmbLink,
                new List<object> { AppLocalization.Get(AppStrings.ImportColumnsNoLink) }.Concat(items).ToList(),
                keepSelection: false);

            if (keepSelection)
            {
                cmbTitle.SelectedIndex = Math.Max(0, title);
                cmbArtist.SelectedIndex = Math.Max(0, artist);
                cmbLink.SelectedIndex = Math.Max(0, link);
            }
        }

        // The preview shows the first rows; the headers say which column is the title and which the artist.
        private void RefreshPreview()
        {
            if (updating)
            {
                return;
            }

            // (The three columns are different ones; "no link" is no column.)
            bool different = cmbTitle.SelectedIndex != cmbArtist.SelectedIndex &&
                (LinkColumn is null || (LinkColumn != cmbTitle.SelectedIndex && LinkColumn != cmbArtist.SelectedIndex));
            btnOk.Enabled = different;
            lblHint.Text = different ? string.Empty : AppLocalization.Get(AppStrings.ImportColumnsSameColumn);

            int columns = Math.Max(2, table.ColumnCount);

            preview.SuspendLayout();
            preview.Columns.Clear();
            preview.Rows.Clear();

            for (int column = 0; column < columns; column++)
            {
                string header = AppLocalization.Get(AppStrings.ImportColumnsColumnFormat, column + 1);

                if (column == cmbTitle.SelectedIndex)
                {
                    header += " → " + AppLocalization.Get(AppStrings.UnreleasedColumnTitle);
                }
                else if (column == cmbArtist.SelectedIndex)
                {
                    header += " → " + AppLocalization.Get(AppStrings.UnreleasedColumnArtist);
                }
                else if (column == LinkColumn)
                {
                    header += " → " + AppLocalization.Get(AppStrings.ImportColumnsLinkShort);
                }

                preview.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = header,
                    SortMode = DataGridViewColumnSortMode.NotSortable
                });
            }

            for (int row = 0; row < Math.Min(PreviewRows, table.Rows.Count); row++)
            {
                object[] cells = new object[columns];

                for (int column = 0; column < columns; column++)
                {
                    // (A cell with a hyperlink behind its text is marked: the text may only say "YouTube", the address is what is used.)
                    cells[column] = table.Cell(row, column) + (table.Hyperlink(row, column) is null ? string.Empty : " ↗");
                }

                int index = preview.Rows.Add(cells);

                // (The header row is shown in bold: it is not a song.)
                if (row == 0 && chkHeader.Checked)
                {
                    preview.Rows[index].DefaultCellStyle.Font = new Font(preview.Font, FontStyle.Bold);
                }
            }

            FitPreviewColumns();

            // (No cell is selected: the blue one in the corner would look like a choice.)
            preview.CurrentCell = null;
            preview.ClearSelection();
            preview.ResumeLayout();
        }

        // The columns share the width of the grid (not less than a readable width, then there is a scroll bar).
        private void FitPreviewColumns()
        {
            if (preview.Columns.Count == 0)
            {
                return;
            }

            int width = Math.Max(110, preview.ClientSize.Width / preview.Columns.Count);

            foreach (DataGridViewColumn column in preview.Columns)
            {
                column.Width = width;
            }
        }
    }
}
