using CustomWFUI;
using CustomWFUI.Controls;
using CustomWFUI.Forms;
using SpotifyRelease.Core.Models;
using SpotifyRelease.Core.Services;
using SpotifyRelease.Data.Reports;
using SpotifyRelease.Data.Storage;
using SpotifyRelease.Spotify.Api;
using SpotifyRelease.Spotify.Auth;

namespace SpotifyReleaseGui
{
    public partial class MainForm : StyledForm
    {
        private readonly AppDataPaths dataPaths;
        private readonly JsonSettingsStore settingsStore;
        private readonly FileTokenStore tokenStore;
        private readonly HtmlReportWriter reportWriter;
        private readonly SpotifyAuthService authService;
        private readonly SpotifyApiClient spotifyApiClient;
        private readonly SpotifyReleaseUpdater releaseUpdater;
        private readonly HttpClient spotifyAccountsHttpClient = new();
        private readonly HttpClient spotifyApiHttpClient = new();

        private MainFormController controller;
        private string? currentPlaylistId;

        private Label lblStatus = null!;
        private Label lblProgress = null!;
        private Label lblLoginStatus = null!;
        private ProgressBar progressBar = null!;

        private TextBox txtPlaylistName = null!;
        private Button btnEditPlaylistName = null!;
        private NumericUpDown numLookbackDays = null!;

        private Button btnLogin = null!;
        private Button btnCancel = null!;
        private Button btnOpenReport = null!;
        private Button btnUpdatePlaylist = null!;

        private TextBox txtOutput = null!;
        private ToolTip spotifyAuthToolTip = null!;
        private bool isSpotifySignedIn;
        private bool isLoadingSettings;

        public string PlaylistName
        {
            get { return txtPlaylistName.Text.Trim(); }
        }

        public int ReleaseLookbackDays
        {
            get { return (int)numLookbackDays.Value; }
        }

        public bool IsPlaylistNameReadOnly
        {
            get { return txtPlaylistName.ReadOnly; }
        }

        public bool IsSpotifySignedIn
        {
            get { return isSpotifySignedIn; }
        }

        public MainForm() : base(
            StyledFormOptions.CreateStandard(
                title: "Spotify Release Updater",
                titleTextAlign: ContentAlignment.MiddleCenter,
                backColor: UIStyles.Colors.BackgroundDarkElevated,
                icon: LoadTitleImage(),
                windowIcon: LoadWindowIcon()))
        {
            InitializeComponent();

            dataPaths = AppDataPaths.CreateDefault();
            settingsStore = new JsonSettingsStore(dataPaths);
            tokenStore = new FileTokenStore(dataPaths);
            reportWriter = new HtmlReportWriter(dataPaths);

            authService = new SpotifyAuthService(
                tokenStore,
                spotifyAccountsHttpClient);

            spotifyApiClient = new SpotifyApiClient(
                authService,
                spotifyApiHttpClient);

            releaseUpdater = new SpotifyReleaseUpdater(
                spotifyApiClient,
                settingsStore,
                reportWriter);

            controller = new MainFormController(
                this,
                settingsStore,
                reportWriter,
                authService,
                releaseUpdater);

            Size = MinimumSize;
            MinimumSize = new Size(520, 700);

            BuildUi();
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
                ReleaseLookbackDays = ReleaseLookbackDays
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

        public void SetAuthenticationBusy()
        {
            btnLogin.Enabled = false;
            SetSpotifyAuthToolTip(
                isSpotifySignedIn
                    ? "Spotify sign-out is currently running."
                    : "Spotify sign-in is currently running.");
        }

        public void SetAuthenticationState(bool isLoggedIn)
        {
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
            btnLogin.Enabled = false;
            btnCancel.Enabled = true;
            SetSpotifyAuthToolTip(
                "Spotify sign-in is disabled while the playlist update is running.");
        }

        public void ShowCompleted(PlaylistUpdateResult result)
        {
            btnUpdatePlaylist.Enabled = true;
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
            btnLogin.Enabled = true;

            lblStatus.Text = "Cancelled";
            txtOutput.AppendText(
                "The update was cancelled." + Environment.NewLine);
        }

        public void EnablePlaylistNameEditing()
        {
            txtPlaylistName.ReadOnly = false;
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
            SetButtonSymbol(
                btnEditPlaylistName,
                ButtonSymbolKind.Edit,
                "Edit playlist name");
            btnUpdatePlaylist.Enabled = true;
        }

        public void FocusPlaylistName()
        {
            txtPlaylistName.Focus();
            txtPlaylistName.SelectAll();
        }

        public void ShowInfo(string message, string title)
        {
            CustomMessageBox.Show(
                message,
                title,
                CustomMessageBoxButtons.OK,
                CustomMessageBoxIcon.Info,
                this,
                CustomMessageBoxSize.Small);
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
                new Size(36, 30),
                true);
            SetButtonSymbol(
                btnLogin,
                ButtonSymbolKind.Login,
                "Sign in with Spotify");
            SetSpotifyAuthToolTip(
                "Click to open Spotify login in your browser.");
            btnLogin.Click += BtnLogin_Click;

            txtPlaylistName = UIStyles.TextBoxes.CreateBorderstyleNone(
                ReleaseDefaults.PlaylistName);
            txtPlaylistName.ReadOnly = true;

            btnEditPlaylistName = UIStyles.Buttons.CreateStandard(
                string.Empty,
                "Edit playlist name",
                new Size(36, 30),
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
            numLookbackDays.ValueChanged += SettingsControl_Changed;

            propertyTable.AddRow("Status", lblStatus);
            propertyTable.AddRow(
                "Progress",
                UIColumn.Percent(lblProgress, 50),
                UIColumn.Percent(progressBar, 50));
            propertyTable.AddSection("Settings");
            propertyTable.AddRow(
                "Spotify",
                UIColumn.Percent(lblLoginStatus, 100),
                UIColumn.Absolute(btnLogin, 54));
            propertyTable.AddRow(
                "Playlist name",
                UIColumn.Percent(txtPlaylistName, 100),
                UIColumn.Absolute(btnEditPlaylistName, 54));
            propertyTable.AddRow(
                "Lookback days",
                UIColumn.Auto(numLookbackDays));

            FlowLayoutPanel buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 42,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 0, 0),
                BackColor = Color.Transparent
            };

