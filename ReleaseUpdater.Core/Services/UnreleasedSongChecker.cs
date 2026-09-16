using System.Globalization;
using System.Text;
using ErikwnkCore;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Localization;
using ReleaseUpdater.Core.Models;

namespace ReleaseUpdater.Core.Services;

/// <summary>
/// Checks a hand-tracked list of not-yet-released songs against Spotify's
/// search, to catch ones that have since been published. Deliberately
/// conservative: a song is only reported "found" when both title and
/// artist match closely after normalization - anything else (no results,
/// or only loose/ambiguous candidates) is reported as "not found" rather
/// than surfaced for manual review, per the app's matching policy.
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

            SpotifyTrackItem? match = candidates.FirstOrDefault(
                candidate => IsConfidentMatch(song, candidate));

            results.Add(new SongMatchResult(song, match, match is not null));
        }

        int foundCount = results.Count(result => result.IsConfidentMatch);
        string reportPath = reportWriter.WriteReport(results);

        return new UnreleasedSongsCheckResult(results.Count, foundCount, reportPath);
    }

    private static bool IsConfidentMatch(TrackedSong song, SpotifyTrackItem candidate)
    {
        string normalizedTitle = Normalize(song.Title);
        string normalizedArtist = Normalize(song.Artist);

        if (normalizedTitle.Length == 0 ||
            normalizedArtist.Length == 0 ||
            Normalize(candidate.Title) != normalizedTitle)
        {
            return false;
        }

        return candidate.Artists.Any(artist =>
        {
            string candidateArtist = Normalize(artist.Name);

            return candidateArtist.Length > 0 &&
                (candidateArtist == normalizedArtist ||
                    candidateArtist.Contains(normalizedArtist, StringComparison.Ordinal) ||
                    normalizedArtist.Contains(candidateArtist, StringComparison.Ordinal));
        });
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
