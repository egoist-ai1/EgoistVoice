using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Egoist.Voice.Core;

namespace Egoist.Voice.Services;

/// <summary>
/// User-facing behaviour that is not the activation binding and not the capsule position.
/// </summary>
public sealed record DictationSettings
{
    [JsonPropertyName("applyDictionary")] public bool ApplyDictionary { get; init; } = true;
    [JsonPropertyName("applyVoiceCommands")] public bool ApplyVoiceCommands { get; init; } = true;

    /// <summary>Off by default: turning "двадцать пять" into 25 is wrong when dictating prose.</summary>
    [JsonPropertyName("applyNumbers")] public bool ApplyNumberNormalization { get; init; }

    [JsonPropertyName("restoreClipboard")] public bool RestoreClipboard { get; init; } = true;

    /// <summary>Forces the mixed-language fallback for every dictation.</summary>
    [JsonPropertyName("mixedLanguageMode")] public bool MixedLanguageMode { get; init; }

    [JsonPropertyName("soundFeedback")] public bool SoundFeedback { get; init; } = true;
    [JsonPropertyName("soundVolume")] public double SoundVolume { get; init; } = 0.4;

    public static DictationSettings Default { get; } = new();

    public PostProcessingOptions ToPostProcessingOptions() =>
        new(ApplyDictionary, ApplyVoiceCommands, ApplyNumberNormalization);
}

/// <summary>
/// Loads settings and the user dictionary, both written atomically through a temporary file so a
/// crash mid-write cannot leave an unreadable configuration behind.
/// </summary>
public sealed class DictationSettingsService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _root;

    public DictationSettingsService(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EgoistVoice");
    }

    public string SettingsPath => Path.Combine(_root, "dictation.json");
    public string DictionaryPath => Path.Combine(_root, "dictionary.json");

    public DictationSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return DictationSettings.Default;
            }
            return JsonSerializer.Deserialize<DictationSettings>(File.ReadAllText(SettingsPath), Json)
                ?? DictationSettings.Default;
        }
        catch (Exception exception)
        {
            AppLog.Write("Could not load dictation settings; using defaults", exception);
            return DictationSettings.Default;
        }
    }

    public void Save(DictationSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_root);
            var temporary = SettingsPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        catch (Exception exception)
        {
            AppLog.Write("Could not save dictation settings", exception);
        }
    }

    public UserDictionary LoadDictionary()
    {
        try
        {
            if (!File.Exists(DictionaryPath))
            {
                // The built-in vocabulary, not an empty one. Without a file the application still
                // knows how to spell GitHub, Docker and Claude Code.
                return UserDictionary.BuiltIn;
            }

            var dictionary = UserDictionary.Parse(File.ReadAllText(DictionaryPath));
            AppLog.Write($"Dictionary loaded: {dictionary.Count} rules (built-in plus user)");
            return dictionary;
        }
        catch (Exception exception)
        {
            AppLog.Write("Could not load the user dictionary; falling back to the built-in one", exception);
            return UserDictionary.BuiltIn;
        }
    }

    /// <summary>
    /// Writes a commented starter file the first time it is needed. An empty editor is a dead end;
    /// a file that already shows the three supported rule shapes is not.
    /// </summary>
    public void EnsureDictionaryTemplate()
    {
        try
        {
            if (File.Exists(DictionaryPath))
            {
                return;
            }

            Directory.CreateDirectory(_root);
            File.WriteAllText(DictionaryPath,
                """
                {
                  // spoken — как вы это произносите, written — как должно быть написано.
                  // Русские окончания подхватываются автоматически: «докер» ловит «докере», «докером».
                  // pattern — регулярное выражение, если нужно точное правило.
                  "terms": [
                    { "spoken": ["питон", "пайтон"], "written": "Python" },
                    { "spoken": ["гитхаб"], "written": "GitHub" },
                    { "spoken": ["пул реквест"], "written": "pull request" },
                    { "pattern": "(?i)\\bкубер(нетес)?\\w{0,3}\\b", "written": "Kubernetes" }
                  ]
                }
                """);
        }
        catch (Exception exception)
        {
            AppLog.Write("Could not create the dictionary template", exception);
        }
    }
}
