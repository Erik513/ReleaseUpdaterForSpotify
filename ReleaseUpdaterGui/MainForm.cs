using ErikwnkCore;
using ErikwnkWFUI;
using ErikwnkWFUI.Forms;
using ErikwnkWFUI.Styles;
using ReleaseUpdater.Data;
using ReleaseUpdater.Core.Localization;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Core.Services;
using ReleaseUpdater.Data.Reports;
using ReleaseUpdater.Data.Storage;
using ReleaseUpdater.Spotify.Api;
using ReleaseUpdater.Spotify.Auth;
// Only these two come from ErikwnkWFUI.Controls: a "using" of the whole
// namespace would make every standard control name used here (Panel,
// ProgressBar, ...) ambiguous with the library's same-named controls.
using PropertyColumn = ErikwnkWFUI.Controls.PropertyColumn;
using PropertyTable = ErikwnkWFUI.Controls.PropertyTable;

namespace ReleaseUpdaterGui
{
    public partial class MainForm : AppForm, IMainView
    {
        private static readonly TimeSpan UpdateCheckTimeout = TimeSpan.FromSeconds(8);
        private const int SettingsSaveDelayMilliseconds = 400;

        private DelayedAction? saveSettingsLater;
        private static readonly Size PropertyIconButtonSize = new(36, 30);
        private static readonly Size ActionIconButtonSize = new(48, 34);
        private const int PropertyIconColumnWidth = 54;
        private const int PropertyRowHeight = 34;

        private readonly AppDataPaths dataPaths;
        private readonly JsonSettingsStore settingsStore;
        private readonly FileTokenStore tokenStore;
        private readonly SettingsSpotifyClientIdProvider clientIdProvider;
        private readonly HtmlReportWriter reportWriter;
        private readonly SpotifyAuthService authService;
        private readonly SpotifyApiClient spotifyApiClient;
        private readonly SpotifyReleaseUpdater releaseUpdater;
        private readonly AppUpdater appUpdater;
        private readonly TrackedSongsStore trackedSongsStore;
        private readonly UnreleasedSongsReportWriter unreleasedSongsReportWriter;
        private readonly UnreleasedSongChecker unreleasedSongChecker;
        private readonly SystemReleaseClock releaseClock = new();
        private readonly HttpClient spotifyAccountsHttpClient = new();
        private readonly HttpClient spotifyApiHttpClient = new();
        private readonly HttpClient updateCheckHttpClient = new();
        private readonly HttpClient selfUpdateHttpClient = new()
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        private MainFormController controller;
        private string? currentPlaylistId;

        private PropertyTable propertyTable = null!;
        private Label lblStatus = null!;
        private Label lblProgress = null!;
        private Label lblLoginStatus = null!;
        private Button btnClientIdStatus = null!;
        private ProgressBar progressBar = null!;

        private ComboBox cmbLanguage = null!;
        private TextBox txtPlaylistName = null!;
        private SymbolToggle btnEditPlaylistName = null!;
        private NumericUpDown numLookbackDays = null!;
        private TextBox txtPlaylistDescription = null!;
        private TextBox txtSpotifyClientId = null!;
        private SymbolToggle btnEditSpotifyClientId = null!;

        private SymbolToggle btnLogin = null!;
        private Button btnCancel = null!;
        private Button btnOpenReport = null!;
        private Button btnUpdatePlaylist = null!;
        private Button btnUnreleasedSongs = null!;

        private TextBox txtOutput = null!;
        private ToolTip spotifyAuthToolTip = null!;
        private System.Windows.Forms.Timer runAvailabilityTimer = null!;
        private Label lblElapsedTime = null!;
        private System.Threading.Timer? elapsedTimeTimer;
        private DateTime runStartTimeUtc;
        private bool isSpotifySignedIn;
        private bool isSpotifyAuthLocked;
        private bool isLoadingSettings;

