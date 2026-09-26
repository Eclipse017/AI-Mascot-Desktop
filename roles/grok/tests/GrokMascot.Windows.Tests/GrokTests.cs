using System;
using System.IO;
using AIMascot.Core;
using AIMascot.Grok;

internal static class GrokTests
{
    public static void Run(Action<bool, string> check)
    {
        var target = new GrokBotTarget(@"C:\Users\fixture\AppData\Local\Programs\grokbot\GrokBot.exe");
        var real = new GrokBotProcess(1, "Grok Bot", target.ExecutablePath);
        PresenceSample Match(params GrokBotProcess[] processes) => GrokBotMatcher.Match(target, processes, true);
        check(Match().Kind == PresenceKind.Stopped, "verified installation without process is stopped");
        check(Match(real).Kind == PresenceKind.Running, "full GrokBot installation path matches");
        check(Match(real with { ExecutablePath = target.ExecutablePath.ToUpperInvariant() }).Kind == PresenceKind.Running, "Windows path comparison ignores case");
        check(Match(real with { ExecutablePath = @"D:\Downloads\GrokBot.exe" }).Kind == PresenceKind.Stopped, "same executable name at another location rejected");
        check(Match(real with { ExecutablePath = target.ExecutablePath + ".fake" }).Kind == PresenceKind.Stopped, "path prefix alone cannot establish identity");
        check(Match(new(2, "AIAppVpnGuard", target.ExecutablePath), new(3, "chrome_proxy", target.ExecutablePath),
            new(4, "node", target.ExecutablePath), new(5, "grok", target.ExecutablePath)).Kind == PresenceKind.Stopped,
            "guard, browser, Node and Grok CLI are not app identity");
        var group = Match(real, real, real with { ProcessId = 2 }, real with { ProcessId = 3 });
        check(group.Kind == PresenceKind.Running && group.MatchedProcesses == 3, "Electron processes aggregate and duplicate PIDs deduplicate");
        check(Match(real with { ReadFailed = true }).Kind == PresenceKind.Unknown, "access denial produces Unknown");
        check(Match(real with { ExecutablePath = null }).Kind == PresenceKind.Unknown, "unreadable executable path produces Unknown");
        check(Match(real with { Exited = true }).Kind == PresenceKind.Stopped, "exited processes ignored");
        check(Match(real, real with { ProcessId = 2, ReadFailed = true }).Kind == PresenceKind.Running, "known app presence survives unreadable child");
        check(GrokBotMatcher.Match(target, new[] {real}, false).Kind == PresenceKind.Unknown, "unverified installation cannot fall back to name matching");
        check(GrokBotMatcher.Match(target, Array.Empty<GrokBotProcess>(), true, true).Kind == PresenceKind.Unknown, "enumeration failure is not a confirmed exit");
        check(GrokBotTarget.ProductMatches("Grok Bot", "SpaceXAI") && !GrokBotTarget.ProductMatches("Chrome", "SpaceXAI") &&
            !GrokBotTarget.ProductMatches("Grok Bot", null), "product and publisher metadata both required");
        check(!new GrokBotTarget(Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".exe")).InstallationVerified(), "missing installation stays unverified");
        check(GrokInteraction.OnHead(.5, .38) && !GrokInteraction.OnHead(.1, .2) &&
            !GrokInteraction.OnHead(.5, .8), "Grok head region excludes ears and costume");
        var behavior = new CompanionBehavior();
        for (int i = 0; i < 8; i++) GrokInteraction.Hover(behavior, i % 2 == 0 ? .35 : .65, .4, 1+i*.08);
        check(behavior.Sample(1.6).Mood == PetMood.Happy, "strokes over Grok forehead trigger happy reaction");
        behavior = new CompanionBehavior();
        for (int i = 0; i < 8; i++) GrokInteraction.Hover(behavior, i % 2 == 0 ? .35 : .65, .8, 1+i*.08);
        check(behavior.Sample(1.6).Mood != PetMood.Happy, "costume motion does not trigger petting");
        check(typeof(AppPresence).Assembly.GetName().Name == "AIMascot.Grok", "independent Grok assembly");
        check(StartupRegistration.ValueName == "AI-Mascot Desktop Grok", "independent Grok startup entry");
        check(new PreferencesStore().StoragePath == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AI-Mascot-Desktop", "grok", "preferences.json"), "preferences belong only to Grok");
        check(new PreferencesStore().LegacyPath == null, "default never imports another role's preferences");
    }
}
