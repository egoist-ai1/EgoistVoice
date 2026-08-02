using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace Egoist.Voice.Services;

/// <summary>
/// Diagnostics that never sit in the caller's path.
/// </summary>
/// <remarks>
/// The previous implementation appended to the file synchronously under a global lock, with a
/// <see cref="File.Exists"/> and a <see cref="FileInfo"/> stat on every call. It was invoked five
/// or more times per dictation, including from places budgeted in microseconds. Writing is now a
/// queue push; a single background worker drains it.
/// </remarks>
internal static class AppLog
{
    private const long MaxFileBytes = 1_000_000;

    private static readonly BlockingCollection<string> Queue = new(new ConcurrentQueue<string>(), 4096);
    private static readonly AsyncLocal<int> SensitiveScopeDepth = new();
    private static readonly string DirectoryPath = ResolveDirectoryPath();

    private static readonly Thread Worker;
    private static long _writtenBytes = -1;

    internal static readonly string FilePath = Path.Combine(DirectoryPath, "app.log");

    internal static bool IsSensitiveDataSuppressed => SensitiveScopeDepth.Value > 0;

    private static string ResolveDirectoryPath()
    {
        var isolatedPath = Environment.GetEnvironmentVariable("EGOISTVOICE_LOG_DIRECTORY");
        return string.IsNullOrWhiteSpace(isolatedPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EgoistVoice",
                "Logs")
            : Path.GetFullPath(isolatedPath);
    }

    static AppLog()
    {
        Worker = new Thread(Drain)
        {
            IsBackground = true,
            Name = "EgoistVoice.Log",
            Priority = ThreadPriority.BelowNormal
        };
        Worker.Start();
    }

    internal static void Write(string message, Exception? exception = null)
    {
        try
        {
            if (IsSensitiveDataSuppressed)
            {
                return;
            }

            var line = $"{DateTimeOffset.Now:O} [{Environment.ProcessId}] {message}{Describe(exception)}";

            // Dropping a line under sustained pressure is preferable to blocking a caller that may
            // be a low-level hook callback. After Flush the collection is closed and TryAdd simply
            // returns false, which is the correct behaviour during shutdown.
            if (!Queue.IsAddingCompleted)
            {
                Queue.TryAdd(line);
            }
        }
        catch
        {
            // Diagnostics must never break dictation.
        }
    }

    /// <summary>
    /// Suppresses diagnostics on the current async flow while private corpus audio and transcripts
    /// are in scope. ASR exceptions can contain the input file path, so merely redacting the outer
    /// benchmark catch is not sufficient. The scope flows through awaited work and Task.Run, while
    /// normal interactive diagnostics outside the benchmark remain unchanged.
    /// </summary>
    internal static IDisposable SuppressSensitiveData()
    {
        SensitiveScopeDepth.Value++;
        return new SensitiveLogScope();
    }

    /// <summary>
    /// Drains the queue and stops the worker. Called on shutdown; without it the background thread
    /// is torn down wherever it happens to be and the last records — usually the interesting ones —
    /// never reach the file.
    /// </summary>
    /// <remarks>
    /// Waiting on <c>Queue.Count</c> would not be enough: the count drops to zero the moment the
    /// worker takes a batch, well before that batch is written. Completing the collection and
    /// joining the thread is the only signal that means "on disk".
    /// </remarks>
    internal static void Flush(TimeSpan timeout)
    {
        try
        {
            Queue.CompleteAdding();
            Worker.Join(timeout);
        }
        catch (Exception)
        {
            // Shutdown diagnostics must never prevent shutdown.
        }
    }

    /// <summary>
    /// Unwraps the whole exception chain.
    /// </summary>
    /// <remarks>
    /// Only the outermost message used to be recorded, which is close to useless for the exceptions
    /// that matter: a XAML parse failure surfaces as "the type initializer threw an exception" and
    /// the actual cause — the element and the line — lives two levels down. A log that hides the
    /// cause costs more than the disk it saves.
    /// </remarks>
    private static string Describe(Exception? exception)
    {
        if (exception is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var depth = 0;
        for (var current = exception; current is not null && depth < 6; current = current.InnerException, depth++)
        {
            builder.Append(Environment.NewLine);
            builder.Append(depth == 0 ? " | " : new string(' ', depth * 2) + "└─ ");
            builder.Append(current.GetType().Name).Append(": ").Append(current.Message);

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    builder.Append(Environment.NewLine).Append("     · ")
                        .Append(inner.GetType().Name).Append(": ").Append(inner.Message);
                }
            }
        }

        builder.Append(Environment.NewLine).Append(exception.StackTrace);
        return builder.ToString();
    }

    private static void Drain()
    {
        var batch = new StringBuilder(4096);
        foreach (var line in Queue.GetConsumingEnumerable())
        {
            batch.Clear();
            batch.AppendLine(line);

            // Coalesce whatever else is already queued: a burst of ten lines should cost one write,
            // not ten.
            while (batch.Length < 32_768 && Queue.TryTake(out var extra))
            {
                batch.AppendLine(extra);
            }

            TryAppend(batch.ToString());
        }
    }

    private static void TryAppend(string payload)
    {
        try
        {
            if (_writtenBytes < 0)
            {
                Directory.CreateDirectory(DirectoryPath);
                _writtenBytes = File.Exists(FilePath) ? new FileInfo(FilePath).Length : 0;
            }

            if (_writtenBytes > MaxFileBytes)
            {
                File.Move(FilePath, FilePath + ".previous", true);
                _writtenBytes = 0;
            }

            File.AppendAllText(FilePath, payload);
            _writtenBytes += Encoding.UTF8.GetByteCount(payload);
        }
        catch
        {
            // A locked or unavailable log file must not escalate. Re-stat on the next write.
            _writtenBytes = -1;
        }
    }

    private sealed class SensitiveLogScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            SensitiveScopeDepth.Value = Math.Max(0, SensitiveScopeDepth.Value - 1);
        }
    }
}