        public string PlaylistName
        {
            get { return txtPlaylistName.Text.Trim(); }
        }

        public int ReleaseLookbackDays
        {
            get { return (int)numLookbackDays.Value; }
        }

        public string CustomSpotifyClientId
        {
            get { return txtSpotifyClientId.Text.Trim(); }
        }

        public bool IsPlaylistNameReadOnly
        {
            get { return txtPlaylistName.ReadOnly; }
        }

        public bool IsSpotifyClientIdReadOnly
        {
            get { return txtSpotifyClientId.ReadOnly; }
        }

        public bool IsSpotifySignedIn
        {
            get { return isSpotifySignedIn; }
        }

        /// <summary>True once the window has agreed to close (after the "close during a run?" question).</summary>
        public bool IsClosing { get; private set; }

        public bool IsShuttingDown => IsClosing || IsDisposed;

        public MainForm() : base(
            FormOptions.CreateStandard(
                title: AppLocalization.Get(AppStrings.MainFormWindowTitle),
                titleTextAlign: ContentAlignment.MiddleCenter,
                backColor: UIStyles.Colors.BackgroundDarkElevated,
                icon: LoadTitleImage(),
                windowIcon: LoadWindowIcon()))
        {
            InitializeComponent();

            dataPaths = AppDataPaths.CreateDefault();
            settingsStore = new JsonSettingsStore(dataPaths);
            tokenStore = new FileTokenStore(dataPaths);
            clientIdProvider = new SettingsSpotifyClientIdProvider(settingsStore);
            reportWriter = new HtmlReportWriter(dataPaths);

            authService = new SpotifyAuthService(
                tokenStore,
                spotifyAccountsHttpClient,
                clientIdProvider);

            spotifyApiClient = new SpotifyApiClient(
                authService,
                spotifyApiHttpClient);

            releaseUpdater = new SpotifyReleaseUpdater(
                spotifyApiClient,
                settingsStore,
                reportWriter,
                releaseClock);

            appUpdater = new AppUpdater(
                "Erik513",
                "ReleaseUpdaterForSpotify",
                updateCheckHttpClient,
                selfUpdateHttpClient);

            trackedSongsStore = new TrackedSongsStore(dataPaths);
            unreleasedSongsReportWriter = new UnreleasedSongsReportWriter(dataPaths);
            unreleasedSongChecker = new UnreleasedSongChecker(
                spotifyApiClient,
                unreleasedSongsReportWriter);

            controller = new MainFormController(
                this,
                settingsStore,
                reportWriter,
                authService,
                releaseUpdater);

            saveSettingsLater = new DelayedAction(controller.SaveSettingsFromUi, SettingsSaveDelayMilliseconds);

            Size = MinimumSize;
            MinimumSize = new Size(520, 740);

            BuildUi();
            StartRunAvailabilityTimer();
            CenterToScreen();

            AppLocalization.LanguageChanged += AppLocalization_LanguageChanged;
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            await controller.LoadSettingsAsync();
        }

        public ReleaseSettings GetSettings()
        {
            return new ReleaseSettings
            {
                PlaylistId = currentPlaylistId,
                PlaylistName = PlaylistName,
                PlaylistDescription = txtPlaylistDescription.Text,
                ReleaseLookbackDays = ReleaseLookbackDays,
                CustomSpotifyClientId = CustomSpotifyClientId,
                Language = AppLocalization.Language == AppLanguage.German ? "German" : "English"
            }.Normalize();
        }

        public void SetSettings(ReleaseSettings settings)
        {
            isLoadingSettings = true;

            try
            {
                ReleaseSettings normalized = settings.Normalize();
                currentPlaylistId = normalized.PlaylistId;

                txtPlaylistName.Text = normalized.PlaylistName;
                txtPlaylistDescription.Text = normalized.PlaylistDescription ?? string.Empty;
                numLookbackDays.Value = ClampForControl(
                    normalized.ReleaseLookbackDays,
                    numLookbackDays);
                txtSpotifyClientId.Text = normalized.CustomSpotifyClientId ?? string.Empty;

                ApplyLanguage(normalized.Language);
            }
            finally
            {
                isLoadingSettings = false;
            }

        }

