using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AIMascot.Core;
using Microsoft.Win32.SafeHandles;

namespace AIMascot.Windows;

internal sealed record PresenceProbe(PresenceSample Sample, IReadOnlyList<ProcessIdentity> Identities);

internal sealed class AppPresence
{
    private readonly TargetBinding _target;
    private readonly int _sessionId = GetSessionId();
    public AppPresence(TargetBinding? target = null) => _target = target ?? TargetBinding.CodexDesktop;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(SafeProcessHandle process, ref uint size, StringBuilder? name);

    public PresenceProbe Probe()
    {
        var identities = new List<ProcessIdentity>();
        Process[] candidates;
        try { candidates = Process.GetProcessesByName(_target.ProcessName); }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException)
        { return new(PresenceMatcher.Match(_target, identities, enumerationFailed: true), identities); }

        foreach (var process in candidates)
        {
            using (process)
            {
                int id = process.Id;
                try
                {
                    if (process.SessionId != _sessionId) continue;
                    using var handle = OpenProcess(0x1000, false, id); // query limited information only
                    if (handle.IsInvalid)
                    {
                        identities.Add(new(id, _target.ProcessName, null, null, ReadFailed: true, Exited: HasExited(process)));
                        continue;
                    }
                    var path = new StringBuilder(32768);
                    uint capacity = (uint)path.Capacity;
                    if (!QueryFullProcessImageName(handle, 0, path, ref capacity))
                    {
                        identities.Add(new(id, _target.ProcessName, null, null, ReadFailed: true, Exited: HasExited(process)));
                        continue;
                    }
                    uint length = 0;
                    int error = GetPackageFamilyName(handle, ref length, null);
                    string? family = null;
                    bool failed = false;
                    if (error == 122 && length > 0 && length <= 65536)
                    {
                        var buffer = new StringBuilder((int)length);
                        error = GetPackageFamilyName(handle, ref length, buffer);
                        if (error == 0) family = buffer.ToString(); else failed = true;
                    }
                    else if (error != 15700) failed = true; // NO_PACKAGE means known non-match
                    identities.Add(new(id, _target.ProcessName, family, Path.GetFileName(path.ToString()),
                        failed, HasExited(process)));
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException)
                { identities.Add(new(id, _target.ProcessName, null, null, ReadFailed: true, Exited: HasExited(process))); }
            }
        }
        return new(PresenceMatcher.Match(_target, identities), identities);
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException) { return false; }
    }
    private static int GetSessionId() { using var process = Process.GetCurrentProcess(); return process.SessionId; }
}
