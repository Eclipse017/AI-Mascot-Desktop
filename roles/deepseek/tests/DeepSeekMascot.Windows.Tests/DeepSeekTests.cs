using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using AIMascot.Core;
using AIMascot.DeepSeek;

internal static class DeepSeekTests
{
    public static void Run(Action<bool, string> check)
    {
        var target = new HarnessTarget(@"C:\Fixture\DeepSeek Harness.exe", @"C:\Fixture\resources\app.asar");
        var host = new HarnessProcess(10, "DeepSeek Harness", target.ExecutablePath);
        var port = new ServiceListener(10, "127.0.0.1", 19387);
        PresenceProbe Match(HarnessProcess[] processes, ServiceListener[]? listeners = null,
            bool verified = true, bool processFailed = false, bool tableFailed = false) =>
            HarnessMatcher.Match(target, processes, listeners ?? new[] { port }, verified, processFailed, tableFailed);
        check(Match(Array.Empty<HarnessProcess>(), Array.Empty<ServiceListener>()).Sample.Kind == PresenceKind.Stopped,
            "installation alone does not mean service is running");
        check(Match(new[] { host }).Sample.Kind == PresenceKind.Running, "audited path plus listener owner confirms service");
        check(Match(new[] { host with { ExecutablePath = target.ExecutablePath.ToUpperInvariant() } }).Sample.Kind == PresenceKind.Running,
            "Windows installation path is case insensitive");
        check(Match(new[] { host with { ExecutablePath = @"D:\Downloads\DeepSeek Harness.exe" } }).Sample.Kind == PresenceKind.Stopped,
            "same name from unrelated directory is rejected");
        check(Match(new[] { host with { ExecutablePath = target.ExecutablePath + ".fake" } }).Sample.Kind == PresenceKind.Stopped,
            "prefix path is insufficient");
        foreach (var name in new[] { "node", "chrome", "msedge", "AIAppVpnGuard", "ClaudeVpnGuard", "dsh" })
            check(Match(new[] { host with { Name = name } }).Sample.Kind == PresenceKind.Stopped,
                name + " with the port never establishes Desktop service identity");
        check(Match(new[] { host }, new[] { port with { ProcessId = 99 } }).Sample.Kind == PresenceKind.Unknown,
            "unrelated port owner cannot turn desktop shell into confirmed service");
        check(Match(new[] { host }, new[] { port with { Port = 3080 } }).Sample.Kind == PresenceKind.Unknown,
            "historical port 3080 does not prove this Desktop service");
        check(Match(new[] { host }, new[] { port with { Port = 9229 } }).Sample.Kind == PresenceKind.Unknown,
            "inspector or unrelated listener is not service evidence");
        check(Match(new[] { host }, new[] { port with { Address = "192.0.2.1" } }).Sample.Kind == PresenceKind.Unknown,
            "unrecognized bind address cannot confirm the audited contract");
        check(Match(new[] { host }, new[] { port with { Address = "0.0.0.0" } }).Sample.Kind == PresenceKind.Running,
            "audited host supports wildcard IPv4 binding");
        var group = Match(new[] { host, host, host with { ProcessId = 11 }, host with { ProcessId = 12 } }, new[] { port, port });
        check(group.Sample.Kind == PresenceKind.Running && group.ConfirmedServiceOwners == 1 && group.VerifiedDesktopProcesses == 3,
            "renderer multiplicity and duplicate snapshots yield one service owner and one mascot");
        check(Match(new[] { host with { ReadFailed = true } }).Sample.Kind == PresenceKind.Unknown, "access denial is Unknown");
        check(Match(new[] { host with { ExecutablePath = null } }).Sample.Kind == PresenceKind.Unknown, "missing path is Unknown");
        check(Match(new[] { host with { Exited = true } }).Sample.Kind == PresenceKind.Stopped, "stale listener for exited process is rejected");
        check(Match(new[] { host with { StartedAfterSnapshot = true } }).Sample.Kind == PresenceKind.Unknown, "PID reuse or startup race is not confirmed");
        check(Match(new[] { host }, verified: false).Sample.Kind == PresenceKind.Unknown, "changed installation cannot fall back to process or port");
        check(Match(Array.Empty<HarnessProcess>(), processFailed: true).Sample.Kind == PresenceKind.Unknown, "failed process enumeration is Unknown");
        check(Match(new[] { host }, tableFailed: true).Sample.Kind == PresenceKind.Unknown, "failed listener read invalidates otherwise matching evidence");
        check(Match(new[] { host }, Array.Empty<ServiceListener>()).Sample.Kind == PresenceKind.Unknown, "desktop without service listener is Unknown, not Running");
        var browser = new HarnessProcess(80, "chrome", @"C:\Browser\chrome.exe");
        check(Match(new[] { browser }, Array.Empty<ServiceListener>()).Sample.Kind == PresenceKind.Stopped, "browser left open after all service processes stop stays stopped");
        check(Match(new[] { host, browser }).Sample.Kind == Match(new[] { host }).Sample.Kind,
            "closing browser cannot hide a confirmed service");
        check(!new InstallationContract(target).Verify(), "missing installation cannot be verified");
        check(DeepSeekInteraction.OnHead(.5, .38) && !DeepSeekInteraction.OnHead(.1, .2) && !DeepSeekInteraction.OnHead(.5, .7),
            "DeepSeek forehead excludes ahoge, eyes and costume");
        var behavior = new CompanionBehavior();
        for (int i = 0; i < 8; i++) DeepSeekInteraction.Hover(behavior, i % 2 == 0 ? .35 : .65, .4, 1 + i * .08);
        check(behavior.Sample(1.6).Mood == PetMood.Happy, "DeepSeek forehead strokes trigger happy frame");
        behavior = new CompanionBehavior();
        for (int i = 0; i < 8; i++) DeepSeekInteraction.Hover(behavior, i % 2 == 0 ? .35 : .65, .8, 1 + i * .08);
        check(behavior.Sample(1.6).Mood != PetMood.Happy, "costume motion does not trigger petting");
        check(typeof(AppPresence).Assembly.GetName().Name == "AIMascot.DeepSeek", "independent DeepSeek assembly");
        check(StartupRegistration.ValueName == "AI-Mascot Desktop DeepSeek", "independent startup entry");
        check(new PreferencesStore().StoragePath == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AI-Mascot-Desktop", "deepseek", "preferences.json"), "DeepSeek-only preferences");
        check(new PreferencesStore().LegacyPath == null, "no import of other roles' preferences");

        // Cross-check the native table parser against a real, test-owned listener when the audited port is free.
        var listener = new TcpListener(IPAddress.Loopback, HarnessTarget.ServicePort);
        bool bound = false;
        try
        {
            try { listener.Start(); bound = true; }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressAlreadyInUse || e.SocketErrorCode == SocketError.AccessDenied)
            { Console.WriteLine("UNVERIFIED: live foreign listener fixture cannot bind; existing service was not disturbed."); }
            if (bound)
            {
                var table = PassiveListenerTable.Read();
                check(table.Any(l => l.ProcessId == Environment.ProcessId && l.Port == HarnessTarget.ServicePort && l.Address == "127.0.0.1"),
                    "native parser finds real test listener owner/address/port");
                check(Match(Array.Empty<HarnessProcess>(), table.ToArray()).Sample.Kind == PresenceKind.Stopped,
                    "real foreign listener alone is rejected");
            }
        }
        finally { listener.Stop(); }
    }

