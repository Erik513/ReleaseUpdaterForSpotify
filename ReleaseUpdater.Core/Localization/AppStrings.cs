using ErikwnkCore;

namespace ReleaseUpdater.Core.Localization;

/// <summary>
/// Translation keys and English/German text for every user-facing string in
/// this app - both ReleaseUpdaterGui's own UI text and the status/log
/// messages SpotifyReleaseUpdater reports, since both end up displayed in
/// the same window. Centralized here (rather than split between this
/// project and ReleaseUpdaterGui) so one Register() call at startup covers
/// everything, and so Core and the UI project can't drift onto different
/// keys for the same string. Call <see cref="Register"/> once, before
/// building any UI or running an update.
/// </summary>
public static class AppStrings
{
    // Shared across multiple call sites - kept to one key each so every
    // site automatically stays in sync when the wording changes.
    public const string ReadyToRun = "Shared.ReadyToRun";
    public const string NotSignedIn = "Shared.NotSignedIn";
    public const string NoNewSongs = "Shared.NoNewSongs";
    public const string Done = "Shared.Done";
    public const string SpotifyClientIdRequired = "Shared.SpotifyClientIdRequired";
    public const string UpdatePlaylist = "Shared.UpdatePlaylist";
    public const string Report = "Shared.Report";
    public const string EditPlaylistName = "Shared.EditPlaylistName";
    public const string EditSpotifyClientId = "Shared.EditSpotifyClientId";
    public const string SearchingReleases = "Shared.SearchingReleases";
    public const string LoadingSongs = "Shared.LoadingSongs";
    public const string SearchingSongs = "Shared.SearchingSongs";

    // MainForm
    public const string MainFormWindowTitle = "MainForm.WindowTitle";
    public const string MainFormSignOutBusyTooltip = "MainForm.SignOutBusyTooltip";
    public const string MainFormSignInBusyTooltip = "MainForm.SignInBusyTooltip";
    public const string MainFormSignOutAccessibleName = "MainForm.SignOutAccessibleName";
    public const string MainFormSignInAccessibleName = "MainForm.SignInAccessibleName";
    public const string MainFormSignOutTooltip = "MainForm.SignOutTooltip";
    public const string MainFormSignInTooltip = "MainForm.SignInTooltip";
    public const string MainFormSaveSettingBeforeUpdateTooltip = "MainForm.SaveSettingBeforeUpdateTooltip";
    public const string MainFormProgressFormat = "MainForm.ProgressFormat";
    public const string MainFormStarting = "MainForm.Starting";
    public const string MainFormSignInDisabledDuringRunTooltip = "MainForm.SignInDisabledDuringRunTooltip";
    public const string MainFormFinishedInFormat = "MainForm.FinishedInFormat";
    public const string MainFormElapsedFormat = "MainForm.ElapsedFormat";
    public const string MainFormHtmlReportUpdated = "MainForm.HtmlReportUpdated";
    public const string MainFormToastNoNewSongs = "MainForm.ToastNoNewSongs";
    public const string MainFormToastPlaylistUpdated = "MainForm.ToastPlaylistUpdated";
    public const string MainFormError = "MainForm.Error";
    public const string MainFormErrorLogFormat = "MainForm.ErrorLogFormat";
    public const string MainFormCancelled = "MainForm.Cancelled";
    public const string MainFormUpdateCancelledLog = "MainForm.UpdateCancelledLog";
    public const string MainFormSavePlaylistName = "MainForm.SavePlaylistName";
    public const string MainFormSaveSpotifyClientId = "MainForm.SaveSpotifyClientId";
    public const string MainFormInitialProgress = "MainForm.InitialProgress";
    public const string MainFormClientIdStatusAccessibleName = "MainForm.ClientIdStatusAccessibleName";
    public const string MainFormClientIdStatusInitial = "MainForm.ClientIdStatusInitial";
    public const string MainFormClientIdPlaceholder = "MainForm.ClientIdPlaceholder";
    public const string MainFormClientIdTooltip = "MainForm.ClientIdTooltip";
    public const string MainFormRowStatus = "MainForm.RowStatus";
    public const string MainFormRowProgress = "MainForm.RowProgress";
    public const string MainFormSectionSettings = "MainForm.SectionSettings";
    public const string MainFormRowLanguage = "MainForm.RowLanguage";
    public const string MainFormRowSpotify = "MainForm.RowSpotify";
    public const string MainFormRowClientId = "MainForm.RowClientId";
    public const string MainFormRowPlaylistName = "MainForm.RowPlaylistName";
    public const string MainFormRowPlaylistDescription = "MainForm.RowPlaylistDescription";
    public const string MainFormPlaylistDescriptionPlaceholder = "MainForm.PlaylistDescriptionPlaceholder";
    public const string MainFormPlaylistDescriptionTooltip = "MainForm.PlaylistDescriptionTooltip";
    public const string MainFormRowLookbackDays = "MainForm.RowLookbackDays";
    public const string MainFormOpenReport = "MainForm.OpenReport";
    public const string MainFormCancelUpdate = "MainForm.CancelUpdate";