        // Applies the language to ErikwnkWFUI's own built-in dialogs (title bar
        // tooltips, message boxes, update prompt) and to AppLocalization, which
        // covers this app's own UI text - both switch together so the whole
        // window (including ErikwnkWFUI's dialogs) stays in one language.
        private static void ApplyLanguage(string language)
        {
            UIStyles.SetLanguage(
                string.Equals(language, "German", StringComparison.OrdinalIgnoreCase)
                    ? AppLanguage.German
                    : AppLanguage.English);
        }

        public void SetStatus(string status)
        {
            lblStatus.Text = status;
        }

        public void SetLoginStatus(string status)
        {
            lblLoginStatus.Text = status;
        }

        public void SetClientIdStatus(
            string status,
            ClientIdStatusKind statusKind)
        {
            UIStyles.Buttons.SetColors(
                btnClientIdStatus,
                UIStyles.Colors.CreateSet(GetClientIdStatusColor(statusKind)));
            btnClientIdStatus.AccessibleName = status;
            spotifyAuthToolTip?.SetToolTip(btnClientIdStatus, status);
        }

        public void SetAuthenticationBusy()
        {
            isSpotifyAuthLocked = true;
            btnLogin.Enabled = true;
            SetSpotifyAuthToolTip(
                isSpotifySignedIn
                    ? AppLocalization.Get(AppStrings.MainFormSignOutBusyTooltip)
                    : AppLocalization.Get(AppStrings.MainFormSignInBusyTooltip));
        }

        public void SetAuthenticationState(bool isLoggedIn)
        {
            isSpotifyAuthLocked = false;
            isSpotifySignedIn = isLoggedIn;
            btnLogin.Enabled = true;
            btnLogin.ShowSecond(isLoggedIn);
            SetSpotifyAuthToolTip(
                isLoggedIn
                    ? AppLocalization.Get(AppStrings.MainFormSignOutTooltip)
                    : AppLocalization.Get(AppStrings.MainFormSignInTooltip));
        }

        public void SetUpdateAvailability(bool isAvailable, string tooltip)
        {
            bool isSettingEditActive = IsSettingEditActive();
            btnUpdatePlaylist.Enabled = isAvailable && !isSettingEditActive;
            spotifyAuthToolTip?.SetToolTip(
                btnUpdatePlaylist,
                isSettingEditActive
                    ? AppLocalization.Get(AppStrings.MainFormSaveSettingBeforeUpdateTooltip)
                    : tooltip);
        }

        public void ApplyProgress(ReleaseProgress progress)
        {
            // (Progress<T> posts to the UI thread, so a report can arrive after the window closed.)
            if (IsClosing || IsDisposed)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(progress.Status))
            {
                lblStatus.Text = progress.Status;
            }

            if (progress.Current.HasValue && progress.Total.HasValue)
            {
                int percent = progress.Percent ?? 0;
                progressBar.Value = Math.Min(100, Math.Max(0, percent));
                lblProgress.Text = AppLocalization.Get(
                    AppStrings.MainFormProgressFormat,
                    progress.Current,
                    progress.Total,
                    percent);
            }

            if (!string.IsNullOrWhiteSpace(progress.Message))
            {
                txtOutput.AppendText(progress.Message + Environment.NewLine);
            }
        }

