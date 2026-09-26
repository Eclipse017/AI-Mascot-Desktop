using System.Text.Json;
using AIMascot.Platform;

var root = Path.Combine(Path.GetTempPath(), "mascot-path-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var file = Path.Combine(root, "targets.json"); int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); checks++; }
Check(TargetPaths.ReadFrom(file, "grok", "fallback") == "fallback", "missing config preserves fallback");
var exe = Path.Combine(root, "App.exe");
File.WriteAllText(file, JsonSerializer.Serialize(new { grok = exe }));
Check(TargetPaths.ReadFrom(file, "grok") == exe, "absolute executable accepted without starting it");
Check(TargetPaths.ReadFrom(file, "deepseek") == "", "unconfigured role remains unbound");
foreach (var invalid in new[] { "relative.exe", @"\\server\share\App.exe", @"\\?\C:\App.exe", "https://example.com/App.exe", "C:\\App.exe --launch", "C:\\App.txt", "\"C:\\App.exe\"" }) {
 File.WriteAllText(file, JsonSerializer.Serialize(new { grok = invalid }));
 Check(TargetPaths.ReadFrom(file, "grok") == "", "unsafe or non-executable target rejected");
}
foreach (var invalid in new[] { "null", "[]", "{broken", new string('x', 17000) }) {
 File.WriteAllText(file, invalid); Check(TargetPaths.ReadFrom(file, "grok") == "", "invalid config falls back");
}
Console.WriteLine($"RESULT: {checks} passive target path checks passed.");
