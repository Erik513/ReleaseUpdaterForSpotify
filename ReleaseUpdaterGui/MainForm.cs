using ErikwnkCore;
using ErikwnkWFUI;
using ErikwnkWFUI.Controls;
using ErikwnkWFUI.Forms;
using ReleaseUpdater.Data;
using ReleaseUpdater.Core.Localization;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Core.Services;
using ReleaseUpdater.Data.Reports;
using ReleaseUpdater.Data.Storage;
using ReleaseUpdater.Spotify.Api;
using ReleaseUpdater.Spotify.Auth;

namespace ReleaseUpdaterGui
{
    public partial class MainForm : StyledForm
    {
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
        private Button btnEditPlaylistName = null!;
        private NumericUpDown numLookbackDays = null!;
        private TextBox txtSpotifyClientId = null!;
        private Button btnEditSpotifyClientId = null!;

        private Button btnLogin = null!;
        private Button btnCancel = null!;
        private Button btnOpenReport = null!;
        private Button btnUpdatePlaylist = null!;

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

        public MainForm() : base(
            StyledFormOptions.CreateStandard(
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

            controller = new MainFormController(
                this,
                settingsStore,
                reportWriter,
                authService,
                releaseUpdater,
                appUpdater);

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
                ReleaseLookbackDays = ReleaseLookbackDays,
                CustomSpotifyClientId = CustomSpotifyClientId,
                Language = (cmbLanguage.SelectedItem as LanguageOption)?.Value ?? "English"
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
                numLookbackDays.Value = ClampForControl(
                    normalized.ReleaseLookbackDays,
                    numLookbackDays);
                txtSpotifyClientId.Text = normalized.CustomSpotifyClientId ?? string.Empty;

                SelectLanguageOption(normalized.Language);
                ApplyLanguage(normalized.Language);
            }
            finally
            {
                isLoadingSettings = false;
            }

        }

        private void SelectLanguageOption(string language)
        {
            foreach (object item in cmbLanguage.Items)
            {
                if (item is LanguageOption option &&
                    string.Equals(option.Value, language, StringComparison.OrdinalIgnoreCase))
                {
                    cmbLanguage.SelectedItem = option;
                    return;
                }
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
            Color backColor = GetClientIdStatusColor(statusKind);

            btnClientIdStatus.BackColor = backColor;
            btnClientIdStatus.ForeColor = Color.White;
            btnClientIdStatus.AccessibleName = status;
            btnClientIdStatus.FlatAppearance.MouseOverBackColor =
                ControlPaint.Light(backColor);
            btnClientIdStatus.FlatAppearance.MouseDownBackColor =
                ControlPaint.Dark(backColor);
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
            SetButtonSymbol(
                btnLogin,
                isLoggedIn ? ButtonSymbolKind.Logout : ButtonSymbolKind.Login,
                isLoggedIn
                    ? AppLocalization.Get(AppStrings.MainFormSignOutAccessibleName)
                    : AppLocalization.Get(AppStrings.MainFormSignInAccessibleName));
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
            SetButtonSymbol(
                btnEditPlaylistName,
                ButtonSymbolKind.Check,
                AppLocalization.Get(AppStrings.MainFormSavePlaylistName));
            btnUpdatePlaylist.Enabled = false;

            txtPlaylistName.Focus();
            txtPlaylistName.SelectAll();
        }

        public void DisablePlaylistNameEditing()
        {
            txtPlaylistName.ReadOnly = true;
            txtPlaylistName.TabStop = false;
            SetButtonSymbol(
                btnEditPlaylistName,
                ButtonSymbolKind.Edit,
                AppLocalization.Get(AppStrings.EditPlaylistName));

            txtPlaylistName.SelectionLength = 0;
            btnEditPlaylistName.Focus();
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
            SetButtonSymbol(
                btnEditSpotifyClientId,
                ButtonSymbolKind.Check,
                AppLocalization.Get(AppStrings.MainFormSaveSpotifyClientId));
            btnUpdatePlaylist.Enabled = false;

            txtSpotifyClientId.Focus();
            txtSpotifyClientId.SelectAll();
        }

        public void DisableSpotifyClientIdEditing()
        {
            txtSpotifyClientId.ReadOnly = true;
            txtSpotifyClientId.TabStop = false;
            SetButtonSymbol(
                btnEditSpotifyClientId,
                ButtonSymbolKind.Edit,
                AppLocalization.Get(AppStrings.EditSpotifyClientId));

            txtSpotifyClientId.SelectionLength = 0;
            btnEditSpotifyClientId.Focus();
        }

        public void FocusSpotifyClientId()
        {
            txtSpotifyClientId.Focus();
            txtSpotifyClientId.SelectAll();
        }

        public void ShowInfo(
            string message,
            string title,
            MessageBoxSize size = MessageBoxSize.Small)
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Info,
                this,
                size);
        }

