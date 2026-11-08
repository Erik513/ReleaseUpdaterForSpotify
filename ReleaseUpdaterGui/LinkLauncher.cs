using System.Diagnostics;
using ReleaseUpdater.Core.Services;

namespace ReleaseUpdaterGui
{
    public enum LinkOpenResult
    {
        Opened,
        NotAWebAddress,
        Failed
    }

    /// <summary>
    /// Opens the link of a tracked song in the browser. The text is typed or pasted by the user (or read from a file), so only a web
    /// address is opened (see <see cref="TrackedSongLinks.Normalize"/>) - never a file, a program or another scheme.
    /// </summary>
    public static class LinkLauncher
    {
        /// <param name="text">The link as the user wrote it.</param>
        /// <param name="error">The reason when the result is <see cref="LinkOpenResult.Failed"/>, else null.</param>
        public static LinkOpenResult Open(string? text, out string? error)
        {
            error = null;
            string? address = TrackedSongLinks.Normalize(text);

            if (address is null)
            {
                return LinkOpenResult.NotAWebAddress;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = address,
                    UseShellExecute = true
                });

                return LinkOpenResult.Opened;
            }
            catch (Exception ex)
            {
                error = ex.Message;

                return LinkOpenResult.Failed;
            }
        }
    }
}
