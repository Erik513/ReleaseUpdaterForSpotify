namespace ReleaseUpdaterGui
{
    /// <summary>
    /// Runs an action once, a short time after the last call of <see cref="Schedule"/> (a UI-thread timer,
    /// so the action runs on the UI thread). Holding the arrow of a number box changes the value a dozen
    /// times a second; with this the settings file is written once, when the value has settled -
    /// and <see cref="Flush"/> writes it at once when the window is about to close.
    /// </summary>
    public sealed class DelayedAction : IDisposable
    {
        private readonly Action action;
        private readonly System.Windows.Forms.Timer timer;
        private bool isPending;

        public DelayedAction(Action action, int delayMilliseconds)
        {
            this.action = action ?? throw new ArgumentNullException(nameof(action));

            timer = new System.Windows.Forms.Timer { Interval = Math.Max(1, delayMilliseconds) };
            timer.Tick += (_, _) => Flush();
        }

        /// <summary>True while an action is waiting to run.</summary>
        public bool IsPending => isPending;

        /// <summary>Asks for the action to run after the delay; asking again starts the delay anew.</summary>
        public void Schedule()
        {
            isPending = true;
            timer.Stop();
            timer.Start();
        }

        /// <summary>Runs the action now if one is waiting (once), else does nothing.</summary>
        public void Flush()
        {
            timer.Stop();

            if (!isPending)
            {
                return;
            }

            isPending = false;
            action();
        }

        /// <summary>Forgets a waiting action without running it.</summary>
        public void Cancel()
        {
            timer.Stop();
            isPending = false;
        }

        public void Dispose()
        {
            timer.Dispose();
        }
    }
}
