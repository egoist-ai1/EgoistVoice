using System.Runtime.CompilerServices;

namespace Egoist.Voice.Tests;

internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var logDirectory = Path.Combine(
            Path.GetTempPath(),
            "EgoistVoice.Tests",
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Logs");
        Environment.SetEnvironmentVariable("EGOISTVOICE_LOG_DIRECTORY", logDirectory);
    }
}
