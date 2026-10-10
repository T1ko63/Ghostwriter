using System.Collections.Concurrent;

namespace Kuroko.Windows.Tests.Fakes;

/// <summary>
/// Runs a test on one thread with a message loop, like the WPF UI thread AppController lives on: every continuation
/// after an await (also after Task.Run) comes back to this thread, so the controller is never entered concurrently.
/// </summary>
internal static class UiThread
{
    public static void Run(Func<Task> test)
    {
        var previous = SynchronizationContext.Current;
        var context = new PumpContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var task = test();
            task.ContinueWith(_ => context.Complete(), TaskScheduler.Default);
            context.RunUntilComplete();
            task.GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>Lets queued work run until <paramref name="condition"/> holds; fails after <paramref name="timeoutMs"/>.</summary>
    public static async Task Until(Func<bool> condition, string what, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException($"Timed out waiting for: {what}");
            await Task.Delay(1);
        }
    }

    /// <summary>Gives already queued continuations a few turns of the loop.</summary>
    public static async Task Settle()
    {
        for (var i = 0; i < 20; i++) await Task.Delay(1);
    }

    private sealed class PumpContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            // Work left over from a finished test (e.g. a timer of a fire-and-forget run) must not throw on a closed loop.
            try
            {
                _queue.Add((d, state));
            }
            catch (InvalidOperationException)
            {
                ThreadPool.QueueUserWorkItem(_ => d(state));
            }
        }

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

        public void Complete() => _queue.CompleteAdding();

        public void RunUntilComplete()
        {
            foreach (var (callback, state) in _queue.GetConsumingEnumerable()) callback(state);
        }
    }
}
