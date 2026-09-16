using System.Globalization;
using System.Text;
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
        double titleSimilarity = SimilarityRatio(normalizedTitle, candidateTitle);

        if (titleSimilarity < SimilarityThreshold)
        {
            return (MatchConfidence.None, 0);
        }

        bool exactArtistMatch = false;
        double bestArtistSimilarity = 0;

        foreach (SpotifyArtist artist in candidate.Artists)
        {
            string candidateArtist = Normalize(artist.Name);

            if (candidateArtist.Length == 0)
            {
                continue;
            }

            // A substring match (either direction) still counts as exact -
            // it's how "The Weeknd" matches a candidate crediting just
            // "Weeknd", or one crediting "Weeknd, Playboi Carti".
            if (candidateArtist == normalizedArtist ||
                candidateArtist.Contains(normalizedArtist, StringComparison.Ordinal) ||
                normalizedArtist.Contains(candidateArtist, StringComparison.Ordinal))
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
    /// Lowercases and strips everything but letters/digits, so spelling
    /// differences in punctuation, spacing, or casing (the most common
    /// mismatch between a hand-typed title and Spotify's own) don't block
    /// an otherwise-correct match.
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

        return builder.ToString();
    }
}
