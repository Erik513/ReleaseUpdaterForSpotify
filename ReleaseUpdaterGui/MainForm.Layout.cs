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
    // The building of the window: its controls and the rows of the settings table.
    public partial class MainForm
    {
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

            lblStatus = UIStyles.Labels.CreateStandard(AppLocalization.Get(AppStrings.ReadyToRun));
            lblProgress = UIStyles.Labels.CreateStandard(AppLocalization.Get(AppStrings.MainFormInitialProgress));
            lblLoginStatus = UIStyles.Labels.CreateStandard(AppLocalization.Get(AppStrings.NotSignedIn));

            progressBar = UIStyles.ProgressBars.CreateGreen();
            progressBar.Dock = DockStyle.Top;
            progressBar.Height = 24;
            progressBar.Maximum = 100;
            progressBar.Value = 0;

            spotifyAuthToolTip = UIStyles.ToolTips.CreateToolTip();

            btnLogin = new SymbolToggle(
                UIStyles.Buttons.Presets.Login,
                UIStyles.Buttons.Presets.Logout,
                AppLocalization.Get(AppStrings.MainFormSignInTooltip),
                AppLocalization.Get(AppStrings.MainFormSignOutTooltip),
                PropertyIconButtonSize,
                BtnLogin_Click);
            btnLogin.SetAccessibleNames(
                AppLocalization.Get(AppStrings.MainFormSignInAccessibleName),
                AppLocalization.Get(AppStrings.MainFormSignOutAccessibleName));

            btnClientIdStatus = UIStyles.Buttons.CreateSymbol(
                UIStyles.Buttons.Presets.Info,
                string.Empty,
                PropertyIconButtonSize);
            btnClientIdStatus.AccessibleName = AppLocalization.Get(AppStrings.MainFormClientIdStatusAccessibleName);
            SetClientIdStatus(
                AppLocalization.Get(AppStrings.MainFormClientIdStatusInitial),
                ClientIdStatusKind.Info);
            btnClientIdStatus.Click += BtnClientIdHelp_Click;

            // Sets UIStyles.Language itself and follows a change from elsewhere; MainForm
            // reacts to AppLocalization.LanguageChanged (texts, saving the choice).
            cmbLanguage = UIStyles.ComboBoxes.CreateLanguagePrimary();
            cmbLanguage.TabStop = false;

            txtPlaylistName = UIStyles.TextBoxes.CreateBorderless(
                ReleaseDefaults.PlaylistName);
            txtPlaylistName.ReadOnly = true;
            txtPlaylistName.TabStop = false;

            btnEditPlaylistName = new SymbolToggle(
                UIStyles.Buttons.Presets.Edit,
                UIStyles.Buttons.Presets.Save,
                AppLocalization.Get(AppStrings.EditPlaylistName),
                AppLocalization.Get(AppStrings.MainFormSavePlaylistName),
                PropertyIconButtonSize,
                BtnEditPlaylistName_Click);

            // Always editable (a free text, nothing that must be checked); saved a moment after the last key, like the number box.
            txtPlaylistDescription = UIStyles.TextBoxes.CreatePrimary(
                string.Empty,
                AppLocalization.Get(AppStrings.MainFormPlaylistDescriptionPlaceholder));
            txtPlaylistDescription.MaxLength = ReleaseDefaults.MaxPlaylistDescriptionLength;
            txtPlaylistDescription.TextChanged += SettingsControl_Changed;
            spotifyAuthToolTip.SetToolTip(
                txtPlaylistDescription,
                AppLocalization.Get(AppStrings.MainFormPlaylistDescriptionTooltip));

            numLookbackDays = UIStyles.NumericUpDowns.CreatePrimary(
                ReleaseDefaults.MinReleaseLookbackDays,
                ReleaseDefaults.MaxReleaseLookbackDays,
                1,
                ReleaseDefaults.ReleaseLookbackDays);
            numLookbackDays.TabStop = false;
            numLookbackDays.ValueChanged += SettingsControl_Changed;

            txtSpotifyClientId = UIStyles.TextBoxes.CreateBorderless(
                string.Empty);
            txtSpotifyClientId.PlaceholderText = AppLocalization.Get(AppStrings.MainFormClientIdPlaceholder);
            txtSpotifyClientId.ReadOnly = true;
            txtSpotifyClientId.TabStop = false;
            spotifyAuthToolTip.SetToolTip(
                txtSpotifyClientId,
                AppLocalization.Get(AppStrings.MainFormClientIdTooltip));

            btnEditSpotifyClientId = new SymbolToggle(
                UIStyles.Buttons.Presets.Edit,
                UIStyles.Buttons.Presets.Save,
                AppLocalization.Get(AppStrings.EditSpotifyClientId),
                AppLocalization.Get(AppStrings.MainFormSaveSpotifyClientId),
                PropertyIconButtonSize,
                BtnEditSpotifyClientId_Click);

            BuildPropertyTableRows();

            // Workaround for ErikwnkWFUI: the PropertyTable docks a number box to the left, which puts it a little too high in its
            // cell (see docs/MIGRATION-FEEDBACK.md, "NumericUpDown sits too high"). Anchored like a button, the cell centers it.
            // To be removed when the table does this itself.
            numLookbackDays.Dock = DockStyle.None;
            numLookbackDays.Anchor = AnchorStyles.Left;

            TableLayoutPanel bottomPanel = UIStyles.TableLayoutPanels.CreateTransparent(
                columnCount: 2,
                rowCount: 1);
            bottomPanel.Dock = DockStyle.Bottom;
            bottomPanel.Height = 48;
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 3 * ActionIconButtonSize.Width + 16));
            bottomPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            FlowLayoutPanel buttonPanel = UIStyles.FlowLayoutPanels.CreateTransparent();
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.FlowDirection = FlowDirection.RightToLeft;
            buttonPanel.Padding = new Padding(0, 8, 0, 0);

            lblElapsedTime = UIStyles.Labels.CreateStandard(string.Empty);
            lblElapsedTime.Dock = DockStyle.Fill;
            lblElapsedTime.TextAlign = ContentAlignment.MiddleLeft;
            lblElapsedTime.Padding = new Padding(4, 0, 0, 0);
            lblElapsedTime.Visible = false;

            // Transparent flickers here: repainting this label every second (while a
            // run is in progress) forces it to redraw its parent's background first,
            // then the text, which is visible as a brief flash. A solid color matching
            // the panel behind it paints in a single pass instead.
            lblElapsedTime.BackColor = UIStyles.Colors.BackgroundLight;

            // One button for both: Play starts the update, and while it runs the same place is Stop.
            btnRun = new SymbolToggle(
                UIStyles.Buttons.Presets.Play,
                UIStyles.Buttons.Presets.Stop,
                AppLocalization.Get(AppStrings.UpdatePlaylist),
                AppLocalization.Get(AppStrings.MainFormCancelUpdate),
                ActionIconButtonSize,
                BtnRun_Click,
                useAccent: false);
            btnRun.SetAccessibleNames(
                AppLocalization.Get(AppStrings.UpdatePlaylist),
                AppLocalization.Get(AppStrings.MainFormCancelUpdate));

            btnOpenReport = UIStyles.Buttons.CreateSymbol(
                UIStyles.Buttons.Presets.Document,
                AppLocalization.Get(AppStrings.MainFormOpenReport),
                ActionIconButtonSize);
            btnOpenReport.Click += BtnOpenReport_Click;

            btnUnreleasedSongs = UIStyles.Buttons.CreateSymbol(
                UIStyles.Buttons.Presets.Search,
                AppLocalization.Get(AppStrings.MainFormOpenUnreleasedSongs),
                ActionIconButtonSize);
            btnUnreleasedSongs.MouseDown += (_, _) => unreleasedOpenedByMouse = true;
            btnUnreleasedSongs.Click += BtnUnreleasedSongs_Click;

            buttonPanel.Controls.Add(btnRun.Host);
            buttonPanel.Controls.Add(btnOpenReport);
            buttonPanel.Controls.Add(btnUnreleasedSongs);

            bottomPanel.Controls.Add(lblElapsedTime, 0, 0);
            bottomPanel.Controls.Add(buttonPanel, 1, 0);

            txtOutput = UIStyles.TextBoxes.CreatePrimary();
            txtOutput.Dock = DockStyle.Fill;
            txtOutput.Multiline = true;
            txtOutput.ScrollBars = ScrollBars.None;
            txtOutput.WordWrap = true;
            txtOutput.ReadOnly = true;
            txtOutput.TabStop = false;
            txtOutput.Font = UIStyles.Fonts.Monospace;

            mainPanel.Controls.Add(txtOutput);
            mainPanel.Controls.Add(bottomPanel);
            mainPanel.Controls.Add(propertyTable);

            ContentPanel.Controls.Add(mainPanel);
        }

        // Built once. The labels are given as functions, so the table follows a
        // language switch by itself - rebuilding the rows on a switch
        // (ClearRows + AddRow again) would take the language combo box out of
        // the table while it is still handling its own selection, leaving it
        // empty.
        private void BuildPropertyTableRows()
        {
            propertyTable.AddRow(
                () => AppLocalization.Get(AppStrings.MainFormRowStatus),
                PropertyRowHeight,
                PropertyColumn.Percent(lblStatus, 100),
                PropertyColumn.Absolute(btnClientIdStatus, PropertyIconColumnWidth));
            propertyTable.AddRow(
                () => AppLocalization.Get(AppStrings.MainFormRowProgress),
                PropertyRowHeight,
                PropertyColumn.Percent(lblProgress, 50),
                PropertyColumn.Percent(progressBar, 50));
            propertyTable.AddSection(() => AppLocalization.Get(AppStrings.MainFormSectionSettings));
            propertyTable.AddRow(
                () => AppLocalization.Get(AppStrings.MainFormRowLanguage),
                PropertyRowHeight,
                PropertyColumn.Percent(cmbLanguage, 100));
            propertyTable.AddRow(
                () => AppLocalization.Get(AppStrings.MainFormRowSpotify),
                PropertyRowHeight,
                PropertyColumn.Percent(lblLoginStatus, 100),
                PropertyColumn.Absolute(btnLogin.Host, PropertyIconColumnWidth));
            propertyTable.AddRow(
                () => AppLocalization.Get(AppStrings.MainFormRowClientId),
                PropertyRowHeight,
                PropertyColumn.Percent(txtSpotifyClientId, 100),
                PropertyColumn.Absolute(btnEditSpotifyClientId.Host, PropertyIconColumnWidth));
            propertyTable.AddRow(
                () => AppLocalization.Get(AppStrings.MainFormRowPlaylistName),
                PropertyRowHeight,
                PropertyColumn.Percent(txtPlaylistName, 100),
                PropertyColumn.Absolute(btnEditPlaylistName.Host, PropertyIconColumnWidth));
            propertyTable.AddRow(
                () => AppLocalization.Get(AppStrings.MainFormRowPlaylistDescription),
                PropertyRowHeight,
                PropertyColumn.Percent(txtPlaylistDescription, 100));
            propertyTable.AddRow(
                () => AppLocalization.Get(AppStrings.MainFormRowLookbackDays),
                PropertyRowHeight,
                PropertyColumn.Auto(numLookbackDays));

            CenterPropertyTableText(propertyTable);
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
    }
}
