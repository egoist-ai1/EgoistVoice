using System.Diagnostics;
using System.Windows.Threading;

namespace Egoist.Voice.Core;

/// <summary>
/// Benchmark-only dispatcher heartbeat. It measures how late a lightweight UI callback runs while
/// native ASR is active; it never samples window content, input or recognized text.
/// </summary>
internal sealed class UiThreadStallMonitor : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(25);

    private readonly DispatcherTimer _timer;
    private readonly List<TimeSpan> _delays = [];
    private long _expectedTimestamp;
    private bool _stopped;

    internal UiThreadStallMonitor(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(Interval, DispatcherPriority.Background, OnTick, dispatcher);
        _expectedTimestamp = NextExpected(Stopwatch.GetTimestamp());
        _timer.Start();
    }

    internal BenchmarkUiStallSummary StopAndSummarize()
    {
        if (!_stopped)
        {
            _stopped = true;
            _timer.Stop();
            _timer.Tick -= OnTick;
        }
        return Summarize(_delays);
    }

    internal static BenchmarkUiStallSummary Summarize(IReadOnlyList<TimeSpan> delays) => new(
        delays.Count,
        LatencyStatistics.Median(delays).TotalMilliseconds,
        LatencyStatistics.Percentile(delays, 0.95).TotalMilliseconds,
        delays.Count == 0 ? 0 : delays.Max(delay => delay.TotalMilliseconds),
        delays.Count(delay => delay > TimeSpan.FromMilliseconds(100)));

    private void OnTick(object? sender, EventArgs args)
    {
        var now = Stopwatch.GetTimestamp();
        _delays.Add(now <= _expectedTimestamp
            ? TimeSpan.Zero
            : Stopwatch.GetElapsedTime(_expectedTimestamp, now));
        _expectedTimestamp = NextExpected(now);
    }

    private static long NextExpected(long now) =>
        now + (long)Math.Ceiling(Interval.TotalSeconds * Stopwatch.Frequency);

    public void Dispose() => StopAndSummarize();
}

