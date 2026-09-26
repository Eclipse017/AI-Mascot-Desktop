using System;
using System.IO;
using AIMascot.Core;
using AIMascot.Gemini;

internal static class GeminiTests
{
    public static void Run(Action<bool, string> check)
    {
        var target = new AntigravityTarget(@"C:\Users\fixture\AppData\Local\Programs\antigravity\Antigravity.exe");
        var real = new AntigravityProcess(1, "Antigravity", target.ExecutablePath);
        PresenceSample Match(params AntigravityProcess[] processes) => AntigravityMatcher.Match(target, processes, true);
        check(Match().Kind == PresenceKind.Stopped, "verified installation without process is stopped");
        check(Match(real).Kind == PresenceKind.Running, "full Antigravity installation path matches");
        check(Match(real with { ExecutablePath = target.ExecutablePath.ToUpperInvariant() }).Kind == PresenceKind.Running, "Windows path comparison ignores case");
        check(Match(real with { ExecutablePath = @"D:\Downloads\Antigravity.exe" }).Kind == PresenceKind.Stopped, "same executable name at another location rejected");
        check(Match(real with { ExecutablePath = target.ExecutablePath + ".fake" }).Kind == PresenceKind.Stopped, "path prefix alone cannot establish identity");
        check(Match(new(2, "AIAppVpnGuard", target.ExecutablePath), new(3, "chrome_proxy", target.ExecutablePath),
            new(4, "node", target.ExecutablePath), new(5, "ag_launcher", target.ExecutablePath)).Kind == PresenceKind.Stopped,
            "guard, Gemini Notebook, Node and CLI are not app identity");
        var group = Match(real, real, real with { ProcessId = 2 }, real with { ProcessId = 3 });
        check(group.Kind == PresenceKind.Running && group.MatchedProcesses == 3, "Electron processes aggregate and duplicate PIDs deduplicate");
        check(Match(real with { ReadFailed = true }).Kind == PresenceKind.Unknown, "access denial produces Unknown");
        check(Match(real with { ExecutablePath = null }).Kind == PresenceKind.Unknown, "unreadable executable path produces Unknown");
        check(Match(real with { Exited = true }).Kind == PresenceKind.Stopped, "exited processes ignored");
        check(Match(real, real with { ProcessId = 2, ReadFailed = true }).Kind == PresenceKind.Running, "known app presence survives unreadable child");
        check(AntigravityMatcher.Match(target, new[] {real}, false).Kind == PresenceKind.Unknown, "unverified installation cannot fall back to name matching");
        check(AntigravityMatcher.Match(target, Array.Empty<AntigravityProcess>(), true, true).Kind == PresenceKind.Unknown, "enumeration failure is not a confirmed exit");
        check(AntigravityTarget.ProductMatches("Antigravity", "Google") && !AntigravityTarget.ProductMatches("Chrome", "Google") &&
            !AntigravityTarget.ProductMatches("Antigravity", null), "product and publisher metadata both required");
        check(!new AntigravityTarget(Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".exe")).InstallationVerified(), "missing installation stays unverified");
        check(GeminiInteraction.OnHead(.5, .38) && !GeminiInteraction.OnHead(.1, .2) &&
            !GeminiInteraction.OnHead(.5, .8), "Gemini head region excludes ears and costume");
        var behavior = new CompanionBehavior();
        for (int i = 0; i < 8; i++) GeminiInteraction.Hover(behavior, i % 2 == 0 ? .35 : .65, .4, 1+i*.08);
        check(behavior.Sample(1.6).Mood == PetMood.Happy, "strokes over Gemini forehead trigger happy reaction");
        behavior = new CompanionBehavior();
        for (int i = 0; i < 8; i++) GeminiInteraction.Hover(behavior, i % 2 == 0 ? .35 : .65, .8, 1+i*.08);
        check(behavior.Sample(1.6).Mood != PetMood.Happy, "costume motion does not trigger petting");
        check(typeof(AppPresence).Assembly.GetName().Name == "AIMascot.Gemini", "independent Gemini assembly");
        check(StartupRegistration.ValueName == "AI-Mascot Desktop Gemini", "independent Gemini startup entry");
        check(new PreferencesStore().StoragePath == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AI-Mascot-Desktop", "gemini", "preferences.json"), "preferences belong only to Gemini");
        check(new PreferencesStore().LegacyPath == null, "default never imports another role's preferences");
    }
}
