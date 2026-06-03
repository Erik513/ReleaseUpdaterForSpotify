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
                "SpotifyReleaseGui_SingleInstance",
                out createdNew
            );

            if (!createdNew)
            {
                MessageBox.Show(
                    "Die Anwendung läuft bereits.",
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