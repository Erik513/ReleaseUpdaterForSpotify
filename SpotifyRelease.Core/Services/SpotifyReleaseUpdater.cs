using SpotifyRelease.Core.Abstractions;
using SpotifyRelease.Core.Models;

namespace SpotifyRelease.Core.Services;

public sealed class SpotifyReleaseUpdater
{
    private readonly ISpotifyGateway spotify;
    private readonly ISettingsStore settingsStore;
    private readonly IReportWriter reportWriter;
    private readonly IReleaseClock clock;

    public SpotifyReleaseUpdater(
        ISpotifyGateway spotify,
        ISettingsStore settingsStore,
        IReportWriter reportWriter,
        IReleaseClock clock)
    {
        this.spotify = spotify;
        this.settingsStore = settingsStore;
        this.reportWriter = reportWriter;
        this.clock = clock;
    }

    /// <summary>
    /// Runs the full update: find releases, update the playlist, and write the report.
    /// </summary>
    public async Task<PlaylistUpdateResult> RunAsync(
        IProgress<ReleaseProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (spotify is ISpotifyRequestDiagnostics diagnostics)
        {
            diagnostics.Progress = progress;
        }

        ReleaseSettings settings = settingsStore.Load().Normalize();

        ReportStatus(progress, "Loading followed artists");
        IReadOnlyList<SpotifyArtist> allArtists =
            await spotify.GetFollowedArtistsAsync(cancellationToken);

        IReadOnlyList<SpotifyArtist> artists = ApplyLogicTestArtistLimit(
            allArtists,
            progress);

        HashSet<string> followedArtistIds = artists
            .Select(artist => artist.Id)
            .ToHashSet(StringComparer.Ordinal);

        ReportMessage(progress, $"Followed artists: {allArtists.Count}");
        ReportMessage(progress, $"Processed artists: {artists.Count}");
        ReportMessage(progress, $"Lookback: last {settings.ReleaseLookbackDays} days");

        IReadOnlyList<ReleaseCandidate> releases =
            await GetRecentReleasesAsync(
                artists,
                settings.ReleaseLookbackDays,
                progress,
                cancellationToken);

        releases = RemoveDuplicateReleases(releases)
            .OrderByDescending(release => release.ReleaseDate)
            .ToList();

        releases = ApplyLogicTestReleaseLimit(releases, progress);

        IReadOnlyList<ReleaseTrack> tracks =
            await GetTracksFromReleasesAsync(
                releases,
                followedArtistIds,
                progress,
                cancellationToken);

        IReadOnlyList<ReleaseTrack> collaborationTracks =
            await GetCollaborationTracksAsync(
                artists,
                settings.ReleaseLookbackDays,
                progress,
                cancellationToken);

        List<ReleaseTrack> allTracks = tracks
            .Concat(collaborationTracks)
            .ToList();

        (IReadOnlyList<ReleaseTrack> uniqueTracks, IReadOnlyList<ReleaseTrack> duplicates) =
            RemoveDuplicateTracks(allTracks);

        ReportMessage(progress, $"Found releases: {releases.Count}");
        ReportMessage(progress, $"Found songs: {allTracks.Count}");
        ReportMessage(progress, $"After duplicate filter: {uniqueTracks.Count}");
        ReportMessage(progress, $"Duplicates: {duplicates.Count}");

        if (uniqueTracks.Count == 0)
        {
            ReportStatus(progress, "Checking playlist");
            string? existingPlaylistId = await GetPlaylistIdAsync(
                settings,
                progress,
                createIfMissing: false,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(existingPlaylistId))
            {
                ReportStatus(progress, "Updating playlist");
                await spotify.ReplacePlaylistTracksAsync(
                    existingPlaylistId,
                    Array.Empty<string>(),
                    cancellationToken);
            }

            ReportStatus(progress, "No new songs");
            string reportPath = reportWriter.WriteReport(
                uniqueTracks,
                duplicates,
                settings.ReleaseLookbackDays);

            return new PlaylistUpdateResult(
                artists.Count,
                releases.Count,
                allTracks.Count,
                uniqueTracks.Count,
                duplicates.Count,
                reportPath);
        }

        ReportStatus(progress, "Checking playlist");
        string playlistId = await GetOrCreatePlaylistIdAsync(
            settings,
            progress,
            cancellationToken);

        ReportStatus(progress, "Updating playlist");
        await spotify.ReplacePlaylistTracksAsync(
            playlistId,
            uniqueTracks.Select(track => track.Uri).ToList(),
            cancellationToken);

        string finalReportPath = reportWriter.WriteReport(
            uniqueTracks,
            duplicates,
            settings.ReleaseLookbackDays);

        ReportStatus(progress, "Done");
        ReportMessage(
            progress,
            uniqueTracks.Count == 1
                ? "Done. 1 song was added to the playlist."
                : $"Done. {uniqueTracks.Count} songs were added to the playlist.");

        return new PlaylistUpdateResult(
            artists.Count,
            releases.Count,
            allTracks.Count,
            uniqueTracks.Count,
            duplicates.Count,
            finalReportPath);
    }

