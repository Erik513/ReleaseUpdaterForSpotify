using System.Text;

namespace ReleaseUpdater.Data.Storage;

internal static class AtomicFile
{
    /// <summary>
    /// Writes via a temp file plus atomic replace so a crash or forced app close mid-write
    /// (e.g. while a playlist update is still running) can never leave a truncated or
    /// corrupted file behind. If the write or the replace fails, the temp file is removed
    /// again and the original exception is rethrown, so no stray ".tmp" is left behind.
    /// </summary>
    public static void WriteAllText(string path, string content)
    {
        string tempPath = path + ".tmp";

        try
        {
            File.WriteAllText(tempPath, content, Encoding.UTF8);

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
