using System.Text.RegularExpressions;

namespace ReleaseUpdater.Core.Services;

/// <summary>Which columns of an imported table hold the title and the artist, and whether the first row is a header.</summary>
public sealed record ImportColumnChoice(int TitleColumn, int ArtistColumn, bool HasHeader);

/// <summary>
/// A table that was read from a file (CSV, Excel, Word) or from the clipboard: rows of texts, not yet songs. The user may have a
/// table with more columns than the two the app needs (a number in the first column, a year, a note): <see cref="GuessColumns"/>
/// proposes two of them, and the user can choose others.
/// </summary>
public sealed class ImportTable
{
    private static readonly string[] TitleWords = { "title", "titel", "song", "songtitel", "track", "lied", "name" };
    private static readonly string[] ArtistWords = { "artist", "interpret", "künstler", "kuenstler", "band", "sänger", "performer" };

    // A column of numbers (a numbering "1", "2.", "#3") is not what is looked for.
    private static readonly Regex NumberLike = new(@"^[\s#]*\d+[.)]?\s*$", RegexOptions.Compiled);

    public ImportTable(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        Rows = rows;
    }

    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }

    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(row => row.Count);

    /// <summary>True when there is no text at all in the table.</summary>
    public bool IsEmpty => Rows.All(row => row.All(string.IsNullOrWhiteSpace));

    public string Cell(int row, int column) =>
        row >= 0 && row < Rows.Count && column >= 0 && column < Rows[row].Count ? Rows[row][column] : string.Empty;

    /// <summary>
    /// The proposal for the columns: from the header when the first row names them (Title / Titel, Artist / Interpret ...), else the
    /// first two columns that are neither empty nor numbers.
    /// </summary>
    public ImportColumnChoice GuessColumns()
    {
        int columns = Math.Max(2, ColumnCount);
        bool hasHeader = Rows.Count > 0 &&
            Rows[0].Any(cell => IsOneOf(cell, TitleWords) || IsOneOf(cell, ArtistWords));
        int firstDataRow = hasHeader ? 1 : 0;

        int? titleByHeader = hasHeader ? FindColumn(Rows[0], TitleWords) : null;
        int? artistByHeader = hasHeader ? FindColumn(Rows[0], ArtistWords) : null;

        List<int> candidates = Enumerable.Range(0, columns)
            .Where(column => LooksLikeText(column, firstDataRow))
            .ToList();

        int title = titleByHeader ?? FirstCandidate(candidates, exclude: artistByHeader, columns);
        int artist = artistByHeader ?? FirstCandidate(candidates, exclude: title, columns);

        // (Two names for the same column cannot be: take the next one.)
        if (artist == title)
        {
            artist = FirstCandidate(candidates, exclude: title, columns);
        }

        return new ImportColumnChoice(title, artist, hasHeader);
    }

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
