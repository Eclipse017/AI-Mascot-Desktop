using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AIMascot.Platform;

// These paths are only used for passive identity checks. They are never launch targets.
public static class TargetPaths
{
    public static string ConfigurationPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AI-Mascot-Desktop", "targets.json");

    public static string Read(string role, string fallback = "") => ReadFrom(ConfigurationPath, role, fallback);

    public static string ReadFrom(string path, string role, string fallback = "")
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 16384) return fallback;
            var paths = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (paths == null || !paths.TryGetValue(role, out var value) || string.IsNullOrWhiteSpace(value)) return fallback;
            value = Environment.ExpandEnvironmentVariables(value);
            // Require an explicit local EXE. Never probe UNC/network shares or accept command arguments.
            if (!Path.IsPathFullyQualified(value) || value.StartsWith(@"\\", StringComparison.Ordinal) ||
                value.Contains('"') || !string.Equals(Path.GetExtension(value), ".exe", StringComparison.OrdinalIgnoreCase)) return fallback;
            return Path.GetFullPath(value);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        { return fallback; }
    }
}
