namespace SpotifyReleaseGui
{
    public class AppSettings
    {
        public string? PlaylistId { get; set; }
        public string PlaylistName { get; set; } = "[Followed Artists - New Releases]";
        public int ReleaseLookbackDays { get; set; } = 10;
    }
}