using System;
using System.IO;
using System.Text.Json;
using AIMascot.Core;
using AIMascot.Claude;

internal static class ClaudeTests
{
    public static void Run(Action<bool, string> check, string directory)
    {
        const string root = @"C:\Program Files\WindowsApps\Claude_2.2553.0.0_x64__pzs8sxrjxfjjc";
        var real = new ClaudeProcess(1, "Claude", ClaudeMatcher.PackageFamily, Path.Combine(root, @"app\Claude.exe"), root);
        PresenceSample Match(params ClaudeProcess[] p) => ClaudeMatcher.Match(p);
        check(Match().Kind == PresenceKind.Stopped, "installation does not imply a running client");
        check(Match(real).Kind == PresenceKind.Running, "OS package family and package-relative executable both match");
        check(Match(real with { ExecutablePath = real.ExecutablePath!.ToUpperInvariant() }).Kind == PresenceKind.Running, "Windows path case ignored");
        check(Match(real with { ExecutablePath = @"D:\Downloads\Claude.exe" }).Kind == PresenceKind.Stopped, "same executable name outside package rejected");
        check(Match(real with { PackageFamily = "Claude_fakepublisher" }).Kind == PresenceKind.Stopped, "wrong publisher family rejected");
        check(Match(real with { PackageFamily = null }).Kind == PresenceKind.Stopped, "unpackaged Claude CLI rejected");
        check(Match(real with { ExecutablePath = real.ExecutablePath + ".fake" }).Kind == PresenceKind.Stopped, "path prefix cannot establish identity");
        check(Match(real with { ExecutablePath = Path.Combine(root, @"app\resources\Claude.exe") }).Kind == PresenceKind.Stopped, "wrong executable inside correct package rejected");
        foreach (var name in new[] { "ClaudeVpnGuard", "AIAppVpnGuard", "chrome", "msedge", "node", "cowork-svc", "claude-ssh-askpass", "ClaudeMascot" })
            check(Match(real with { Name = name }).Kind == PresenceKind.Stopped, name + " cannot trigger presence");
        const string updateRoot = @"D:\WindowsApps\Claude_3.0.0.0_x64__pzs8sxrjxfjjc";
        check(Match(real with { PackageRoot = updateRoot, ExecutablePath = Path.Combine(updateRoot, ClaudeMatcher.RelativeExecutable) }).Kind == PresenceKind.Running, "OS-resolved updated package root accepted without hardcoded version");
        var grouped = Match(real, real, real with { ProcessId = 2 }, real with { ProcessId = 3 });
        check(grouped.Kind == PresenceKind.Running && grouped.MatchedProcesses == 3, "Electron children aggregate and duplicate PIDs deduplicate");
        check(Match(real with { ReadFailed = true }).Kind == PresenceKind.Unknown, "access denial gives Unknown");
        check(Match(real with { ExecutablePath = null }).Kind == PresenceKind.Unknown, "missing path gives Unknown");
        check(Match(real with { PackageRoot = null }).Kind == PresenceKind.Unknown, "unresolved package root gives Unknown");
        check(Match(real with { PackageRoot = "relative" }).Kind == PresenceKind.Unknown, "relative package root cannot establish identity");
        check(Match(real with { Exited = true }).Kind == PresenceKind.Stopped, "exited process ignored");
        check(Match(real, real with { ProcessId = 2, ReadFailed = true }).Kind == PresenceKind.Running, "valid presence survives unreadable child");
        check(ClaudeMatcher.Match(Array.Empty<ClaudeProcess>(), true).Kind == PresenceKind.Unknown, "enumeration failure is Unknown");
        check(ClaudeInteraction.OnHead(.5, .35) && !ClaudeInteraction.OnHead(.88, .35) && !ClaudeInteraction.OnHead(.5, .8), "Claude forehead excludes flower and clothing");
        var behavior = new CompanionBehavior();
        for (int i = 0; i < 8; i++) ClaudeInteraction.Hover(behavior, i % 2 == 0 ? .35 : .65, .35, 1+i*.08);
        check(behavior.Sample(1.6).Mood == PetMood.Happy, "Claude head strokes produce happy expression");
        behavior = new CompanionBehavior();
        for (int i = 0; i < 8; i++) ClaudeInteraction.Hover(behavior, i % 2 == 0 ? .35 : .65, .8, 1+i*.08);
        check(behavior.Sample(1.6).Mood != PetMood.Happy, "clothing does not trigger head petting");
        check(typeof(AppPresence).Assembly.GetName().Name == "AIMascot.Claude", "isolated assembly");
        check(StartupRegistration.ValueName == "AI-Mascot Desktop Claude", "isolated startup value");
        check(new PreferencesStore().StoragePath == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI-Mascot-Desktop", "claude", "preferences.json"), "isolated preferences");
        check(new PreferencesStore().LegacyPath == null, "never imports another role settings");
        var runtime = new AIMascot.Platform.RuntimeStatus("claude", directory);
        runtime.Interaction("greet", double.NaN, double.PositiveInfinity); runtime.Write(new { Reason = "WaitingForApp" });
        using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "runtime-status.json"))))
            check(json.RootElement.GetProperty("Interactions")[0].GetProperty("Left").ValueKind == JsonValueKind.Null && json.RootElement.GetProperty("Interactions")[0].GetProperty("Top").ValueKind == JsonValueKind.Null, "uninitialized coordinates serialize as null");
        for (int i = 0; i < 40; i++) runtime.Interaction("press", i, i);
        runtime.Write(new { Reason = "Following" });
        using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "runtime-status.json"))))
            check(json.RootElement.GetProperty("Interactions").GetArrayLength() == 32 && json.RootElement.GetProperty("Interactions")[0].GetProperty("Left").GetInt32() == 8, "diagnostics limited to latest 32 interactions");
        var blocked = Path.Combine(directory, "not-a-directory"); File.WriteAllText(blocked, "fixture");
        new AIMascot.Platform.RuntimeStatus("claude", blocked).Write(new { Reason = "Demo" });
        check(true, "status IO failure does not crash companion");
    }
}
