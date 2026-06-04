using CustomWFUI;
using CustomWFUI.Controls;
using CustomWFUI.Forms;

namespace SpotifyReleaseGui
{
    public partial class MainForm : StyledForm
    {
        private static readonly Icon AppIcon = new Icon("Assets\\Icons\\SpotifyReleaseUpdater_48px.ico");

        private readonly BackendRunner backendRunner = new BackendRunner();
        private MainFormController controller;

        private Label lblStatus;
        private Label lblProgress;
        private ProgressBar progressBar;

        private TextBox txtPlaylistName;
        private Button btnEditPlaylistName;
        private NumericUpDown numLookbackDays;

        private Button btnCancel;
        private Button btnOpenReport;
        private Button btnUpdatePlaylist;

        private TextBox txtOutput;


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

        
        public MainForm() : base(
            StyledFormOptions.CreateStandard(
                title: "Spotify Release Updater",
                titleTextAlign: ContentAlignment.MiddleCenter,
                backColor: UIStyles.Colors.BackgroundDarkElevated,
                icon: Image.FromFile(
                    Path.Combine(
                        Application.StartupPath,
                        "Assets",
                        "Pictures",
                        "SpotifyReleaseUpdater.png"
                    )
                )
            )
        )
        {
            InitializeComponent();
            Icon = AppIcon;
            controller = new MainFormController(this, backendRunner);
            RegisterBackendEvents();

            Size = MinimumSize;
            MinimumSize = new Size(460, 580);

            Panel mainPanel = UIStyles.Panels.CreateElevated();
            mainPanel.BackColor = UIStyles.Colors.BackgroundLight;
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = new Padding(16);

            StyledPropertyTable propertyTable = new()
            {
                Dock = DockStyle.Top,
                Padding = new Padding(5)
            };

            lblStatus = UIStyles.Labels.CreateNormal("Bereit");

            lblProgress = UIStyles.Labels.CreateNormal("0 / 0");

            progressBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 24,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            txtPlaylistName = UIStyles.TextBoxes.CreateBorderstyleNone("[Followed Artists - New Releases]");
            txtPlaylistName.ReadOnly = true;

            btnEditPlaylistName = UIStyles.Buttons.CreateStandard(
                "✎",
                "Playlistnamen bearbeiten",
                new Size(50, 30),
                true
            );

            btnEditPlaylistName.FlatAppearance.BorderSize = 1;
            btnEditPlaylistName.FlatAppearance.BorderColor = UIStyles.Colors.BorderLight;

            btnEditPlaylistName.Click += BtnEditPlaylistName_Click;

            numLookbackDays = UIStyles.NumericUpDowns.CreateStandard(1, 20, 1, 10);

            propertyTable.AddRow("Status", lblStatus);
            propertyTable.AddRow("Fortschritt", UIColumn.Percent(lblProgress, 50), UIColumn.Percent(progressBar, 50));
            propertyTable.AddSection("Einstellungen");
            propertyTable.AddRow("Playlistname", UIColumn.Percent(txtPlaylistName, 100), UIColumn.Absolute(btnEditPlaylistName, 50));
            propertyTable.AddRow("Zeitraum (in Tagen)", UIColumn.Auto(numLookbackDays));

            FlowLayoutPanel buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 42,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 0, 0),
                BackColor = Color.Transparent
            };

            btnUpdatePlaylist = UIStyles.Buttons.CreateGreen(
                "▶️",
                "Playlist aktualisieren",
                new Size(100, 32)
            );
            btnUpdatePlaylist.Click += BtnUpdatePlaylist_Click;

            btnOpenReport = UIStyles.Buttons.CreateStandard(
                "📄",
                "Letzten HTML-Bericht öffnen",
                new Size(100, 32)
            );
            btnOpenReport.Click += BtnOpenReport_Click;

            btnCancel = UIStyles.Buttons.CreateDanger(
                "■",
                "Laufenden Vorgang abbrechen",
                new Size(100, 32)
            );
            btnCancel.Enabled = false;
            btnCancel.Click += BtnCancel_Click;

            buttonPanel.Controls.Add(btnUpdatePlaylist);
            buttonPanel.Controls.Add(btnOpenReport);
            buttonPanel.Controls.Add(btnCancel);

            txtOutput = UIStyles.TextBoxes.CreateStandard();
            txtOutput.Dock = DockStyle.Fill;
            txtOutput.Multiline = true;
            txtOutput.ScrollBars = ScrollBars.None;
            txtOutput.WordWrap = false;
            txtOutput.ReadOnly = true;
            txtOutput.Font = UIStyles.Fonts.Monospace;

