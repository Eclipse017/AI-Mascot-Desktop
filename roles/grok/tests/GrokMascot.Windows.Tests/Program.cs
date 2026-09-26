using System;
using System.IO;
using System.Text.Json;
using AIMascot.Grok;
using AIMascot.Core;
using Microsoft.Win32;

if (args.Length >= 2 && args[0] is "--platform-probe" or "--fullscreen-fixture")
{
    Environment.ExitCode = PlatformTests.Run(args[0], Path.GetFullPath(args[1]));
    return;
}

var directory = Path.Combine(Path.GetTempPath(), "AIMascot-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); checks++; }
var store = new PreferencesStore(directory);
Check(store.Load().Mode == DisplayMode.Demo, "fresh settings use offline demo mode");
Check(store.Save(new PetPreferences { Left = 150, Top = 220, Mode = DisplayMode.Demo, UserHidden = true, Paused = true }),
    "preferences save succeeds");
var loaded = store.Load();
Check(loaded.Mode == DisplayMode.Demo && loaded.UserHidden && loaded.Paused && loaded.Left == 150, "display preferences round trip");
File.WriteAllText(Path.Combine(directory, "preferences.json"), "{ malformed");
Check(store.Load().Mode == DisplayMode.Demo && !store.Load().UserHidden, "corrupt settings safely recover");
File.WriteAllText(Path.Combine(directory, "preferences.json"), "{\"Version\":1,\"Left\":12,\"Top\":34,\"Size\":180}");
loaded = store.Load();
Check(loaded.Left == 12 && loaded.Size == 180 && loaded.Version == 3 && loaded.Mode == DisplayMode.Auto, "v1 settings migrate geometry");
File.WriteAllText(Path.Combine(directory, "preferences.json"), "{\"Version\":999,\"UserHidden\":true}");
Check(!store.Load().UserHidden, "future schema does not silently import");
File.WriteAllText(Path.Combine(directory, "preferences.json"), new string('x', 40000));
Check(store.Load().Mode == DisplayMode.Demo, "oversized settings recover without unbounded parsing");
var migratedDirectory = Path.Combine(directory, "migrated");
File.WriteAllText(Path.Combine(directory, "preferences.json"), "{\"Version\":2,\"Left\":125,\"Mode\":1,\"Paused\":true}");
var migrated = new PreferencesStore(migratedDirectory, directory);
loaded = migrated.Load();
Check(migrated.Migrated && loaded.Left == 125 && loaded.Paused && loaded.Mode == DisplayMode.Demo && loaded.HideOnFullscreen,
    "stage 2 settings migrate with final default options");
Check(migrated.Save(loaded) && File.ReadAllText(Path.Combine(directory,"preferences.json")).Contains("\"Version\":2"), "migration preserves original settings");
var registryPath = @"Software\AI-Mascot\Tests\" + Guid.NewGuid().ToString("N");
var startup = new StartupRegistration(Environment.ProcessPath!, registryPath, "StartupTest");
try
{
    Check(!startup.Read().Enabled, "test startup initially absent");
    Check(startup.SetEnabled(true).Enabled, "startup registration saves and reads current executable");
    using (var key = Registry.CurrentUser.OpenSubKey(registryPath))
        Check((string?)key?.GetValue("StartupTest") == startup.Command && startup.Command.EndsWith("--background"), "startup command is quoted and silent");
    using (var key = Registry.CurrentUser.CreateSubKey(registryPath)) key.SetValue("OtherApp", "untouched");
    Check(!startup.SetEnabled(false).Enabled, "startup disable removes only own entry");
    using (var key = Registry.CurrentUser.OpenSubKey(registryPath)) Check((string?)key?.GetValue("OtherApp") == "untouched", "unrelated startup value preserved");
    using (var key = Registry.CurrentUser.CreateSubKey(registryPath)) key.SetValue("StartupTest", "different-installation");
    startup.SetEnabled(false);
    using (var key = Registry.CurrentUser.OpenSubKey(registryPath)) Check((string?)key?.GetValue("StartupTest") == "different-installation", "another installed copy's startup entry is preserved");
}
finally { Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false); }
GrokTests.Run(Check);
var probe = new AppPresence().Probe();
Console.WriteLine("LIVE_READ_ONLY: " + JsonSerializer.Serialize(probe));
if (args.Length > 0) File.WriteAllText(args[0], JsonSerializer.Serialize(probe, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"RESULT: {checks} Windows checks passed; live presence is observational, not simulated.");
// Only this test-created directory is cleaned. It never contains user configuration.
var resolved = Path.GetFullPath(directory);
var expectedRoot = Path.GetFullPath(Path.GetTempPath());
if (!resolved.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) ||
    !Path.GetFileName(resolved).StartsWith("AIMascot-tests-", StringComparison.Ordinal))
    throw new InvalidOperationException("Refusing cleanup outside this test's temporary directory.");
Directory.Delete(resolved, recursive: true);