    /// <summary>
    /// Limits artists in logic-test mode to keep Spotify requests low.
    /// </summary>
    private static IReadOnlyList<SpotifyArtist> ApplyLogicTestArtistLimit(
        IReadOnlyList<SpotifyArtist> artists,
        IProgress<ReleaseProgress>? progress)
    {
        if (!ReleaseDefaults.LogicTestModeEnabled ||
            artists.Count <= ReleaseDefaults.LogicTestArtistLimit)
        {
            return artists;
        }

        ReportMessage(
            progress,
            $"Logic test mode: artists limited to {ReleaseDefaults.LogicTestArtistLimit}.");

        return artists
            .Take(ReleaseDefaults.LogicTestArtistLimit)
            .ToList();
    }

    /// <summary>
    /// Limits releases in logic-test mode while still writing found songs to the playlist.
    /// </summary>
    private static IReadOnlyList<ReleaseCandidate> ApplyLogicTestReleaseLimit(
        IReadOnlyList<ReleaseCandidate> releases,
        IProgress<ReleaseProgress>? progress)
    {
        if (!ReleaseDefaults.LogicTestModeEnabled ||
            releases.Count <= ReleaseDefaults.LogicTestReleaseLimit)
        {
            return releases;
        }

        ReportMessage(
            progress,
            $"Logic test mode: releases limited to {ReleaseDefaults.LogicTestReleaseLimit}.");

        return releases
            .Take(ReleaseDefaults.LogicTestReleaseLimit)
            .ToList();
    }

    /// <summary>
    /// Finds albums and singles from followed artists in the selected lookback window.
    /// </summary>
    private async Task<IReadOnlyList<ReleaseCandidate>> GetRecentReleasesAsync(
        IReadOnlyList<SpotifyArtist> artists,
        int days,
        IProgress<ReleaseProgress>? progress,
        CancellationToken cancellationToken)
    {
        List<ReleaseCandidate> releases = new();
        DateOnly cutoffDate = clock.Today.AddDays(-days);

        ReportStatus(progress, "Searching releases");

        for (int index = 0; index < artists.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SpotifyArtist artist = artists[index];
            ReportProgress(progress, "Searching releases", index + 1, artists.Count);

            IReadOnlyList<SpotifyAlbum> albums =
                await spotify.GetArtistAlbumsAsync(artist.Id, cancellationToken);

            foreach (SpotifyAlbum album in albums)
            {
                if (album.ReleaseDate is null || album.ReleaseDate < cutoffDate)
                {
                    continue;
                }

                releases.Add(new ReleaseCandidate(
                    artist.Name,
                    album.Name,
                    album.ReleaseDate.Value,
                    album.Id));
            }
        }

        return releases;
    }

