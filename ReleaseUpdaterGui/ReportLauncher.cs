using System.Diagnostics;

namespace ReleaseUpdaterGui
{
    public enum ReportOpenResult
    {
        Opened,
        Missing,
        Failed
    }

    /// <summary>Opens a report file in the program the system has for it (the two controllers did this identically).</summary>
    public static class ReportLauncher
    {
        /// <param name="path">The report file.</param>
        /// <param name="error">The reason when the result is <see cref="ReportOpenResult.Failed"/>, else null.</param>
        public static ReportOpenResult Open(string path, out string? error)
        {
            error = null;

            if (!File.Exists(path))
            {
                return ReportOpenResult.Missing;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });

                return ReportOpenResult.Opened;
            }
            catch (Exception ex)
            {
                error = ex.Message;

                return ReportOpenResult.Failed;
            }
        }
    }
}
