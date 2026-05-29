using System.IO;

namespace SpotifyReleaseGui
{
    public static class AppPaths
    {
        public static string BackendPath =>
            Path.Combine(Application.StartupPath, "PythonBackend");

        public static string ConfigPath =>
            Path.Combine(BackendPath, "config.json");

        public static string BackendExePath =>
            Path.Combine(BackendPath, "main.exe");

        public static string ReportPath =>
            Path.Combine(BackendPath, "last_run_report.html");
    }
}