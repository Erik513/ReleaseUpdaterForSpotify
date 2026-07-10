namespace SpotifyReleaseGui
{
    internal static class Program
    {
        private static Mutex? mutex;

        [STAThread]
        static void Main()
        {
            bool createdNew;

            mutex = new Mutex(
                true,
                "SpotifyReleaseUpdater_SingleInstance",
                out createdNew
            );

            if (!createdNew)
            {
                MessageBox.Show(
                    "The application is already running.",
                    "Spotify Release Updater"
                );

                return;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());

            mutex.ReleaseMutex();
        }
    }
}