    // MainFormController
    public const string ControllerWaitingForLogin = "Controller.WaitingForLogin";
    public const string ControllerBrowserLoginInProgress = "Controller.BrowserLoginInProgress";
    public const string ControllerSignedInFormat = "Controller.SignedInFormat";
    public const string ControllerSignedInUnverified = "Controller.SignedInUnverified";
    public const string ControllerSignedIn = "Controller.SignedIn";
    public const string ControllerToastLoginSuccess = "Controller.ToastLoginSuccess";
    public const string ControllerLoginCancelledStatus = "Controller.LoginCancelledStatus";
    public const string ControllerToastLoginCancelled = "Controller.ToastLoginCancelled";
    public const string ControllerLoginFailed = "Controller.LoginFailed";
    public const string ControllerSpotifyLoginTitle = "Controller.SpotifyLoginTitle";
    public const string ControllerSignedOut = "Controller.SignedOut";
    public const string ControllerToastSignedOut = "Controller.ToastSignedOut";
    public const string ControllerLogoutFailed = "Controller.LogoutFailed";
    public const string ControllerSpotifyLogoutTitle = "Controller.SpotifyLogoutTitle";
    public const string ControllerTestModeRateLimit = "Controller.TestModeRateLimit";
    public const string ControllerCancelling = "Controller.Cancelling";
    public const string ControllerToastCancelling = "Controller.ToastCancelling";
    public const string ControllerConfirmCloseMessage = "Controller.ConfirmCloseMessage";
    public const string ControllerConfirmCloseTitle = "Controller.ConfirmCloseTitle";
    public const string ControllerNoReportAvailable = "Controller.NoReportAvailable";
    public const string ControllerToastOpeningReport = "Controller.ToastOpeningReport";
    public const string ControllerCouldNotOpenReportFormat = "Controller.CouldNotOpenReportFormat";
    public const string ControllerToastPlaylistNameSaved = "Controller.ToastPlaylistNameSaved";
    public const string ControllerToastClientIdSaved = "Controller.ToastClientIdSaved";
    public const string ControllerInvalidClientIdMessage = "Controller.InvalidClientIdMessage";
    public const string ControllerInvalidClientIdTitle = "Controller.InvalidClientIdTitle";
    public const string ControllerClientIdRequiredForLoginMessage = "Controller.ClientIdRequiredForLoginMessage";
    public const string ControllerClientIdRequiredForUpdateMessage = "Controller.ClientIdRequiredForUpdateMessage";
    public const string ControllerCooldownActive = "Controller.CooldownActive";
    public const string ControllerCooldownTitle = "Controller.CooldownTitle";
    public const string ControllerClientIdRequiredTooltip = "Controller.ClientIdRequiredTooltip";
    public const string ControllerCooldownMessageFormat = "Controller.CooldownMessageFormat";
    public const string ControllerClientIdStatusMissing = "Controller.ClientIdStatusMissing";
    public const string ControllerClientIdStatusTestMode = "Controller.ClientIdStatusTestMode";
    public const string ControllerClientIdStatusConfigured = "Controller.ClientIdStatusConfigured";
    public const string ControllerConfirmStartMessage = "Controller.ConfirmStartMessage";
    public const string ControllerSettingsSaved = "Controller.SettingsSaved";
    public const string ControllerSettingsSaveFailedFormat = "Controller.SettingsSaveFailedFormat";

    // SpotifyClientIdHelpForm
    public const string HelpFormTitle = "HelpForm.Title";
    public const string HelpFormClose = "HelpForm.Close";
    public const string HelpFormCloseAccessibleName = "HelpForm.CloseAccessibleName";
    public const string HelpFormDashboardButton = "HelpForm.DashboardButton";
    public const string HelpFormDashboardAccessibleName = "HelpForm.DashboardAccessibleName";
    public const string HelpFormInstructionsIntro = "HelpForm.Instructions.Intro";
    public const string HelpFormInstructionsHeading = "HelpForm.Instructions.Heading";
    public const string HelpFormInstructionsStep1 = "HelpForm.Instructions.Step1";
    public const string HelpFormInstructionsStep2 = "HelpForm.Instructions.Step2";
    public const string HelpFormInstructionsStep3 = "HelpForm.Instructions.Step3";
    public const string HelpFormInstructionsStep4 = "HelpForm.Instructions.Step4";
    public const string HelpFormInstructionsStep5 = "HelpForm.Instructions.Step5";
    public const string HelpFormInstructionsClosingNote = "HelpForm.Instructions.ClosingNote";
    public const string HelpFormToastRedirectUriCopied = "HelpForm.ToastRedirectUriCopied";
    public const string HelpFormCouldNotOpenDashboardFormat = "HelpForm.CouldNotOpenDashboardFormat";
    public const string HelpFormCouldNotCopyUriFormat = "HelpForm.CouldNotCopyUriFormat";

    // Program
    public const string ProgramAlreadyRunningMessage = "Program.AlreadyRunningMessage";

    // PlaylistValidator
    public const string ValidatorPlaylistNameRequired = "Validator.PlaylistNameRequired";
    public const string ValidatorPlaylistNameTooLong = "Validator.PlaylistNameTooLong";
    public const string ValidatorPlaylistNameInvalidChars = "Validator.PlaylistNameInvalidChars";

    // SpotifyReleaseUpdater (Core)
    public const string CoreLoadingFollowedArtists = "Core.LoadingFollowedArtists";
    public const string CoreFollowedArtistsFormat = "Core.FollowedArtistsFormat";
    public const string CoreProcessedArtistsFormat = "Core.ProcessedArtistsFormat";
    public const string CoreLookbackFormat = "Core.LookbackFormat";
    public const string CoreFoundReleasesFormat = "Core.FoundReleasesFormat";
    public const string CoreFoundSongsFormat = "Core.FoundSongsFormat";
    public const string CoreAfterDuplicateFilterFormat = "Core.AfterDuplicateFilterFormat";
    public const string CoreDuplicatesFormat = "Core.DuplicatesFormat";
    public const string CoreTestModePlaylistNotModified = "Core.TestModePlaylistNotModified";
    public const string CoreCheckingPlaylist = "Core.CheckingPlaylist";
    public const string CoreUpdatingPlaylist = "Core.UpdatingPlaylist";
    public const string CoreDoneSingularSong = "Core.DoneSingularSong";
    public const string CoreDoneMultipleSongsFormat = "Core.DoneMultipleSongsFormat";
    public const string CoreTestModeArtistsLimitedFormat = "Core.TestModeArtistsLimitedFormat";
    public const string CoreTestModeReleasesLimitedFormat = "Core.TestModeReleasesLimitedFormat";
    public const string CoreSavedPlaylistWillBeRenamed = "Core.SavedPlaylistWillBeRenamed";
    public const string CoreSavedPlaylistNotFoundReuseOrCreate = "Core.SavedPlaylistNotFoundReuseOrCreate";
    public const string CoreSavedPlaylistNotFound = "Core.SavedPlaylistNotFound";
    public const string CoreExistingPlaylistReused = "Core.ExistingPlaylistReused";
    public const string CorePlaylistCouldNotBeCreated = "Core.PlaylistCouldNotBeCreated";

    // MainForm - unreleased-songs entry point
    public const string MainFormOpenUnreleasedSongs = "MainForm.OpenUnreleasedSongs";