            btnUpdatePlaylist = UIStyles.Buttons.CreateGreen(
                string.Empty,
                "Update playlist",
                new Size(38, 32));
            SetButtonSymbol(
                btnUpdatePlaylist,
                ButtonSymbolKind.Play,
                "Update playlist");
            btnUpdatePlaylist.Click += BtnUpdatePlaylist_Click;

            btnOpenReport = UIStyles.Buttons.CreateStandard(
                string.Empty,
                "Open last HTML report",
                new Size(38, 32));
            SetButtonSymbol(
                btnOpenReport,
                ButtonSymbolKind.Report,
                "Open last HTML report");
            btnOpenReport.Click += BtnOpenReport_Click;

            btnCancel = UIStyles.Buttons.CreateDanger(
                string.Empty,
                "Cancel running update",
                new Size(38, 32));
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
            txtOutput.WordWrap = false;
            txtOutput.ReadOnly = true;
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

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            controller.CancelUpdate();
            spotifyAuthToolTip?.Dispose();
            spotifyAccountsHttpClient.Dispose();
            spotifyApiHttpClient.Dispose();
            base.OnFormClosing(e);
        }

        private static decimal ClampForControl(int value, NumericUpDown control)
        {
            return Math.Min(
                control.Maximum,
                Math.Max(control.Minimum, value));
        }

        private static Image? LoadTitleImage()
        {
            string path = Path.Combine(
                Application.StartupPath,
                "Assets",
                "Pictures",
                "SpotifyReleaseUpdater.png");

            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                using Image image = Image.FromFile(path);
                return new Bitmap(image);
            }
            catch
            {
                return null;
            }
        }

        private static Icon? LoadWindowIcon()
        {
            string path = Path.Combine(
                Application.StartupPath,
                "Assets",
                "Icons",
                "SpotifyReleaseUpdater_48px.ico");

            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return new Icon(path);
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

        private static string GetButtonSymbol(ButtonSymbolKind kind)
        {
            return kind switch
            {
                ButtonSymbolKind.Play => "\u25B6",
                ButtonSymbolKind.Stop => "\u25A0",
                ButtonSymbolKind.Report => "\u25A4",
                ButtonSymbolKind.Login => "\u21AA",
                ButtonSymbolKind.Logout => "\u21A9",
                ButtonSymbolKind.Edit => "\u270E",
                ButtonSymbolKind.Check => "\u2713",
                _ => string.Empty
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
            Check
        }
    }
}
