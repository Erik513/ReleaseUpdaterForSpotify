using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SpotifyReleaseGui
{
    public partial class Form1 : Form
    {
        private BackendRunner backendRunner = new BackendRunner();

        private Label lblStatus;
        private Label lblProgress;
        private ProgressBar progressBar;
        private Button btnUpdatePlaylist;
        private TextBox txtOutput;
        private Button btnOpenReport;

        private Label lblPlaylistName;
        private Label lblLookbackDays;

        private TextBox txtPlaylistName;
        private NumericUpDown numLookbackDays;

        public Form1()
        {
            InitializeComponent();
            RegisterBackendEvents();
            this.Size = new Size(740, 640);

            lblStatus = new Label
            {
                Text = "Status: Idle",
                Location = new Point(10, 10),
                Size = new Size(500, 20)
            };

            lblProgress = new Label
            {
                Text = "Fortschritt: -",
                Location = new Point(10, 35),
                Size = new Size(500, 20)
            };

            progressBar = new ProgressBar
            {
                Location = new Point(10, 60),
                Size = new Size(500, 20),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            btnUpdatePlaylist = new Button
            {
                Text = "Start",
                Location = new Point(10, 175),
                Size = new Size(120, 30)
            };

            btnUpdatePlaylist.Click += BtnUpdatePlaylist_Click;

            txtOutput = new TextBox
            {
                Location = new Point(10, 220),
                Size = new Size(700, 360),
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                ReadOnly = true
            };

            btnOpenReport = new Button
            {
                Text = "Bericht öffnen",
                Location = new Point(140, 175),
                Size = new Size(120, 30)
            };
            btnOpenReport.Click += BtnOpenReport_Click;

            lblPlaylistName = new Label
            {
                Text = "Playlistname:",
                Location = new Point(10, 100),
                Size = new Size(120, 20)
            };

            txtPlaylistName = new TextBox
            {
                Location = new Point(140, 97),
                Size = new Size(400, 25),
                Text = "000 [Followed Artists - New Releases]"
            };

            lblLookbackDays = new Label
            {
                Text = "Zeitraum in Tagen:",
                Location = new Point(10, 135),
                Size = new Size(120, 20)
            };

            numLookbackDays = new NumericUpDown
            {
                Location = new Point(140, 132),
                Size = new Size(80, 25),
                Minimum = 1,
                Maximum = 20,
                Value = 10
            };

            Controls.Add(lblStatus);
            Controls.Add(lblProgress);
            Controls.Add(progressBar);
            Controls.Add(btnUpdatePlaylist);
            Controls.Add(txtOutput);
            Controls.Add(btnOpenReport);
            Controls.Add(lblPlaylistName);
            Controls.Add(txtPlaylistName);
            Controls.Add(lblLookbackDays);
            Controls.Add(numLookbackDays);

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
                    lblStatus.Text = $"Status: {status}";
                }));
            };

            backendRunner.ProgressReceived += (current, total, percent) =>
            {
                progressBar.Invoke(new Action(() =>
                {
                    progressBar.Value = Math.Min(100, Math.Max(0, percent));
                    lblProgress.Text = $"Fortschritt: {current} / {total} ({percent}%)";
                }));
            };

            backendRunner.Exited += exitCode =>
            {
                Invoke(new Action(() =>
                {
                    btnUpdatePlaylist.Enabled = true;

                    if (exitCode == 0)
                    {
                        lblStatus.Text = "Status: Fertig";
                        progressBar.Value = 100;
                    }
                    else
                    {
                        lblStatus.Text = $"Status: Fehler ({exitCode})";
                    }
                }));
            };
        }

        private void BtnUpdatePlaylist_Click(object? sender, EventArgs e)
        {
            if (!SaveSettingsFromUi())
            {
                return;
            }

            lblStatus.Text = "Status: Starte Backend...";
            lblProgress.Text = "Fortschritt: -";
            progressBar.Value = 0;
            txtOutput.Clear();
            btnUpdatePlaylist.Enabled = false;

            try
            {
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

            lblStatus.Text = "Status: Einstellungen gespeichert";

            return true;
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            backendRunner.Stop();
            base.OnFormClosing(e);
        }
    }
}