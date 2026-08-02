using Egoist.Voice.Core;
using Egoist.Voice.Services;

namespace Egoist.Voice.Tests;

public sealed class EntityProfilePolicyTests
{
    [Theory]
    [InlineData("code")]
    [InlineData("Cursor.exe")]
    [InlineData("WindowsTerminal")]
    [InlineData("rider64")]
    public void Developer_targets_enable_technology_entities(string processName)
    {
        var profile = EntityProfilePolicy.Resolve("обычная фраза", processName, false, false);

        Assert.True(profile.HasFlag(EntityProfile.General));
        Assert.True(profile.HasFlag(EntityProfile.Technology));
        Assert.False(profile.HasFlag(EntityProfile.Gaming));
    }

    [Theory]
    [InlineData("Кодекс предложил исправление.")]
    [InlineData("Открой клауд код.")]
    [InlineData("Проверь Docker и GitHub.")]
    public void Technology_terms_enable_their_local_profile(string transcript)
    {
        var profile = EntityProfilePolicy.Resolve(transcript, null, false, false);

        Assert.True(profile.HasFlag(EntityProfile.Technology));
    }

    [Theory]
    [InlineData("Игра доступна в стим.")]
    [InlineData("Запусти Minecraft сервер.")]
    [InlineData("Матч уже начался.")]
    public void Gaming_context_enables_gaming_entities(string transcript)
    {
        var profile = EntityProfilePolicy.Resolve(transcript, null, false, false);

        Assert.True(profile.HasFlag(EntityProfile.Gaming));
    }

    [Fact]
    public void Explicit_mixed_language_mode_enables_technology_without_target_detection()
    {
        var profile = EntityProfilePolicy.Resolve("спроси клауд", null, false, true);

        Assert.True(profile.HasFlag(EntityProfile.Technology));
    }

    [Fact]
    public void Ordinary_russian_stays_in_the_general_profile()
    {
        var profile = EntityProfilePolicy.Resolve("Тихий вечер, мягкий свет и свежий воздух.", "notepad", false, false);

        Assert.Equal(EntityProfile.General, profile);
    }

    [Fact]
    public void Profile_and_dictionary_preserve_negative_controls()
    {
        const string text = "Поставь курсор в конец строки и открой гражданский кодекс.";
        var profile = EntityProfilePolicy.Resolve(text, "code", false, false);

        Assert.Equal(text, UserDictionary.BuiltIn.Apply(text, profile));
    }
}