        public void PrepareRunUi()
        {
            lblStatus.Text = AppLocalization.Get(AppStrings.MainFormStarting);
            lblProgress.Text = "-";
            progressBar.Value = 0;
            txtOutput.Clear();

            btnUpdatePlaylist.Enabled = false;
            isSpotifyAuthLocked = true;
            btnLogin.Enabled = true;
            btnCancel.Enabled = true;
            SetSpotifyAuthToolTip(
                AppLocalization.Get(AppStrings.MainFormSignInDisabledDuringRunTooltip));

            runStartTimeUtc = DateTime.UtcNow;
            lblElapsedTime.Visible = true;
            UpdateElapsedTimeLabel();

            elapsedTimeTimer?.Dispose();
            elapsedTimeTimer = new System.Threading.Timer(
                _ => RequestElapsedTimeLabelUpdate(),
                null,
                1000,
                1000);
        }

        /// <summary>
        /// Stops the elapsed-time display, hides it, and logs the final duration -
        /// the timer itself is only meant to be visible while a run is in progress.
        /// </summary>
        public void StopElapsedTimer()
        {
            elapsedTimeTimer?.Dispose();
            elapsedTimeTimer = null;

            TimeSpan elapsed = DateTime.UtcNow - runStartTimeUtc;
            lblElapsedTime.Visible = false;
            txtOutput.AppendText(
                AppLocalization.Get(AppStrings.MainFormFinishedInFormat, elapsed.ToString(@"hh\:mm\:ss")) +
                Environment.NewLine);
        }

        /// <summary>
        /// Runs on a threadpool thread (not the UI message queue), so the elapsed
        /// display keeps ticking on time even while the UI thread is busy handling a
        /// burst of progress updates - unlike a WinForms Timer, which relies on the
        /// low-priority WM_TIMER message and can visibly stall under that load.
        /// </summary>
        private void RequestElapsedTimeLabelUpdate()
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(UpdateElapsedTimeLabel);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void UpdateElapsedTimeLabel()
        {
            TimeSpan elapsed = DateTime.UtcNow - runStartTimeUtc;
            lblElapsedTime.Text = AppLocalization.Get(
                AppStrings.MainFormElapsedFormat,
                elapsed.ToString(@"hh\:mm\:ss"));
        }

        public void ShowCompleted(PlaylistUpdateResult result)
        {
            btnUpdatePlaylist.Enabled = true;
            isSpotifyAuthLocked = false;
            btnLogin.Enabled = true;
            btnCancel.Enabled = false;
            progressBar.Value = 100;

            lblStatus.Text = result.UniqueTrackCount == 0
                ? AppLocalization.Get(AppStrings.NoNewSongs)
                : AppLocalization.Get(AppStrings.Done);

            txtOutput.AppendText(AppLocalization.Get(AppStrings.MainFormHtmlReportUpdated) + Environment.NewLine);

            ToastForm.ShowToast(
                result.UniqueTrackCount == 0
                    ? AppLocalization.Get(AppStrings.MainFormToastNoNewSongs)
                    : AppLocalization.Get(AppStrings.MainFormToastPlaylistUpdated),
                this);
        }

        public void ShowRunFailed(string message)
        {
            btnUpdatePlaylist.Enabled = true;
            isSpotifyAuthLocked = false;
            btnLogin.Enabled = true;
            btnCancel.Enabled = false;

            string errorTitle = AppLocalization.Get(AppStrings.MainFormError);
            lblStatus.Text = errorTitle;
            txtOutput.AppendText(AppLocalization.Get(AppStrings.MainFormErrorLogFormat, message) + Environment.NewLine);
            ShowError(message, errorTitle);
        }

        public void ShowCancelled()
        {
            btnCancel.Enabled = false;
            btnUpdatePlaylist.Enabled = true;
            isSpotifyAuthLocked = false;
            btnLogin.Enabled = true;

            lblStatus.Text = AppLocalization.Get(AppStrings.MainFormCancelled);
            txtOutput.AppendText(
                AppLocalization.Get(AppStrings.MainFormUpdateCancelledLog) + Environment.NewLine);
        }

        public void AppendOutput(string message)
        {
            txtOutput.AppendText(message + Environment.NewLine);
        }

