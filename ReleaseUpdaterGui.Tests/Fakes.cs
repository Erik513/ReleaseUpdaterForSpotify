using System.Runtime.CompilerServices;
using ReleaseUpdater.Core.Abstractions;
using ReleaseUpdater.Core.Localization;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Core.Services;

namespace ReleaseUpdaterGui.Tests;

// The app registers its texts once at start-up; so do the tests.
internal static class TestSetup
{
    [ModuleInitializer]
    internal static void RegisterTexts()
    {
        AppStrings.Register();
    }
}

internal static class Texts
{
    // A well-formed Client ID: 32 hexadecimal characters.
    public const string ValidClientId = "0123456789abcdef0123456789abcdef";
}

/// <summary>Stands in for the main window: records every call as a short text and lets a test decide the answers.</summary>
internal sealed class FakeMainView : IMainView
{
    public List<string> Calls { get; } = new();

    public bool IsShuttingDown { get; set; }
    public bool ConfirmStartAnswer { get; set; } = true;
    public Action? OnPrepareRunUi { get; set; }

    public string PlaylistName { get; set; } = ReleaseDefaults.PlaylistName;
    public string CustomSpotifyClientId { get; set; } = string.Empty;
    public bool IsPlaylistNameReadOnly { get; set; } = true;
    public bool IsSpotifyClientIdReadOnly { get; set; } = true;
    public string? PlaylistId { get; set; }
    public ReleaseSettings? LastSetSettings { get; private set; }

    public ReleaseSettings GetSettings() => new ReleaseSettings
    {
        PlaylistId = PlaylistId,
        PlaylistName = PlaylistName,
        CustomSpotifyClientId = CustomSpotifyClientId
    }.Normalize();

    public void SetSettings(ReleaseSettings settings)
    {
        LastSetSettings = settings;
        Calls.Add("SetSettings");
    }

    public void SetStatus(string status) => Calls.Add("SetStatus:" + status);
    public void SetLoginStatus(string status) => Calls.Add("SetLoginStatus:" + status);
    public void SetClientIdStatus(string status, ClientIdStatusKind statusKind) => Calls.Add("SetClientIdStatus:" + statusKind);
    public void SetUpdateAvailability(bool isAvailable, string tooltip) => Calls.Add("SetUpdateAvailability:" + isAvailable);
    public void AppendOutput(string message) => Calls.Add("AppendOutput:" + message);
    public void SetAuthenticationBusy() => Calls.Add("SetAuthenticationBusy");
    public void SetAuthenticationState(bool isLoggedIn) => Calls.Add("SetAuthenticationState:" + isLoggedIn);

    public void PrepareRunUi()
    {
        Calls.Add("PrepareRunUi");
        OnPrepareRunUi?.Invoke();
    }

    // Not recorded: Progress<T> delivers its reports from a thread-pool thread (no window, no
    // synchronization context in a test), at a time the test does not control. The real window
    // ignores them once it is closing - see MainForm.ApplyProgress.
    public void ApplyProgress(ReleaseProgress progress)
    {
    }

    public void ShowCompleted(PlaylistUpdateResult result) => Calls.Add("ShowCompleted");
    public void ShowRunFailed(string message) => Calls.Add("ShowRunFailed:" + message);
    public void ShowCancelled() => Calls.Add("ShowCancelled");
    public void StopElapsedTimer() => Calls.Add("StopElapsedTimer");

    public void EnablePlaylistNameEditing()
    {
        IsPlaylistNameReadOnly = false;
        Calls.Add("EnablePlaylistNameEditing");
    }

    public void DisablePlaylistNameEditing()
    {
        IsPlaylistNameReadOnly = true;
        Calls.Add("DisablePlaylistNameEditing");
    }

    public void FocusPlaylistName() => Calls.Add("FocusPlaylistName");
    public string? PlaylistNameError { get; private set; }
    public void SetPlaylistNameError(string? message) => PlaylistNameError = message;

    public void EnableSpotifyClientIdEditing()
    {
        IsSpotifyClientIdReadOnly = false;
        Calls.Add("EnableSpotifyClientIdEditing");
    }

    public void DisableSpotifyClientIdEditing()
    {
        IsSpotifyClientIdReadOnly = true;
        Calls.Add("DisableSpotifyClientIdEditing");
    }