            mainPanel.Controls.Add(txtOutput);
            mainPanel.Controls.Add(buttonPanel);
            mainPanel.Controls.Add(propertyTable);

            ContentPanel.Controls.Add(mainPanel);

            controller.LoadSettings();
            CenterToScreen();
        }
        public void SetSettings(AppSettings settings)
        {
            txtPlaylistName.Text = settings.PlaylistName;

            int days = settings.ReleaseLookbackDays;

            if (days < numLookbackDays.Minimum)
            {
                days = (int)numLookbackDays.Minimum;
            }

            if (days > numLookbackDays.Maximum)
            {
                days = (int)numLookbackDays.Maximum;
            }

            numLookbackDays.Value = days;
        }

        public void SetStatus(string status)
        {
            lblStatus.Text = status;
        }

        public void PrepareRunUi()
        {
            lblStatus.Text = "Starte Backend...";
            lblProgress.Text = "-";
            progressBar.Value = 0;
            txtOutput.Clear();
            btnUpdatePlaylist.Enabled = false;
            btnCancel.Enabled = true;
        }

        public void ShowStartFailed(string message)
        {
            btnUpdatePlaylist.Enabled = true;
            lblStatus.Text = "Start fehlgeschlagen";
            txtOutput.AppendText(message + Environment.NewLine);
        }

        public void ShowCancelled()
        {
            btnCancel.Enabled = false;
            btnUpdatePlaylist.Enabled = true;
            lblStatus.Text = "Abgebrochen";
            txtOutput.AppendText("Vorgang wurde abgebrochen." + Environment.NewLine);
        }

        public void EnablePlaylistNameEditing()
        {
            txtPlaylistName.ReadOnly = false;
            btnEditPlaylistName.Text = "💾";
            btnUpdatePlaylist.Enabled = false;

            txtPlaylistName.Focus();
            txtPlaylistName.SelectAll();
        }

        public void DisablePlaylistNameEditing()
        {
            txtPlaylistName.ReadOnly = true;
            btnEditPlaylistName.Text = "✎";
            btnUpdatePlaylist.Enabled = true;
        }

        public void FocusPlaylistName()
        {
            txtPlaylistName.Focus();
            txtPlaylistName.SelectAll();
        }

        private void RegisterBackendEvents()
        {
            backendRunner.OutputReceived += message =>
            {
                txtOutput.Invoke(new Action(() =>
                {
                    txtOutput.AppendText(message + Environment.NewLine);
                }));
            };

            backendRunner.ErrorReceived += message =>
            {
                txtOutput.Invoke(new Action(() =>
                {
                    txtOutput.AppendText("ERROR: " + message + Environment.NewLine);
                }));
            };

            backendRunner.StatusReceived += status =>
            {
                lblStatus.Invoke(new Action(() =>
                {
                    lblStatus.Text = $"{status}";
                }));
            };

            backendRunner.ProgressReceived += (current, total, percent) =>
            {
                progressBar.Invoke(new Action(() =>
                {
                    progressBar.Value = Math.Min(100, Math.Max(0, percent));
                    lblProgress.Text = $"{current} / {total} ({percent}%)";
                }));
            };

            backendRunner.Exited += (exitCode, stoppedByUser) =>
            {
                Invoke(new Action(() =>
                {
                    btnUpdatePlaylist.Enabled = true;
                    btnCancel.Enabled = false;

                    if (stoppedByUser)
                    {
                        lblStatus.Text = "Abgebrochen";
                        txtOutput.AppendText(
                            "Vorgang wurde abgebrochen." + Environment.NewLine
                        );
                        return;
                    }

                    if (exitCode == 0)
                    {
                        lblStatus.Text = "Fertig";
                        progressBar.Value = 100;

                        ToastForm.ShowToast(
                            "Playlist erfolgreich aktualisiert.",
                            this
                        );
                    }
                    else
                    {
                        lblStatus.Text = $"Fehler ({exitCode})";

                        CustomMessageBox.Show(
                            "Das Backend wurde unerwartet beendet.",
                            "Fehler",
                            CustomMessageBoxButtons.OK,
                            CustomMessageBoxIcon.Error,
                            this,
                            CustomMessageBoxSize.Small
                        );
                    }
                }));
            };
        }

        private void BtnUpdatePlaylist_Click(object? sender, EventArgs e)
        {
            controller.StartUpdate();
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
            backendRunner.Stop();
            base.OnFormClosing(e);
        }
    }
}