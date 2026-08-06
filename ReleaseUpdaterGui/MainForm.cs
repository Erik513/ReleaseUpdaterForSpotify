using CustomWFUI;
using CustomWFUI.Controls;
using CustomWFUI.Forms;
using ReleaseUpdater.Data;
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

        private Label lblStatus = null!;
        private Label lblProgress = null!;
        private Label lblLoginStatus = null!;
        private Button btnClientIdStatus = null!;
        private ProgressBar progressBar = null!;

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
                title: "Release Updater for Spotify",
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
                CustomSpotifyClientId = CustomSpotifyClientId
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
            }
            finally
            {
                isLoadingSettings = false;
            }

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
                    ? "Spotify sign-out is currently running."
                    : "Spotify sign-in is currently running.");
        }

        public void SetAuthenticationState(bool isLoggedIn)
        {
            isSpotifyAuthLocked = false;
            isSpotifySignedIn = isLoggedIn;
            btnLogin.Enabled = true;
            SetButtonSymbol(
                btnLogin,
                isLoggedIn ? ButtonSymbolKind.Logout : ButtonSymbolKind.Login,
                isLoggedIn ? "Sign out from Spotify" : "Sign in with Spotify");
            SetSpotifyAuthToolTip(
                isLoggedIn
                    ? "Click to sign out from this app and remove the saved Spotify login."
                    : "Click to open Spotify login in your browser.");
        }

        public void SetUpdateAvailability(bool isAvailable, string tooltip)
        {
            bool isSettingEditActive = IsSettingEditActive();
            btnUpdatePlaylist.Enabled = isAvailable && !isSettingEditActive;
            spotifyAuthToolTip?.SetToolTip(
                btnUpdatePlaylist,
                isSettingEditActive
                    ? "Save the edited setting before updating the playlist."
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
                lblProgress.Text =
                    $"{progress.Current} / {progress.Total} ({percent}%)";
            }

            if (!string.IsNullOrWhiteSpace(progress.Message))
            {
                txtOutput.AppendText(progress.Message + Environment.NewLine);
            }
        }

        public void PrepareRunUi()
        {
            lblStatus.Text = "Starting...";
            lblProgress.Text = "-";
            progressBar.Value = 0;
            txtOutput.Clear();

            btnUpdatePlaylist.Enabled = false;
            isSpotifyAuthLocked = true;
            btnLogin.Enabled = true;
            btnCancel.Enabled = true;
            SetSpotifyAuthToolTip(
                "Spotify sign-in is disabled while the playlist update is running.");
        }

        public void ShowCompleted(PlaylistUpdateResult result)
        {
            btnUpdatePlaylist.Enabled = true;
            isSpotifyAuthLocked = false;
            btnLogin.Enabled = true;
            btnCancel.Enabled = false;
            progressBar.Value = 100;

            lblStatus.Text = result.UniqueTrackCount == 0
                ? "No new songs"
                : "Done";

            txtOutput.AppendText("HTML report updated." + Environment.NewLine);

            ToastForm.ShowToast(
                result.UniqueTrackCount == 0
                    ? "No new songs found."
                    : "Playlist updated successfully.",
                this);
        }

        public void ShowRunFailed(string message)
        {
            btnUpdatePlaylist.Enabled = true;
            isSpotifyAuthLocked = false;
            btnLogin.Enabled = true;
            btnCancel.Enabled = false;

            lblStatus.Text = "Error";
            txtOutput.AppendText("ERROR: " + message + Environment.NewLine);
            ShowError(message, "Error");
        }

        public void ShowCancelled()
        {
            btnCancel.Enabled = false;
            btnUpdatePlaylist.Enabled = true;
            isSpotifyAuthLocked = false;
            btnLogin.Enabled = true;

            lblStatus.Text = "Cancelled";
            txtOutput.AppendText(
                "The update was cancelled." + Environment.NewLine);
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
                "Save playlist name");
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
                "Edit playlist name");

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
                "Save Spotify Client ID");
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
                "Edit Spotify Client ID");

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
            CustomMessageBoxSize size = CustomMessageBoxSize.Small)
        {
            CustomMessageBox.Show(
                message,
                title,
                CustomMessageBoxButtons.OK,
                CustomMessageBoxIcon.Info,
                this,
                size);
        }

        public void ShowWarning(string message, string title)
        {
            CustomMessageBox.Show(
                message,
                title,
                CustomMessageBoxButtons.OK,
                CustomMessageBoxIcon.Warning,
                this,
                CustomMessageBoxSize.Small);
        }

        public void ShowError(string message, string title)
        {
            CustomMessageBox.Show(
                message,
                title,
                CustomMessageBoxButtons.OK,
                CustomMessageBoxIcon.Error,
                this,
                CustomMessageBoxSize.Small);
        }

        private void BuildUi()
        {
            Panel mainPanel = UIStyles.Panels.CreateElevated();
            mainPanel.BackColor = UIStyles.Colors.BackgroundLight;
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = new Padding(16);

            StyledPropertyTable propertyTable = new()
            {
                Dock = DockStyle.Top,
                Padding = new Padding(5)
            };

            lblStatus = UIStyles.Labels.CreateNormal("Ready to run");
            lblProgress = UIStyles.Labels.CreateNormal("0 / 0");
            lblLoginStatus = UIStyles.Labels.CreateNormal("Not signed in");

            progressBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 24,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            spotifyAuthToolTip = UIStyles.ToolTips.CreateToolTip();

            btnLogin = UIStyles.Buttons.CreateStandard(
                string.Empty,
                string.Empty,
                PropertyIconButtonSize,
                true);
            SetButtonSymbol(
                btnLogin,
                ButtonSymbolKind.Login,
                "Sign in with Spotify");
            SetSpotifyAuthToolTip(
                "Click to open Spotify login in your browser.");
            btnLogin.Click += BtnLogin_Click;

            btnClientIdStatus = UIStyles.Buttons.CreateStandard(
                string.Empty,
                string.Empty,
                PropertyIconButtonSize,
                true);
            SetButtonSymbol(
                btnClientIdStatus,
                ButtonSymbolKind.Status,
                "Spotify Client ID status");
            SetClientIdStatus(
                "Spotify Client ID status will appear here.",
                ClientIdStatusKind.Info);
            btnClientIdStatus.Click += BtnClientIdHelp_Click;

            txtPlaylistName = UIStyles.TextBoxes.CreateBorderstyleNone(
                ReleaseDefaults.PlaylistName);
            txtPlaylistName.ReadOnly = true;
            txtPlaylistName.TabStop = false;

            btnEditPlaylistName = UIStyles.Buttons.CreateStandard(
                string.Empty,
                "Edit playlist name",
                PropertyIconButtonSize,
                true);
            SetButtonSymbol(
                btnEditPlaylistName,
                ButtonSymbolKind.Edit,
                "Edit playlist name");
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
            txtSpotifyClientId.PlaceholderText = "Required Spotify Client ID";
            txtSpotifyClientId.ReadOnly = true;
            txtSpotifyClientId.TabStop = false;
            spotifyAuthToolTip.SetToolTip(
                txtSpotifyClientId,
                "Required. Create a Spotify app and enter its Client ID. Do not enter a Client Secret.");

            btnEditSpotifyClientId = UIStyles.Buttons.CreateStandard(
                string.Empty,
                "Edit Spotify Client ID",
                PropertyIconButtonSize,
                true);
            SetButtonSymbol(
                btnEditSpotifyClientId,
                ButtonSymbolKind.Edit,
                "Edit Spotify Client ID");
            btnEditSpotifyClientId.Click += BtnEditSpotifyClientId_Click;

            propertyTable.AddRow(
                "Status",
                PropertyRowHeight,
                UIColumn.Percent(lblStatus, 100),
                UIColumn.Absolute(btnClientIdStatus, PropertyIconColumnWidth));
            propertyTable.AddRow(
                "Progress",
                PropertyRowHeight,
                UIColumn.Percent(lblProgress, 50),
                UIColumn.Percent(progressBar, 50));
            propertyTable.AddSection("Settings");
            propertyTable.AddRow(
                "Spotify",
                PropertyRowHeight,
                UIColumn.Percent(lblLoginStatus, 100),
                UIColumn.Absolute(btnLogin, PropertyIconColumnWidth));
            propertyTable.AddRow(
                "Client ID",
                PropertyRowHeight,
                UIColumn.Percent(txtSpotifyClientId, 100),
                UIColumn.Absolute(btnEditSpotifyClientId, PropertyIconColumnWidth));
            propertyTable.AddRow(
                "Playlist name",
                PropertyRowHeight,
                UIColumn.Percent(txtPlaylistName, 100),
                UIColumn.Absolute(btnEditPlaylistName, PropertyIconColumnWidth));
            propertyTable.AddRow(
                "Lookback days",
                PropertyRowHeight,
                UIColumn.Auto(numLookbackDays));

            CenterPropertyTableText(propertyTable);

            FlowLayoutPanel buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 0, 0),
                BackColor = Color.Transparent
            };

            btnUpdatePlaylist = UIStyles.Buttons.CreateGreen(
                string.Empty,
                "Update playlist",
                ActionIconButtonSize);
            SetButtonSymbol(
                btnUpdatePlaylist,
                ButtonSymbolKind.Play,
                "Update playlist");
            btnUpdatePlaylist.Click += BtnUpdatePlaylist_Click;
            spotifyAuthToolTip.SetToolTip(
                btnUpdatePlaylist,
                "Update playlist");

            btnOpenReport = UIStyles.Buttons.CreateStandard(
                string.Empty,
                "Open last HTML report",
                ActionIconButtonSize);
            SetButtonSymbol(
                btnOpenReport,
                ButtonSymbolKind.Report,
                "Open last HTML report");
            btnOpenReport.Click += BtnOpenReport_Click;

            btnCancel = UIStyles.Buttons.CreateDanger(
                string.Empty,
                "Cancel running update",
                ActionIconButtonSize);
            SetButtonSymbol(
                btnCancel,
                ButtonSymbolKind.Stop,
                "Cancel running update");
            btnCancel.Enabled = false;
            btnCancel.Click += BtnCancel_Click;

            buttonPanel.Controls.Add(btnUpdatePlaylist);
            buttonPanel.Controls.Add(btnOpenReport);
            buttonPanel.Controls.Add(btnCancel);

            txtOutput = UIStyles.TextBoxes.CreateStandard();
            txtOutput.Dock = DockStyle.Fill;
            txtOutput.Multiline = true;
            txtOutput.ScrollBars = ScrollBars.Vertical;
            txtOutput.WordWrap = true;
            txtOutput.ReadOnly = true;
            txtOutput.TabStop = true;
            txtOutput.Font = UIStyles.Fonts.Monospace;

            mainPanel.Controls.Add(txtOutput);
            mainPanel.Controls.Add(buttonPanel);
            mainPanel.Controls.Add(propertyTable);

            ContentPanel.Controls.Add(mainPanel);
        }

        private void SettingsControl_Changed(object? sender, EventArgs e)
        {
            if (isLoadingSettings)
            {
                return;
            }

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
            spotifyAuthToolTip?.Dispose();
            runAvailabilityTimer?.Stop();
            runAvailabilityTimer?.Dispose();
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
