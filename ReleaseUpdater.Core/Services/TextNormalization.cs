using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ReleaseUpdater.Core.Services;

/// <summary>Text comparison for titles and artists: normalizing (case, accents, extras), word splitting and similarity.</summary>
internal static class TextNormalization
{
    // How close (0-1, from the Levenshtein-based ratio in SimilarityRatio)
    // a title/artist needs to be to count as a match at all. Picked as a
    // moderate middle ground: tolerates the odd typo or missing word
    // without also matching songs that are actually just different.
    internal const double SimilarityThreshold = 0.8;

    /// <summary>
    /// How alike two already-normalized strings are, from 0 (nothing in
    /// common) to 1 (identical) - one minus the Levenshtein edit distance
    /// relative to the longer string's length, so a typo or two in a long
    /// title costs less than the same edit distance would in a short one.
    /// </summary>
    internal static double SimilarityRatio(string a, string b)
    {
        int maxLength = Math.Max(a.Length, b.Length);

        if (maxLength == 0)
        {
            return 1.0;
        }

        return 1.0 - ((double)LevenshteinDistance(a, b) / maxLength);
    }

    internal static int LevenshteinDistance(string a, string b)
    {
        int[] previousRow = new int[b.Length + 1];
        int[] currentRow = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++)
        {
            previousRow[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            currentRow[0] = i;

            for (int j = 1; j <= b.Length; j++)
            {
                int substitutionCost = a[i - 1] == b[j - 1] ? 0 : 1;
                currentRow[j] = Math.Min(
                    Math.Min(currentRow[j - 1] + 1, previousRow[j] + 1),
                    previousRow[j - 1] + substitutionCost);
            }

            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[b.Length];
    }

    /// <summary>
    /// How alike two titles are. Compares the whole titles, and also the
    /// titles without their "extras" (a bracketed part, "- Remastered 2011",
    /// "feat. X"), because Spotify's title often carries an extra that a
    /// hand-typed one lacks, or the other way round - and takes the better
    /// of the two. A match that is only close because of the second
    /// comparison is never an exact title (see ScoreCandidate), so it is
    /// still reported as "Similar", for a manual glance.
    /// </summary>
    internal static double TitleSimilarity(string trackedTitle, string candidateTitle)
    {
        double whole = SimilarityRatio(Normalize(trackedTitle), Normalize(candidateTitle));

        string trackedCore = Normalize(StripExtras(trackedTitle));
        string candidateCore = Normalize(StripExtras(candidateTitle));

        if (trackedCore.Length == 0 || candidateCore.Length == 0)
        {
            return whole;
        }

        return Math.Max(whole, SimilarityRatio(trackedCore, candidateCore));
    }

    // Removes (...) and [...] parts, a note after "->", a " - ..." suffix and a "feat./ft./featuring ..." tail.
    // ("with" is deliberately not one of them: "Dancing with Myself" is a title, not a credit.)
    internal static string StripExtras(string title)
    {
        string result = BracketedPart.Replace(title, " ");

        // A note of the user behind the title ("-> 10:43", a time of a set): not part of the song.
        int arrow = result.IndexOf("->", StringComparison.Ordinal);

        if (arrow >= 0)
        {
            result = result[..arrow];
        }

        int dash = result.IndexOf(" - ", StringComparison.Ordinal);

        if (dash >= 0)
        {
            result = result[..dash];
        }

        Match credit = FeatureCredit.Match(result);

        if (credit.Success)
        {
            result = result[..credit.Index];
        }

        return result;
    }

    internal static readonly Regex BracketedPart =
        new(@"\([^)]*\)|\[[^\]]*\]", RegexOptions.Compiled);

    internal static readonly Regex FeatureCredit =
        new(@"\s(feat|ft|featuring)\b\.?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // A shorter artist name only counts as "contained" in a longer one from this many
    // letters on; a one- or two-letter credit ("A", "Ke") would match far too much.
    internal const int MinContainedNameLength = 3;

    /// <summary>
    /// Whether <paramref name="shorter"/> appears as a run of whole words
    /// inside <paramref name="longer"/> ("weeknd" in "the weeknd"). Equal
    /// word lists count, too short a name does not (see MinContainedNameLength)
    /// unless the lists are equal.
    /// </summary>
    internal static bool ContainsWords(string[] longer, string[] shorter)
    {
        if (shorter.Length == 0 || shorter.Length > longer.Length)
        {
            return false;
        }

        if (shorter.Length == longer.Length)
        {
            return shorter.SequenceEqual(longer, StringComparer.Ordinal);
        }

        if (string.Concat(shorter).Length < MinContainedNameLength)
        {
            return false;
        }

        for (int start = 0; start + shorter.Length <= longer.Length; start++)
        {
            bool all = true;

            for (int offset = 0; offset < shorter.Length && all; offset++)
            {
                all = string.Equals(longer[start + offset], shorter[offset], StringComparison.Ordinal);
            }

            if (all)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The lowercased words of a name (runs of letters/digits). A name with
    /// no letters or digits at all ("!!!") is one word, as it is.
    /// </summary>
    internal static string[] Words(string value)
    {
        List<string> words = new();
        StringBuilder current = new();

        foreach (char character in FoldDiacritics(value.ToLower(CultureInfo.InvariantCulture)))
        {
            if (char.IsLetterOrDigit(character))
            {
                current.Append(character);
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        if (words.Count == 0)
        {
            string fallback = Normalize(value);

            if (fallback.Length > 0)
            {
                words.Add(fallback);
            }
        }

        return words.ToArray();
    }

    /// <summary>
    /// The letters without their accents (the text is already lowercase): "beyoncé" is "beyonce", "mötley crüe" is "motley crue", and the
    /// letters that are no accented ones but are written the other way in a plain alphabet ("pæde" is "paede", "ø" is "o", "ß" is "ss").
    /// Spotify and a hand-typed list rarely agree on those.
    /// </summary>
    internal static string FoldDiacritics(string lowercase)
    {
        StringBuilder builder = new(lowercase.Length);

        foreach (char character in lowercase.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            switch (character)
            {
                case 'æ': builder.Append("ae"); break;
                case 'œ': builder.Append("oe"); break;
                case 'ø': builder.Append('o'); break;
                case 'ß': builder.Append("ss"); break;
                case 'đ': builder.Append('d'); break;
                case 'ð': builder.Append('d'); break;
                case 'ł': builder.Append('l'); break;
                case 'þ': builder.Append("th"); break;
                case 'ı': builder.Append('i'); break;
                default: builder.Append(character); break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Lowercases and strips everything but letters/digits, so spelling
    /// differences in punctuation, spacing, or casing (the most common
    /// mismatch between a hand-typed title and Spotify's own) don't block
    /// an otherwise-correct match. A text with no letters or digits at all
    /// (a band called "!!!") keeps its symbols, minus spaces, instead of
    /// becoming empty - an empty text could never be matched.
    /// </summary>
    internal static string Normalize(string value)
    {
        StringBuilder builder = new(value.Length);

        foreach (char character in FoldDiacritics(value.ToLower(CultureInfo.InvariantCulture)))
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        if (builder.Length > 0)
        {
            return builder.ToString();
        }

        return string.Concat(value.Where(character => !char.IsWhiteSpace(character)));
    }
}