    public static void Diagnostics(Action<bool, string> check, string directory)
    {
        var runtime = new AIMascot.Platform.RuntimeStatus("deepseek", directory);
        runtime.Interaction("greet", double.NaN, double.PositiveInfinity);
        runtime.Write(new { Reason = "WaitingForApp" });
        using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "runtime-status.json"))))
            check(json.RootElement.GetProperty("Interactions")[0].GetProperty("Left").ValueKind == JsonValueKind.Null &&
                json.RootElement.GetProperty("Interactions")[0].GetProperty("Top").ValueKind == JsonValueKind.Null,
                "uninitialized coordinates serialize as null");
        for (int i = 0; i < 40; i++) runtime.Interaction("press", i, i);
        runtime.Write(new { Reason = "Following" });
        using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "runtime-status.json"))))
            check(json.RootElement.GetProperty("Interactions").GetArrayLength() == 32 &&
                json.RootElement.GetProperty("Interactions")[0].GetProperty("Left").GetInt32() == 8,
                "diagnostics retain only latest 32 interactions");
        var blocked = Path.Combine(directory, "blocked"); File.WriteAllText(blocked, "fixture");
        new AIMascot.Platform.RuntimeStatus("deepseek", blocked).Write(new { Reason = "Demo" });
        check(File.ReadAllText(blocked) == "fixture", "unwritable diagnostics cannot stop host or overwrite unrelated file");
    }
}