    // UnreleasedSongsForm / UnreleasedSongChecker
    public const string UnreleasedFormTitle = "Unreleased.FormTitle";
    public const string UnreleasedColumnTitle = "Unreleased.ColumnTitle";
    public const string UnreleasedColumnArtist = "Unreleased.ColumnArtist";
    public const string UnreleasedStart = "Unreleased.Start";
    public const string UnreleasedCancel = "Unreleased.Cancel";
    public const string UnreleasedOpenReport = "Unreleased.OpenReport";
    public const string UnreleasedSearching = "Unreleased.Searching";
    public const string UnreleasedStatusSearchingFormat = "Unreleased.StatusSearchingFormat";
    public const string UnreleasedStatusDoneFormat = "Unreleased.StatusDoneFormat";
    public const string UnreleasedNoSongsMessage = "Unreleased.NoSongsMessage";
    public const string UnreleasedSaveFailedFormat = "Unreleased.SaveFailedFormat";
    public const string UnreleasedImport = "Unreleased.Import";
    public const string UnreleasedExport = "Unreleased.Export";
    public const string UnreleasedFileFilterSongs = "Unreleased.FileFilterSongs";
    public const string UnreleasedNothingToExport = "Unreleased.NothingToExport";
    public const string UnreleasedExportDoneFormat = "Unreleased.ExportDoneFormat";
    public const string UnreleasedExportFailedFormat = "Unreleased.ExportFailedFormat";
    public const string UnreleasedImportDoneFormat = "Unreleased.ImportDoneFormat";
    public const string UnreleasedImportNothingFound = "Unreleased.ImportNothingFound";
    public const string UnreleasedImportInvalidFile = "Unreleased.ImportInvalidFile";
    public const string UnreleasedImportFailedFormat = "Unreleased.ImportFailedFormat";
    public const string UnreleasedRateLimitedFormat = "Unreleased.RateLimitedFormat";

    // Spotify sign-in and API errors (ReleaseUpdater.Spotify) - shown to the user as they are,
    // so they follow the language like everything else.
    public const string SpotifyClientIdNotSaved = "Spotify.ClientIdNotSaved";
    public const string SpotifySignInFirst = "Spotify.SignInFirst";
    public const string SpotifyLoginOtherClientId = "Spotify.LoginOtherClientId";
    public const string SpotifyLoginMissingPermissions = "Spotify.LoginMissingPermissions";
    public const string SpotifyPermissionsNotGranted = "Spotify.PermissionsNotGranted";
    public const string SpotifyClientIdMissing = "Spotify.ClientIdMissing";
    public const string SpotifyRefreshTokenMissing = "Spotify.RefreshTokenMissing";
    public const string SpotifyLoginFailedFormat = "Spotify.LoginFailedFormat";
    public const string SpotifyAccessTokenMissing = "Spotify.AccessTokenMissing";
    public const string SpotifyCallbackEmpty = "Spotify.CallbackEmpty";
    public const string SpotifyLoginCancelledFormat = "Spotify.LoginCancelledFormat";
    public const string SpotifyCallbackStateInvalid = "Spotify.CallbackStateInvalid";
    public const string SpotifyCallbackCodeMissing = "Spotify.CallbackCodeMissing";
    public const string SpotifyCallbackInvalid = "Spotify.CallbackInvalid";
    public const string SpotifyCallbackPathInvalid = "Spotify.CallbackPathInvalid";
    public const string SpotifyPageLoginSuccessfulTitle = "Spotify.PageLoginSuccessfulTitle";
    public const string SpotifyPageLoginSuccessfulMessage = "Spotify.PageLoginSuccessfulMessage";
    public const string SpotifyPageLoginCancelledTitle = "Spotify.PageLoginCancelledTitle";
    public const string SpotifyPageLoginCancelledMessage = "Spotify.PageLoginCancelledMessage";
    public const string SpotifyPageLoginFailedTitle = "Spotify.PageLoginFailedTitle";
    public const string SpotifyPageStateMismatchMessage = "Spotify.PageStateMismatchMessage";
    public const string SpotifyPageCodeMissingMessage = "Spotify.PageCodeMissingMessage";
    public const string SpotifyApiForbiddenFormat = "Spotify.ApiForbiddenFormat";
    public const string SpotifyApiErrorFormat = "Spotify.ApiErrorFormat";
    public const string SpotifyApiRetriesExhausted = "Spotify.ApiRetriesExhausted";
    public const string SpotifyResponseMissingFieldFormat = "Spotify.ResponseMissingFieldFormat";
    public const string SpotifyResponseMissingTextFormat = "Spotify.ResponseMissingTextFormat";
    public const string SpotifyRequestLimitReached = "Spotify.RequestLimitReached";
    public const string SpotifyRepeatedTemporaryErrors = "Spotify.RepeatedTemporaryErrors";

    private static bool isRegistered;

    /// <summary>
    /// Registers the English and German text for every key above. Safe to
    /// call more than once - only the first call does anything, since a
    /// re-registration would just be redundant work.
    /// </summary>
    public static void Register()
    {
        if (isRegistered)
        {
            return;
        }

        isRegistered = true;

        AppLocalization.Register(AppLanguage.English, English);
        AppLocalization.Register(AppLanguage.German, German);
    }

