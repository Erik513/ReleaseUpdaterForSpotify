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
                errorMessage = "Please enter a playlist name.";
                return false;
            }

            if (playlistName.Trim().Length > 100)
            {
                errorMessage = "The playlist name can be up to 100 characters long.";
                return false;
            }

            char[] forbiddenChars = Path.GetInvalidFileNameChars();

            foreach (char forbiddenChar in forbiddenChars)
            {
                if (playlistName.Contains(forbiddenChar))
                {
                    errorMessage = "The playlist name contains invalid characters.";
                    return false;
                }
            }

            return true;
        }
    }
}
