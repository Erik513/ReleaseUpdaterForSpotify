using CustomWFUI.Forms;
using CustomWFUI.Styles;

namespace ReleaseUpdaterGui
{
    internal static class Program
    {
        private static Mutex? mutex;

        [STAThread]
        static void Main()
        {
            // Initialize before the mutex check too, so the "already running"
            // dialog below gets the same visual styles/DPI setup as the main
            // form instead of rendering unstyled.
            ApplicationConfiguration.Initialize();

            // Spotify green, since this app is Spotify-flavored.
            UIColors.SetAccent(UIAccentColors.Green);

            bool createdNew;

            mutex = new Mutex(
                true,
                "SpotifyReleaseUpdater_SingleInstance",
                out createdNew);

            if (!createdNew)
            {
                CustomMessageBox.Show(
                    "The application is already running.",
                    "Release Updater for Spotify",
                    icon: CustomMessageBoxIcon.Info);

                mutex.Dispose();
                return;
            }

            try
            {
                Application.Run(new MainForm());
            }
            finally
            {
                mutex.ReleaseMutex();
                mutex.Dispose();
            }
        }
    }
}