        public void ShowWarning(string message, string title)
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning,
                this,
                MessageBoxSize.Small);
        }

        public void ShowError(string message, string title)
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                this,
                MessageBoxSize.Small);
        }

        private void BuildUi()
        {
            Panel mainPanel = UIStyles.Panels.CreateElevated();
            mainPanel.BackColor = UIStyles.Colors.BackgroundLight;
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = new Padding(16);

            propertyTable = new PropertyTable
            {
                Dock = DockStyle.Top,
                Padding = new Padding(5)
            };

            lblStatus = UIStyles.Labels.CreateNormal(AppLocalization.Get(AppStrings.ReadyToRun));
            lblProgress = UIStyles.Labels.CreateNormal(AppLocalization.Get(AppStrings.MainFormInitialProgress));
            lblLoginStatus = UIStyles.Labels.CreateNormal(AppLocalization.Get(AppStrings.NotSignedIn));

            progressBar = UIStyles.ProgressBars.CreateGreen();
            progressBar.Dock = DockStyle.Top;
            progressBar.Height = 24;
            progressBar.Maximum = 100;
            progressBar.Value = 0;

            spotifyAuthToolTip = UIStyles.ToolTips.CreateToolTip();

            btnLogin = UIStyles.Buttons.CreateStandard(
                string.Empty,
                string.Empty,
                PropertyIconButtonSize,
                true);
            SetButtonSymbol(
                btnLogin,
                ButtonSymbolKind.Login,
                AppLocalization.Get(AppStrings.MainFormSignInAccessibleName));
            SetSpotifyAuthToolTip(
                AppLocalization.Get(AppStrings.MainFormSignInTooltip));
            btnLogin.Click += BtnLogin_Click;

            btnClientIdStatus = UIStyles.Buttons.CreateStandard(
                string.Empty,
                string.Empty,
                PropertyIconButtonSize,
                true);
            SetButtonSymbol(
                btnClientIdStatus,
                ButtonSymbolKind.Status,
                AppLocalization.Get(AppStrings.MainFormClientIdStatusAccessibleName));
            SetClientIdStatus(
                AppLocalization.Get(AppStrings.MainFormClientIdStatusInitial),
                ClientIdStatusKind.Info);
            btnClientIdStatus.Click += BtnClientIdHelp_Click;

            cmbLanguage = UIStyles.ComboBoxes.CreateStandard();
            cmbLanguage.Items.Add(new LanguageOption("English", "English"));
            cmbLanguage.Items.Add(new LanguageOption("German", "Deutsch"));
            cmbLanguage.SelectedIndex = 0;
            cmbLanguage.TabStop = false;
            cmbLanguage.SelectedIndexChanged += CmbLanguage_SelectedIndexChanged;

            txtPlaylistName = UIStyles.TextBoxes.CreateBorderstyleNone(
                ReleaseDefaults.PlaylistName);
            txtPlaylistName.ReadOnly = true;
            txtPlaylistName.TabStop = false;

            btnEditPlaylistName = UIStyles.Buttons.CreateStandard(
                string.Empty,
                AppLocalization.Get(AppStrings.EditPlaylistName),
                PropertyIconButtonSize,
                true);
            SetButtonSymbol(
                btnEditPlaylistName,
                ButtonSymbolKind.Edit,
                AppLocalization.Get(AppStrings.EditPlaylistName));
            btnEditPlaylistName.Click += BtnEditPlaylistName_Click;

            numLookbackDays = UIStyles.NumericUpDowns.CreateStandard(
                ReleaseDefaults.MinReleaseLookbackDays,
                ReleaseDefaults.MaxReleaseLookbackDays,
                1,
                ReleaseDefaults.ReleaseLookbackDays);
            numLookbackDays.TabStop = false;
            numLookbackDays.ValueChanged += SettingsControl_Changed;

            txtSpotifyClientId = UIStyles.TextBoxes.CreateBorderstyleNone(
                string.Empty);
            txtSpotifyClientId.PlaceholderText = AppLocalization.Get(AppStrings.MainFormClientIdPlaceholder);
            txtSpotifyClientId.ReadOnly = true;
            txtSpotifyClientId.TabStop = false;
            spotifyAuthToolTip.SetToolTip(
                txtSpotifyClientId,
                AppLocalization.Get(AppStrings.MainFormClientIdTooltip));

            btnEditSpotifyClientId = UIStyles.Buttons.CreateStandard(
                string.Empty,
                AppLocalization.Get(AppStrings.EditSpotifyClientId),
                PropertyIconButtonSize,
                true);
            SetButtonSymbol(
                btnEditSpotifyClientId,
                ButtonSymbolKind.Edit,
                AppLocalization.Get(AppStrings.EditSpotifyClientId));
            btnEditSpotifyClientId.Click += BtnEditSpotifyClientId_Click;

            BuildPropertyTableRows();

            TableLayoutPanel bottomPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 3 * ActionIconButtonSize.Width + 16));
            bottomPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            FlowLayoutPanel buttonPanel = UIStyles.FlowLayoutPanels.CreateStandard();
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.FlowDirection = FlowDirection.RightToLeft;
            buttonPanel.Padding = new Padding(0, 8, 0, 0);

            lblElapsedTime = UIStyles.Labels.CreateNormal(string.Empty);
            lblElapsedTime.Dock = DockStyle.Fill;
            lblElapsedTime.TextAlign = ContentAlignment.MiddleLeft;
            lblElapsedTime.Padding = new Padding(4, 0, 0, 0);
            lblElapsedTime.Visible = false;

            // Transparent flickers here: repainting this label every second (while a
            // run is in progress) forces it to redraw its parent's background first,
            // then the text, which is visible as a brief flash. A solid color matching
            // the panel behind it paints in a single pass instead.
            lblElapsedTime.BackColor = UIStyles.Colors.BackgroundLight;

            btnUpdatePlaylist = UIStyles.Buttons.CreateGreen(
                string.Empty,
                AppLocalization.Get(AppStrings.UpdatePlaylist),
                ActionIconButtonSize);
            SetButtonSymbol(
                btnUpdatePlaylist,
                ButtonSymbolKind.Play,
                AppLocalization.Get(AppStrings.UpdatePlaylist));
            btnUpdatePlaylist.Click += BtnUpdatePlaylist_Click;
            spotifyAuthToolTip.SetToolTip(
                btnUpdatePlaylist,
                AppLocalization.Get(AppStrings.UpdatePlaylist));

            btnOpenReport = UIStyles.Buttons.CreateStandard(
                string.Empty,
                AppLocalization.Get(AppStrings.MainFormOpenReport),
                ActionIconButtonSize);
            SetButtonSymbol(
                btnOpenReport,
                ButtonSymbolKind.Report,
                AppLocalization.Get(AppStrings.MainFormOpenReport));
            btnOpenReport.Click += BtnOpenReport_Click;

            btnCancel = UIStyles.Buttons.CreateRed(
                string.Empty,
                AppLocalization.Get(AppStrings.MainFormCancelUpdate),
                ActionIconButtonSize);
            SetButtonSymbol(
                btnCancel,
                ButtonSymbolKind.Stop,
                AppLocalization.Get(AppStrings.MainFormCancelUpdate));
            btnCancel.Enabled = false;
            btnCancel.Click += BtnCancel_Click;

            buttonPanel.Controls.Add(btnUpdatePlaylist);
            buttonPanel.Controls.Add(btnOpenReport);
            buttonPanel.Controls.Add(btnCancel);

            bottomPanel.Controls.Add(lblElapsedTime, 0, 0);
            bottomPanel.Controls.Add(buttonPanel, 1, 0);

            txtOutput = UIStyles.TextBoxes.CreateStandard();
            txtOutput.Dock = DockStyle.Fill;
            txtOutput.Multiline = true;
            txtOutput.ScrollBars = ScrollBars.Vertical;
            txtOutput.WordWrap = true;
            txtOutput.ReadOnly = true;
            txtOutput.TabStop = true;
            txtOutput.Font = UIStyles.Fonts.Monospace;

            mainPanel.Controls.Add(txtOutput);
            mainPanel.Controls.Add(bottomPanel);
            mainPanel.Controls.Add(propertyTable);

            ContentPanel.Controls.Add(mainPanel);
        }

        // Split out from BuildUi so a language switch can rebuild just the
        // row labels: PropertyTable.ClearRows() is documented as being
        // exactly for this (re-adding the same editor controls under fresh
        // labels without losing their state).
        private void BuildPropertyTableRows()
        {
            propertyTable.AddRow(
                AppLocalization.Get(AppStrings.MainFormRowStatus),
                PropertyRowHeight,
                UIColumn.Percent(lblStatus, 100),
                UIColumn.Absolute(btnClientIdStatus, PropertyIconColumnWidth));
            propertyTable.AddRow(
                AppLocalization.Get(AppStrings.MainFormRowProgress),
                PropertyRowHeight,
                UIColumn.Percent(lblProgress, 50),
                UIColumn.Percent(progressBar, 50));
            propertyTable.AddSection(AppLocalization.Get(AppStrings.MainFormSectionSettings));
            propertyTable.AddRow(
                AppLocalization.Get(AppStrings.MainFormRowLanguage),
                PropertyRowHeight,
                UIColumn.Percent(cmbLanguage, 100));
            propertyTable.AddRow(
                AppLocalization.Get(AppStrings.MainFormRowSpotify),
                PropertyRowHeight,
                UIColumn.Percent(lblLoginStatus, 100),
                UIColumn.Absolute(btnLogin, PropertyIconColumnWidth));
            propertyTable.AddRow(
                AppLocalization.Get(AppStrings.MainFormRowClientId),
                PropertyRowHeight,
                UIColumn.Percent(txtSpotifyClientId, 100),
                UIColumn.Absolute(btnEditSpotifyClientId, PropertyIconColumnWidth));
            propertyTable.AddRow(
                AppLocalization.Get(AppStrings.MainFormRowPlaylistName),
                PropertyRowHeight,
                UIColumn.Percent(txtPlaylistName, 100),
                UIColumn.Absolute(btnEditPlaylistName, PropertyIconColumnWidth));
            propertyTable.AddRow(
                AppLocalization.Get(AppStrings.MainFormRowLookbackDays),
                PropertyRowHeight,
                UIColumn.Auto(numLookbackDays));

            CenterPropertyTableText(propertyTable);
        }

        // Refreshes every piece of static (state-independent) UI text after a
        // language switch. Dynamic text (login status, client ID status, run
        // availability tooltip) is refreshed separately by the controller,
        // since it depends on app state this form doesn't track on its own.
        private void RefreshLocalizedUi()
        {
            Text = AppLocalization.Get(AppStrings.MainFormWindowTitle);

            propertyTable.ClearRows();
            BuildPropertyTableRows();

            txtSpotifyClientId.PlaceholderText = AppLocalization.Get(AppStrings.MainFormClientIdPlaceholder);
            spotifyAuthToolTip?.SetToolTip(
                txtSpotifyClientId,
                AppLocalization.Get(AppStrings.MainFormClientIdTooltip));

            SetButtonSymbol(
                btnEditPlaylistName,
                txtPlaylistName.ReadOnly ? ButtonSymbolKind.Edit : ButtonSymbolKind.Check,
                txtPlaylistName.ReadOnly
                    ? AppLocalization.Get(AppStrings.EditPlaylistName)
                    : AppLocalization.Get(AppStrings.MainFormSavePlaylistName));
            SetButtonSymbol(
                btnEditSpotifyClientId,
                txtSpotifyClientId.ReadOnly ? ButtonSymbolKind.Edit : ButtonSymbolKind.Check,
                txtSpotifyClientId.ReadOnly
                    ? AppLocalization.Get(AppStrings.EditSpotifyClientId)
                    : AppLocalization.Get(AppStrings.MainFormSaveSpotifyClientId));

            SetButtonSymbol(
                btnUpdatePlaylist,
                ButtonSymbolKind.Play,
                AppLocalization.Get(AppStrings.UpdatePlaylist));
            SetButtonSymbol(
                btnOpenReport,
                ButtonSymbolKind.Report,
                AppLocalization.Get(AppStrings.MainFormOpenReport));
            spotifyAuthToolTip?.SetToolTip(
                btnOpenReport,
                AppLocalization.Get(AppStrings.MainFormOpenReport));
            SetButtonSymbol(
                btnCancel,
                ButtonSymbolKind.Stop,
                AppLocalization.Get(AppStrings.MainFormCancelUpdate));
            spotifyAuthToolTip?.SetToolTip(
                btnCancel,
                AppLocalization.Get(AppStrings.MainFormCancelUpdate));

            _ = controller.RefreshLocalizedUiAsync();
        }

        private void AppLocalization_LanguageChanged(object? sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            BeginInvoke(RefreshLocalizedUi);
        }

        private void SettingsControl_Changed(object? sender, EventArgs e)
        {
            if (isLoadingSettings)
            {
                return;
            }

            controller.SaveSettingsFromUi();
        }

        private void CmbLanguage_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (isLoadingSettings)
            {
                return;
            }

            ApplyLanguage((cmbLanguage.SelectedItem as LanguageOption)?.Value ?? "English");
            controller.SaveSettingsFromUi();
        }

        private async void BtnLogin_Click(object? sender, EventArgs e)
        {
            if (isSpotifyAuthLocked)
            {
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
            controller.OpenSpotifyClientIdHelp();
        }

        private void RunAvailabilityTimer_Tick(object? sender, EventArgs e)
        {
            controller.RefreshRunAvailability();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (controller.IsRunInProgress && !controller.ConfirmCloseWhileRunning())
            {
                e.Cancel = true;
                return;
            }

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

        private static void SetButtonSymbol(
            Button button,
            ButtonSymbolKind kind,
            string accessibleName)
        {
            Image? previousImage = button.Image;

            button.Image = null;
            button.Text = GetButtonSymbol(kind);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.TextImageRelation = TextImageRelation.TextBeforeImage;
            button.Font = new Font(button.Font.FontFamily, 10.5f, FontStyle.Regular);
            button.Padding = Padding.Empty;
            button.AccessibleName = accessibleName;

            previousImage?.Dispose();
        }

        private void SetSpotifyAuthToolTip(string text)
        {
            spotifyAuthToolTip?.SetToolTip(btnLogin, text);
        }

        private bool IsSettingEditActive()
        {
            return !txtPlaylistName.ReadOnly || !txtSpotifyClientId.ReadOnly;
        }

        private static void CenterPropertyTableText(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is Label label)
                {
                    label.TextAlign = ContentAlignment.MiddleLeft;
                    label.Margin = new Padding(
                        label.Margin.Left,
                        0,
                        label.Margin.Right,
                        0);
                }

                CenterPropertyTableText(child);
            }
        }

        private static string GetButtonSymbol(ButtonSymbolKind kind)
        {
            return kind switch
            {
                ButtonSymbolKind.Play => "\u25B6",
                ButtonSymbolKind.Stop => "\u25A0",
                ButtonSymbolKind.Report => "\U0001F4C4",
                ButtonSymbolKind.Login => "\u21AA",
                ButtonSymbolKind.Logout => "\u21A9",
                ButtonSymbolKind.Edit => "\u270E",
                ButtonSymbolKind.Check => "\u2713",
                ButtonSymbolKind.Status => "\u25CF",
                _ => string.Empty
            };
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

        private enum ButtonSymbolKind
        {
            Play,
            Stop,
            Report,
            Login,
            Logout,
            Edit,
            Check,
            Status
        }

        // Value is the stable, persisted setting ("English"/"German", matching
        // AppLanguage) - DisplayName is what the combo box shows and is always
        // each language's own native name, regardless of which language is
        // currently active, so it doesn't need to change on a language switch.
        private sealed class LanguageOption
        {
            public LanguageOption(string value, string displayName)
            {
                Value = value;
                DisplayName = displayName;
            }

            public string Value { get; }
            public string DisplayName { get; }

            public override string ToString() => DisplayName;
        }
    }

    public enum ClientIdStatusKind
    {
        Info,
        Missing,
        Configured,
        TestMode,
        Cooldown
    }
}
