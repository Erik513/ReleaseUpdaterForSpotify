using ErikwnkCore;
using ErikwnkWFUI;
using ReleaseUpdater.Core.Localization;

namespace ReleaseUpdaterGui
{
    /// <summary>
    /// The link column of the list of songs: no text in the list (it would make it wide), one small button per row. The column is bound to
    /// the link, but its text is never shown: the symbol is painted over the cell - the VALUE of the cell is the link itself, so that
    /// copying the cell copies the address and not a symbol. A double click opens the link (or asks for one when there is none), Ctrl +
    /// double click changes it (a right click is the grid's own, it has a menu). The form says which rows are songs and what to do.
    /// </summary>
    internal sealed class UnreleasedLinkColumn
    {
        public const string Name = "colLink";
        public const int Width = 52;

        private static readonly Font SymbolFont = new("Segoe UI Symbol", 10f);

        private readonly DataGridView grid;
        private readonly Func<int, bool> isSongRow;
        private readonly Func<int, string?> linkOf;
        private readonly Action<int> askForLink;
        private readonly Action<string?> openLink;

        /// <param name="grid">The grid the column is in (add <see cref="CreateColumn"/> to it first, then call <see cref="Attach"/>).</param>
        /// <param name="isSongRow">Whether a row is a song: not the header, not the empty row at the end of the list. Only such a row has a link.</param>
        /// <param name="linkOf">The link of the song in a row.</param>
        /// <param name="askForLink">Asks the user for the link of the song in a row.</param>
        /// <param name="openLink">Opens a link.</param>
        public UnreleasedLinkColumn(
            DataGridView grid,
            Func<int, bool> isSongRow,
            Func<int, string?> linkOf,
            Action<int> askForLink,
            Action<string?> openLink)
        {
            this.grid = grid;
            this.isSongRow = isSongRow;
            this.linkOf = linkOf;
            this.askForLink = askForLink;
            this.openLink = openLink;
        }

        /// <param name="dataPropertyName">The property of the row that holds the link.</param>
        public static DataGridViewTextBoxColumn CreateColumn(string dataPropertyName) => new()
        {
            Name = Name,
            DataPropertyName = dataPropertyName,
            HeaderText = AppLocalization.Get(AppStrings.UnreleasedColumnLink),
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Width = Width
        };

        /// <summary>Hooks up the tooltips, the symbol and the clicks (the column has to be in the grid).</summary>
        public void Attach()
        {
            // The header of the column says which clicks do what (the tooltip of the header cell).
            grid.Columns[Name]!.HeaderCell.ToolTipText = AppLocalization.Get(AppStrings.UnreleasedLinkHeaderTooltip);
            grid.CellFormatting += UpdateTooltip;
            grid.CellPainting += PaintSymbol;
            grid.CellMouseDoubleClick += OnDoubleClick;
        }

        private bool IsLinkCell(int rowIndex, int columnIndex) =>
            columnIndex >= 0 && isSongRow(rowIndex) && grid.Columns[columnIndex].Name == Name;

        // The tooltip of the small button of a row says what a double click does (the text of the cell is not touched: it is the link).
        private void UpdateTooltip(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (!IsLinkCell(e.RowIndex, e.ColumnIndex))
            {
                return;
            }

            string? link = linkOf(e.RowIndex);

            grid[e.ColumnIndex, e.RowIndex].ToolTipText = !string.IsNullOrWhiteSpace(link)
                ? AppLocalization.Get(AppStrings.UnreleasedLinkTooltipOpenFormat, link.Trim())
                : AppLocalization.Get(AppStrings.UnreleasedLinkTooltipAdd);
        }

        // The small button of a row: the symbol of a link, bright when the song has one and dim when not. The cell is painted without its text
        // (the address) and the symbol is drawn over it, so the cell keeps the link as its value.
        private void PaintSymbol(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            if (!IsLinkCell(e.RowIndex, e.ColumnIndex))
            {
                return;
            }

            e.Paint(e.ClipBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);

            bool hasLink = !string.IsNullOrWhiteSpace(linkOf(e.RowIndex));
            // (The text color of the cell, not a fixed one: the grid dims it while a search runs, and the symbol dims with it.)
            Color text = e.CellStyle!.ForeColor;
            Color color = hasLink ? text : Blend(text, e.CellStyle.BackColor, 0.7);

            TextRenderer.DrawText(
                e.Graphics!,
                UIStyles.Symbols.Link,
                SymbolFont,
                e.CellBounds,
                color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            e.Handled = true;
        }

        private void OnDoubleClick(object? sender, DataGridViewCellMouseEventArgs e)
        {
            // (Not the header, and not the empty row at the end of the list.)
            if (!IsLinkCell(e.RowIndex, e.ColumnIndex) || e.Button != MouseButtons.Left)
            {
                return;
            }

            string? link = linkOf(e.RowIndex);

            // A song without a link is asked for one; with a link, the double click opens it and Ctrl + double click changes it.
            if (string.IsNullOrWhiteSpace(link) || (Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                askForLink(e.RowIndex);
            }
            else
            {
                openLink(link);
            }
        }

        private static Color Blend(Color from, Color to, double toShare) =>
            Color.FromArgb(
                (int)(from.R + ((to.R - from.R) * toShare)),
                (int)(from.G + ((to.G - from.G) * toShare)),
                (int)(from.B + ((to.B - from.B) * toShare)));
    }
}
