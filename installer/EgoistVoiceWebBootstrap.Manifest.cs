internal static class EgoistVoiceReleaseManifest
{
    internal const string ApplicationVersion = "2.2.0";
    internal const string ReleaseTag = "v2.2.0-preview.1";
    internal const string ReleaseBaseUrl =
        "https://github.com/egoist-ai1/EgoistVoice/releases/download/v2.2.0-preview.1/";
    internal const string LaunchFile = "EgoistVoice-Setup-2.2.0-inner.exe";

    internal static readonly PayloadFile[] Files = new PayloadFile[]
    {
        new PayloadFile(
            "EgoistVoice-Setup-2.2.0-inner.exe",
            1762824L,
            "0fd1706666f1411404799308f70c4bd6f82d9e801c07177f24f8bca49399175a"),
        new PayloadFile(
            "EgoistVoice-Setup-2.2.0-inner-1.bin",
            2098236672L,
            "691f5e60b76b7595dc8752a2264bccda04ebb9d9dbc5e1257eb9a300846b2a02"),
        new PayloadFile(
            "EgoistVoice-Setup-2.2.0-inner-2.bin",
            1091700448L,
            "6d7cada0e16fcec94899811d30fd386d816945f8185e62a3c20aab05b468c74c")
    };
}
