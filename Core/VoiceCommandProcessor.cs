using System.Text;

namespace Egoist.Voice.Core;

/// <summary>
/// How a command's output joins the text around it. Every command drops the sentence boundary the
/// engine put in front of it — the user said "запятая", not "точка, потом запятая" — and openers
/// additionally swallow the space that would otherwise follow.
/// </summary>
[Flags]
public enum VoiceCommandGlue
{
    None = 0,

    /// <summary>Opening bracket or quote: the next word must sit flush against it.</summary>
    NoSpaceAfter = 1
}

public sealed record VoiceCommand(
    IReadOnlyList<string> Spoken,
    string Replacement,
    VoiceCommandGlue Glue = VoiceCommandGlue.None);

/// <summary>
/// Turns spoken formatting commands into the characters they name.
/// </summary>
/// <remarks>
/// The hard part is not the mapping, it is not firing inside ordinary speech: "точка входа" must
/// stay two words. A command is therefore only honoured when it occupies a whole segment between
/// sentence boundaries, which the engine already marks — GigaAM v3 e2e emits punctuation natively.
/// The same phrase embedded in a sentence is left alone.
/// </remarks>
public sealed class VoiceCommandProcessor
{
    private static readonly char[] Boundaries = ['.', ',', '!', '?', ';', ':', '\n', '\r'];

    public static IReadOnlyList<VoiceCommand> DefaultCommands { get; } =
    [
        new(["новая строка", "с новой строки", "новая строчка"], "\n", VoiceCommandGlue.NoSpaceAfter),
        new(["новый абзац", "с нового абзаца"], "\n\n", VoiceCommandGlue.NoSpaceAfter),
        new(["запятая"], ","),
        new(["точка"], "."),
        new(["точка с запятой"], ";"),
        new(["двоеточие"], ":"),
        new(["тире", "длинное тире"], " —"),
        new(["вопросительный знак"], "?"),
        new(["восклицательный знак"], "!"),
        new(["открыть скобку", "скобка открывается"], " (", VoiceCommandGlue.NoSpaceAfter),
        new(["закрыть скобку", "скобка закрывается"], ")"),
        new(["открыть кавычки", "кавычки открываются"], " «", VoiceCommandGlue.NoSpaceAfter),
        new(["закрыть кавычки", "кавычки закрываются"], "»")
    ];

    private readonly Dictionary<string, VoiceCommand> _lookup;

    public VoiceCommandProcessor(IEnumerable<VoiceCommand>? commands = null)
    {
        _lookup = new Dictionary<string, VoiceCommand>(StringComparer.Ordinal);
        foreach (var command in commands ?? DefaultCommands)
        {
            foreach (var spoken in command.Spoken)
            {
                _lookup[Fold(spoken)] = command;
            }
        }
    }

    public string Apply(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || _lookup.Count == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var start = 0;
        var replaced = false;
        var suppressLeadingSpace = false;

        while (start <= text.Length)
        {
            var end = text.IndexOfAny(Boundaries, start);
            var segmentEnd = end < 0 ? text.Length : end;
            var segment = text[start..segmentEnd];
            var delimiter = end < 0 ? string.Empty : text[end].ToString();

            if (IsPureCommand(segment) && _lookup.TryGetValue(Fold(segment), out var command))
            {
                replaced = true;

                // The engine already closed the previous segment with a comma or a period. The user
                // asked for a specific mark, so that boundary is replaced rather than added to.
                TrimTrailingPunctuation(builder);
                builder.Append(command.Replacement);
                suppressLeadingSpace = command.Glue.HasFlag(VoiceCommandGlue.NoSpaceAfter);
            }
            else
            {
                builder.Append(suppressLeadingSpace ? segment.TrimStart() : segment).Append(delimiter);
                suppressLeadingSpace = false;
            }

            if (end < 0)
            {
                break;
            }
            start = end + 1;
        }

        return replaced ? Tidy(builder.ToString()) : text;
    }

    /// <summary>
    /// Rejects a segment that carries anything the command lookup would silently discard.
    /// </summary>
    /// <remarks>
    /// Folding drops everything that is not a letter, so "новая строка 5" collapsed onto the
    /// command key and the "5" disappeared from the user's text without a trace. Digits are
    /// especially likely here because number normalization runs before this pass.
    /// </remarks>
    private static bool IsPureCommand(string segment)
    {
        foreach (var character in segment)
        {
            if (!char.IsLetter(character) && !char.IsWhiteSpace(character))
            {
                return false;
            }
        }
        return true;
    }

    private static void TrimTrailingPunctuation(StringBuilder builder)
    {
        while (builder.Length > 0 &&
            (char.IsWhiteSpace(builder[^1]) || Array.IndexOf(Boundaries, builder[^1]) >= 0))
        {
            builder.Length--;
        }
    }

    private static string Tidy(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            var previous = builder.Length > 0 ? builder[^1] : '\0';

            // Spaces that ended up next to a mark came from stitching segments, not from speech.
            if (character == ' ' && (previous is ' ' or '\n' or '(' or '«'))
            {
                continue;
            }

            if (character is ',' or '.' or '!' or '?' or ';' or ':' or ')' or '»' && previous == ' ')
            {
                builder.Length--;
            }

            builder.Append(character);
        }

        return builder.ToString().Trim();
    }

    private static string Fold(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSpace = true;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }

            if (!char.IsLetter(character))
            {
                continue;
            }

            var lowered = char.ToLowerInvariant(character);
            builder.Append(lowered is 'ё' ? 'е' : lowered);
            lastWasSpace = false;
        }

        return builder.ToString().Trim();
    }
}
