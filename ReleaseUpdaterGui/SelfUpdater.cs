using System.Diagnostics;

namespace ReleaseUpdaterGui
{
    /// <summary>
    /// Downloads a new build and swaps it in for the currently running exe. Windows
    /// won't let a running exe overwrite itself, so a short-lived PowerShell helper
    /// does the actual file swap and relaunch after this process exits.
    /// </summary>
    public sealed class SelfUpdater
    {
        private readonly HttpClient httpClient;

        public SelfUpdater(HttpClient httpClient)
        {
            this.httpClient = httpClient;
        }

        /// <summary>
        /// Downloads the new exe and hands off to the swap helper. Returns true once
        /// the caller should exit so the helper can complete the swap; false if the
        /// download failed, in which case nothing was changed.
        /// </summary>
        public async Task<bool> DownloadAndPrepareUpdateAsync(
            string downloadUrl,
            IProgress<int>? downloadProgress,
            CancellationToken cancellationToken)
        {
            string? currentExePath = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(currentExePath))
            {
                return false;
            }

            string updateDir = Path.Combine(Path.GetTempPath(), "ReleaseUpdaterGui_update");
            Directory.CreateDirectory(updateDir);
            string newExePath = Path.Combine(
                updateDir,
                Path.GetFileName(currentExePath) + ".new");

            bool downloaded = await TryDownloadAsync(
                downloadUrl,
                newExePath,
                downloadProgress,
                cancellationToken);

            if (!downloaded)
            {
                TryDelete(newExePath);
                return false;
            }

            LaunchSwapHelper(updateDir, currentExePath, newExePath, Environment.ProcessId);
            return true;
        }

        private async Task<bool> TryDownloadAsync(
            string downloadUrl,
            string destinationPath,
            IProgress<int>? downloadProgress,
            CancellationToken cancellationToken)
        {
            try
            {
                using HttpResponseMessage response = await httpClient.GetAsync(
                    downloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                long? totalBytes = response.Content.Headers.ContentLength;

                await using Stream sourceStream =
                    await response.Content.ReadAsStreamAsync(cancellationToken);
                await using FileStream fileStream = new(
                    destinationPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);

                byte[] buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await sourceStream.ReadAsync(
                    buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(
                        buffer.AsMemory(0, bytesRead), cancellationToken);
                    totalRead += bytesRead;

                    if (totalBytes is > 0)
                    {
                        downloadProgress?.Report((int)(totalRead * 100 / totalBytes.Value));
                    }
                }

                return new FileInfo(destinationPath).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Spawns a detached PowerShell script that waits for this process to exit,
        /// moves the downloaded exe over the current one, relaunches it, then deletes
        /// itself.
        /// </summary>
        private static void LaunchSwapHelper(
            string updateDir,
            string targetExePath,
            string newExePath,
            int processId)
        {
            string scriptPath = Path.Combine(updateDir, "apply-update.ps1");

            string script = $$"""
                $ErrorActionPreference = 'SilentlyContinue'
                try { Wait-Process -Id {{processId}} -Timeout 30 } catch {}
                Start-Sleep -Milliseconds 500
                for ($i = 0; $i -lt 20; $i++) {
                    try {
                        Move-Item -Path '{{newExePath}}' -Destination '{{targetExePath}}' -Force
                        break
                    } catch {
                        Start-Sleep -Milliseconds 500
                    }
                }
                Start-Process -FilePath '{{targetExePath}}'
                Remove-Item -Path $MyInvocation.MyCommand.Path -Force
                """;

            File.WriteAllText(scriptPath, script);

            ProcessStartInfo startInfo = new()
            {
                FileName = "powershell.exe",
                Arguments =
                    $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(startInfo);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
