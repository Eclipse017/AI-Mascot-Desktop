using System;
using System.IO;
using System.Text.Json;
using AIMascot.Core;

namespace AIMascot.Claude;

internal sealed class PreferencesStore
{
    private readonly string _path;
    private readonly string? _legacyPath;
    internal string StoragePath => _path;
    internal string? LegacyPath => _legacyPath;
    public bool Migrated { get; private set; }
    private static PetPreferences Defaults() => new() { Mode = DisplayMode.Demo };
    public PreferencesStore(string? directory = null, string? legacyDirectory = null)
    {
        var basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI-Mascot-Desktop");
        _path = Path.Combine(directory ?? Path.Combine(basePath, "claude"), "preferences.json");
        _legacyPath = legacyDirectory != null ? Path.Combine(legacyDirectory, "preferences.json")
            : null;
    }
    public PetPreferences Load()
    {
        try
        {
            string? readPath = File.Exists(_path) ? _path : _legacyPath != null && File.Exists(_legacyPath) ? _legacyPath : null;
            if (readPath == null) return Defaults();
            if (new FileInfo(readPath).Length > 32768) return Defaults();
            var value = JsonSerializer.Deserialize<PetPreferences>(File.ReadAllText(readPath));
            if (value?.Version is not (1 or 2 or 3)) return Defaults();
            if (value.Version == 1) value = value with { Mode = DisplayMode.Auto };
            Migrated = readPath != _path;
            return value.Sanitize();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return Defaults(); }
    }
    public bool Save(PetPreferences value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(value.Sanitize(), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(_path + ".tmp", _path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
