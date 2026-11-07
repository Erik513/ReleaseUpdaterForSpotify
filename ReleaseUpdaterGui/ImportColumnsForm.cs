using ErikwnkCore;
using ErikwnkWFUI;
using ErikwnkWFUI.Forms;
using ReleaseUpdater.Core.Localization;
using ReleaseUpdater.Core.Services;

namespace ReleaseUpdaterGui
{
    /// <summary>
    /// Asks what the columns of an imported table are (a table from Excel or Word often has a number or a note in front of the title).
    /// Above each column of a few sample rows there is one choice: skip it, or title, artist, link. It starts with the proposal of
    /// <see cref="ImportTable.GuessColumns"/>; choosing a meaning that another column has takes it away from that one, so there is no wrong
    /// combination. A first row that looks like a header (it names the columns) is shown dimmed and not imported.
    /// </summary>
    public sealed class ImportColumnsForm : AppForm
    {
        private const int SampleRows = 6;
        private const int MinimumColumnWidth = 140;

        // What a column can be: the position in the list of a choice.
        private const int Skip = 0;
        private const int Title = 1;
        private const int Artist = 2;
        private const int LinkRole = 3;

        private readonly ImportTable table;
        private readonly bool hasHeader;
        private readonly ComboBox[] choices;
        private readonly Panel scrollPanel;
        private readonly TableLayoutPanel grid;
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
            ClientSize = new Size(640, 360);
            MinimumSize = new Size(480, 330);

            ImportColumnChoice guess = table.GuessColumns();
            hasHeader = guess.HasHeader;
            int columns = Math.Max(2, table.ColumnCount);

            TableLayoutPanel layout = UIStyles.TableLayoutPanels.CreateStandard(
                columnCount: 1,
                rowCount: 3,
                colors: UIStyles.Colors.BackgroundLight);
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(16);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            // One line that says what to do; the second says what happens with a header row, when there is one.
            Label intro = UIStyles.Labels.CreateStandard(
                AppLocalization.Get(AppStrings.ImportColumnsIntro) +
                (hasHeader ? Environment.NewLine + AppLocalization.Get(AppStrings.ImportColumnsHeaderSkipped) : string.Empty));
            intro.Dock = DockStyle.Fill;
            intro.AutoSize = false;

            // The choices over the sample rows: one table, so that each choice stands exactly over its column.
            grid = UIStyles.TableLayoutPanels.CreateTransparent(columnCount: columns, rowCount: 1 + SampleRows);
            grid.Dock = DockStyle.None;
            grid.Location = Point.Empty;

            for (int column = 0; column < columns; column++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            }

            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            for (int row = 0; row < SampleRows; row++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            }

            choices = new ComboBox[columns];

            for (int column = 0; column < columns; column++)
            {
                ComboBox choice = UIStyles.ComboBoxes.CreatePrimary();
                choice.Dock = DockStyle.Fill;
                choice.Margin = new Padding(2, 4, 2, 6);
                choice.Items.AddRange(new object[]
                {
                    AppLocalization.Get(AppStrings.ImportColumnsSkip),
                    AppLocalization.Get(AppStrings.UnreleasedColumnTitle),
                    AppLocalization.Get(AppStrings.UnreleasedColumnArtist),
                    AppLocalization.Get(AppStrings.UnreleasedColumnLink)
                });
                choice.SelectedIndex = column == guess.TitleColumn ? Title
                    : column == guess.ArtistColumn ? Artist
                    : column == guess.LinkColumn ? LinkRole
                    : Skip;

                int thisColumn = column;
                choice.SelectedIndexChanged += (_, _) => OnChoiceChanged(thisColumn);
                choices[column] = choice;
                grid.Controls.Add(choice, column, 0);
            }

