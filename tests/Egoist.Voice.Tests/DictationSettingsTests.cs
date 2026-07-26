using System.IO;
using Egoist.Voice.Core;
using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class DictationSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "EgoistVoiceTests",
        Guid.NewGuid().ToString("N"));

    private DictationSettingsService Service => new(_root);

    [Fact]
    public void Defaults_are_returned_when_nothing_was_ever_saved()
    {
        var settings = Service.Load();

        Assert.True(settings.ApplyDictionary);
        Assert.True(settings.ApplyVoiceCommands);
        Assert.True(settings.RestoreClipboard);

        // Number normalization is the one step that rewrites text the user did not ask to rewrite,
        // so it has to be opt-in.
        Assert.False(settings.ApplyNumberNormalization);
    }

    [Fact]
    public void Settings_round_trip_through_disk()
    {
        var service = Service;
        service.Save(DictationSettings.Default with
        {
            ApplyNumberNormalization = true,
            MixedLanguageMode = true,
            SoundVolume = 0.75
        });

        var loaded = service.Load();

        Assert.True(loaded.ApplyNumberNormalization);
        Assert.True(loaded.MixedLanguageMode);
        Assert.Equal(0.75, loaded.SoundVolume);
    }

    [Fact]
    public void A_corrupt_settings_file_falls_back_to_defaults_instead_of_crashing()
    {
        var service = Service;
        Directory.CreateDirectory(_root);
        File.WriteAllText(service.SettingsPath, "{ this is not json");

        Assert.Equal(DictationSettings.Default, service.Load());
    }

    [Fact]
    public void The_dictionary_template_is_valid_and_immediately_useful()
    {
        var service = Service;
        service.EnsureDictionaryTemplate();

        var dictionary = service.LoadDictionary();

        Assert.True(dictionary.Count >= 4);
        Assert.Equal("Открой Python.", dictionary.Apply("Открой питон."));
        Assert.Equal("Разверни Kubernetes.", dictionary.Apply("Разверни кубернетес."));
    }

    [Fact]
    public void The_template_is_not_overwritten_once_the_user_has_edited_it()
    {
        var service = Service;
        service.EnsureDictionaryTemplate();
        File.WriteAllText(service.DictionaryPath,
            """{ "terms": [ { "spoken": ["моё"], "written": "MINE" } ] }""");

        service.EnsureDictionaryTemplate();

        Assert.Equal("MINE", service.LoadDictionary().Apply("моё"));
    }

    [Fact]
    public void Without_a_file_the_built_in_vocabulary_is_still_in_effect()
    {
        // The whole point of shipping a vocabulary: a fresh installation already knows how to
        // spell the terms, without the user first discovering a JSON file.
        Assert.Contains("GitHub", Service.LoadDictionary().Apply("открой гитхаб"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_broken_dictionary_falls_back_to_the_built_in_one()
    {
        var service = Service;
        Directory.CreateDirectory(_root);
        File.WriteAllText(service.DictionaryPath, "not json at all");

        // Falling back to nothing would silently downgrade recognition quality on a typo in a
        // config file, which is exactly the kind of failure nobody connects to its cause.
        Assert.Contains("Docker", service.LoadDictionary().Apply("запусти докер"), StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_translate_into_post_processing_options()
    {
        var options = (DictationSettings.Default with { ApplyNumberNormalization = true })
            .ToPostProcessingOptions();

        Assert.True(options.ApplyDictionary);
        Assert.True(options.ApplyNumberNormalization);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A temporary directory left behind is not worth failing a test over.
        }
    }
}
