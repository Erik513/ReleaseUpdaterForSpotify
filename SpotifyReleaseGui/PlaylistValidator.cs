namespace SpotifyReleaseGui
{
    public static class PlaylistValidator
    {
        public static bool IsValidPlaylistName(
            string playlistName,
            out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(playlistName))
            {
                errorMessage = "Bitte gib einen Playlistnamen ein.";
                return false;
            }

            if (playlistName.Trim().Length > 100)
            {
                errorMessage = "Der Playlistname darf maximal 100 Zeichen lang sein.";
                return false;
            }

            char[] forbiddenChars = Path.GetInvalidFileNameChars();

            foreach (char forbiddenChar in forbiddenChars)
            {
                if (playlistName.Contains(forbiddenChar))
                {
                    errorMessage = "Der Playlistname enthält ungültige Zeichen.";
                    return false;
                }
            }

            return true;
        }
    }
}