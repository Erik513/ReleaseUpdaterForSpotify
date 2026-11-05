using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ErikwnkCore;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Localization;
using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Core.Services;

/// <summary>
/// Checks a hand-tracked list of not-yet-released songs against Spotify's
/// search, to catch ones that have since been published. Matching is
/// fuzzy on purpose - a hand-typed title or artist rarely matches
/// Spotify's own spelling exactly, so this tolerates typos and small
/// wording differences (see <see cref="SimilarityThreshold"/>) rather than
/// requiring an exact match. Candidates that clear the threshold are
/// still labelled <see cref="MatchConfidence.Similar"/> rather than
/// <see cref="MatchConfidence.Confident"/>, so the report can flag which
/// ones are worth a manual glance.
/// </summary>
public sealed class UnreleasedSongChecker
{
    // How close (0-1, from the Levenshtein-based ratio in SimilarityRatio)
    // a title/artist needs to be to count as a match at all. Picked as a
    // moderate middle ground: tolerates the odd typo or missing word
    // without also matching songs that are actually just different.
    private const double SimilarityThreshold = 0.8;

    private readonly ISpotifyGateway spotify;
    private readonly IUnreleasedSongsReportWriter reportWriter;

    public UnreleasedSongChecker(
        ISpotifyGateway spotify,
        IUnreleasedSongsReportWriter reportWriter)
    {
        this.spotify = spotify;
        this.reportWriter = reportWriter;
    }

    public async Task<UnreleasedSongsCheckResult> RunAsync(
        IReadOnlyList<TrackedSong> songs,
        IProgress<ReleaseProgress>? progress,
        CancellationToken cancellationToken)
    {
        List<SongMatchResult> results = new();
        string searchingStatus = AppLocalization.Get(AppStrings.UnreleasedSearching);

        for (int index = 0; index < songs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TrackedSong song = songs[index];

            progress?.Report(new ReleaseProgress(
                searchingStatus,
                index + 1,
                songs.Count,
                AppLocalization.Get(
                    AppStrings.UnreleasedStatusSearchingFormat,
                    index + 1,
                    songs.Count,
                    song.Title)));

            IReadOnlyList<SpotifyTrackItem> candidates =
                await spotify.SearchTrackByTitleArtistAsync(
                    song.Title,
                    song.Artist,
                    cancellationToken);

            (SpotifyTrackItem? match, MatchConfidence confidence) = FindBestMatch(song, candidates);

            results.Add(new SongMatchResult(song, match, confidence));
        }

        int foundCount = results.Count(result => result.IsFound);
        string reportPath = reportWriter.WriteReport(results);

        return new UnreleasedSongsCheckResult(results.Count, foundCount, reportPath);
    }

    // Scores every candidate rather than stopping at the first one that
    // clears the threshold - fuzzy matching means several candidates can
    // plausibly qualify (e.g. a remix and the original), and the closest
    // one is what's actually worth reporting.
    private static (SpotifyTrackItem? Match, MatchConfidence Confidence) FindBestMatch(
        TrackedSong song,
        IReadOnlyList<SpotifyTrackItem> candidates)
    {
        SpotifyTrackItem? bestMatch = null;
        MatchConfidence bestConfidence = MatchConfidence.None;
        double bestScore = -1;

        foreach (SpotifyTrackItem candidate in candidates)
        {
            (MatchConfidence confidence, double score) = ScoreCandidate(song, candidate);

            if (confidence == MatchConfidence.None)
            {
                continue;
            }

            if (confidence > bestConfidence || (confidence == bestConfidence && score > bestScore))
            {
                bestMatch = candidate;
                bestConfidence = confidence;
                bestScore = score;
            }
        }

        return (bestMatch, bestConfidence);
    }

