using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AIMascot.Core;
using Microsoft.Win32.SafeHandles;

namespace AIMascot.Gemini;

// Installation identity only; this is not a security/signature attestation.
// Never match command lines, ports, window titles or arbitrary same-name executables.
internal sealed record AntigravityTarget(string ExecutablePath)
{
    public static AntigravityTarget CurrentUser { get; } = new(AIMascot.Platform.TargetPaths.Read("gemini", Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "antigravity", "Antigravity.exe")));
    public const string ProcessName = "Antigravity";
    internal static bool ProductMatches(string? product, string? company) =>
        string.Equals(product, "Antigravity", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(company, "Google", StringComparison.OrdinalIgnoreCase);
    public bool InstallationVerified()
    {
        try
        {
            if (!File.Exists(ExecutablePath)) return false;
            var version = FileVersionInfo.GetVersionInfo(ExecutablePath);
            return ProductMatches(version.ProductName, version.CompanyName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException)
        { return false; }
    }
}

internal sealed record AntigravityProcess(int ProcessId, string Name, string? ExecutablePath,
    bool ReadFailed = false, bool Exited = false);
internal sealed record PresenceProbe(PresenceSample Sample, IReadOnlyList<AntigravityProcess> Identities,
    string ExpectedExecutable, bool InstallationVerified);

internal static class AntigravityMatcher
{
    public static PresenceSample Match(AntigravityTarget target, IEnumerable<AntigravityProcess> processes,
        bool installationVerified, bool enumerationFailed = false)
    {
        var matched = new HashSet<int>();
        var unreadable = new HashSet<int>();
        var candidates = new HashSet<int>();
        foreach (var process in processes)
        {
            if (process.Exited || !string.Equals(process.Name, AntigravityTarget.ProcessName, StringComparison.OrdinalIgnoreCase)) continue;
            candidates.Add(process.ProcessId);
            if (process.ReadFailed || string.IsNullOrWhiteSpace(process.ExecutablePath))
            { unreadable.Add(process.ProcessId); continue; }
            if (installationVerified && string.Equals(process.ExecutablePath, target.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                matched.Add(process.ProcessId);
        }
        var kind = !installationVerified ? PresenceKind.Unknown : matched.Count > 0 ? PresenceKind.Running
            : enumerationFailed || unreadable.Count > 0 ? PresenceKind.Unknown : PresenceKind.Stopped;
        return new(kind, matched.Count, unreadable.Count, candidates.Count);
    }
}

internal sealed class AppPresence
{
    private readonly AntigravityTarget _target;
    private readonly int _sessionId = GetSessionId();
    public AppPresence(AntigravityTarget? target = null) => _target = target ?? AntigravityTarget.CurrentUser;
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref uint size);

    public PresenceProbe Probe()
    {
        var identities = new List<AntigravityProcess>();
        bool verified = _target.InstallationVerified();
        Process[] candidates;
        try { candidates = Process.GetProcessesByName(AntigravityTarget.ProcessName); }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException)
        { return Result(true); }
        foreach (var process in candidates)
        {
            using (process)
            {
                int id = process.Id;
                try
                {
                    if (process.SessionId != _sessionId) continue;
                    using var handle = OpenProcess(0x1000, false, id);
                    var path = new StringBuilder(32768);
                    uint capacity = (uint)path.Capacity;
                    if (handle.IsInvalid || !QueryFullProcessImageName(handle, 0, path, ref capacity))
                    {
                        identities.Add(new(id, AntigravityTarget.ProcessName, null, ReadFailed: true, Exited: HasExited(process)));
                        continue;
                    }
                    identities.Add(new(id, AntigravityTarget.ProcessName, path.ToString(), Exited: HasExited(process)));
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException)
                { identities.Add(new(id, AntigravityTarget.ProcessName, null, ReadFailed: true, Exited: HasExited(process))); }
            }
        }
        return Result(false);
        PresenceProbe Result(bool failed) => new(AntigravityMatcher.Match(_target, identities, verified, failed),
            identities, _target.ExecutablePath, verified);
    }
    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException) { return false; }
    }
    private static int GetSessionId() { using var process = Process.GetCurrentProcess(); return process.SessionId; }
}
