using System.IO;

namespace AIMascot.ConsoleApp;

public sealed record Role(string Id, string Name, string Binding, string Executable, string Prefix, string Color)
{
    public string ProcessName => Path.GetFileNameWithoutExtension(Executable);
    public string PreferencesPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI-Mascot-Desktop", Id, "preferences.json");
    public string StartupName => "AI-Mascot Desktop " + Prefix;
    public string StartupCommand => $"\"{Executable}\" --background";
    public string EventName(string command) => $"Local\\AIMascot.Desktop.{Prefix}.{command}";
    public static string HostPath(string role) => Path.Combine(AppContext.BaseDirectory, $"AIMascot.{role}.exe");
    public static IReadOnlyList<Role> Installed { get; } = new[] {
        new Role("dragon", "白色龙娘", "Codex", HostPath("Dragon"), "Dragon", "#9780C8"),
        new Role("gemini", "Gemini 娘", "Antigravity", HostPath("Gemini"), "Gemini", "#4D91BF"),
        new Role("grok", "Grok 娘", "Grok Bot", HostPath("Grok"), "Grok", "#B99048"),
        new Role("claude", "Claude 娘", "Claude（仅被动识别）", HostPath("Claude"), "Claude", "#B77557"),
        new Role("deepseek", "DeepSeek 娘", "Harness 本地服务", HostPath("DeepSeek"), "DeepSeek", "#567CCC")
    };
}

public sealed record SavedOptions(int Mode, bool UserHidden, bool Paused);
public sealed record RoleState(string Id, bool Installed, bool Running, bool OtherCopy, bool? Visible,
    bool? StartupEnabled, SavedOptions? Options, string? Error, int[] ProcessIds, string? Reason = null);
public sealed record CommandResult(string Role, string Command, bool Success, string Message);
