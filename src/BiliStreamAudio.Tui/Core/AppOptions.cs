namespace BiliStreamAudio.Tui.Core;

internal static class AppOptions
{
    internal const string MockModeEnvironmentVariable = "BILISTREAMAUDIO_MOCK";

    public static bool IsMockMode(IEnumerable<string> arguments, string? environmentValue)
    {
        return arguments.Any(argument => string.Equals(
                   argument,
                   "--mock",
                   StringComparison.OrdinalIgnoreCase))
            || string.Equals(environmentValue, "1", StringComparison.Ordinal)
            || bool.TryParse(environmentValue, out var enabled) && enabled;
    }
}
