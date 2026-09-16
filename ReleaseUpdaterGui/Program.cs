using ErikwnkCore;
using ErikwnkWFUI;
using ErikwnkWFUI.Forms;
using ErikwnkWFUI.Styles;
using ReleaseUpdater.Core.Localization;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Data.Storage;

namespace ReleaseUpdaterGui
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            AppStrings.Register();
            ApplyPersistedLanguage();

            // Initialize before the mutex check too, so the "already running"
            // dialog below gets the same visual styles/DPI setup as the main
            // form instead of rendering unstyled.
            ApplicationConfiguration.Initialize();

            // Spotify green, since this app is Spotify-flavored.
            UIColors.SetAccent(UIAccentColors.Green);

            using (var guard = new SingleInstanceGuard("SpotifyReleaseUpdater_SingleInstance"))
            {
                if (!guard.IsFirstInstance)
                {
                    MessageBox.Show(
                        AppLocalization.Get(AppStrings.ProgramAlreadyRunningMessage),
                        AppLocalization.Get(AppStrings.MainFormWindowTitle),
                        icon: MessageBoxIcon.Info);

                    return;
                }

                Application.Run(new MainForm());
            }
        }

        // Reads the saved language before any UI (including the
        // already-running dialog) is built, so that dialog and MainForm's
        // initial build both come up in the right language immediately -
        // MainForm's own settings load happens later, in OnShown, which is
        // too late for text built before the form is even shown.
        private static void ApplyPersistedLanguage()
        {
            try
            {
                AppDataPaths dataPaths = AppDataPaths.CreateDefault();
                JsonSettingsStore settingsStore = new(dataPaths);
                ReleaseSettings settings = settingsStore.Load().Normalize();

                UIStyles.SetLanguage(
                    string.Equals(settings.Language, "German", StringComparison.OrdinalIgnoreCase)
                        ? AppLanguage.German
                        : AppLanguage.English);
            }
            catch
            {
                // Falls back to the default (English) if settings can't be read yet.
            }
        }
    }
}
