using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AIMascot.Core;
using Microsoft.Win32.SafeHandles;

namespace AIMascot.Claude;

// Passive OS identity only. Never start a client or read content/command lines.
internal sealed record ClaudeProcess(int ProcessId, string Name, string? PackageFamily,
    string? ExecutablePath, string? PackageRoot, bool ReadFailed = false, bool Exited = false);
internal sealed record PresenceProbe(PresenceSample Sample, IReadOnlyList<ClaudeProcess> Identities,
    string ExpectedPackageFamily = ClaudeMatcher.PackageFamily);

internal static class ClaudeMatcher
{
    internal const string ProcessName = "Claude";
    internal const string PackageFamily = "Claude_pzs8sxrjxfjjc";
    internal const string RelativeExecutable = @"app\Claude.exe";
    internal static PresenceSample Match(IEnumerable<ClaudeProcess> processes, bool enumerationFailed = false)
    {
        var matched = new HashSet<int>(); var unreadable = new HashSet<int>(); var candidates = new HashSet<int>();
        foreach (var p in processes)
        {
            if (p.Exited || !string.Equals(p.Name, ProcessName, StringComparison.OrdinalIgnoreCase)) continue;
            candidates.Add(p.ProcessId);
            if (p.ReadFailed) { unreadable.Add(p.ProcessId); continue; }
            // NO_PACKAGE is a known non-match (CLI or an unpackaged clone).
            if (!string.Equals(p.PackageFamily, PackageFamily, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(p.PackageRoot) || string.IsNullOrWhiteSpace(p.ExecutablePath))
            { unreadable.Add(p.ProcessId); continue; }
            try
            {
                if (!Path.IsPathFullyQualified(p.PackageRoot) || !Path.IsPathFullyQualified(p.ExecutablePath))
                { unreadable.Add(p.ProcessId); continue; }
                var expected = Path.GetFullPath(Path.Combine(p.PackageRoot, RelativeExecutable));
                if (string.Equals(Path.GetFullPath(p.ExecutablePath), expected, StringComparison.OrdinalIgnoreCase)) matched.Add(p.ProcessId);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            { unreadable.Add(p.ProcessId); }
        }
        var kind = matched.Count > 0 ? PresenceKind.Running : enumerationFailed || unreadable.Count > 0
            ? PresenceKind.Unknown : PresenceKind.Stopped;
        return new(kind, matched.Count, unreadable.Count, candidates.Count);
    }
}

internal sealed class AppPresence
{
    private readonly int _sessionId = GetSessionId();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(SafeProcessHandle process, ref uint size, StringBuilder? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFullName(SafeProcessHandle process, ref uint size, StringBuilder? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagePathByFullName(string fullName, ref uint size, StringBuilder? path);

    private delegate int QueryString(ref uint size, StringBuilder? buffer);
    private static string? ReadString(QueryString query, out bool failed)
    {
        uint length = 0; int code = query(ref length, null); failed = false;
        if (code == 15700) return null;
        if (code != 122 || length == 0 || length > 32768) { failed = true; return null; }
        var buffer = new StringBuilder((int)length);
        if (query(ref length, buffer) != 0) { failed = true; return null; }
        return buffer.ToString();
    }
    public PresenceProbe Probe()
    {
        var identities = new List<ClaudeProcess>();
        Process[] candidates;
        try { candidates = Process.GetProcessesByName(ClaudeMatcher.ProcessName); }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException)
        { return new(ClaudeMatcher.Match(identities, true), identities); }
        foreach (var process in candidates)
        {
            using (process)
            {
                int id = process.Id;
                try
                {
                    if (process.SessionId != _sessionId) continue;
                    using var handle = OpenProcess(0x1000, false, id);
                    if (handle.IsInvalid) { Unknown(); continue; }
                    var family = ReadString((ref uint n, StringBuilder? b) => GetPackageFamilyName(handle, ref n, b), out bool failed);
                    if (failed) { Unknown(); continue; }
                    if (!string.Equals(family, ClaudeMatcher.PackageFamily, StringComparison.OrdinalIgnoreCase))
                    { identities.Add(new(id, ClaudeMatcher.ProcessName, family, null, null, Exited: HasExited(process))); continue; }
                    var fullName = ReadString((ref uint n, StringBuilder? b) => GetPackageFullName(handle, ref n, b), out failed);
                    if (failed || fullName == null) { Unknown(); continue; }
                    var root = ReadString((ref uint n, StringBuilder? b) => GetPackagePathByFullName(fullName, ref n, b), out failed);
                    var path = new StringBuilder(32768); uint capacity = (uint)path.Capacity;
                    if (failed || root == null || !QueryFullProcessImageName(handle, 0, path, ref capacity)) { Unknown(); continue; }
                    identities.Add(new(id, ClaudeMatcher.ProcessName, family, path.ToString(), root, Exited: HasExited(process)));
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException) { Unknown(); }
                void Unknown() => identities.Add(new(id, ClaudeMatcher.ProcessName, null, null, null, true, HasExited(process)));
            }
        }
        return new(ClaudeMatcher.Match(identities), identities);
    }
    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException) { return false; }
    }
    private static int GetSessionId() { using var process = Process.GetCurrentProcess(); return process.SessionId; }
}