        public void EnablePlaylistNameEditing()
        {
            txtPlaylistName.ReadOnly = false;
            txtPlaylistName.TabStop = true;
            btnEditPlaylistName.ShowSecond(true);
            btnUpdatePlaylist.Enabled = false;

            txtPlaylistName.Focus();
            txtPlaylistName.SelectAll();
        }

        public void DisablePlaylistNameEditing()
        {
            txtPlaylistName.ReadOnly = true;
            txtPlaylistName.TabStop = false;
            btnEditPlaylistName.ShowSecond(false);

            txtPlaylistName.SelectionLength = 0;
            btnEditPlaylistName.Focus();
        }

        public void SetPlaylistNameError(string? message)
        {
            // (The factories give the standard type; the box is the library's TextField.)
            if (txtPlaylistName is ErikwnkWFUI.Controls.TextField field)
            {
                field.ErrorText = message;
            }
        }

        public void FocusPlaylistName()
        {
            txtPlaylistName.Focus();
            txtPlaylistName.SelectAll();
        }

        public void EnableSpotifyClientIdEditing()
        {
            txtSpotifyClientId.ReadOnly = false;
            txtSpotifyClientId.TabStop = true;
            btnEditSpotifyClientId.ShowSecond(true);
            btnUpdatePlaylist.Enabled = false;

            txtSpotifyClientId.Focus();
            txtSpotifyClientId.SelectAll();
        }

        public void DisableSpotifyClientIdEditing()
        {
            txtSpotifyClientId.ReadOnly = true;
            txtSpotifyClientId.TabStop = false;
            btnEditSpotifyClientId.ShowSecond(false);

            txtSpotifyClientId.SelectionLength = 0;
            btnEditSpotifyClientId.Focus();
        }

        public void FocusSpotifyClientId()
        {
            txtSpotifyClientId.Focus();
            txtSpotifyClientId.SelectAll();
        }

        public void ShowInfo(string message, string title)
        {
            Dialogs.Info(this, message, title);
        }

        public void ShowWarning(string message, string title)
        {
            Dialogs.Warning(this, message, title);
        }

        public void ShowError(string message, string title)
        {
            Dialogs.Error(this, message, title);
        }

        public void ShowToast(string message)
        {
            ToastForm.ShowToast(message, this);
        }

        public bool ConfirmStart()
        {
            DialogResult result = MessageBox.Show(
                AppLocalization.Get(AppStrings.ControllerConfirmStartMessage),
                AppLocalization.Get(AppStrings.UpdatePlaylist),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                this,
                FormSize.Medium,
                defaultButton: MessageBoxDefault.Button1);

            return result == DialogResult.Yes;
        }

        // Asks before the window closes mid-run, since the playlist may be left only partially
        // updated if the run is interrupted now.
        private bool ConfirmCloseWhileRunning()
        {
            DialogResult result = MessageBox.Show(
                AppLocalization.Get(AppStrings.ControllerConfirmCloseMessage),
                AppLocalization.Get(AppStrings.ControllerConfirmCloseTitle),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                this,
                FormSize.Small);

            return result == DialogResult.Yes;
        }

        public void BeginUpdateCheck()
        {
            Version currentVersion =
                System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
                    ?? new Version(0, 0, 0);

            _ = appUpdater.CheckForUpdateAsync(currentVersion, UpdateCheckTimeout, this);
        }