    /// <summary>
    /// Loads tracks from matching releases and keeps the album release date.
    /// </summary>
    private async Task<IReadOnlyList<ReleaseTrack>> GetTracksFromReleasesAsync(
        IReadOnlyList<ReleaseCandidate> releases,
        HashSet<string> followedArtistIds,
        IProgress<ReleaseProgress>? progress,
        CancellationToken cancellationToken)
    {
        List<ReleaseTrack> tracks = new();

        ReportStatus(progress, "Loading songs");

        for (int index = 0; index < releases.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ReleaseCandidate release = releases[index];
            ReportProgress(progress, "Loading songs", index + 1, releases.Count);

            IReadOnlyList<SpotifyTrackItem> albumTracks =
                await spotify.GetAlbumTracksAsync(release.AlbumId, cancellationToken);

            foreach (SpotifyTrackItem track in albumTracks)
            {
                HashSet<string> trackArtistIds = track.Artists
                    .Select(artist => artist.Id)
                    .ToHashSet(StringComparer.Ordinal);

                bool isFollowedArtistTrack = trackArtistIds
                    .Any(followedArtistIds.Contains);

                tracks.Add(new ReleaseTrack(
                    track.Title,
                    JoinArtistNames(track.Artists),
                    track.Uri,
                    track.SpotifyUrl,
                    release.ReleaseDate,
                    release.AlbumName,
                    ReleaseTrackSource.ArtistAlbums,
                    isFollowedArtistTrack));
            }
        }

        return tracks;
    }

    /// <summary>
    /// Searches for additional new songs where followed artists appear as track artists.
    /// </summary>
    private async Task<IReadOnlyList<ReleaseTrack>> GetCollaborationTracksAsync(
        IReadOnlyList<SpotifyArtist> artists,
        int days,
        IProgress<ReleaseProgress>? progress,
        CancellationToken cancellationToken)
    {
        List<ReleaseTrack> tracks = new();
        DateOnly cutoffDate = clock.Today.AddDays(-days);

        ReportStatus(progress, "Searching songs");

        for (int index = 0; index < artists.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SpotifyArtist artist = artists[index];
            ReportProgress(progress, "Searching songs", index + 1, artists.Count);

            IReadOnlyList<SpotifyTrackItem> searchResults =
                await spotify.SearchTracksByArtistAsync(artist.Name, cancellationToken);

            foreach (SpotifyTrackItem item in searchResults)
            {
                if (item.Album?.ReleaseDate is null || item.Album.ReleaseDate < cutoffDate)
                {
                    continue;
                }

                bool artistIsOnTrack = item.Artists
                    .Any(trackArtist => trackArtist.Id == artist.Id);

                if (!artistIsOnTrack)
                {
                    continue;
                }

                tracks.Add(new ReleaseTrack(
                    item.Title,
                    JoinArtistNames(item.Artists),
                    item.Uri,
                    item.SpotifyUrl,
                    item.Album.ReleaseDate.Value,
                    item.Album.Name,
                    ReleaseTrackSource.Search,
                    true));
            }
        }

        return tracks;
    }

    /// <summary>
    /// Reuses an existing playlist when it still belongs to the signed-in user.
    /// </summary>
    private async Task<string> GetOrCreatePlaylistIdAsync(
        ReleaseSettings settings,
        IProgress<ReleaseProgress>? progress,
        CancellationToken cancellationToken)
    {
        string? playlistId = await GetPlaylistIdAsync(
            settings,
            progress,
            createIfMissing: true,
            cancellationToken);

        return playlistId
            ?? throw new InvalidOperationException("Spotify playlist could not be created.");
    }