    private static readonly Dictionary<string, string> English = new()
    {
        [ReadyToRun] = "Ready to run",
        [NotSignedIn] = "Not signed in",
        [NoNewSongs] = "No new songs",
        [Done] = "Done",
        [SpotifyClientIdRequired] = "Spotify Client ID required",
        [UpdatePlaylist] = "Update playlist",
        [Report] = "Report",
        [EditPlaylistName] = "Edit playlist name",
        [EditSpotifyClientId] = "Edit Spotify Client ID",
        [SearchingReleases] = "Searching releases",
        [LoadingSongs] = "Loading songs",
        [SearchingSongs] = "Searching songs",

        [MainFormWindowTitle] = "Release Updater for Spotify",
        [MainFormSignOutBusyTooltip] = "Spotify sign-out is currently running.",
        [MainFormSignInBusyTooltip] = "Spotify sign-in is running. Click to cancel it.",
        [MainFormSignOutAccessibleName] = "Sign out from Spotify",
        [MainFormSignInAccessibleName] = "Sign in with Spotify",
        [MainFormSignOutTooltip] = "Click to sign out from this app and remove the saved Spotify login.",
        [MainFormSignInTooltip] = "Click to open Spotify login in your browser.",
        [MainFormSaveSettingBeforeUpdateTooltip] = "Save the edited setting before updating the playlist.",
        [MainFormProgressFormat] = "{0} / {1} ({2}%)",
        [MainFormStarting] = "Starting...",
        [MainFormSignInDisabledDuringRunTooltip] = "Spotify sign-in is disabled while the playlist update is running.",
        [MainFormFinishedInFormat] = "Finished in {0}.",
        [MainFormElapsedFormat] = "Elapsed: {0}",
        [MainFormHtmlReportUpdated] = "HTML report updated.",
        [MainFormToastNoNewSongs] = "No new songs found.",
        [MainFormToastPlaylistUpdated] = "Playlist updated successfully.",
        [MainFormError] = "Error",
        [MainFormErrorLogFormat] = "ERROR: {0}",
        [MainFormCancelled] = "Cancelled",
        [MainFormUpdateCancelledLog] = "The update was cancelled.",
        [MainFormSavePlaylistName] = "Save playlist name",
        [MainFormSaveSpotifyClientId] = "Save Spotify Client ID",
        [MainFormInitialProgress] = "0 / 0",
        [MainFormClientIdStatusAccessibleName] = "Spotify Client ID status",
        [MainFormClientIdStatusInitial] = "Spotify Client ID status will appear here.",
        [MainFormClientIdPlaceholder] = "Required Spotify Client ID",
        [MainFormClientIdTooltip] = "Required. Create a Spotify app and enter its Client ID. Do not enter a Client Secret.",
        [MainFormRowStatus] = "Status",
        [MainFormRowProgress] = "Progress",
        [MainFormSectionSettings] = "Settings",
        [MainFormRowLanguage] = "Language",
        [MainFormRowSpotify] = "Spotify",
        [MainFormRowClientId] = "Client ID",
        [MainFormRowPlaylistName] = "Playlist name",
        [MainFormRowPlaylistDescription] = "Description",
        [MainFormPlaylistDescriptionPlaceholder] = "Automatic description",
        [MainFormPlaylistDescriptionTooltip] = "Description of the playlist on Spotify (up to 300 characters). Leave empty for the automatic text.",
        [MainFormRowLookbackDays] = "Lookback days",
        [MainFormOpenReport] = "Open last HTML report",
        [MainFormCancelUpdate] = "Cancel running update",

        [ControllerWaitingForLogin] = "Waiting for Spotify login...",
        [ControllerBrowserLoginInProgress] = "Browser login in progress...",
        [ControllerSignedInFormat] = "Signed in: {0}",
        [ControllerSignedInUnverified] = "Signed in (Spotify could not be reached)",
        [ControllerSignedIn] = "Signed in",
        [ControllerToastLoginSuccess] = "Spotify login successful.",
        [ControllerLoginCancelledStatus] = "Login cancelled or timed out",
        [ControllerToastLoginCancelled] = "Spotify login cancelled or timed out.",
        [ControllerLoginFailed] = "Login failed",
        [ControllerSpotifyLoginTitle] = "Spotify Login",
        [ControllerSignedOut] = "Signed out",
        [ControllerToastSignedOut] = "Signed out from Spotify.",
        [ControllerLogoutFailed] = "Logout failed",
        [ControllerSpotifyLogoutTitle] = "Spotify Logout",
        [ControllerTestModeRateLimit] = "Spotify request limit reached. Test mode ignores saved cooldowns, so you can try again later without changing settings.",
        [ControllerCancelling] = "Cancelling...",
        [ControllerToastCancelling] = "Cancelling update...",
        [ControllerConfirmCloseMessage] = "An update is still running. The playlist may end up only partially updated if you close now.\n\nClose anyway?",
        [ControllerConfirmCloseTitle] = "Update in progress",
        [ControllerNoReportAvailable] = "No report is available yet.",
        [ControllerToastOpeningReport] = "Opening HTML report.",
        [ControllerCouldNotOpenReportFormat] = "Could not open the report: {0}",
        [ControllerToastPlaylistNameSaved] = "Playlist name saved.",
        [ControllerToastClientIdSaved] = "Spotify Client ID saved.",
        [ControllerInvalidClientIdMessage] = "Spotify Client ID must be 32 hexadecimal characters. Do not enter a Client Secret.",
        [ControllerInvalidClientIdTitle] = "Invalid Spotify Client ID",
        [ControllerClientIdRequiredForLoginMessage] = "Please enter and save your Spotify Client ID before signing in or updating the playlist.",
        [ControllerClientIdRequiredForUpdateMessage] = "Please enter and save your Spotify Client ID before updating the playlist.",
        [ControllerCooldownActive] = "Spotify cooldown active",
        [ControllerCooldownTitle] = "Spotify cooldown",
        [ControllerClientIdRequiredTooltip] = "Enter and save your Spotify Client ID before updating the playlist.",
        [ControllerCooldownMessageFormat] = "Spotify request limit reached. Updates are paused until {0}.",
        [ControllerClientIdStatusMissing] = "Spotify Client ID required. Click for setup help.",
        [ControllerClientIdStatusTestMode] = "Test mode active. Your configured Client ID will be used with limited requests.",
        [ControllerClientIdStatusConfigured] = "Spotify Client ID configured. Click for setup help.",
        [ControllerConfirmStartMessage] = "The playlist will be replaced with the found songs.\n\nThis can take a few minutes depending on how many artists you follow.\n\nContinue?",
        [ControllerSettingsSaved] = "Settings saved",
        [ControllerSettingsSaveFailedFormat] = "Settings could not be saved: {0}",

        [HelpFormTitle] = "Spotify Client ID",
        [HelpFormClose] = "Close",
        [HelpFormCloseAccessibleName] = "Close Spotify Client ID help",
        [HelpFormDashboardButton] = "Copy URI and open dashboard",
        [HelpFormDashboardAccessibleName] = "Copy redirect URI and open Spotify Developer Dashboard",
        [HelpFormInstructionsIntro] = "Release Updater for Spotify does not include a shared Spotify Client ID. Each user must create and enter their own Client ID.",
        [HelpFormInstructionsHeading] = "To create your Spotify Client ID:",
        [HelpFormInstructionsStep1] = "1. Click the button below to copy the redirect URI and open the Spotify Developer Dashboard.",
        [HelpFormInstructionsStep2] = "2. Create a new Spotify app.",
        [HelpFormInstructionsStep3] = "3. Add this redirect URI in the Spotify app settings:",
        [HelpFormInstructionsStep4] = "4. Copy the Client ID from the Spotify app settings.",
        [HelpFormInstructionsStep5] = "5. Paste the Client ID into this app and save it with the check button.",
        [HelpFormInstructionsClosingNote] = "Do not enter or share a Client Secret.",
        [HelpFormToastRedirectUriCopied] = "Redirect URI copied.",
        [HelpFormCouldNotOpenDashboardFormat] = "Could not open the Spotify Developer Dashboard: {0}",
        [HelpFormCouldNotCopyUriFormat] = "Could not copy the redirect URI: {0}",

        [ProgramAlreadyRunningMessage] = "The application is already running.",

        [ValidatorPlaylistNameRequired] = "Please enter a playlist name.",
        [ValidatorPlaylistNameTooLong] = "The playlist name can be up to 100 characters long.",
        [ValidatorPlaylistNameInvalidChars] = "The playlist name contains invalid characters.",

        [CoreLoadingFollowedArtists] = "Loading followed artists",
        [CoreFollowedArtistsFormat] = "Followed artists: {0}",
        [CoreProcessedArtistsFormat] = "Processed artists: {0}",
        [CoreLookbackFormat] = "Lookback: last {0} days",
        [CoreFoundReleasesFormat] = "Found releases: {0}",
        [CoreFoundSongsFormat] = "Found songs: {0}",
        [CoreAfterDuplicateFilterFormat] = "After duplicate filter: {0}",
        [CoreDuplicatesFormat] = "Duplicates: {0}",
        [CoreTestModePlaylistNotModified] = "Logic test mode: playlist was not modified.",
        [CoreCheckingPlaylist] = "Checking playlist",
        [CoreUpdatingPlaylist] = "Updating playlist",
        [CoreDoneSingularSong] = "Done. 1 song was added to the playlist.",
        [CoreDoneMultipleSongsFormat] = "Done. {0} songs were added to the playlist.",
        [CoreTestModeArtistsLimitedFormat] = "Logic test mode: artists limited to {0}.",
        [CoreTestModeReleasesLimitedFormat] = "Logic test mode: releases limited to {0}.",
        [CoreSavedPlaylistWillBeRenamed] = "Saved playlist will be renamed.",
        [CoreSavedPlaylistNotFoundReuseOrCreate] = "Saved playlist was not found in your Spotify library. A visible playlist will be reused or created.",
        [CoreSavedPlaylistNotFound] = "Saved playlist was not found in your Spotify library.",
        [CoreExistingPlaylistReused] = "Existing playlist with the selected name will be reused.",
        [CorePlaylistCouldNotBeCreated] = "Spotify playlist could not be created.",

        [MainFormOpenUnreleasedSongs] = "Check unreleased songs",

        [UnreleasedFormTitle] = "Unreleased Songs",
        [UnreleasedColumnTitle] = "Title",
        [UnreleasedColumnArtist] = "Artist",
        [UnreleasedStart] = "Start search",
        [UnreleasedCancel] = "Cancel running search",
        [UnreleasedOpenReport] = "Open last song check report",
        [UnreleasedSearching] = "Checking Spotify",
        [UnreleasedStatusSearchingFormat] = "Checking {0} / {1}: {2}",
        [UnreleasedStatusDoneFormat] = "Done. {0} of {1} songs found on Spotify.",
        [UnreleasedNoSongsMessage] = "Add at least one song before starting the search.",
        [UnreleasedSaveFailedFormat] = "The song list could not be saved: {0}",
        [UnreleasedImport] = "Import songs from a file (Excel, Word, CSV or JSON)",
        [UnreleasedExport] = "Export the song list to a file (Excel, Word, CSV or JSON)",
        [UnreleasedFileFilterSongs] = "Song lists",
        [UnreleasedNothingToExport] = "There are no songs to export.",
        [UnreleasedExportDoneFormat] = "Exported {0} songs.",
        [UnreleasedExportFailedFormat] = "The song list could not be exported: {0}",
        [UnreleasedImportDoneFormat] = "Imported {0} songs ({1} already in the list, {2} entries skipped).",
        [UnreleasedImportNothingFound] = "The file contains no songs with a title and an artist.",
        [UnreleasedImportInvalidFile] = "The file is not a list of songs (a table with the title in the first column and the artist in the second: Excel, Word or CSV; or JSON with title and artist).",
        [UnreleasedImportFailedFormat] = "The file could not be read: {0}",
        [UnreleasedRateLimitedFormat] = "Spotify's request limit was reached. Please try again in about {0} minute(s).",

        [SpotifyClientIdNotSaved] = "Please enter and save your Spotify Client ID first.",
        [SpotifySignInFirst] = "Please sign in to Spotify first.",
        [SpotifyLoginOtherClientId] = "The saved Spotify login belongs to another Client ID. Please sign in again.",
        [SpotifyLoginMissingPermissions] = "Spotify login is missing required permissions. Please sign in again.",
        [SpotifyPermissionsNotGranted] = "Spotify did not grant the required permissions. Please sign in again and approve the requested access.",
        [SpotifyClientIdMissing] = "Spotify client ID is missing.",
        [SpotifyRefreshTokenMissing] = "Spotify refresh token is missing. Please sign in again.",
        [SpotifyLoginFailedFormat] = "Spotify login failed: {0} {1}",
        [SpotifyAccessTokenMissing] = "Spotify access token is missing.",
        [SpotifyCallbackEmpty] = "Spotify callback was empty.",
        [SpotifyLoginCancelledFormat] = "Spotify login cancelled: {0}",
        [SpotifyCallbackStateInvalid] = "Spotify callback state is invalid.",
        [SpotifyCallbackCodeMissing] = "Spotify authorization code is missing.",
        [SpotifyCallbackInvalid] = "Spotify callback is invalid.",
        [SpotifyCallbackPathInvalid] = "Spotify callback path is invalid.",
        [SpotifyPageLoginSuccessfulTitle] = "Login successful",
        [SpotifyPageLoginSuccessfulMessage] = "You can close this browser window now.",
        [SpotifyPageLoginCancelledTitle] = "Login cancelled",
        [SpotifyPageLoginCancelledMessage] = "Spotify cancelled the login.",
        [SpotifyPageLoginFailedTitle] = "Login failed",
        [SpotifyPageStateMismatchMessage] = "The login security state did not match.",
        [SpotifyPageCodeMissingMessage] = "Spotify did not return an authorization code.",
        [SpotifyApiForbiddenFormat] = "Spotify denied access for this account on {0} /{1}. Make sure this Spotify account is allowlisted in your Spotify Developer Dashboard app, or request extended quota mode from Spotify.",
        [SpotifyApiErrorFormat] = "Spotify API error after {0} attempt(s) on {1} /{2}: {3} {4} {5}",
        [SpotifyApiRetriesExhausted] = "Spotify API request failed after all retry attempts.",
        [SpotifyResponseMissingFieldFormat] = "Spotify response does not contain '{0}'.",
        [SpotifyResponseMissingTextFormat] = "Spotify response does not contain text for '{0}'.",
        [SpotifyRequestLimitReached] = "Spotify request limit reached. Switching to robust request mode.",
        [SpotifyRepeatedTemporaryErrors] = "Spotify returned repeated temporary errors. Switching to robust request mode.",
    };

