namespace Egoist.Voice.Core;

/// <summary>
/// Terms the application knows how to spell out of the box.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the previous design had a hole: the technical catalogue was only consulted
/// by the engine selector, and the substitution dictionary started empty. A user who said "гитхаб"
/// got "гитхаб" in their document unless they had first discovered a JSON file and written the rule
/// themselves. A feature nobody can reach is not a feature.
/// </para>
/// <para>
/// Deterministic substitution is the right mechanism here rather than a smarter model: measured
/// against neural correction it achieves the same one to two percentage points at 0.02 ms instead
/// of 80 ms, and — more importantly — it is predictable. It always produces the same output for the
/// same input, which is what makes it safe to ship on by default.
/// </para>
/// <para>
/// Entries are chosen conservatively, and the bar has been raised twice. A term is only here when
/// its russified form has no ordinary Russian meaning. Excluded for that reason: "хром" (хромой),
/// "интел" (интеллект), "нода", "курсор", "стим" — and, after review caught them shipping,
/// "питон" (the snake), "редис" (the vegetable), "телега" (the cart), "ношен" (ношеный свитер) and
/// "шарп" (шарпей). A wrong replacement in the middle of a sentence is far more damaging than a
/// missed one: the user can retype a term they noticed, but they may never notice a word that was
/// quietly swapped.
/// </para>
/// </remarks>
public static class BuiltInVocabulary
{
    /// <summary>
    /// Ordered by nothing in particular — the dictionary sorts by length itself, so multi-word
    /// entries always win over the shorter forms they contain.
    /// </summary>
    public static IReadOnlyList<DictionaryTerm> Terms { get; } =
    [
        // ── ИИ-инструменты ───────────────────────────────────────────────────
        new(["клод код", "клауд код", "клод коде"], "Claude Code"),
        new(["клод", "клауд"], "Claude"),
        new(["чат джипити", "чатджипити", "чат гпт"], "ChatGPT"),
        new(["джемини", "гемини", "джеминай"], "Gemini"),
        new(["копайлот", "копилот"], "Copilot"),
        new(["опенай", "оупенэйай"], "OpenAI"),
        new(["антропик"], "Anthropic"),

        // "Кодекс" — единственная спорная запись в списке: у слова есть обычное русское
        // значение. Оставлена потому, что в диктовке разработчика она почти всегда про
        // инструмент; кому мешает — удаляет её в своём словаре.
        new(["кодекс"], "Codex"),

        // ── Платформы и сервисы ──────────────────────────────────────────────
        new(["гитхаб", "гит хаб"], "GitHub"),
        new(["гитлаб", "гит лаб"], "GitLab"),
        new(["телеграм"], "Telegram"),
        new(["дискорд"], "Discord"),
        new(["ноушен"], "Notion"),
        new(["фигма"], "Figma"),
        new(["ютуб", "ю туб"], "YouTube"),
        new(["реддит"], "Reddit"),
        new(["спотифай"], "Spotify"),
        new(["гугл", "гугль"], "Google"),
        new(["ямл", "яамл"], "YAML"),

        // ── Языки и технологии ───────────────────────────────────────────────
        new(["пайтон"], "Python"),
        new(["джаваскрипт", "джава скрипт"], "JavaScript"),
        new(["тайпскрипт", "тайп скрипт"], "TypeScript"),
        new(["джейсон", "джсон"], "JSON"),
        new(["реакт"], "React"),
        new(["некст джиэс", "нэкст джиэс"], "Next.js"),
        new(["кубернетес", "кубернетис"], "Kubernetes"),
        new(["докер"], "Docker"),
        new(["постгрес", "постгрескл"], "PostgreSQL"),
        new(["графана"], "Grafana"),
        new(["терраформ"], "Terraform"),
        new(["дотнет", "дот нет"], ".NET"),
        new(["си шарп"], "C#"),
        new(["раст ленг"], "Rust"),
        new(["линукс"], "Linux"),
        new(["убунту"], "Ubuntu"),
        new(["виндовс", "виндоус"], "Windows"),
        new(["макос", "мак ос"], "macOS"),
        new(["павершелл", "пауэршелл", "повершелл"], "PowerShell"),

        // ── Железо ───────────────────────────────────────────────────────────
        new(["энвидиа", "нвидиа"], "NVIDIA"),
        new(["эпл"], "Apple"),
        new(["майкрософт"], "Microsoft"),

        // ── Рабочие термины ──────────────────────────────────────────────────
        new(["пул реквест", "пулреквест", "пул-реквест"], "pull request"),
        new(["мердж реквест"], "merge request"),
        new(["код ревью", "кодревью"], "code review"),
        new(["вижуал студио код", "вижл студио код"], "Visual Studio Code"),
        new(["вс код", "вэ эс код"], "VS Code"),
        new(["эндпоинт", "энд поинт"], "endpoint"),
        new(["бэкенд", "бекенд"], "backend"),
        new(["фронтенд", "фронтэнд"], "frontend")
    ];

    /// <summary>Spoken forms of every built-in entry, for the mixed-speech suspicion map.</summary>
    public static IEnumerable<string> SpokenForms =>
        Terms.SelectMany(term => term.Spoken ?? []);
}
