using System.Text.Json;
using AIMascot.ConsoleApp;

int checks = 0;
void Check(bool success, string name) { if (!success) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); checks++; }
Check(Role.Installed.Select(r => r.Id).Order().SequenceEqual(new[] { "claude", "deepseek", "dragon", "gemini", "grok" }), "all five delivered roles registered once");
Check(Role.Installed.Select(r => r.EventName("hide")).Distinct().Count() == 5, "five separate IPC namespaces");
Check(Role.Installed.Select(r => r.PreferencesPath).Distinct().Count() == 5, "five separate settings paths");
Check(Role.Installed.Select(r => r.StartupName).Distinct().Count() == 5, "five separate startup entries");
Check(Role.Installed.All(r => r.ProcessName.StartsWith("AIMascot.") && r.StartupCommand == $"\"{r.Executable}\" --background"), "every launch target is a local mascot host");
Check(Role.Installed.Single(r => r.Id == "claude").Executable == Role.HostPath("Claude"), "Claude target is the offline mascot, never the client or guard");
Check(Role.Installed.Single(r => r.Id == "deepseek").Executable == Role.HostPath("DeepSeek"), "DeepSeek target is the mascot, never Harness");
var fixture = Path.Combine(Path.GetTempPath(), "MascotConsole-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var settings = Path.Combine(fixture, "preferences.json");
Check(RoleController.ReadOptions(settings) == null, "missing settings stay unknown");
File.WriteAllText(settings, "{\"Version\":3,\"Mode\":1,\"UserHidden\":true,\"Paused\":false}");
Check(RoleController.ReadOptions(settings) == new SavedOptions(1,true,false), "reads shared v3 display preferences");
foreach (string json in new[] { "{malformed", "null", "[]", "{}", "{\"Version\":999}", "{\"Version\":3,\"Mode\":2}", new string('x',33000) })
{ File.WriteAllText(settings, json); Check(RoleController.ReadOptions(settings) == null, "invalid or unsupported settings stay unknown"); }
var a = new Role("fixture-a", "a", "test", Path.Combine(fixture, "missing.exe"), "FixtureA-"+Guid.NewGuid().ToString("N"), "#000000");
var b = a with { Id = "fixture-b", Prefix = "FixtureB-"+Guid.NewGuid().ToString("N") };
using var signalA = new EventWaitHandle(false, EventResetMode.AutoReset, a.EventName("hide"));
using var signalB = new EventWaitHandle(false, EventResetMode.AutoReset, b.EventName("hide"));
Check(RoleController.TrySignal(a,"hide") && signalA.WaitOne(500) && !signalB.WaitOne(20), "A command cannot signal B");
Check(RoleController.TrySignal(b,"hide") && signalB.WaitOne(500) && !signalA.WaitOne(20), "B command cannot signal A");
Check(!RoleController.TrySignal(a,"unknown") && !RoleController.TrySignal(a,"enable-startup"), "unrecognized and non-IPC commands rejected");
var controller = new RoleController();
Check(!(await controller.ExecuteAsync(a,"start")).Success, "missing installation refuses launch");
Check(!(await controller.ExecuteAsync(a,"unknown")).Success, "unknown command refuses launch");
var isolated = Role.Installed.Select(r => r with { Executable = Path.Combine(fixture, r.ProcessName + ".exe"), Prefix = r.Prefix + "-Test-" + Guid.NewGuid().ToString("N") }).ToArray();
foreach (var role in isolated)
{
    Check(!controller.Read(role).Installed && !(await controller.ExecuteAsync(role, "start")).Success, role.Id + " missing install reported without launching an AI app");
}
var signals = isolated.Select(r => new EventWaitHandle(false, EventResetMode.AutoReset, r.EventName("hide"))).ToArray();
try {
    for (int i = 0; i < isolated.Length; i++)
    {
        Check(RoleController.TrySignal(isolated[i], "hide") && signals[i].WaitOne(500), isolated[i].Id + " own IPC delivered");
        Check(signals.Where((_, j) => j != i).All(s => !s.WaitOne(10)), isolated[i].Id + " leaves all four peer signals untouched");
    }
} finally { foreach (var signal in signals) signal.Dispose(); }
var hashBefore = File.ReadAllText(settings); controller.Read(a);
Check(File.ReadAllText(settings) == hashBefore, "status reads do not write preferences");
var runtime = Path.Combine(fixture,"runtime-status.json");
File.WriteAllText(runtime, JsonSerializer.Serialize(new {Version=1,ProcessId=123,Utc=DateTime.UtcNow,State=new {Reason="Fullscreen"}}));
Check(RoleController.ReadReason(runtime,new[]{123}) == "Fullscreen", "live matching host explains fullscreen hide");
Check(RoleController.ReadReason(runtime,new[]{124}) == null, "old host status cannot explain a different process");
File.WriteAllText(runtime, JsonSerializer.Serialize(new {Version=1,ProcessId=123,Utc=DateTime.UtcNow.AddMinutes(-1),State=new {Reason="Fullscreen"}}));
Check(RoleController.ReadReason(runtime,new[]{123}) == null, "stale active status ignored");
File.WriteAllText(runtime, JsonSerializer.Serialize(new {Version=1,ProcessId=123,Utc=DateTime.UtcNow.AddMinutes(-1),State=new {Reason="Suspended"}}));
Check(RoleController.ReadReason(runtime,new[]{123}) == "Suspended" && RoleController.ReadReason(runtime,new[]{124}) == null, "suspended status still requires the live host PID");
File.WriteAllText(runtime, JsonSerializer.Serialize(new {Version=1,ProcessId=123,Utc=DateTime.UtcNow,State=new {Reason="Unknown",IdentityEvidence="DesktopPresentServiceUnconfirmed"}}));
Check(RoleController.ReadReason(runtime,new[]{123}) == "Unknown", "DeepSeek optional identity evidence stays protocol compatible");
File.WriteAllText(runtime, "null");
Check(RoleController.ReadReason(runtime,new[]{123}) == null, "malformed runtime status is safe");
File.WriteAllText(Path.Combine(fixture,"result.json"), JsonSerializer.Serialize(new { Passed=true, Checks=checks }));
Console.WriteLine($"RESULT: {checks} console checks passed. Fixtures: {fixture}");
