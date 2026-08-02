namespace Egoist.Voice.Services;

/// <summary>
/// Decides whether a session contained speech at all.
/// </summary>
/// <remarks>
/// <para>
/// The thresholds are absolute, and an attempt to make them adapt to the room was reverted. The
/// reason is worth recording: within a single push-to-talk session there is no reliable way to tell
/// "quiet speech throughout" from "noise throughout" — both look like a steady level a little above
/// the floor. Every adaptive variant that rejected a noisy room also rejected genuine quiet speech,
/// which is the far worse failure: a dictation silently discarded is a dictation lost.
/// </para>
/// <para>
/// The honest fix for the original complaint is not a cleverer gate but a visible one. The snapshot
/// now carries enough context for the UI to say why a session was dropped instead of hiding it, and
/// a real adaptive floor becomes possible once the pre-roll buffer gives us audio recorded before
/// the user started speaking.
/// </para>
/// </remarks>
internal sealed class SpeechActivityDetector
{
    private const double DefaultSpeechRmsThresholdDb = -48;
    private const double MinimumSpeechMilliseconds = 120;
    private const double MinimumContinuousSpeechMilliseconds = 60;
    private double _speechRmsThresholdDb = DefaultSpeechRmsThresholdDb;
    private double _speechPeakThresholdDb = -38;

    /// <summary>Below this the microphone is effectively delivering nothing at all.</summary>
    internal const double SilentSessionPeakDb = -60;

    private double _durationMilliseconds;
    private double _speechMilliseconds;
    private double _continuousSpeechMilliseconds;
    private double _longestSpeechMilliseconds;
    private double _peakDecibels = -120;
    private double _quietestRmsDecibels = double.PositiveInfinity;

    internal void Reset(double? noiseFloorDb = null)
    {
        _durationMilliseconds = 0;
        _speechMilliseconds = 0;
        _continuousSpeechMilliseconds = 0;
        _longestSpeechMilliseconds = 0;
        _peakDecibels = -120;
        _quietestRmsDecibels = double.PositiveInfinity;
        _speechRmsThresholdDb = noiseFloorDb is { } finite && double.IsFinite(finite)
            ? Math.Clamp(finite + 8, -56, -42)
            : DefaultSpeechRmsThresholdDb;
        _speechPeakThresholdDb = Math.Clamp(_speechRmsThresholdDb + 8, -48, -34);
    }

    internal void Process(double rmsAmplitude, double peakAmplitude, double durationMilliseconds)
    {
        if (durationMilliseconds <= 0)
        {
            return;
        }

        var rmsDb = AmplitudeToDecibels(rmsAmplitude);
        var peakDb = AmplitudeToDecibels(peakAmplitude);
        _durationMilliseconds += durationMilliseconds;
        _peakDecibels = Math.Max(_peakDecibels, peakDb);
        _quietestRmsDecibels = Math.Min(_quietestRmsDecibels, rmsDb);

        var sustained = rmsDb >= _speechRmsThresholdDb && peakDb >= _speechRmsThresholdDb + 4;
        var lowEnergyConsonant = rmsDb >= _speechRmsThresholdDb - 3 && peakDb >= _speechPeakThresholdDb;
        if (sustained || lowEnergyConsonant)
        {
            _speechMilliseconds += durationMilliseconds;
            _continuousSpeechMilliseconds += durationMilliseconds;
            _longestSpeechMilliseconds = Math.Max(_longestSpeechMilliseconds, _continuousSpeechMilliseconds);
        }
        else
        {
            _continuousSpeechMilliseconds = 0;
        }
    }

    internal SpeechActivitySnapshot Snapshot()
    {
        var hasSpeech = _speechMilliseconds >= MinimumSpeechMilliseconds &&
            _longestSpeechMilliseconds >= MinimumContinuousSpeechMilliseconds;

        return new SpeechActivitySnapshot(
            hasSpeech,
            TimeSpan.FromMilliseconds(_durationMilliseconds),
            TimeSpan.FromMilliseconds(_speechMilliseconds),
            _peakDecibels,
            hasSpeech ? SpeechRejection.None : Classify());
    }

    /// <summary>
    /// Explains a rejected session. Without this the capsule simply vanished, and the two causes —
    /// nothing was said, and the microphone is too quiet to hear — are indistinguishable to a user
    /// who is looking at an empty text field wondering what went wrong.
    /// </summary>
    private SpeechRejection Classify()
    {
        if (_durationMilliseconds <= 0)
        {
            return SpeechRejection.NoAudio;
        }

        if (_peakDecibels < SilentSessionPeakDb)
        {
            return SpeechRejection.MicrophoneSilent;
        }

        // Loud enough to be audible, but never sustained long enough to be a phrase: this is the
        // signature of a stray click or a microphone picking up room noise only.
        return _speechMilliseconds > 0 ? SpeechRejection.TooShort : SpeechRejection.TooQuiet;
    }

    internal static double AmplitudeToDecibels(double amplitude) =>
        20 * Math.Log10(Math.Max(amplitude, 0.000001));
}

internal enum SpeechRejection
{
    None,
    NoAudio,
    MicrophoneSilent,
    TooQuiet,
    TooShort
}

internal sealed record SpeechActivitySnapshot(
    bool HasSpeech,
    TimeSpan Duration,
    TimeSpan DetectedSpeech,
    double PeakDecibels,
    SpeechRejection Rejection = SpeechRejection.None);
