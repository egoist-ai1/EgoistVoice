namespace Egoist.Voice.Core;

/// <summary>
/// Versioned, conservative catalogue of names Egoist Voice can canonicalize locally.
/// </summary>
/// <remarks>
/// <para>
/// Every substitution is deterministic and whole-token bounded. Safe aliases are available in all
/// profiles; genuinely ambiguous forms are limited to an explicit technology or gaming context.
/// A missed brand is preferable to silently changing an ordinary Russian word.
/// </para>
/// <para>
/// Deliberately excluded as standalone global aliases: «хром», «курсор», «нода», «питон»,
/// «редис», «стим», «телега», «мета», «опера», «лама» and «сора». Multi-word forms below repair
/// only catalogue-backed whitespace/hyphen splits; there is no arbitrary edit-distance guessing.
/// </para>
/// </remarks>
public static class BuiltInVocabulary
{
    public const string Version = "3";

    /// <summary>
    /// The dictionary sorts aliases by length, so a longer entity always wins over a contained one.
    /// Canonical Latin aliases also repair casing without changing already-correct text.
    /// </summary>
    public static IReadOnlyList<DictionaryTerm> Terms { get; } =
    [
        // ── EGOIST product names reported by the user's own dictation ───────
        new(
            [
                "эгоист войс", "эгист войс", "егоист войс", "эгаист войс", "эгейст войс",
                "эгоист voice", "эгист voice", "егоист voice", "эгаист voice",
                "эгaist voice", "эгаist voice", "egoist voice", "egist voice", "egast voice"
            ],
            "Egoist Voice"),
        new(
            [
                "эгоист транслейтор", "эгист транслейтор", "эгаист транслейтор",
                "эгоист транслейт", "эгист транслейт", "эгаст транслейт",
                "эгоист translate", "эгист translate", "эгаст translate",
                "эгaist translate", "эгаist translate", "egoist translator", "egoist translate",
                "egist translate", "egast translate"
            ],
            "EGOIST Translator"),

        // ── AI and local assistants ──────────────────────────────────────────
        new(["клод код", "клодкод", "клод коуд", "к лод код", "claude code"], "Claude Code"),
        new(["клод", "к лод", "claude"], "Claude"),
        new(["чат джипити", "чатджипити", "чат гпт", "чат джи пи ти", "chatgpt"], "ChatGPT"),
        new(["джемини", "гемини", "джеминай", "джи мини", "gemini"], "Gemini"),
        new(["копайлот", "копилот", "ко пайлот", "copilot"], "Copilot"),
        new(["опенай", "оупенэйай", "опен ай", "оупен ай", "openai"], "OpenAI"),
        new(["антропик", "антро пик", "энтропик", "anthropic"], "Anthropic"),
        new(["дипсик", "дип сик", "deapsik", "deepsik", "deepseek"], "DeepSeek"),
        new(["перплексити", "пер плексити", "perplexity"], "Perplexity"),
        new(["миджорни", "мид джорни", "midjourney"], "Midjourney"),
        new(["хаггинг фейс", "хагин фейс", "hugging face"], "Hugging Face"),
        new(["гигачат", "гига чат", "gigachat"], "GigaChat"),
        new(["гига ам", "gigaam"], "GigaAM"),
        new(["кьювен", "qwen"], "Qwen"),
        new(["оллама", "ollama"], "Ollama"),
        new(["грок", "grok"], "Grok"),
        new(["стейбл диффьюжн", "stable diffusion"], "Stable Diffusion"),

        // These pronunciations collide with ordinary English/Russian wording. They become active
        // only in a detected or explicitly requested technology context.
        new(
            ["клауд код", "клаудкод", "cloud code"],
            "Claude Code",
            Profiles: EntityProfile.Technology,
            BlockWhenTextContains: ["google cloud", "гугл клауд", "cloud function", "облачн"]),
        new(["клауд"], "Claude", Profiles: EntityProfile.Technology),
        new(
            ["кодекс", "codex"],
            "Codex",
            Profiles: EntityProfile.Technology,
            BlockWhenTextContains: ["гражданск", "уголовн", "налогов", "правов", "чести", "законов"]),
        new(
            ["курсор", "cursor"],
            "Cursor",
            Profiles: EntityProfile.Technology,
            BlockWhenTextContains: ["постав", "перемест", "строк", "мыш", "указател", "позици"]),
        new(
            ["мета", "meta"],
            "Meta",
            Profiles: EntityProfile.Technology,
            BlockWhenTextContains: ["метадан", "анализ", "уровен", "ирони", "шутк"]),

        // ── Services, collaboration and design ──────────────────────────────
        new(["гитхаб", "гит хаб", "git hub", "github"], "GitHub"),
        new(["гитлаб", "гит лаб", "git lab", "gitlab"], "GitLab"),
        new(["битбакет", "бит бакет", "bitbucket"], "Bitbucket"),
        new(["телеграм", "теле грам", "telegram"], "Telegram"),
        new(["дискорд", "дис корд", "discord"], "Discord"),
        new(["слак", "slack"], "Slack"),
        new(["майкрософт тимс", "тимс", "microsoft teams"], "Microsoft Teams"),
        new(["зум", "zoom"], "Zoom"),
        new(["ноушен", "ноу шен", "notion"], "Notion"),
        new(["фигма", "фиг ма", "figma"], "Figma"),
        new(["канва", "canva"], "Canva"),
        new(["джира", "jira"], "Jira"),
        new(["конфлюэнс", "confluence"], "Confluence"),
        new(["линеар", "linear"], "Linear"),
        new(["сентри", "sentry"], "Sentry"),
        new(["страйп", "stripe"], "Stripe"),
        new(["ютуб", "ю туб", "youtube"], "YouTube"),
        new(["реддит", "ред дит", "reddit"], "Reddit"),
        new(["спотифай", "spotify"], "Spotify"),
        new(["гугл хром", "хром браузер", "google chrome"], "Google Chrome"),
        new(["файрфокс", "firefox"], "Firefox"),
        new(["майкрософт эдж", "эдж браузер", "microsoft edge"], "Microsoft Edge"),
        new(["опера браузер", "opera browser"], "Opera browser"),
        new(["брейв браузер", "brave browser"], "Brave browser"),
        new(["гугл драйв", "google drive"], "Google Drive"),
        new(["гугл", "гугль", "google"], "Google"),
        new(["ван драйв", "onedrive"], "OneDrive"),
        new(["дропбокс", "dropbox"], "Dropbox"),
        new(["майкрософт ворд", "ворд", "microsoft word"], "Microsoft Word"),
        new(["майкрософт эксель", "excel"], "Microsoft Excel"),
        new(["пауэрпоинт", "powerpoint"], "PowerPoint"),

        // ── Languages, runtimes and data ─────────────────────────────────────
        new(["пайтон", "python"], "Python"),
        new(["джаваскрипт", "джава скрипт", "java script", "javascript"], "JavaScript"),
        new(["тайпскрипт", "тайп скрипт", "type script", "typescript"], "TypeScript"),
        new(["джейсон", "джсон", "json"], "JSON"),
        new(["ямл", "яамл", "yaml"], "YAML"),
        new(["эйч ти эм эл", "html"], "HTML"),
        new(["си эс эс", "css"], "CSS"),
        new(["эс кью эл", "sql"], "SQL"),
        new(["эйч ти ти пи", "http"], "HTTP"),
        new(["эй пи ай", "апи", "api"], "API"),
        new(["реакт", "react"], "React"),
        new(["вью джиэс", "vue js", "vue.js"], "Vue.js"),
        new(["нэкст джиэс", "некст джиэс", "next js", "next.js"], "Next.js"),
        new(["ноуд джиэс", "нод джиэс", "node js", "node.js"], "Node.js"),
        new(["дотнет", "дот нет", "dotnet", ".net"], ".NET"),
        new(["си шарп", "c sharp", "c#"], "C#"),
        new(["си плюс плюс", "c plus plus", "c++"], "C++"),
        new(["раст ленг", "rust lang", "rust"], "Rust"),
        new(["гоу ленг", "go lang"], "Go"),
        new(["котлин", "kotlin"], "Kotlin"),
        new(["свифт ленг", "swift lang"], "Swift"),

        // ── Development, cloud and databases ────────────────────────────────
        new(["докер компоуз", "docker compose"], "Docker Compose"),
        new(["докер", "docker"], "Docker"),
        new(["кубернетес", "кубер нетес", "кубернетис", "kubernetes"], "Kubernetes"),
        new(["постгрескл", "постгрес", "пост грес", "postgresql"], "PostgreSQL"),
        new(["эс кью лайт", "sqlite"], "SQLite"),
        new(["май эс кью эл", "mysql"], "MySQL"),
        new(["монго ди би", "mongodb"], "MongoDB"),
        new(["графана", "гра фана", "grafana"], "Grafana"),
        new(["прометеус", "prometheus"], "Prometheus"),
        new(["терраформ", "терра форм", "terraform"], "Terraform"),
        new(["ансибл", "ansible"], "Ansible"),
        new(["дженкинс", "jenkins"], "Jenkins"),
        new(["энджин икс", "nginx"], "Nginx"),
        new(["эн пи эм", "npm"], "npm"),
        new(["верцел", "версел", "vercel"], "Vercel"),
        new(["клаудфлэр", "клауд флэр", "cloudflare"], "Cloudflare"),
        new(["супабейс", "супа бейс", "supabase"], "Supabase"),
        new(["файрбейс", "firebase"], "Firebase"),
        new(["неон", "neon"], "Neon"),
        new(["электрон", "electron"], "Electron"),
        new(["вайт", "vite"], "Vite"),
        new(["дабл ю пи эф", "wpf"], "WPF"),
        new(["эм ви ви эм", "mvvm"], "MVVM"),
        new(["вижуал студио код", "вижл студио код", "visual studio code"], "Visual Studio Code"),
        new(["вс код", "вэ эс код", "vs code"], "VS Code"),
        new(["вижуал студио", "visual studio"], "Visual Studio"),
        new(["андроид студио", "android studio"], "Android Studio"),
        new(["джетбрейнс", "jetbrains"], "JetBrains"),
        new(["интеллиджей идея", "intellij idea"], "IntelliJ IDEA"),
        new(["пайчарм", "pycharm"], "PyCharm"),
        new(["вебшторм", "webstorm"], "WebStorm"),
        new(["икскод", "xcode"], "Xcode"),
        new(["тестфлайт", "testflight"], "TestFlight"),
        new(["джетпак компоуз", "jetpack compose"], "Jetpack Compose"),
        new(["постман", "postman"], "Postman"),

        // ── Operating systems, hardware and large companies ─────────────────
        new(["линукс", "linux"], "Linux"),
        new(["убунту", "ubuntu"], "Ubuntu"),
        new(["виндовс", "виндоус", "windows"], "Windows"),
        new(["макос", "мак ос", "macos"], "macOS"),
        new(["ай о эс", "ios"], "iOS"),
        new(["андроид", "android"], "Android"),
        new(["павершелл", "пауэршелл", "повершелл", "пауэр шелл", "powershell"], "PowerShell"),
        new(["энвидиа", "эн видиа", "нвидиа", "nvidia"], "NVIDIA"),
        new(["эй эм ди", "amd"], "AMD"),
        new(["радеон", "radeon"], "Radeon"),
        new(["интел", "intel"], "Intel"),
        new(["эпл", "apple"], "Apple"),
        new(["майкрософт", "майкро софт", "microsoft"], "Microsoft"),
        new(["адоби", "эдоби", "adobe"], "Adobe"),
        new(["амазон", "amazon"], "Amazon"),
        new(["эй дабл ю эс", "aws"], "AWS"),
        new(["эжур", "azure"], "Azure"),
        new(["самсунг", "samsung"], "Samsung"),
        new(["сони", "sony"], "Sony"),
        new(["оракл", "oracle"], "Oracle"),
        new(["ай би эм", "ibm"], "IBM"),
        new(["тесла", "tesla"], "Tesla"),
        new(["спейс икс", "spacex"], "SpaceX"),

        // ── Gaming, studios and creative applications ───────────────────────
        new(["эпик геймс стор", "epic games store"], "Epic Games Store"),
        new(["эпик геймс", "epic games"], "Epic Games"),
        new(["плейстейшен", "плей стейшен", "playstation"], "PlayStation"),
        new(["иксбокс", "икс бокс", "xbox"], "Xbox"),
        new(["нинтендо", "nintendo"], "Nintendo"),
        new(["вэлв", "valve"], "Valve"),
        new(["юбисофт", "ubisoft"], "Ubisoft"),
        new(["рокстар геймс", "rockstar games"], "Rockstar Games"),
        new(["близзард", "blizzard"], "Blizzard"),
        new(["активижн", "activision"], "Activision"),
        new(["райот геймс", "riot games"], "Riot Games"),
        new(["си ди проджект ред", "cd projekt red"], "CD Projekt Red"),
        new(["анриал энджин", "unreal engine"], "Unreal Engine"),
        new(["юнити", "unity"], "Unity"),
        new(["годо энджин", "godot engine"], "Godot Engine"),
        new(["майнкрафт", "minecraft"], "Minecraft"),
        new(["фортнайт", "fortnite"], "Fortnite"),
        new(["контр страйк", "counter strike", "counter-strike"], "Counter-Strike"),
        new(["дота два", "dota 2"], "Dota 2"),
        new(["лига легенд", "league of legends"], "League of Legends"),
        new(["валорант", "valorant"], "Valorant"),
        new(["киберпанк двадцать семьдесят семь", "cyberpunk 2077"], "Cyberpunk 2077"),
        new(["джи ти эй", "gta"], "GTA"),
        new(["кол оф дьюти", "call of duty"], "Call of Duty"),
        new(["ворлд оф варкрафт", "world of warcraft"], "World of Warcraft"),
        new(["гог гэлакси", "gog galaxy"], "GOG Galaxy"),
        new(["блендер", "blender"], "Blender"),
        new(["фотошоп", "photoshop"], "Photoshop"),
        new(["о би эс студио", "obs studio"], "OBS Studio"),
        new(["давинчи резолв", "davinci resolve"], "DaVinci Resolve"),

        // Steam is profile-gated; the bounded case-ending grammar cannot consume «-ул» in «стимул».
        new(["стим", "steam"], "Steam", Profiles: EntityProfile.Gaming),

        // ── Work vocabulary ──────────────────────────────────────────────────
        new(["пул реквест", "пулреквест", "пул-реквест"], "pull request"),
        new(["мердж реквест"], "merge request"),
        new(["код ревью", "кодревью"], "code review"),
        new(["эндпоинт", "энд поинт"], "endpoint"),
        new(["бэкенд", "бекенд"], "backend"),
        new(["фронтенд", "фронтэнд"], "frontend")
    ];

    /// <summary>Safe general forms used by the conditional mixed-speech detector.</summary>
    public static IEnumerable<string> SpokenForms => Terms
        .Where(term => (term.Profiles & EntityProfile.General) != 0)
        .SelectMany(term => term.Spoken ?? []);
}
