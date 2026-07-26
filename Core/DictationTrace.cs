using System.Diagnostics;

namespace Egoist.Voice.Core;

/// <summary>
/// Stages of one dictation, in the order the user experiences them. Everything from
/// <see cref="CaptureStopped"/> onwards happens after the button is released and is therefore
/// perceived latency.
/// </summary>
public enum DictationStage
{
    CaptureStarted,
    CaptureStopped,
    SpeechChecked,
    PrimaryDecoded,
    FallbackDecoded,
    TextFormatted,
    Delivered
}

/// <summary>
/// A stopwatch with named marks. Deliberately allocation-light and lock-free: it sits in the hot
/// path, and a measurement tool that changes the thing it measures is worse than no tool.
/// </summary>
public sealed class DictationTrace
{
    private readonly long _origin = Stopwatch.GetTimestamp();
    private readonly List<(DictationStage Stage, long Timestamp)> _marks = new(8);

    public void Mark(DictationStage stage) => _marks.Add((stage, Stopwatch.GetTimestamp()));

    public TimeSpan Total => _marks.Count == 0
        ? TimeSpan.Zero
        : Stopwatch.GetElapsedTime(_origin, _marks[^1].Timestamp);

    /// <summary>Time from release of the trigger to the last mark — what the user actually waits.</summary>
    public TimeSpan PerceivedLatency
    {
        get
        {
            var release = FindTimestamp(DictationStage.CaptureStopped);
            return release is null || _marks.Count == 0
                ? TimeSpan.Zero
                : Stopwatch.GetElapsedTime(release.Value, _marks[^1].Timestamp);
        }
    }

    public IReadOnlyList<StageTiming> Segments()
    {
        var timings = new List<StageTiming>(_marks.Count);
        var previous = _origin;
        foreach (var (stage, timestamp) in _marks)
        {
            timings.Add(new StageTiming(stage, Stopwatch.GetElapsedTime(previous, timestamp)));
            previous = timestamp;
        }
        return timings;
    }

    public string Format()
    {
        var parts = Segments().Select(segment =>
            $"{segment.Stage}={segment.Duration.TotalMilliseconds:0.0}ms");
        return $"total={Total.TotalMilliseconds:0.0}ms perceived={PerceivedLatency.TotalMilliseconds:0.0}ms " +
            string.Join(' ', parts);
    }

    private long? FindTimestamp(DictationStage stage)
    {
        foreach (var mark in _marks)
        {
            if (mark.Stage == stage)
            {
                return mark.Timestamp;
            }
        }
        return null;
    }
}

public readonly record struct StageTiming(DictationStage Stage, TimeSpan Duration);

public static class LatencyStatistics
{
    /// <summary>
    /// Nearest-rank percentile. With the sample sizes involved here — tens of runs, not thousands —
    /// interpolation would invent precision the data does not have.
    /// </summary>
    public static TimeSpan Percentile(IReadOnlyList<TimeSpan> samples, double percentile)
    {
        if (samples.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var ordered = samples.OrderBy(sample => sample).ToArray();
        var rank = (int)Math.Ceiling(Math.Clamp(percentile, 0, 1) * ordered.Length);
        return ordered[Math.Clamp(rank - 1, 0, ordered.Length - 1)];
    }

    public static TimeSpan Median(IReadOnlyList<TimeSpan> samples) => Percentile(samples, 0.5);
}
