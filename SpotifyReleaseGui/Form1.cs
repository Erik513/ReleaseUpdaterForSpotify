using CustomWFUI;
using CustomWFUI.Controls;
using CustomWFUI.Forms;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SpotifyReleaseGui
{
    public partial class Form1 : StyledForm
    {
        private BackendRunner backendRunner = new BackendRunner();

        private Label lblStatus;
        private Label lblProgress;
        private ProgressBar progressBar;
        private Button btnUpdatePlaylist;
        private TextBox txtOutput;
        private Button btnOpenReport;
        private Button btnCancel;

        private TextBox txtPlaylistName;
        private NumericUpDown numLookbackDays;

        public Form1() : base("Spotify Release Updater")
        {
            InitializeComponent();
            RegisterBackendEvents();
            CenterToScreen();

            Size = MinimumSize;
            MinimumSize = new Size(460, 580);

            Panel mainPanel = UIStyles.Panels.CreateDark();
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = new Padding(16);

            StyledPropertyTable propertyTable = new StyledPropertyTable
            {
                Dock = DockStyle.Top
            };

            lblStatus = UIStyles.Labels.CreateNormal("Idle");

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

            Button btnEditPlaylistName = UIStyles.Buttons.CreateStandard(
                "✎",
                "Playlistnamen bearbeiten",
                new Size(30, 30),
                true
            );
            btnEditPlaylistName.FlatAppearance.BorderSize = 1;
            btnEditPlaylistName.FlatAppearance.BorderColor = UIStyles.Colors.BorderLight;

            btnEditPlaylistName.Click += delegate
            {
                txtPlaylistName.ReadOnly = !txtPlaylistName.ReadOnly;

                if (txtPlaylistName.ReadOnly)
                {
                    btnEditPlaylistName.Text = "✎";
                }
                else
                {
                    btnEditPlaylistName.Text = "💾";
                    txtPlaylistName.Focus();
                    txtPlaylistName.SelectAll();
                }
            };

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

            LoadSettingsToUi();
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

            backendRunner.Exited += exitCode =>
            {
                Invoke(new Action(() =>
                {
                    btnUpdatePlaylist.Enabled = true;
                    btnCancel.Enabled = false;
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
                        lblStatus.Text = $"Status: Fehler ({exitCode})";
                    }
                }));
            };
        }

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            backendRunner.Stop();
            ToastForm.ShowToast(
                "Vorgang abgebrochen.",
                this
            );

            btnCancel.Enabled = false;
            btnUpdatePlaylist.Enabled = true;

            lblStatus.Text = "Status: Abgebrochen";
            txtOutput.AppendText("Vorgang wurde abgebrochen." + Environment.NewLine);
        }

        private void BtnUpdatePlaylist_Click(object? sender, EventArgs e)
        {
            if (!SaveSettingsFromUi())
            {
                return;
            }

            lblStatus.Text = "Starte Backend...";
            lblProgress.Text = "-";
            progressBar.Value = 0;
            txtOutput.Clear();
            btnUpdatePlaylist.Enabled = false;
            btnCancel.Enabled = true;

            try
            {
                ToastForm.ShowToast(
                    "Playlist-Aktualisierung gestartet.",
                    this
                );
                backendRunner.Start();
            }
            catch (Exception ex)
            {
                btnUpdatePlaylist.Enabled = true;
                lblStatus.Text = "Status: Start fehlgeschlagen";
                txtOutput.AppendText(ex.Message + Environment.NewLine);
            }
        }
        private void BtnOpenReport_Click(object? sender, EventArgs e)
        {
            if (!File.Exists(AppPaths.ReportPath))
            {
                MessageBox.Show("Noch kein Bericht vorhanden.");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.ReportPath,
                UseShellExecute = true
            });
        }
        private bool ValidatePlaylistName()
        {
            bool isValid = PlaylistValidator.IsValidPlaylistName(
                txtPlaylistName.Text,
                out string errorMessage
            );

            if (!isValid)
            {
                MessageBox.Show(errorMessage);
                return false;
            }

            return true;
        }

        private void LoadSettingsToUi()
        {
            AppSettings settings = SettingsService.Load();

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

        private bool SaveSettingsFromUi()
        {
            if (!ValidatePlaylistName())
            {
                return false;
            }

            AppSettings settings = SettingsService.Load();

            settings.PlaylistName = txtPlaylistName.Text.Trim();
            settings.ReleaseLookbackDays = (int)numLookbackDays.Value;

            SettingsService.Save(settings);

            lblStatus.Text = "Einstellungen gespeichert";

            return true;
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            backendRunner.Stop();
            base.OnFormClosing(e);
        }
    }
}