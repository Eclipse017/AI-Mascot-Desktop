using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace AIMascot.Gemini;

internal sealed record StartupState(bool Enabled, bool Available, string? Message = null);
internal sealed class StartupRegistration
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "AI-Mascot Desktop Gemini";
    private readonly string _executable, _keyPath, _valueName;
    public StartupRegistration(string executable, string keyPath = RunKey, string valueName = ValueName)
    { _executable = Path.GetFullPath(executable); _keyPath = keyPath; _valueName = valueName; }
    internal string Command => $"\"{_executable}\" --background";
    public StartupState Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
            var existing = key?.GetValue(_valueName) as string;
            bool enabled = string.Equals(existing, Command, StringComparison.OrdinalIgnoreCase);
            return new(enabled, true, existing != null && !enabled ? "启动项指向另一个Gemini 娘版本；启用后会切换到当前版本。" : null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        { return new(false, false, "无法读取自动启动设置。可稍后重试。"); }
    }
    public StartupState SetEnabled(bool enabled)
    {
        try
        {
            if (enabled && (!File.Exists(_executable) || Command.Length > 260))
                return new(false, false, "当前程序路径无效或过长，无法设置自动启动。");
            using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
            if (enabled) key.SetValue(_valueName, Command, RegistryValueKind.String);
            else
            {
                // Never remove a registration belonging to a different installed copy.
                if (string.Equals(key.GetValue(_valueName) as string, Command, StringComparison.OrdinalIgnoreCase))
                    key.DeleteValue(_valueName, throwOnMissingValue: false);
            }
            return Read();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        { return new(false, false, "自动启动设置未能保存。本次仍可正常使用。"); }
    }
}
