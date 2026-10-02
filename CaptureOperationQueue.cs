using Egoist.Voice.Services;

namespace Egoist.Voice;

/// <summary>FIFO ownership of synchronous native capture work. No operation runs on the dispatcher.</summary>
internal sealed class CaptureOperationQueue(IAudioCaptureService capture)
{
    internal const int MaximumPendingOperations = 16;
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;
    private Task? _shutdown;
    private int _pending;
    private bool _closing;

    internal Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default,
        bool cleanup = false)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (!cleanup && _pending >= MaximumPendingOperations)
                throw new InvalidOperationException("Дождитесь завершения операции с микрофоном.");
            var previous = _tail;
            _pending++;
            var next = Task.Run(async () =>
            {
                try
                {
                    // An earlier failure cannot poison the next device/recovery operation.
                    try { await previous.ConfigureAwait(false); } catch { }
                    cancellationToken.ThrowIfCancellationRequested();
                    return await operation().ConfigureAwait(false);
                }
                finally { lock (_gate) _pending--; }
            });
            // Keep only a completion barrier: retaining Task<T> here would also keep the
            // last capture/result array alive while the queue is idle. The caller still owns
            // the original task, including its result, cancellation or failure.
            _tail = next.ContinueWith(static completed => { _ = completed.Exception; },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return next;
        }
    }

    internal Task RunAsync(Action operation, CancellationToken cancellationToken = default, bool cleanup = false) =>
        RunAsync(() => { operation(); return Task.FromResult(true); }, cancellationToken, cleanup);

    internal Task ShutdownAsync()
    {
        lock (_gate)
        {
            if (_shutdown is not null) return _shutdown;
            _closing = true;
            var previous = _tail;
            return _shutdown = Task.Run(async () =>
            {
                try { await previous.ConfigureAwait(false); } catch { }
                capture.Dispose();
            });
        }
    }
}
