using System.Text.RegularExpressions;

namespace ReleaseUpdater.Core.Services;

/// <summary>What an import does with the songs that are in the list already.</summary>
public enum ImportMode
{
    /// <summary>The new songs are added behind the list; songs that are in it already stay where they are.</summary>
    Add,

    /// <summary>The list becomes the imported songs, in the order of the file (what was in it is dropped).</summary>
    Replace
}

/// <summary>
/// Which columns of an imported table hold the title and the artist (and the link, when there is one), and whether the first row is a header.
/// </summary>
public sealed record ImportColumnChoice(int TitleColumn, int ArtistColumn, bool HasHeader, int? LinkColumn = null);

/// <summary>
/// A table that was read from a file (CSV, Excel, Word) or from the clipboard: rows of texts, not yet songs. The user may have a
/// table with more columns than the two the app needs (a number in the first column, a year, a note): <see cref="GuessColumns"/>
/// proposes two of them, and the user can choose others.
/// </summary>
public sealed class ImportTable
{
    private static readonly string[] TitleWords = { "title", "titel", "song", "songtitel", "track", "lied", "name" };
    private static readonly string[] ArtistWords = { "artist", "interpret", "künstler", "kuenstler", "band", "sänger", "performer" };
    private static readonly string[] LinkWords = { "link", "links", "url", "quelle", "source", "website" };

    // A column of numbers (a numbering "1", "2.", "#3") is not what is looked for.
    private static readonly Regex NumberLike = new(@"^[\s#]*\d+[.)]?\s*$", RegexOptions.Compiled);

    /// <param name="rows">The texts of the cells.</param>
    /// <param name="hyperlinks">
    /// The addresses that the cells link to, where they do (a Word or Excel cell with a text and a hyperlink behind it): the same shape as
    /// <paramref name="rows"/>, null for a cell without one; null when the table has none at all.
    /// </param>
    public ImportTable(IReadOnlyList<IReadOnlyList<string>> rows, IReadOnlyList<IReadOnlyList<string?>>? hyperlinks = null)
    {
        Rows = rows;
        Hyperlinks = hyperlinks;
    }

    public IReadOnlyList<IReadOnlyList<string?>>? Hyperlinks { get; }

