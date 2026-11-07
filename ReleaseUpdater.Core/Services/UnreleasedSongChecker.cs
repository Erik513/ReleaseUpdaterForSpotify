using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ErikwnkCore;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Localization;
using ReleaseUpdater.Core.Models;
using static ReleaseUpdater.Core.Services.TextNormalization;
using static ReleaseUpdater.Core.Services.VersionRules;

namespace ReleaseUpdater.Core.Services;

/// <summary>
/// Checks a hand-tracked list of not-yet-released songs against Spotify's
/// search, to catch ones that have since been published. Matching is
/// fuzzy on purpose - a hand-typed title or artist rarely matches
/// Spotify's own spelling exactly, so this tolerates typos and small
/// wording differences (see <see cref="TextNormalization.SimilarityThreshold"/>) rather than
/// requiring an exact match. Candidates that clear the threshold are
/// still labelled <see cref="MatchConfidence.Similar"/> rather than
/// <see cref="MatchConfidence.Confident"/>, so the report can flag which
/// ones are worth a manual glance.
/// </summary>
public sealed class UnreleasedSongChecker
{
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

        // A remix, an edit or a version that is named in the entry ("(Gabss Edit)", "(2025 Version)") is another release than the original:
        // the original must not be taken for it - and the other way round, a remix is not the original that is tracked.
        if (!VersionsFit(song.Title, candidate.Title))
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

}
