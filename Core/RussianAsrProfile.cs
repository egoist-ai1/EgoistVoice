using SherpaOnnx;

namespace Egoist.Voice.Core;

/// <summary>The single shipped Russian recognizer profile, measured with sherpa-onnx 1.13.4.</summary>
internal static class RussianAsrProfile
{
    internal const string ModelName = "GigaAM v3 RNNT";
    internal const int SampleRate = 16_000;
    internal const int FeatureDimension = 64;
    internal const string Provider = "cpu";
    internal const string DecodingMethod = "greedy_search";
    internal const int MaxActivePaths = 4;
    internal const int MaximumDefaultThreads = 8;
    internal static bool ContextualBiasSupported => false;

    // On the release host, 8 threads matched all 4/12-thread transcripts and had the lowest
    // warm p95 in the public paired pilot. Bound other machines while retaining callback/UI headroom.
    internal static int GetDefaultThreads(int logicalProcessorCount) =>
        Math.Clamp(Math.Max(1, logicalProcessorCount) * 3 / 4, 1, MaximumDefaultThreads);

    internal static OfflineRecognizerConfig CreateRecognizerConfig(
        string encoder, string decoder, string joiner, string tokens, int threads) => new()
    {
        FeatConfig = new FeatureConfig { SampleRate = SampleRate, FeatureDim = FeatureDimension },
        ModelConfig = new OfflineModelConfig
        {
            Tokens = tokens,
            NumThreads = Math.Clamp(threads, 1, 12),
            Debug = 0,
            Provider = Provider,
            Transducer = new OfflineTransducerModelConfig
            {
                Encoder = encoder,
                Decoder = decoder,
                Joiner = joiner
            }
        },
        // Plain RNNT has 33 Russian/space symbols plus blank, rather than the E2E BPE vocabulary.
        // Greedy reduced public pilot errors from 17 to 16 and latency from 154.8 to 91.9 ms p50.
        // That small fixture is not a personal-voice accuracy guarantee.
        DecodingMethod = DecodingMethod,
        MaxActivePaths = MaxActivePaths
    };
}