    /// <summary>The address behind the text of a cell (a hyperlink of Word or Excel), or null.</summary>
    public string? Hyperlink(int row, int column)
    {
        if (Hyperlinks is null || row < 0 || row >= Hyperlinks.Count || column < 0 || column >= Hyperlinks[row].Count)
        {
            return null;
        }

        string? address = Hyperlinks[row][column];

        return string.IsNullOrWhiteSpace(address) ? null : address.Trim();
    }

    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }

    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(row => row.Count);

    /// <summary>True when there is no text at all in the table.</summary>
    public bool IsEmpty => Rows.All(row => row.All(string.IsNullOrWhiteSpace));

    public string Cell(int row, int column) =>
        row >= 0 && row < Rows.Count && column >= 0 && column < Rows[row].Count ? Rows[row][column] : string.Empty;

    /// <summary>
    /// True for a table that is exactly what the app writes: up to three columns, every cell of the first row is a name of the title, the artist
    /// or the link (Title / Titel, Artist / Interpret, Link / URL ...), the title and the artist are both there, and each name once. No
    /// question about the columns is needed for such a table, whatever their order.
    /// </summary>
    public bool HasRecognizedHeader()
    {
        if (Rows.Count == 0 || ColumnCount > 3 || ColumnCount < 2)
        {
            return false;
        }

        IReadOnlyList<string> header = Rows[0];

        if (header.Count != ColumnCount || header.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        int titles = header.Count(cell => IsOneOf(cell, TitleWords));
        int artists = header.Count(cell => IsOneOf(cell, ArtistWords));
        int links = header.Count(cell => IsOneOf(cell, LinkWords));

        return titles == 1 && artists == 1 && links <= 1 && titles + artists + links == header.Count;
    }

    /// <summary>
    /// The proposal for the columns: from the header when the first row names them (Title / Titel, Artist / Interpret, Link / URL ...), else the
    /// first two columns that are neither empty nor numbers nor links; the column of links is the one that is named so, or the one that is
    /// full of addresses.
    /// </summary>
    public ImportColumnChoice GuessColumns()
    {
        int columns = Math.Max(2, ColumnCount);
        bool hasHeader = Rows.Count > 0 &&
            Rows[0].Any(cell => IsOneOf(cell, TitleWords) || IsOneOf(cell, ArtistWords) || IsOneOf(cell, LinkWords));
        int firstDataRow = hasHeader ? 1 : 0;

        int? titleByHeader = hasHeader ? FindColumn(Rows[0], TitleWords) : null;
        int? artistByHeader = hasHeader ? FindColumn(Rows[0], ArtistWords) : null;
        int? link = (hasHeader ? FindColumn(Rows[0], LinkWords) : null) ?? FindLinkColumn(firstDataRow, columns);

        List<int> candidates = Enumerable.Range(0, columns)
            .Where(column => column != link && LooksLikeText(column, firstDataRow))
            .ToList();

        int title = titleByHeader ?? FirstCandidate(candidates, exclude: artistByHeader, columns);
        int artist = artistByHeader ?? FirstCandidate(candidates, exclude: title, columns);

        // (Two names for the same column cannot be: take the next one.)
        if (artist == title)
        {
            artist = FirstCandidate(candidates, exclude: title, columns);
        }

        // (A column cannot be two things.)
        if (link == title || link == artist)
        {
            link = null;
        }

        return new ImportColumnChoice(title, artist, hasHeader, link);
    }

    // The column of the links. First one in which most of the cells that have something in them look like an address; else (a Word table
    // with a column of short texts - "Link", "YouTube" - that each have a hyperlink behind them) the last column in which most of the
    // cells have a hyperlink and the texts are short: a column of titles that are all links is not meant.
    private int? FindLinkColumn(int firstDataRow, int columns)
    {
        for (int column = 0; column < columns; column++)
        {
            List<string> values = ValuesOfColumn(column, firstDataRow);

            if (values.Count > 0 && values.Count(TrackedSongLinks.LooksLikeLink) * 5 >= values.Count * 3)
            {
                return column;
            }
        }

        int? byHyperlink = null;

        for (int column = 0; column < columns; column++)
        {
            List<string> values = ValuesOfColumn(column, firstDataRow);
            int linked = Enumerable.Range(firstDataRow, Math.Max(0, Rows.Count - firstDataRow)).Count(row => Hyperlink(row, column) is not null);

            if (values.Count > 0 && linked * 5 >= values.Count * 3 && values.Average(value => value.Trim().Length) <= ShortLinkTextLength)
            {
                byHyperlink = column;
            }
        }

        return byHyperlink;
    }

    // The texts of the cells of a column that have something in them (below the header).
    private List<string> ValuesOfColumn(int column, int firstDataRow)
    {
        List<string> values = new();

        for (int row = firstDataRow; row < Rows.Count; row++)
        {
            string cell = Cell(row, column);

            if (!string.IsNullOrWhiteSpace(cell))
            {
                values.Add(cell);
            }
        }

        return values;
    }

    // A text like "Link", "Quelle", "YouTube", "SoundCloud set 2": not a title.
    private const int ShortLinkTextLength = 18;

    private bool LooksLikeText(int column, int firstDataRow)
    {
        List<string> values = new();

        for (int row = firstDataRow; row < Rows.Count; row++)
        {
            string cell = Cell(row, column);

            if (!string.IsNullOrWhiteSpace(cell))
            {
                values.Add(cell);
            }
        }

        return values.Count > 0 && !values.All(value => NumberLike.IsMatch(value));
    }

    // The first column that looks like text and is not the one to leave out; failing that, the first column that is not left out.
    private static int FirstCandidate(List<int> candidates, int? exclude, int columns)
    {
        foreach (int column in candidates)
        {
            if (column != exclude)
            {
                return column;
            }
        }

        for (int column = 0; column < columns; column++)
        {
            if (column != exclude)
            {
                return column;
            }
        }

        return 0;
    }

    private static int? FindColumn(IReadOnlyList<string> header, string[] words)
    {
        for (int column = 0; column < header.Count; column++)
        {
            if (IsOneOf(header[column], words))
            {
                return column;
            }
        }

        return null;
    }

    private static bool IsOneOf(string cell, string[] words) =>
        words.Contains(cell.Trim(), StringComparer.OrdinalIgnoreCase);
}