    public void FocusSpotifyClientId() => Calls.Add("FocusSpotifyClientId");

    public void ShowInfo(string message, string title) => Calls.Add("ShowInfo:" + message);
    public void ShowWarning(string message, string title) => Calls.Add("ShowWarning:" + message);
    public void ShowError(string message, string title) => Calls.Add("ShowError:" + message);
    public void ShowToast(string message) => Calls.Add("ShowToast:" + message);
    public bool ConfirmStart()
    {
        Calls.Add("ConfirmStart");
        return ConfirmStartAnswer;
    }

    public void BeginUpdateCheck() => Calls.Add("BeginUpdateCheck");

    public bool Has(string prefix) => Calls.Any(call => call.StartsWith(prefix, StringComparison.Ordinal));
    public int Count(string prefix) => Calls.Count(call => call.StartsWith(prefix, StringComparison.Ordinal));
}

/// <summary>Stands in for the unreleased-songs window.</summary>
internal sealed class FakeUnreleasedView : IUnreleasedSongsView
{
    public List<string> Calls { get; } = new();

    public bool IsShuttingDown { get; set; }

    public void SetStatus(string status) => Calls.Add("SetStatus:" + status);

    /// <summary>What the user "chooses" in the column dialog; null = the guess of the table, <see cref="CancelColumnChoice"/> = Cancel.</summary>
    public ImportColumnChoice? ColumnChoice { get; set; }
    public bool CancelColumnChoice { get; set; }
    public ImportTable? AskedTable { get; private set; }

    public ImportColumnChoice? ChooseImportColumns(ImportTable table)
    {
        AskedTable = table;
        Calls.Add("ChooseImportColumns:" + table.ColumnCount);

        return CancelColumnChoice ? null : ColumnChoice ?? table.GuessColumns();
    }

    public void PrepareRunUi() => Calls.Add("PrepareRunUi");
    // Not recorded, for the reason given at FakeMainView.ApplyProgress.
    public void ApplyProgress(ReleaseProgress progress)
    {
    }

    public void ShowCompleted(UnreleasedSongsCheckResult result) => Calls.Add("ShowCompleted:" + result.FoundCount + "/" + result.CheckedCount);
    public void ShowCancelled() => Calls.Add("ShowCancelled");
    public void ShowRunFailed(string message) => Calls.Add("ShowRunFailed:" + message);
    public void ShowInfo(string message, string title) => Calls.Add("ShowInfo:" + message);
    public void ShowError(string message, string title) => Calls.Add("ShowError:" + message);

    public bool Has(string prefix) => Calls.Any(call => call.StartsWith(prefix, StringComparison.Ordinal));
}

/// <summary>An in-memory settings file; a test can make saving fail.</summary>
internal sealed class FakeSettingsStore : ISettingsStore
{
    public ReleaseSettings Stored { get; set; } = new ReleaseSettings { CustomSpotifyClientId = Texts.ValidClientId };
    public Exception? SaveFailure { get; set; }
    public int SaveCount { get; private set; }

    public string SettingsPath => "memory-settings.json";

    public ReleaseSettings Load() => Clone(Stored);

    public void Save(ReleaseSettings settings)
    {
        if (SaveFailure is not null)
        {
            throw SaveFailure;
        }

        SaveCount++;
        Stored = Clone(settings);
    }

    private static ReleaseSettings Clone(ReleaseSettings settings) => new()
    {
        PlaylistId = settings.PlaylistId,
        PlaylistName = settings.PlaylistName,
        PlaylistDescription = settings.PlaylistDescription,
        ReleaseLookbackDays = settings.ReleaseLookbackDays,
        CustomSpotifyClientId = settings.CustomSpotifyClientId,
        SpotifyCooldownUntilUtc = settings.SpotifyCooldownUntilUtc,
        SpotifyCooldownClientId = settings.SpotifyCooldownClientId,
        SpotifyCooldownAccountWide = settings.SpotifyCooldownAccountWide,
        Language = settings.Language
    };
}

internal sealed class FakeReportWriter : IReportWriter
{
    public string ReportPath { get; set; } = Path.Combine(Path.GetTempPath(), "no-such-report-" + Guid.NewGuid().ToString("N") + ".html");

