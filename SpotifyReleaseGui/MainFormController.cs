using CustomWFUI.Forms;
using System.Diagnostics;

namespace SpotifyReleaseGui
{
    public class MainFormController
    {
        private readonly MainForm form;
        private readonly BackendRunner backendRunner;

        public MainFormController(MainForm form, BackendRunner backendRunner)
        {
            this.form = form;
            this.backendRunner = backendRunner;
        }

        public void StartUpdate()
        {
            if (!ConfirmStart())
            {
                return;
            }

            if (!SaveSettings())
            {
                return;
            }

            form.PrepareRunUi();

            try
            {
                ToastForm.ShowToast(
                    "Playlist-Aktualisierung gestartet.",
                    form
                );

                backendRunner.Start();
            }
            catch (Exception ex)
            {
                form.ShowStartFailed(ex.Message);
            }
        }

        public void CancelUpdate()
        {
            backendRunner.Stop();

            ToastForm.ShowToast(
                "Vorgang abgebrochen.",
                form
            );

            form.ShowCancelled();
        }

        public void OpenReport()
        {
            if (!File.Exists(AppPaths.ReportPath))
            {
                CustomMessageBox.Show(
                    "Noch kein Bericht vorhanden.",
                    "Bericht",
                    CustomMessageBoxButtons.OK,
                    CustomMessageBoxIcon.Info,
                    form,
                    CustomMessageBoxSize.Small
                );

                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.ReportPath,
                UseShellExecute = true
            });
        }

        public void TogglePlaylistNameEdit()
        {
            if (form.IsPlaylistNameReadOnly)
            {
                form.EnablePlaylistNameEditing();
                return;
            }

            if (!ValidatePlaylistName())
            {
                form.FocusPlaylistName();
                return;
            }

            form.DisablePlaylistNameEditing();
            SaveSettings();
        }

        public void LoadSettings()
        {
            AppSettings settings = SettingsService.Load();
            form.SetSettings(settings);
        }

        private bool SaveSettings()
        {
            if (!ValidatePlaylistName())
            {
                return false;
            }

            AppSettings settings = SettingsService.Load();

            settings.PlaylistName = form.PlaylistName;
            settings.ReleaseLookbackDays = form.ReleaseLookbackDays;

            SettingsService.Save(settings);

            form.SetStatus("Einstellungen gespeichert");

            return true;
        }

        private bool ValidatePlaylistName()
        {
            bool isValid = PlaylistValidator.IsValidPlaylistName(
                form.PlaylistName,
                out string errorMessage
            );

            if (!isValid)
            {
                CustomMessageBox.Show(
                    errorMessage,
                    "Ungültiger Playlistname",
                    CustomMessageBoxButtons.OK,
                    CustomMessageBoxIcon.Warning,
                    form,
                    CustomMessageBoxSize.Small
                );

                return false;
            }

            return true;
        }

        private bool ConfirmStart()
        {
            DialogResult result = CustomMessageBox.Show(
                "Die Playlist wird geleert und anschließend neu befüllt.\n\nFortfahren?",
                "Playlist aktualisieren",
                CustomMessageBoxButtons.YesNo,
                CustomMessageBoxIcon.Question,
                form,
                CustomMessageBoxSize.Small
            );

            return result == DialogResult.Yes;
        }
    }
}