        // Refreshes every piece of static (state-independent) UI text after a
        // language switch (the property table's own row labels follow the
        // language by themselves, see BuildPropertyTableRows). Dynamic text
        // (login status, client ID status, run availability tooltip) is
        // refreshed separately by the controller, since it depends on app
        // state this form doesn't track on its own.
        private void RefreshLocalizedUi()
        {
            Text = AppLocalization.Get(AppStrings.MainFormWindowTitle);

            txtPlaylistDescription.PlaceholderText = AppLocalization.Get(AppStrings.MainFormPlaylistDescriptionPlaceholder);
            spotifyAuthToolTip?.SetToolTip(
                txtPlaylistDescription,
                AppLocalization.Get(AppStrings.MainFormPlaylistDescriptionTooltip));
            txtSpotifyClientId.PlaceholderText = AppLocalization.Get(AppStrings.MainFormClientIdPlaceholder);
            spotifyAuthToolTip?.SetToolTip(
                txtSpotifyClientId,
                AppLocalization.Get(AppStrings.MainFormClientIdTooltip));

            btnLogin.SetTooltips(
                AppLocalization.Get(AppStrings.MainFormSignInTooltip),
                AppLocalization.Get(AppStrings.MainFormSignOutTooltip));
            btnLogin.SetAccessibleNames(
                AppLocalization.Get(AppStrings.MainFormSignInAccessibleName),
                AppLocalization.Get(AppStrings.MainFormSignOutAccessibleName));
            btnEditPlaylistName.SetTooltips(
                AppLocalization.Get(AppStrings.EditPlaylistName),
                AppLocalization.Get(AppStrings.MainFormSavePlaylistName));
            btnEditSpotifyClientId.SetTooltips(
                AppLocalization.Get(AppStrings.EditSpotifyClientId),
                AppLocalization.Get(AppStrings.MainFormSaveSpotifyClientId));

            UIStyles.Buttons.UpdateTooltip(btnOpenReport, AppLocalization.Get(AppStrings.MainFormOpenReport));
            UIStyles.Buttons.UpdateTooltip(btnCancel, AppLocalization.Get(AppStrings.MainFormCancelUpdate));
            UIStyles.Buttons.UpdateTooltip(btnUnreleasedSongs, AppLocalization.Get(AppStrings.MainFormOpenUnreleasedSongs));
            spotifyAuthToolTip?.SetToolTip(btnUpdatePlaylist, AppLocalization.Get(AppStrings.UpdatePlaylist));

            _ = controller.RefreshLocalizedUiAsync();
        }

        private void AppLocalization_LanguageChanged(object? sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            // A choice in the language box (not the loading of the settings) is kept.
            bool keepChoice = !isLoadingSettings;

            BeginInvoke(() =>
            {
                RefreshLocalizedUi();

                if (keepChoice && !IsShuttingDown)
                {
                    controller.SaveSettingsFromUi();
                }
            });
        }

        private void SettingsControl_Changed(object? sender, EventArgs e)
        {
            if (isLoadingSettings)
            {
                return;
            }

            // Held back a moment: holding the arrow of the number box changes the value many times a
            // second, and each change used to rewrite the settings file.
            saveSettingsLater?.Schedule();
        }

        private async void BtnLogin_Click(object? sender, EventArgs e)
        {
            if (isSpotifyAuthLocked)
            {
                // A sign-in that waits for the browser can be ended by pressing the button again.
                if (controller.IsLoginInProgress)
                {
                    controller.CancelLogin();
                }

                return;
            }

            if (IsSpotifySignedIn)
            {
                await controller.LogoutAsync();
                return;
            }

            await controller.LoginAsync();
        }

