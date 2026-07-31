namespace ReleaseUpdater.Data.Storage;

public sealed class AppDataPaths
{
    public AppDataPaths(string baseDirectory)
    {
        BaseDirectory = baseDirectory;
        SettingsPath = Path.Combine(BaseDirectory, "settings.json");
        TokenPath = Path.Combine(BaseDirectory, "spotify-token.json");
        ReportPath = Path.Combine(BaseDirectory, "last_run_report.html");
    }

    public string BaseDirectory { get; }
    public string SettingsPath { get; }
    public string TokenPath { get; }
    public string ReportPath { get; }

    public static AppDataPaths CreateDefault()
    {
        string appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);

        return new AppDataPaths(
            Path.Combine(appData, "SpotifyReleaseUpdater"));
    }

    public void EnsureDirectory()
    {
        Directory.CreateDirectory(BaseDirectory);
    }
}