    public string WriteReport(IReadOnlyList<ReleaseTrack> uniqueTracks, IReadOnlyList<ReleaseTrack> duplicateTracks, int releaseLookbackDays) =>
        ReportPath;
}

internal sealed class FakeUnreleasedReportWriter : IUnreleasedSongsReportWriter
{
    public string ReportPath { get; set; } = Path.Combine(Path.GetTempPath(), "no-such-report-" + Guid.NewGuid().ToString("N") + ".html");

    public string WriteReport(IReadOnlyList<SongMatchResult> results) => ReportPath;
}

internal sealed class FakeClock : IReleaseClock
{
    public DateOnly Today { get; set; } = new(2026, 1, 15);
}

internal sealed class FakeAuthService : ISpotifyAuthService
{
    public SpotifyUser? CurrentUser { get; set; }

    /// <summary>When set, the check says "could not find out" (the network is down), whatever the user is.</summary>
    public bool Unavailable { get; set; }

    public Func<CancellationToken, Task<SpotifyUser>>? OnLogin { get; set; }
    public int LogoutCount { get; private set; }

    public Task<SpotifySignInCheck> CheckSignInAsync(CancellationToken cancellationToken) =>
        Task.FromResult(
            Unavailable
                ? new SpotifySignInCheck(SpotifySignInState.Unavailable)
                : CurrentUser is null
                    ? new SpotifySignInCheck(SpotifySignInState.NotSignedIn)
                    : new SpotifySignInCheck(SpotifySignInState.SignedIn, CurrentUser));

    public Task<SpotifyUser> LoginAsync(CancellationToken cancellationToken) =>
        OnLogin is not null ? OnLogin(cancellationToken) : Task.FromResult(new SpotifyUser("id", "Name"));

    public Task LogoutAsync(CancellationToken cancellationToken)
    {
        LogoutCount++;
        CurrentUser = null;
        return Task.CompletedTask;
    }
}

/// <summary>A Spotify gateway where only what a test sets up works; anything else throws, so a test notices a call it did not expect.</summary>
internal sealed class FakeGateway : ISpotifyGateway
{
    public Func<CancellationToken, Task<IReadOnlyList<SpotifyArtist>>> OnFollowedArtists { get; set; } =
        _ => Task.FromResult<IReadOnlyList<SpotifyArtist>>(Array.Empty<SpotifyArtist>());

    public Func<string, string, CancellationToken, Task<IReadOnlyList<SpotifyTrackItem>>> OnSearch { get; set; } =
        (_, _, _) => Task.FromResult<IReadOnlyList<SpotifyTrackItem>>(Array.Empty<SpotifyTrackItem>());

    public List<string> Searches { get; } = new();

    public Task<IReadOnlyList<SpotifyArtist>> GetFollowedArtistsAsync(CancellationToken cancellationToken) =>
        OnFollowedArtists(cancellationToken);

    public Task<IReadOnlyList<SpotifyTrackItem>> SearchTrackByTitleArtistAsync(string title, string artist, CancellationToken cancellationToken)
    {
        lock (Searches)
        {
            Searches.Add(title + "|" + artist);
        }

        return OnSearch(title, artist, cancellationToken);
    }

    public Task<SpotifyUser> GetCurrentUserAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<IReadOnlyList<SpotifyAlbum>> GetArtistAlbumsAsync(string artistId, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<IReadOnlyList<SpotifyTrackItem>> GetAlbumTracksAsync(string albumId, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<IReadOnlyList<SpotifyTrackItem>> SearchTracksByArtistAsync(string artistName, int fromYear, int toYear, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<SpotifyPlaylist?> TryGetPlaylistAsync(string playlistId, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<IReadOnlyList<SpotifyPlaylist>> GetCurrentUserPlaylistsAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<string> CreatePlaylistAsync(string name, string description, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task UpdatePlaylistDetailsAsync(string playlistId, string name, string description, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task ReplacePlaylistTracksAsync(string playlistId, IReadOnlyList<string> trackUris, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task AddTracksToPlaylistAsync(string playlistId, IReadOnlyList<string> trackUris, CancellationToken cancellationToken) => throw new NotImplementedException();
}