    private async Task<string?> GetPlaylistIdAsync(
        ReleaseSettings settings,
        IProgress<ReleaseProgress>? progress,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        SpotifyUser currentUser = await spotify.GetCurrentUserAsync(cancellationToken);
        string description = CreatePlaylistDescription(settings.ReleaseLookbackDays);
        IReadOnlyList<SpotifyPlaylist> userPlaylists =
            await spotify.GetCurrentUserPlaylistsAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(settings.PlaylistId))
        {
            SpotifyPlaylist? playlist =
                await spotify.TryGetPlaylistAsync(settings.PlaylistId, cancellationToken);
            bool isInLibrary = userPlaylists.Any(
                listedPlaylist => listedPlaylist.Id == settings.PlaylistId);

            if (playlist is not null &&
                playlist.OwnerId == currentUser.Id &&
                isInLibrary)
            {
                if (playlist.Name != settings.PlaylistName)
                {
                    ReportMessage(progress, "Saved playlist will be renamed.");
                }

                await spotify.UpdatePlaylistDetailsAsync(
                    settings.PlaylistId,
                    settings.PlaylistName,
                    description,
                    cancellationToken);

                return settings.PlaylistId;
            }

            ReportMessage(
                progress,
                createIfMissing
                    ? "Saved playlist was not found in your Spotify library. A visible playlist will be reused or created."
                    : "Saved playlist was not found in your Spotify library.");
        }

        SpotifyPlaylist? existingPlaylist = userPlaylists.FirstOrDefault(
            playlist => playlist.OwnerId == currentUser.Id &&
                string.Equals(
                    playlist.Name,
                    settings.PlaylistName,
                    StringComparison.Ordinal));

        if (existingPlaylist is not null)
        {
            ReportMessage(
                progress,
                "Existing playlist with the selected name will be reused.");

            await spotify.UpdatePlaylistDetailsAsync(
                existingPlaylist.Id,
                settings.PlaylistName,
                description,
                cancellationToken);

            settings.PlaylistId = existingPlaylist.Id;
            settingsStore.Save(settings);

            return existingPlaylist.Id;
        }

        if (!createIfMissing)
        {
            return null;
        }

        string playlistId = await spotify.CreatePlaylistAsync(
            settings.PlaylistName,
            description,
            cancellationToken);

        settings.PlaylistId = playlistId;
        settingsStore.Save(settings);

        return playlistId;
    }

    /// <summary>
    /// Removes duplicate albums by Spotify album ID.
    /// </summary>
    private static IReadOnlyList<ReleaseCandidate> RemoveDuplicateReleases(
        IReadOnlyList<ReleaseCandidate> releases)
    {
        HashSet<string> seenAlbumIds = new(StringComparer.Ordinal);
        List<ReleaseCandidate> uniqueReleases = new();

        foreach (ReleaseCandidate release in releases)
        {
            if (seenAlbumIds.Add(release.AlbumId))
            {
                uniqueReleases.Add(release);
            }
        }

        return uniqueReleases;
    }

    /// <summary>
    /// Removes duplicate songs by title and artist text.
    /// </summary>
    private static (IReadOnlyList<ReleaseTrack> UniqueTracks, IReadOnlyList<ReleaseTrack> Duplicates)
        RemoveDuplicateTracks(IReadOnlyList<ReleaseTrack> tracks)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<ReleaseTrack> uniqueTracks = new();
        List<ReleaseTrack> duplicates = new();

        foreach (ReleaseTrack track in tracks)
        {
            string key = $"{track.Title.Trim().ToLowerInvariant()}|{track.Artists.Trim().ToLowerInvariant()}";

            if (!seen.Add(key))
            {
                duplicates.Add(track);
                continue;
            }

            uniqueTracks.Add(track);
        }

        return (uniqueTracks, duplicates);
    }

    private static string CreatePlaylistDescription(int releaseLookbackDays) =>
        "Automatically generated playlist with releases from followed artists " +
        $"and collaborations from the last {releaseLookbackDays} days.";

    private static string JoinArtistNames(IReadOnlyList<SpotifyArtist> artists) =>
        string.Join(", ", artists.Select(artist => artist.Name));

    private static void ReportStatus(
        IProgress<ReleaseProgress>? progress,
        string status) =>
        progress?.Report(new ReleaseProgress(status));

    private static void ReportProgress(
        IProgress<ReleaseProgress>? progress,
        string status,
        int current,
        int total) =>
        progress?.Report(new ReleaseProgress(status, current, total));

    private static void ReportMessage(
        IProgress<ReleaseProgress>? progress,
        string message) =>
        progress?.Report(new ReleaseProgress(string.Empty, Message: message));
}
