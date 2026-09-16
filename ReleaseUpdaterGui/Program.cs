using ErikwnkCore;
using ErikwnkWFUI.Forms;
using ErikwnkWFUI.Styles;

namespace ReleaseUpdaterGui
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
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
                        "The application is already running.",
                        "Release Updater for Spotify",
                        icon: MessageBoxIcon.Info);

                    return;
                }

                Application.Run(new MainForm());
            }
        }
    }
}
