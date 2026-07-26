namespace Egoist.Voice.Core;

public sealed record PostProcessingOptions(
    bool ApplyDictionary = true,
    bool ApplyVoiceCommands = true,
    bool ApplyNumberNormalization = false)
{
    /// <summary>
    /// Number normalization is off by default on purpose. It is the one step that changes text the
    /// user did not ask to change — dictating prose, "двадцать пять" should stay words — so it is
    /// opt-in, unlike the dictionary and the formatting commands.
    /// </summary>
    public static PostProcessingOptions Default { get; } = new();
}

/// <summary>
/// The single deterministic pipeline applied to whichever engine won. Previously each engine had
/// its own formatting path, so the same speech came out differently depending on which one the
/// selector happened to pick.
/// </summary>
public sealed class TranscriptPostProcessor
{
    private readonly VoiceCommandProcessor _commands;

    public TranscriptPostProcessor(
        UserDictionary? dictionary = null,
        PostProcessingOptions? options = null,
        VoiceCommandProcessor? commands = null)
    {
        Dictionary = dictionary ?? UserDictionary.Empty;
        Options = options ?? PostProcessingOptions.Default;
        _commands = commands ?? new VoiceCommandProcessor();
    }

    public UserDictionary Dictionary { get; }
    public PostProcessingOptions Options { get; }

    public string Process(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Order matters. The dictionary runs first so its replacements are visible to the command
        // pass; numbers run before the commands so that "пять процентов, точка" resolves cleanly;
        // whitespace and capitalization are tidied last, over the final shape of the text.
        if (Options.ApplyDictionary)
        {
            text = Dictionary.Apply(text);
        }

        if (Options.ApplyNumberNormalization)
        {
            text = RussianNumberNormalizer.Normalize(text);
        }

        if (Options.ApplyVoiceCommands)
        {
            text = _commands.Apply(text);
        }

        return TranscriptNormalizer.Normalize(text);
    }
}
