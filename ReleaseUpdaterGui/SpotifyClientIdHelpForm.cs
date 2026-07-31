using CustomWFUI;
using CustomWFUI.Forms;
using ReleaseUpdater.Spotify.Auth;
using System.Diagnostics;

namespace ReleaseUpdaterGui
{
    public sealed class SpotifyClientIdHelpForm : StyledForm
    {
        private const string DashboardUrl = "https://developer.spotify.com/dashboard";

        private static readonly string RedirectUri = SpotifyAuthService.RedirectUri;

        private TextBox instructionsTextBox = null!;
        private Button closeButton = null!;

        public SpotifyClientIdHelpForm() : base(
            StyledFormOptions.CreateDialog(
                title: "Spotify Client ID",
                titleTextAlign: ContentAlignment.MiddleCenter,
                backColor: UIStyles.Colors.BackgroundDarkElevated))
        {
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(620, 390);
            MinimumSize = new Size(620, 390);

            ContentPanel.Controls.Add(CreateContent());
            Shown += SpotifyClientIdHelpForm_Shown;
        }

        private Control CreateContent()
        {
            TableLayoutPanel layout = new()
            {
                Dock = DockStyle.Fill,
                BackColor = UIStyles.Colors.BackgroundLight,
                Padding = new Padding(16),
                ColumnCount = 1,
                RowCount = 2
            };

            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            instructionsTextBox = UIStyles.TextBoxes.CreateStandard(
                CreateInstructionText(),
                string.Empty);
            instructionsTextBox.Dock = DockStyle.Fill;
            instructionsTextBox.Multiline = true;
            instructionsTextBox.ReadOnly = true;
            instructionsTextBox.ScrollBars = ScrollBars.Vertical;
            instructionsTextBox.TabStop = false;
            instructionsTextBox.WordWrap = true;
            instructionsTextBox.Margin = new Padding(0, 0, 0, 12);

            FlowLayoutPanel buttons = CreateButtonRow();

            layout.Controls.Add(instructionsTextBox, 0, 0);
            layout.Controls.Add(buttons, 0, 1);

            return layout;
        }

        private FlowLayoutPanel CreateButtonRow()
        {
            FlowLayoutPanel buttons = new()
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.Transparent
            };

            closeButton = UIStyles.Buttons.CreateStandard(
                "Close",
                "Close Spotify Client ID help",
                new Size(96, 32),
                true);
            closeButton.Margin = new Padding(8, 4, 0, 4);
            closeButton.Click += (_, _) => Close();

            Button dashboardButton = UIStyles.Buttons.CreateStandard(
                "Copy URI and open dashboard",
                "Copy redirect URI and open Spotify Developer Dashboard",
                new Size(220, 32),
                true);
            dashboardButton.Margin = new Padding(0, 4, 0, 4);
            dashboardButton.Click += (_, _) => CopyRedirectUriAndOpenDashboard();

            buttons.Controls.Add(closeButton);
            buttons.Controls.Add(dashboardButton);

            CancelButton = closeButton;

            return buttons;
        }

        private void SpotifyClientIdHelpForm_Shown(object? sender, EventArgs e)
        {
            instructionsTextBox.SelectionStart = 0;
            instructionsTextBox.SelectionLength = 0;
            closeButton.Focus();
        }

        private static string CreateInstructionText()
        {
            return
                "Release Updater for Spotify does not include a shared Spotify Client ID. " +
                "Each user must create and enter their own Client ID." +
                Environment.NewLine +
                Environment.NewLine +
                "To create your Spotify Client ID:" + Environment.NewLine +
                Environment.NewLine +
                "1. Click the button below to copy the redirect URI and open the Spotify Developer Dashboard." +
                Environment.NewLine +
                "2. Create a new Spotify app." + Environment.NewLine +
                "3. Add this redirect URI in the Spotify app settings:" + Environment.NewLine +
                RedirectUri + Environment.NewLine +
                "4. Copy the Client ID from the Spotify app settings." + Environment.NewLine +
                "5. Paste the Client ID into this app and save it with the check button." +
                Environment.NewLine +
                Environment.NewLine +
                "Do not enter or share a Client Secret.";
        }

        private void CopyRedirectUriAndOpenDashboard()
        {
            CopyText(RedirectUri);
            OpenDashboard();
        }

        private void OpenDashboard()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = DashboardUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowError(
                    $"Could not open the Spotify Developer Dashboard: {ex.Message}");
            }
        }

        private void CopyText(string text)
        {
            try
            {
                Clipboard.SetText(text);
                ToastForm.ShowToast("Redirect URI copied.", this);
            }
            catch (Exception ex)
            {
                ShowError($"Could not copy the redirect URI: {ex.Message}");
            }
        }

        private void ShowError(string message)
        {
            CustomMessageBox.Show(
                message,
                "Spotify Client ID",
                CustomMessageBoxButtons.OK,
                CustomMessageBoxIcon.Error,
                this,
                CustomMessageBoxSize.Small);
        }
    }
}
