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

            BestMatch best = FindBestMatch(song, candidates, allowSwapped: false);

            // The search of the fields found nothing that counts. A hand-typed entry has the fields the wrong way round, an artist in
            // the title, a name that is a little off - the search of the fields cannot find that, a search of free text can (it does not
            // care in which order the words come). Only the songs that need it get this one more request.
            if (best.Confidence < MatchConfidence.Similar)
            {
                IReadOnlyList<SpotifyTrackItem> freeText =
                    await spotify.SearchTracksAsync(BuildFreeTextQuery(song), cancellationToken);

                best = FindBestMatch(song, MergeCandidates(candidates, freeText), allowSwapped: true);
            }

            results.Add(new SongMatchResult(song, best.Track, best.Confidence, best.Hint));
        }

        int foundCount = results.Count(result => result.IsFound);
        int possibleCount = results.Count(result => result.IsPossible);
        string reportPath = reportWriter.WriteReport(results);

        return new UnreleasedSongsCheckResult(results.Count, foundCount, reportPath, possibleCount);
    }

    private readonly record struct Scored(MatchConfidence Confidence, double Score, MatchHint Hint);

    private readonly record struct BestMatch(
        SpotifyTrackItem? Track,
        MatchConfidence Confidence,
        MatchHint Hint,
        double Score,
        bool Swapped);

    // How close a name has to be for a partial match (only one of title and artist fits): a title that fits plus an artist that is at
    // least this close, or an artist that fits plus a title at least this close. A title that is specific enough (see IsSpecificTitle) is
    // a hint even with an artist that is quite different.
    private const double PartialArtistCloseness = 0.5;
    private const double PartialTitleCloseness = 0.6;

    // Scores every candidate rather than stopping at the first one that
    // clears the threshold - fuzzy matching means several candidates can
    // plausibly qualify (e.g. a remix and the original), and the closest
    // one is what's actually worth reporting. With allowSwapped the candidates are also tried with the title and the artist of the
    // entry the other way round; such a match is believed (never as "Confident") but marked, so that the report says what happened.
    private static BestMatch FindBestMatch(
        TrackedSong song,
        IReadOnlyList<SpotifyTrackItem> candidates,
        bool allowSwapped)
    {
        BestMatch best = new(null, MatchConfidence.None, MatchHint.None, -1, false);
        TrackedSong swapped = new(song.Artist, song.Title);
        bool trySwapped = allowSwapped && Normalize(song.Title) != Normalize(song.Artist);

        foreach (SpotifyTrackItem candidate in candidates)
        {
            Scored normal = ScoreCandidate(song, candidate);
            Consider(ref best, candidate, normal.Confidence, normal.Hint, normal.Score, swapped: false);

            if (trySwapped)
            {
                Scored other = ScoreCandidate(swapped, candidate);

                if (other.Confidence >= MatchConfidence.Similar)
                {
                    Consider(ref best, candidate, MatchConfidence.Similar, MatchHint.SwappedFields, other.Score, swapped: true);
                }
            }
        }

        return best;
    }

    // The better of the best so far and this candidate: the higher confidence; with the same one, a match of the entry as it is
    // before a swapped one, and then the higher score.
    private static void Consider(
        ref BestMatch best,
        SpotifyTrackItem candidate,
        MatchConfidence confidence,
        MatchHint hint,
        double score,
        bool swapped)
    {
        if (confidence == MatchConfidence.None)
        {
            return;
        }

        bool better;

        if (confidence != best.Confidence)
        {
            better = confidence > best.Confidence;
        }
        else if (best.Swapped != swapped)
        {
            better = best.Swapped;
        }
        else
        {
            better = score > best.Score;
        }

        if (better)
        {
            best = new BestMatch(candidate, confidence, hint, score, swapped);
        }
    }

    private static Scored ScoreCandidate(
        TrackedSong song,
        SpotifyTrackItem candidate)
    {
        string normalizedTitle = Normalize(song.Title);
        string normalizedArtist = Normalize(song.Artist);

        if (normalizedTitle.Length == 0 || normalizedArtist.Length == 0)
        {
            return new Scored(MatchConfidence.None, 0, MatchHint.None);
        }

        string candidateTitle = Normalize(candidate.Title);
        double titleSimilarity = TitleSimilarity(song.Title, candidate.Title);
        (bool exactArtistMatch, double bestArtistSimilarity) = CompareArtists(song.Artist, normalizedArtist, candidate);

        bool titleMatches = titleSimilarity >= SimilarityThreshold;
        bool artistMatches = exactArtistMatch || bestArtistSimilarity >= SimilarityThreshold;
        double artistSimilarity = exactArtistMatch ? 1.0 : bestArtistSimilarity;

        if (titleMatches && artistMatches)
        {
            bool isExactTitle = candidateTitle == normalizedTitle;
            MatchConfidence confidence = isExactTitle && exactArtistMatch
                ? MatchConfidence.Confident
                : MatchConfidence.Similar;

            return new Scored(confidence, (titleSimilarity + artistSimilarity) / 2.0, MatchHint.None);
        }

        // Only one of the two fits. That is not a match, but a hint when the other one is not far off (a misspelled artist, a title
        // with something added) - or when the title is so specific that it hardly belongs to anybody else.
        double partialScore = (titleSimilarity + artistSimilarity) / 2.0;

        if (titleMatches && (IsSpecificTitle(song.Title) || artistSimilarity >= PartialArtistCloseness))
        {
            return new Scored(MatchConfidence.Partial, partialScore, MatchHint.TitleOnly);
        }

        if (artistMatches && titleSimilarity >= PartialTitleCloseness)
        {
            return new Scored(MatchConfidence.Partial, partialScore, MatchHint.ArtistOnly);
        }

        return new Scored(MatchConfidence.None, 0, MatchHint.None);
    }

    // Whether the artist of the entry is credited on the candidate: exactly (whole words, either direction - "The Weeknd" is "Weeknd",
    // and one of "Weeknd, Playboi Carti"), and how close the closest credit is.
    private static (bool Exact, double Closest) CompareArtists(string artist, string normalizedArtist, SpotifyTrackItem candidate)
    {
        string[] artistWords = Words(artist);
        bool exactArtistMatch = false;
        double bestArtistSimilarity = 0;

        foreach (SpotifyArtist credited in candidate.Artists)
        {
            string candidateArtist = Normalize(credited.Name);

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
                ContainsWords(Words(credited.Name), artistWords) ||
                ContainsWords(artistWords, Words(credited.Name)))
            {
                exactArtistMatch = true;
            }

            bestArtistSimilarity = Math.Max(
                bestArtistSimilarity,
                SimilarityRatio(normalizedArtist, candidateArtist));
        }

        return (exactArtistMatch, bestArtistSimilarity);
    }

    // A title that hardly belongs to another song: three words or more, or a long one. "Sammy In My Room" is one; "Revolution", "One Day" and
    // "Heal" are not (there are dozens of them), so for those the artist has to be close as well.
    private static bool IsSpecificTitle(string title)
    {
        string core = StripExtras(title);
        string subject = core.Trim().Length > 0 ? core : title;

        return Words(subject).Length >= 3 || Normalize(subject).Length >= 14;
    }

    // The words for a search of free text: the title without its extras and the first artist. Both orders find the same song, so the
    // search does not depend on the fields being right.
    private static string BuildFreeTextQuery(TrackedSong song)
    {
        string core = StripExtras(song.Title).Trim();
        string title = core.Length > 0 ? core : song.Title.Trim();
        string artist = ArtistSeparator.Split(song.Artist.Trim(), 2)[0].Trim();

        return (title + " " + artist).Replace("\"", string.Empty).Trim();
    }

    private static readonly Regex ArtistSeparator =
        new(@"\s*(?:[,;&/]|\s(?:x|feat\.?|ft\.?|and|und|with)\s)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // The candidates of the search of the fields and of the search of free text, each track once.
    private static IReadOnlyList<SpotifyTrackItem> MergeCandidates(
        IReadOnlyList<SpotifyTrackItem> first,
        IReadOnlyList<SpotifyTrackItem> second)
    {
        List<SpotifyTrackItem> merged = new(first);
        HashSet<string> known = new(first.Select(track => track.Id), StringComparer.Ordinal);

        foreach (SpotifyTrackItem track in second)
        {
            if (known.Add(track.Id))
            {
                merged.Add(track);
            }
        }

        return merged;
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

    // Removes (...) and [...] parts, a note after "->", a " - ..." suffix and a "feat./ft./featuring ..." tail.
    // ("with" is deliberately not one of them: "Dancing with Myself" is a title, not a credit.)
    private static string StripExtras(string title)
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
    private static string FoldDiacritics(string lowercase)
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
    private static string Normalize(string value)
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
