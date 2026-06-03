using System.Diagnostics;
using System.Text;

namespace SpotifyReleaseGui
{
    public class BackendRunner
    {
        private Process? currentProcess;

        public event Action<string>? OutputReceived;
        public event Action<string>? ErrorReceived;
        public event Action<string>? StatusReceived;
        public event Action<int, int, int>? ProgressReceived;
        public event Action<int, bool>? Exited;

        private bool wasStoppedByUser;
        public bool IsRunning
        {
            get
            {
                return currentProcess != null && !currentProcess.HasExited;
            }
        }

        public void Start()
        {
            wasStoppedByUser = false;
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = AppPaths.BackendExePath,
                WorkingDirectory = AppPaths.BackendPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            startInfo.EnvironmentVariables["PYTHONUTF8"] = "1";

            currentProcess = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            currentProcess.OutputDataReceived += HandleOutput;
            currentProcess.ErrorDataReceived += HandleError;
            currentProcess.Exited += HandleExit;

            currentProcess.Start();
            currentProcess.BeginOutputReadLine();
            currentProcess.BeginErrorReadLine();
        }

        public void Stop()
        {
            wasStoppedByUser = true;

            try
            {
                if (currentProcess != null && !currentProcess.HasExited)
                {
                    currentProcess.Kill(true);
                }
            }
            catch
            {
            }
        }

        private void HandleOutput(object sender, DataReceivedEventArgs args)
        {
            if (args.Data == null)
            {
                return;
            }

            if (args.Data.StartsWith("STATUS:"))
            {
                string status = args.Data.Substring("STATUS:".Length);
                StatusReceived?.Invoke(status);
                return;
            }

            if (args.Data.StartsWith("PROGRESS:"))
            {
                string progressText = args.Data.Substring("PROGRESS:".Length);
                string[] parts = progressText.Split(':');

                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out int current) &&
                    int.TryParse(parts[1], out int total) &&
                    total > 0)
                {
                    int percent = current * 100 / total;
                    ProgressReceived?.Invoke(current, total, percent);
                }

                return;
            }

            OutputReceived?.Invoke(args.Data);
        }

        private void HandleError(object sender, DataReceivedEventArgs args)
        {
            if (args.Data == null)
            {
                return;
            }

            ErrorReceived?.Invoke(args.Data);
        }

        private void HandleExit(object? sender, EventArgs args)
        {
            int exitCode = 0;

            if (currentProcess != null)
            {
                exitCode = currentProcess.ExitCode;
                currentProcess.Dispose();
                currentProcess = null;
            }

            Exited?.Invoke(exitCode, wasStoppedByUser);
        }
    }
}