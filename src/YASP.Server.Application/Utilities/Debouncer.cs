namespace YASP.Server.Application.Utilities
{
    /// <summary>
    /// Helper class to throttle calls to a lambda.
    /// </summary>
    public class Debouncer
    {
        private CancellationTokenSource _throttleCancel = new CancellationTokenSource();
        private Task _task;

        /// <summary>
        /// Runs the specified <paramref name="action"/> after <paramref name="timeRunAfter"/>. Another call to this method within this period resets the timer.
        /// </summary>
        /// <param name="timeRunAfter"></param>
        /// <param name="action"></param>
        public void Throttle(TimeSpan timeRunAfter, Func<Task> action)
        {
            lock (this)
            {
                if (_task != null)
                {
                    _throttleCancel.Cancel();
                    _throttleCancel = new CancellationTokenSource();
                }
            }

            _task = Task.Run(async () =>
            {
                await Task.Delay(timeRunAfter, _throttleCancel.Token).ConfigureAwait(false);
                await action();
            });
        }
    }
}
