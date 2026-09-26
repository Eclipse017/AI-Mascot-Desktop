using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AIMascot.Core;
using Microsoft.Win32.SafeHandles;

namespace AIMascot.Grok;

// Installation identity only; this is not a security/signature attestation.
// Never match command lines, ports, window titles or arbitrary same-name executables.
internal sealed record GrokBotTarget(string ExecutablePath)
{
    public static GrokBotTarget CurrentUser { get; } = new(AIMascot.Platform.TargetPaths.Read("grok"));
    public const string ProcessName = "Grok Bot";
    internal static bool ProductMatches(string? product, string? company) =>
        string.Equals(product, "Grok Bot", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(company, "SpaceXAI", StringComparison.OrdinalIgnoreCase);
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

internal sealed record GrokBotProcess(int ProcessId, string Name, string? ExecutablePath,
    bool ReadFailed = false, bool Exited = false);
internal sealed record PresenceProbe(PresenceSample Sample, IReadOnlyList<GrokBotProcess> Identities,
    string ExpectedExecutable, bool InstallationVerified);

internal static class GrokBotMatcher
{
    public static PresenceSample Match(GrokBotTarget target, IEnumerable<GrokBotProcess> processes,
        bool installationVerified, bool enumerationFailed = false)
    {
        var matched = new HashSet<int>();
        var unreadable = new HashSet<int>();
        var candidates = new HashSet<int>();
        foreach (var process in processes)
        {
            if (process.Exited || !string.Equals(process.Name, GrokBotTarget.ProcessName, StringComparison.OrdinalIgnoreCase)) continue;
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
    private readonly GrokBotTarget _target;
    private readonly int _sessionId = GetSessionId();
    public AppPresence(GrokBotTarget? target = null) => _target = target ?? GrokBotTarget.CurrentUser;
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref uint size);

    public PresenceProbe Probe()
    {
        var identities = new List<GrokBotProcess>();
        bool verified = _target.InstallationVerified();
        Process[] candidates;
        try { candidates = Process.GetProcessesByName(GrokBotTarget.ProcessName); }
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
                        identities.Add(new(id, GrokBotTarget.ProcessName, null, ReadFailed: true, Exited: HasExited(process)));
                        continue;
                    }
                    identities.Add(new(id, GrokBotTarget.ProcessName, path.ToString(), Exited: HasExited(process)));
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException)
                { identities.Add(new(id, GrokBotTarget.ProcessName, null, ReadFailed: true, Exited: HasExited(process))); }
            }
        }
        return Result(false);
        PresenceProbe Result(bool failed) => new(GrokBotMatcher.Match(_target, identities, verified, failed),
            identities, _target.ExecutablePath, verified);
    }
    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException) { return false; }
    }
    private static int GetSessionId() { using var process = Process.GetCurrentProcess(); return process.SessionId; }
}