        private async void BtnUpdatePlaylist_Click(object? sender, EventArgs e)
        {
            await controller.StartUpdateAsync();
        }

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            controller.CancelUpdate();
        }

        private void BtnOpenReport_Click(object? sender, EventArgs e)
        {
            controller.OpenReport();
        }

        private void BtnEditPlaylistName_Click(object? sender, EventArgs e)
        {
            controller.TogglePlaylistNameEdit();
        }

        private void BtnEditSpotifyClientId_Click(object? sender, EventArgs e)
        {
            controller.ToggleSpotifyClientIdEdit();
        }

        private void BtnClientIdHelp_Click(object? sender, EventArgs e)
        {
            using SpotifyClientIdHelpForm helpForm = new();
            helpForm.ShowDialog(this);
        }

        private void BtnUnreleasedSongs_Click(object? sender, EventArgs e)
        {
            using UnreleasedSongsForm form = new(
                trackedSongsStore,
                unreleasedSongsReportWriter,
                unreleasedSongChecker);

            form.ShowDialog(this);
        }

        private void RunAvailabilityTimer_Tick(object? sender, EventArgs e)
        {
            controller.RefreshRunAvailability();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (controller.IsRunInProgress && !ConfirmCloseWhileRunning())
            {
                e.Cancel = true;
                return;
            }

            // From here on a run that is still ending must not touch this window or the
            // HTTP clients below (see MainFormController.IsShuttingDown).
            // A change that was still waiting for its save is written now, not lost with the window.
            saveSettingsLater?.Flush();
            saveSettingsLater?.Dispose();
            saveSettingsLater = null;

            IsClosing = true;
            controller.CancelLogin();
            controller.CancelUpdate();
            AppLocalization.LanguageChanged -= AppLocalization_LanguageChanged;
            spotifyAuthToolTip?.Dispose();
            runAvailabilityTimer?.Stop();
            runAvailabilityTimer?.Dispose();
            elapsedTimeTimer?.Dispose();
            spotifyAccountsHttpClient.Dispose();
            spotifyApiHttpClient.Dispose();
            updateCheckHttpClient.Dispose();
            selfUpdateHttpClient.Dispose();
            base.OnFormClosing(e);
        }

        private void StartRunAvailabilityTimer()
        {
            runAvailabilityTimer = new System.Windows.Forms.Timer
            {
                Interval = 30_000
            };
            runAvailabilityTimer.Tick += RunAvailabilityTimer_Tick;
            runAvailabilityTimer.Start();
        }

        private static decimal ClampForControl(int value, NumericUpDown control)
        {
            return Math.Min(
                control.Maximum,
                Math.Max(control.Minimum, value));
        }

        // Embedded (not loose Content files) so the logo and window icon survive being
        // distributed as a single self-contained exe with nothing copied alongside it.
        private static Image? LoadTitleImage()
        {
            try
            {
                using Stream? stream = typeof(MainForm).Assembly.GetManifestResourceStream(
                    "ReleaseUpdaterGui.Assets.Pictures.SpotifyReleaseUpdater.png");

                if (stream is null)
                {
                    return null;
                }

                using Bitmap loaded = new(stream);
                return new Bitmap(loaded);
            }
            catch
            {
                return null;
            }
        }

        private static Icon? LoadWindowIcon()
        {
            try
            {
                using Stream? stream = typeof(MainForm).Assembly.GetManifestResourceStream(
                    "ReleaseUpdaterGui.Assets.Icons.SpotifyReleaseUpdater_48px.ico");

                if (stream is null)
                {
                    return null;
                }

                using Icon loaded = new(stream);
                return new Icon(loaded, loaded.Size);
            }
            catch
            {
                return null;
            }
        }

        private void SetSpotifyAuthToolTip(string text)
        {
            btnLogin.SetTooltips(text, text);
        }

        private bool IsSettingEditActive()
        {
            return !txtPlaylistName.ReadOnly || !txtSpotifyClientId.ReadOnly;
        }

        private static Color GetClientIdStatusColor(
            ClientIdStatusKind statusKind)
        {
            return statusKind switch
            {
                ClientIdStatusKind.Missing => Color.FromArgb(190, 55, 55),
                ClientIdStatusKind.Configured => Color.FromArgb(40, 115, 180),
                ClientIdStatusKind.TestMode => Color.FromArgb(33, 150, 83),
                ClientIdStatusKind.Cooldown => Color.FromArgb(190, 55, 55),
                _ => Color.FromArgb(90, 100, 110)
            };
        }

    }

}