    private static readonly Dictionary<string, string> German = new()
    {
        [ReadyToRun] = "Bereit",
        [NotSignedIn] = "Nicht angemeldet",
        [NoNewSongs] = "Keine neuen Songs",
        [Done] = "Fertig",
        [SpotifyClientIdRequired] = "Spotify Client-ID erforderlich",
        [UpdatePlaylist] = "Playlist aktualisieren",
        [Report] = "Bericht",
        [EditPlaylistName] = "Playlist-Namen bearbeiten",
        [EditSpotifyClientId] = "Spotify Client-ID bearbeiten",
        [SearchingReleases] = "Suche nach Veröffentlichungen",
        [LoadingSongs] = "Songs werden geladen",
        [SearchingSongs] = "Songs werden gesucht",

        [MainFormWindowTitle] = "Release Updater for Spotify",
        [MainFormSignOutBusyTooltip] = "Spotify-Abmeldung läuft gerade.",
        [MainFormSignInBusyTooltip] = "Spotify-Anmeldung läuft. Klicke, um sie abzubrechen.",
        [MainFormSignOutAccessibleName] = "Von Spotify abmelden",
        [MainFormSignInAccessibleName] = "Mit Spotify anmelden",
        [MainFormSignOutTooltip] = "Klicken, um dich von dieser App abzumelden und die gespeicherte Spotify-Anmeldung zu entfernen.",
        [MainFormSignInTooltip] = "Klicken, um die Spotify-Anmeldung im Browser zu öffnen.",
        [MainFormSaveSettingBeforeUpdateTooltip] = "Speichere die bearbeitete Einstellung, bevor du die Playlist aktualisierst.",
        [MainFormProgressFormat] = "{0} / {1} ({2}%)",
        [MainFormStarting] = "Wird gestartet...",
        [MainFormSignInDisabledDuringRunTooltip] = "Die Spotify-Anmeldung ist deaktiviert, während die Playlist-Aktualisierung läuft.",
        [MainFormFinishedInFormat] = "Abgeschlossen in {0}.",
        [MainFormElapsedFormat] = "Verstrichen: {0}",
        [MainFormHtmlReportUpdated] = "HTML-Bericht aktualisiert.",
        [MainFormToastNoNewSongs] = "Keine neuen Songs gefunden.",
        [MainFormToastPlaylistUpdated] = "Playlist erfolgreich aktualisiert.",
        [MainFormError] = "Fehler",
        [MainFormErrorLogFormat] = "FEHLER: {0}",
        [MainFormCancelled] = "Abgebrochen",
        [MainFormUpdateCancelledLog] = "Die Aktualisierung wurde abgebrochen.",
        [MainFormSavePlaylistName] = "Playlist-Namen speichern",
        [MainFormSaveSpotifyClientId] = "Spotify Client-ID speichern",
        [MainFormInitialProgress] = "0 / 0",
        [MainFormClientIdStatusAccessibleName] = "Spotify Client-ID Status",
        [MainFormClientIdStatusInitial] = "Der Spotify Client-ID-Status wird hier angezeigt.",
        [MainFormClientIdPlaceholder] = "Erforderliche Spotify Client-ID",
        [MainFormClientIdTooltip] = "Erforderlich. Erstelle eine Spotify-App und gib ihre Client-ID ein. Gib kein Client Secret ein.",
        [MainFormRowStatus] = "Status",
        [MainFormRowProgress] = "Fortschritt",
        [MainFormSectionSettings] = "Einstellungen",
        [MainFormRowLanguage] = "Sprache",
        [MainFormRowSpotify] = "Spotify",
        [MainFormRowClientId] = "Client-ID",
        [MainFormRowPlaylistName] = "Playlist-Name",
        [MainFormRowPlaylistDescription] = "Beschreibung",
        [MainFormPlaylistDescriptionPlaceholder] = "Automatische Beschreibung",
        [MainFormPlaylistDescriptionTooltip] = "Beschreibung der Playlist bei Spotify (bis zu 300 Zeichen). Leer lassen für den automatischen Text.",
        [MainFormRowLookbackDays] = "Rückblick (Tage)",
        [MainFormOpenReport] = "Letzten HTML-Bericht öffnen",
        [MainFormCancelUpdate] = "Laufende Aktualisierung abbrechen",

        [ControllerWaitingForLogin] = "Warte auf Spotify-Anmeldung...",
        [ControllerBrowserLoginInProgress] = "Anmeldung im Browser läuft...",
        [ControllerSignedInFormat] = "Angemeldet als: {0}",
        [ControllerSignedInUnverified] = "Angemeldet (Spotify nicht erreichbar)",
        [ControllerSignedIn] = "Angemeldet",
        [ControllerToastLoginSuccess] = "Spotify-Anmeldung erfolgreich.",
        [ControllerLoginCancelledStatus] = "Anmeldung abgebrochen oder Zeit abgelaufen",
        [ControllerToastLoginCancelled] = "Spotify-Anmeldung abgebrochen oder Zeit abgelaufen.",
        [ControllerLoginFailed] = "Anmeldung fehlgeschlagen",
        [ControllerSpotifyLoginTitle] = "Spotify-Anmeldung",
        [ControllerSignedOut] = "Abgemeldet",
        [ControllerToastSignedOut] = "Von Spotify abgemeldet.",
        [ControllerLogoutFailed] = "Abmeldung fehlgeschlagen",
        [ControllerSpotifyLogoutTitle] = "Spotify-Abmeldung",
        [ControllerTestModeRateLimit] = "Spotify-Anfragelimit erreicht. Der Testmodus ignoriert gespeicherte Sperrzeiten, du kannst es also später ohne Änderungen an den Einstellungen erneut versuchen.",
        [ControllerCancelling] = "Wird abgebrochen...",
        [ControllerToastCancelling] = "Aktualisierung wird abgebrochen...",
        [ControllerConfirmCloseMessage] = "Eine Aktualisierung läuft noch. Die Playlist könnte nur teilweise aktualisiert sein, wenn du jetzt schließt.\n\nTrotzdem schließen?",
        [ControllerConfirmCloseTitle] = "Aktualisierung läuft",
        [ControllerNoReportAvailable] = "Es ist noch kein Bericht verfügbar.",
        [ControllerToastOpeningReport] = "HTML-Bericht wird geöffnet.",
        [ControllerCouldNotOpenReportFormat] = "Der Bericht konnte nicht geöffnet werden: {0}",
        [ControllerToastPlaylistNameSaved] = "Playlist-Name gespeichert.",
        [ControllerToastClientIdSaved] = "Spotify Client-ID gespeichert.",
        [ControllerInvalidClientIdMessage] = "Die Spotify Client-ID muss aus 32 hexadezimalen Zeichen bestehen. Gib kein Client Secret ein.",
        [ControllerInvalidClientIdTitle] = "Ungültige Spotify Client-ID",
        [ControllerClientIdRequiredForLoginMessage] = "Bitte gib deine Spotify Client-ID ein und speichere sie, bevor du dich anmeldest oder die Playlist aktualisierst.",
        [ControllerClientIdRequiredForUpdateMessage] = "Bitte gib deine Spotify Client-ID ein und speichere sie, bevor du die Playlist aktualisierst.",
        [ControllerCooldownActive] = "Spotify-Sperrzeit aktiv",
        [ControllerCooldownTitle] = "Spotify-Sperrzeit",
        [ControllerClientIdRequiredTooltip] = "Gib deine Spotify Client-ID ein und speichere sie, bevor du die Playlist aktualisierst.",
        [ControllerCooldownMessageFormat] = "Spotify-Anfragelimit erreicht. Aktualisierungen sind pausiert bis {0}.",
        [ControllerClientIdStatusMissing] = "Spotify Client-ID erforderlich. Klicken für Einrichtungshilfe.",
        [ControllerClientIdStatusTestMode] = "Testmodus aktiv. Deine konfigurierte Client-ID wird mit begrenzten Anfragen verwendet.",
        [ControllerClientIdStatusConfigured] = "Spotify Client-ID konfiguriert. Klicken für Einrichtungshilfe.",
        [ControllerConfirmStartMessage] = "Die Playlist wird durch die gefundenen Songs ersetzt.\n\nDies kann je nach Anzahl der von dir verfolgten Künstler einige Minuten dauern.\n\nFortfahren?",
        [ControllerSettingsSaved] = "Einstellungen gespeichert",
        [ControllerSettingsSaveFailedFormat] = "Einstellungen konnten nicht gespeichert werden: {0}",

        [HelpFormTitle] = "Spotify Client-ID",
        [HelpFormClose] = "Schließen",
        [HelpFormCloseAccessibleName] = "Spotify Client-ID-Hilfe schließen",
        [HelpFormDashboardButton] = "URI kopieren & Dashboard öffnen",
        [HelpFormDashboardAccessibleName] = "Redirect-URI kopieren und Spotify Developer Dashboard öffnen",
        [HelpFormInstructionsIntro] = "Release Updater for Spotify enthält keine gemeinsam genutzte Spotify Client-ID. Jeder Nutzer muss seine eigene Client-ID erstellen und eingeben.",
        [HelpFormInstructionsHeading] = "So erstellst du deine Spotify Client-ID:",
        [HelpFormInstructionsStep1] = "1. Klicke unten auf die Schaltfläche, um die Redirect-URI zu kopieren und das Spotify Developer Dashboard zu öffnen.",
        [HelpFormInstructionsStep2] = "2. Erstelle eine neue Spotify-App.",
        [HelpFormInstructionsStep3] = "3. Füge diese Redirect-URI in den Einstellungen der Spotify-App hinzu:",
        [HelpFormInstructionsStep4] = "4. Kopiere die Client-ID aus den Einstellungen der Spotify-App.",
        [HelpFormInstructionsStep5] = "5. Füge die Client-ID in diese App ein und speichere sie mit der Häkchen-Schaltfläche.",
        [HelpFormInstructionsClosingNote] = "Gib kein Client Secret ein und teile es nicht.",
        [HelpFormToastRedirectUriCopied] = "Redirect-URI kopiert.",
        [HelpFormCouldNotOpenDashboardFormat] = "Das Spotify Developer Dashboard konnte nicht geöffnet werden: {0}",
        [HelpFormCouldNotCopyUriFormat] = "Die Redirect-URI konnte nicht kopiert werden: {0}",

        [ProgramAlreadyRunningMessage] = "Die Anwendung wird bereits ausgeführt.",

        [ValidatorPlaylistNameRequired] = "Bitte gib einen Playlist-Namen ein.",
        [ValidatorPlaylistNameTooLong] = "Der Playlist-Name darf höchstens 100 Zeichen lang sein.",
        [ValidatorPlaylistNameInvalidChars] = "Der Playlist-Name enthält ungültige Zeichen.",

        [CoreLoadingFollowedArtists] = "Verfolgte Künstler werden geladen",
        [CoreFollowedArtistsFormat] = "Verfolgte Künstler: {0}",
        [CoreProcessedArtistsFormat] = "Verarbeitete Künstler: {0}",
        [CoreLookbackFormat] = "Rückblick: letzte {0} Tage",
        [CoreFoundReleasesFormat] = "Gefundene Veröffentlichungen: {0}",
        [CoreFoundSongsFormat] = "Gefundene Songs: {0}",
        [CoreAfterDuplicateFilterFormat] = "Nach Duplikatfilter: {0}",
        [CoreDuplicatesFormat] = "Duplikate: {0}",
        [CoreTestModePlaylistNotModified] = "Testmodus: Playlist wurde nicht geändert.",
        [CoreCheckingPlaylist] = "Playlist wird geprüft",
        [CoreUpdatingPlaylist] = "Playlist wird aktualisiert",
        [CoreDoneSingularSong] = "Fertig. 1 Song wurde zur Playlist hinzugefügt.",
        [CoreDoneMultipleSongsFormat] = "Fertig. {0} Songs wurden zur Playlist hinzugefügt.",
        [CoreTestModeArtistsLimitedFormat] = "Testmodus: Künstler begrenzt auf {0}.",
        [CoreTestModeReleasesLimitedFormat] = "Testmodus: Veröffentlichungen begrenzt auf {0}.",
        [CoreSavedPlaylistWillBeRenamed] = "Die gespeicherte Playlist wird umbenannt.",
        [CoreSavedPlaylistNotFoundReuseOrCreate] = "Die gespeicherte Playlist wurde in deiner Spotify-Bibliothek nicht gefunden. Eine sichtbare Playlist wird wiederverwendet oder erstellt.",
        [CoreSavedPlaylistNotFound] = "Die gespeicherte Playlist wurde in deiner Spotify-Bibliothek nicht gefunden.",
        [CoreExistingPlaylistReused] = "Eine vorhandene Playlist mit dem gewählten Namen wird wiederverwendet.",
        [CorePlaylistCouldNotBeCreated] = "Die Spotify-Playlist konnte nicht erstellt werden.",

        [MainFormOpenUnreleasedSongs] = "Nicht veröffentlichte Songs prüfen",

        [UnreleasedFormTitle] = "Nicht veröffentlichte Songs",
        [UnreleasedColumnTitle] = "Titel",
        [UnreleasedColumnArtist] = "Interpret",
        [UnreleasedStart] = "Suche starten",
        [UnreleasedCancel] = "Laufende Suche abbrechen",
        [UnreleasedOpenReport] = "Letzten Song-Check-Bericht öffnen",
        [UnreleasedSearching] = "Spotify wird geprüft",
        [UnreleasedStatusSearchingFormat] = "Prüfe {0} / {1}: {2}",
        [UnreleasedStatusDoneFormat] = "Fertig. {0} von {1} Songs auf Spotify gefunden.",
        [UnreleasedNoSongsMessage] = "Füge mindestens einen Song hinzu, bevor du die Suche startest.",
        [UnreleasedSaveFailedFormat] = "Die Songliste konnte nicht gespeichert werden: {0}",
        [UnreleasedImport] = "Songs aus einer Datei importieren (Excel, Word, CSV oder JSON)",
        [UnreleasedExport] = "Songliste in eine Datei exportieren (Excel, Word, CSV oder JSON)",
        [UnreleasedFileFilterSongs] = "Songlisten",
        [UnreleasedNothingToExport] = "Es gibt keine Songs zum Exportieren.",
        [UnreleasedExportDoneFormat] = "{0} Songs exportiert.",
        [UnreleasedExportFailedFormat] = "Die Songliste konnte nicht exportiert werden: {0}",
        [UnreleasedImportDoneFormat] = "{0} Songs importiert ({1} schon in der Liste, {2} Einträge übersprungen).",
        [UnreleasedImportNothingFound] = "Die Datei enthält keine Songs mit Titel und Interpret.",
        [UnreleasedImportInvalidFile] = "Die Datei ist keine Songliste (eine Tabelle mit dem Titel in der ersten und dem Interpreten in der zweiten Spalte: Excel, Word oder CSV; oder JSON mit title und artist).",
        [UnreleasedImportFailedFormat] = "Die Datei konnte nicht gelesen werden: {0}",
        [UnreleasedRateLimitedFormat] = "Das Anfragelimit von Spotify wurde erreicht. Bitte versuche es in etwa {0} Minute(n) erneut.",

        [SpotifyClientIdNotSaved] = "Bitte gib zuerst deine Spotify-Client-ID ein und speichere sie.",
        [SpotifySignInFirst] = "Bitte melde dich zuerst bei Spotify an.",
        [SpotifyLoginOtherClientId] = "Die gespeicherte Spotify-Anmeldung gehört zu einer anderen Client-ID. Bitte melde dich erneut an.",
        [SpotifyLoginMissingPermissions] = "Der Spotify-Anmeldung fehlen erforderliche Berechtigungen. Bitte melde dich erneut an.",
        [SpotifyPermissionsNotGranted] = "Spotify hat die erforderlichen Berechtigungen nicht erteilt. Bitte melde dich erneut an und bestätige den angeforderten Zugriff.",
        [SpotifyClientIdMissing] = "Die Spotify-Client-ID fehlt.",
        [SpotifyRefreshTokenMissing] = "Das Spotify-Refresh-Token fehlt. Bitte melde dich erneut an.",
        [SpotifyLoginFailedFormat] = "Spotify-Anmeldung fehlgeschlagen: {0} {1}",
        [SpotifyAccessTokenMissing] = "Das Spotify-Access-Token fehlt.",
        [SpotifyCallbackEmpty] = "Der Spotify-Rückruf war leer.",
        [SpotifyLoginCancelledFormat] = "Spotify-Anmeldung abgebrochen: {0}",
        [SpotifyCallbackStateInvalid] = "Der State des Spotify-Rückrufs ist ungültig.",
        [SpotifyCallbackCodeMissing] = "Der Spotify-Autorisierungscode fehlt.",
        [SpotifyCallbackInvalid] = "Der Spotify-Rückruf ist ungültig.",
        [SpotifyCallbackPathInvalid] = "Der Pfad des Spotify-Rückrufs ist ungültig.",
        [SpotifyPageLoginSuccessfulTitle] = "Anmeldung erfolgreich",
        [SpotifyPageLoginSuccessfulMessage] = "Du kannst dieses Browserfenster jetzt schließen.",
        [SpotifyPageLoginCancelledTitle] = "Anmeldung abgebrochen",
        [SpotifyPageLoginCancelledMessage] = "Spotify hat die Anmeldung abgebrochen.",
        [SpotifyPageLoginFailedTitle] = "Anmeldung fehlgeschlagen",
        [SpotifyPageStateMismatchMessage] = "Der Sicherheits-State der Anmeldung stimmte nicht überein.",
        [SpotifyPageCodeMissingMessage] = "Spotify hat keinen Autorisierungscode zurückgegeben.",
        [SpotifyApiForbiddenFormat] = "Spotify hat den Zugriff für dieses Konto bei {0} /{1} verweigert. Stelle sicher, dass dieses Spotify-Konto in deiner App im Spotify Developer Dashboard freigeschaltet ist, oder beantrage bei Spotify den erweiterten Kontingentmodus.",
        [SpotifyApiErrorFormat] = "Spotify-API-Fehler nach {0} Versuch(en) bei {1} /{2}: {3} {4} {5}",
        [SpotifyApiRetriesExhausted] = "Die Spotify-API-Anfrage ist nach allen Wiederholungen fehlgeschlagen.",
        [SpotifyResponseMissingFieldFormat] = "Die Spotify-Antwort enthält '{0}' nicht.",
        [SpotifyResponseMissingTextFormat] = "Die Spotify-Antwort enthält keinen Text für '{0}'.",
        [SpotifyRequestLimitReached] = "Spotify-Anfragelimit erreicht. Wechsle in den robusten Anfragemodus.",
        [SpotifyRepeatedTemporaryErrors] = "Spotify meldet wiederholt vorübergehende Fehler. Wechsle in den robusten Anfragemodus.",
    };
}
