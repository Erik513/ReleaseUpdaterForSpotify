using ErikwnkCore;
using ReleaseUpdater.Core.Localization;

namespace ReleaseUpdaterGui
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
                errorMessage = AppLocalization.Get(AppStrings.ValidatorPlaylistNameRequired);
                return false;
            }

            if (playlistName.Trim().Length > 100)
            {
                errorMessage = AppLocalization.Get(AppStrings.ValidatorPlaylistNameTooLong);
                return false;
            }

            if (playlistName.Any(char.IsControl))
            {
                errorMessage = AppLocalization.Get(AppStrings.ValidatorPlaylistNameInvalidChars);
                return false;
            }

            return true;
        }
    }
}