            for (int row = 0; row < Math.Min(SampleRows, table.Rows.Count); row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    // (A cell with a hyperlink behind its text is marked: the text may only say "YouTube", the address is what is used.)
                    string text = table.Cell(row, column) + (table.Hyperlink(row, column) is null ? string.Empty : " ↗");

                    // One line per cell: a text that is too long moves back and forth while the mouse is not over it (the library's way, as
                    // in the status line) - it must not wrap into a second line, which makes the cell stand out of its row.
                    ErikwnkWFUI.Controls.LabelScroll scroll = ErikwnkWFUI.Controls.LabelScroll.BackAndForth(pauseOnHover: true);
                    Label cell = row == 0 && hasHeader
                        ? UIStyles.Labels.CreateMuted(text, scroll: scroll)
                        : UIStyles.Labels.CreateStandard(text, scroll: scroll);
                    cell.Dock = DockStyle.Fill;
                    cell.TextAlign = ContentAlignment.MiddleLeft;
                    cell.Margin = new Padding(6, 0, 6, 0);
                    grid.Controls.Add(cell, column, row + 1);
                }
            }

            scrollPanel = UIStyles.Panels.CreateTransparent();
            scrollPanel.Dock = DockStyle.Fill;
            scrollPanel.AutoScroll = true;
            scrollPanel.Controls.Add(grid);
            scrollPanel.Resize += (_, _) => FitGrid();

            lblHint = UIStyles.Labels.CreateStandard(string.Empty, UIStyles.Colors.Sets.Red);
            lblHint.Dock = DockStyle.Fill;
            lblHint.TextAlign = ContentAlignment.MiddleLeft;

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

            // The hint stands in the row of the buttons, at the left of them.
            TableLayoutPanel bottom = UIStyles.TableLayoutPanels.CreateTransparent(columnCount: 2, rowCount: 1);
            bottom.Dock = DockStyle.Fill;
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            bottom.Controls.Add(lblHint, 0, 0);
            bottom.Controls.Add(buttons, 1, 0);

            layout.Controls.Add(intro, 0, 0);
            layout.Controls.Add(scrollPanel, 0, 1);
            layout.Controls.Add(bottom, 0, 2);
            ContentPanel.Controls.Add(layout);

            FitGrid();
            UpdateState();
        }

        /// <summary>What the user chose for the columns (valid after OK).</summary>
        public ImportColumnChoice Choice
        {
            get
            {
                int title = ColumnWith(Title);
                int artist = ColumnWith(Artist);
                int link = ColumnWith(LinkRole);

                return new ImportColumnChoice(Math.Max(0, title), Math.Max(0, artist), hasHeader, link >= 0 ? link : null);
            }
        }

        // The column that has this meaning, -1 when none.
        private int ColumnWith(int role) => Array.FindIndex(choices, choice => choice.SelectedIndex == role);

        // A meaning is held by one column: choosing it here takes it away from the column that had it (that one is skipped now).
        private void OnChoiceChanged(int column)
        {
            if (updating)
            {
                return;
            }

            int role = choices[column].SelectedIndex;

            if (role != Skip)
            {
                updating = true;

                for (int other = 0; other < choices.Length; other++)
                {
                    if (other != column && choices[other].SelectedIndex == role)
                    {
                        choices[other].SelectedIndex = Skip;
                    }
                }

                updating = false;
            }

            UpdateState();
        }

        // The song needs a title and an artist; the rest is optional.
        private void UpdateState()
        {
            bool ready = ColumnWith(Title) >= 0 && ColumnWith(Artist) >= 0;
            btnOk.Enabled = ready;
            lblHint.Text = ready ? string.Empty : AppLocalization.Get(AppStrings.ImportColumnsNeedTitleAndArtist);
        }

        // As wide as the panel, but not narrower than the columns need (then the panel scrolls); as high as its rows.
        private void FitGrid()
        {
            int height = 40 + (SampleRows * 26);
            grid.Size = new Size(Math.Max(scrollPanel.ClientSize.Width, choices.Length * MinimumColumnWidth), height);
        }
    }
}