    private static (MatchConfidence Confidence, double Score) ScoreCandidate(
        TrackedSong song,
        SpotifyTrackItem candidate)
    {
        string normalizedTitle = Normalize(song.Title);
        string normalizedArtist = Normalize(song.Artist);

        if (normalizedTitle.Length == 0 || normalizedArtist.Length == 0)
        {
            return (MatchConfidence.None, 0);
        }

        string candidateTitle = Normalize(candidate.Title);
        double titleSimilarity = TitleSimilarity(song.Title, candidate.Title);

        if (titleSimilarity < SimilarityThreshold)
        {
            return (MatchConfidence.None, 0);
        }

        string[] artistWords = Words(song.Artist);
        bool exactArtistMatch = false;
        double bestArtistSimilarity = 0;

        foreach (SpotifyArtist artist in candidate.Artists)
        {
            string candidateArtist = Normalize(artist.Name);

            if (candidateArtist.Length == 0)
            {
                continue;
            }

            // A match of whole words (either direction) still counts as exact -
            // it's how "The Weeknd" matches a candidate crediting just
            // "Weeknd", or one crediting "Weeknd, Playboi Carti". Whole words,
            // not just any substring: "Alex" must not match a credit of "Lex"
            // or "A".
            if (candidateArtist == normalizedArtist ||
                ContainsWords(Words(artist.Name), artistWords) ||
                ContainsWords(artistWords, Words(artist.Name)))
            {
                exactArtistMatch = true;
            }

            bestArtistSimilarity = Math.Max(
                bestArtistSimilarity,
                SimilarityRatio(normalizedArtist, candidateArtist));
        }

        if (!exactArtistMatch && bestArtistSimilarity < SimilarityThreshold)
        {
            return (MatchConfidence.None, 0);
        }

        bool isExactTitle = candidateTitle == normalizedTitle;
        MatchConfidence confidence = isExactTitle && exactArtistMatch
            ? MatchConfidence.Confident
            : MatchConfidence.Similar;

        double artistSimilarity = exactArtistMatch ? 1.0 : bestArtistSimilarity;
        double score = (titleSimilarity + artistSimilarity) / 2.0;

        return (confidence, score);
    }

    /// <summary>
    /// How alike two already-normalized strings are, from 0 (nothing in
    /// common) to 1 (identical) - one minus the Levenshtein edit distance
    /// relative to the longer string's length, so a typo or two in a long
    /// title costs less than the same edit distance would in a short one.
    /// </summary>
    private static double SimilarityRatio(string a, string b)
    {
        int maxLength = Math.Max(a.Length, b.Length);

        if (maxLength == 0)
        {
            return 1.0;
        }

        return 1.0 - ((double)LevenshteinDistance(a, b) / maxLength);
    }

    private static int LevenshteinDistance(string a, string b)
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
    private static double TitleSimilarity(string trackedTitle, string candidateTitle)
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

    // Removes (...) and [...] parts, a " - ..." suffix and a "feat./ft./featuring ..." tail.
    // ("with" is deliberately not one of them: "Dancing with Myself" is a title, not a credit.)
    private static string StripExtras(string title)
    {
        string result = BracketedPart.Replace(title, " ");

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

    private static readonly Regex BracketedPart =
        new(@"\([^)]*\)|\[[^\]]*\]", RegexOptions.Compiled);

    private static readonly Regex FeatureCredit =
        new(@"\s(feat|ft|featuring)\b\.?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // A shorter artist name only counts as "contained" in a longer one from this many
    // letters on; a one- or two-letter credit ("A", "Ke") would match far too much.
    private const int MinContainedNameLength = 3;

    /// <summary>
    /// Whether <paramref name="shorter"/> appears as a run of whole words
    /// inside <paramref name="longer"/> ("weeknd" in "the weeknd"). Equal
    /// word lists count, too short a name does not (see MinContainedNameLength)
    /// unless the lists are equal.
    /// </summary>
    private static bool ContainsWords(string[] longer, string[] shorter)
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
    private static string[] Words(string value)
    {
        List<string> words = new();
        StringBuilder current = new();

        foreach (char character in value.ToLower(CultureInfo.InvariantCulture))
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
    /// Lowercases and strips everything but letters/digits, so spelling
    /// differences in punctuation, spacing, or casing (the most common
    /// mismatch between a hand-typed title and Spotify's own) don't block
    /// an otherwise-correct match. A text with no letters or digits at all
    /// (a band called "!!!") keeps its symbols, minus spaces, instead of
    /// becoming empty - an empty text could never be matched.
    /// </summary>
    private static string Normalize(string value)
    {
        StringBuilder builder = new(value.Length);

        foreach (char character in value.ToLower(CultureInfo.InvariantCulture))
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
