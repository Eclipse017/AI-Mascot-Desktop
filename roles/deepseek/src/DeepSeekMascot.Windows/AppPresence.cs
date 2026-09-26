using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AIMascot.Core;
using Microsoft.Win32.SafeHandles;

namespace AIMascot.DeepSeek;

// Audited Desktop 0.1.7-rc.2 contract; changed/moved installations require re-audit.
// Installation/lifecycle identity is not a security attestation or model/service health check.
internal sealed record HarnessTarget(string ExecutablePath, string ArchivePath)
{
    public const string ProcessName = "DeepSeek Harness";
    public const int ServicePort = 19387;
    internal const string ArchiveSha256 = "708229949F0533D6D69FCA3D4D72C3AD6814FDA484EA3B0A7AF96B71E83A6126";
    internal const string ExecutableSha256 = "9788AF2D1DA9BFC2C4B26A7AFD707FACE45252BF1880AE04F39997CD1DB9D3BD";
    public static HarnessTarget CurrentUser { get; } = Configured();
    private static HarnessTarget Configured()
    {
        string executable = AIMascot.Platform.TargetPaths.Read("deepseek");
        return new(executable, string.IsNullOrEmpty(executable) ? "" :
            Path.Combine(Path.GetDirectoryName(executable)!, "resources", "app.asar"));
    }
}

internal sealed class InstallationContract
{
    private readonly HarnessTarget _target;
    private (long, long, long, long)? _stamp;
    private bool _verified;
    public InstallationContract(HarnessTarget target) => _target = target;
    public bool Verify()
    {
        try
        {
            var exe = new FileInfo(_target.ExecutablePath);
            var archive = new FileInfo(_target.ArchivePath);
            if (!exe.Exists || !archive.Exists) { _stamp = null; return false; }
            var stamp = (exe.Length, exe.LastWriteTimeUtc.Ticks, archive.Length, archive.LastWriteTimeUtc.Ticks);
            if (_stamp == stamp) return _verified;
            using var exeStream = File.OpenRead(exe.FullName);
            using var archiveStream = File.OpenRead(archive.FullName);
            _verified = Convert.ToHexString(SHA256.HashData(exeStream)) == HarnessTarget.ExecutableSha256 &&
                Convert.ToHexString(SHA256.HashData(archiveStream)) == HarnessTarget.ArchiveSha256;
            _stamp = stamp;
            return _verified;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { _stamp = null; return false; }
    }
}

internal sealed record HarnessProcess(int ProcessId, string Name, string? ExecutablePath,
    bool ReadFailed = false, bool Exited = false, bool StartedAfterSnapshot = false);
internal sealed record ServiceListener(int ProcessId, string Address, int Port);
internal sealed record PresenceProbe(PresenceSample Sample, string Evidence, bool InstallationVerified,
    int VerifiedDesktopProcesses, int ConfirmedServiceOwners);

internal static class HarnessMatcher
{
    public static PresenceProbe Match(HarnessTarget target, IEnumerable<HarnessProcess> processes,
        IEnumerable<ServiceListener> listeners, bool installationVerified,
        bool enumerationFailed = false, bool listenerReadFailed = false)
    {
        var verified = new HashSet<int>();
        var unreadable = new HashSet<int>();
        var candidates = new HashSet<int>();
        foreach (var process in processes)
        {
            if (process.Exited || !string.Equals(process.Name, HarnessTarget.ProcessName, StringComparison.OrdinalIgnoreCase)) continue;
            candidates.Add(process.ProcessId);
            if (process.ReadFailed || process.StartedAfterSnapshot || string.IsNullOrWhiteSpace(process.ExecutablePath))
            { unreadable.Add(process.ProcessId); continue; }
            if (string.Equals(process.ExecutablePath, target.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                verified.Add(process.ProcessId);
        }
        // Port alone, shared node.exe, browser presence, and an Electron renderer cannot establish service identity.
        int owners = listeners.Where(l => l.Port == HarnessTarget.ServicePort &&
            (l.Address is "127.0.0.1" or "0.0.0.0") && verified.Contains(l.ProcessId))
            .Select(l => l.ProcessId).Distinct().Count();
        PresenceKind kind;
        string evidence;
        if (!installationVerified) { kind = PresenceKind.Unknown; evidence = "UnsupportedOrChangedInstallation"; }
        else if (!listenerReadFailed && owners > 0) { kind = PresenceKind.Running; evidence = "VerifiedDesktopServiceListener"; }
        else if (enumerationFailed || listenerReadFailed || unreadable.Count > 0)
        { kind = PresenceKind.Unknown; evidence = "ReadUnavailableOrProcessRace"; }
        else if (verified.Count > 0)
        { kind = PresenceKind.Unknown; evidence = "DesktopPresentServiceUnconfirmed"; }
        else { kind = PresenceKind.Stopped; evidence = "NoVerifiedDesktopProcess"; }
        return new(new(kind, kind == PresenceKind.Running ? owners : 0, unreadable.Count, candidates.Count),
            evidence, installationVerified, verified.Count, kind == PresenceKind.Running ? owners : 0);
    }
}

internal sealed class AppPresence
{
    private readonly HarnessTarget _target;
    private readonly InstallationContract _installation;
    private readonly int _sessionId = GetSessionId();
    public AppPresence(HarnessTarget? target = null)
    { _target = target ?? HarnessTarget.CurrentUser; _installation = new(_target); }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref uint size);

    public PresenceProbe Probe()
    {
        bool verified = _installation.Verify();
        var identities = new List<HarnessProcess>();
        IReadOnlyList<ServiceListener> listeners;
        bool listenerFailed = false;
        var snapshotTime = DateTime.UtcNow;
        try { listeners = PassiveListenerTable.Read(); }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        { listeners = Array.Empty<ServiceListener>(); listenerFailed = true; }
        Process[] candidates;
        try { candidates = Process.GetProcessesByName(HarnessTarget.ProcessName); }
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
                    { identities.Add(new(id, HarnessTarget.ProcessName, null, ReadFailed: true, Exited: HasExited(process))); continue; }
                    identities.Add(new(id, HarnessTarget.ProcessName, path.ToString(), Exited: HasExited(process),
                        StartedAfterSnapshot: process.StartTime.ToUniversalTime() > snapshotTime));
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException)
                { identities.Add(new(id, HarnessTarget.ProcessName, null, ReadFailed: true, Exited: HasExited(process))); }
            }
        }
        return Result(false);
        PresenceProbe Result(bool failed) => HarnessMatcher.Match(_target, identities, listeners, verified, failed, listenerFailed);
    }
    private static bool HasExited(Process process)
    { try { return process.HasExited; } catch (Exception e) when (e is Win32Exception or InvalidOperationException) { return false; } }
    private static int GetSessionId() { using var process = Process.GetCurrentProcess(); return process.SessionId; }
}

// OS listener ownership only: no sockets, HTTP, authentication endpoints, command lines, or environment reads.
internal static class PassiveListenerTable
{
    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

    public static IReadOnlyList<ServiceListener> Read()
    {
        int size = 0;
        uint result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0); // AF_INET, OWNER_PID_LISTENER
        if (result != 122 && result != 0) throw new Win32Exception((int)result);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (size < 4 || size > 16 * 1024 * 1024) throw new InvalidOperationException("Invalid listener table size.");
            int allocated = size;
            var buffer = Marshal.AllocHGlobal(allocated);
            try
            {
                result = GetExtendedTcpTable(buffer, ref size, false, 2, 3, 0);
                if (result == 122) continue;
                if (result != 0) throw new Win32Exception((int)result);
                int count = Marshal.ReadInt32(buffer);
                const int rowSize = 24; // MIB_TCPROW_OWNER_PID (six DWORDs)
                if (count < 0 || count > (allocated - 4) / rowSize) throw new InvalidOperationException("Invalid listener table count.");
                var rows = new List<ServiceListener>();
                for (int i = 0; i < count; i++)
                {
                    var row = IntPtr.Add(buffer, 4 + i * rowSize);
                    int port = Marshal.ReadByte(row, 8) * 256 + Marshal.ReadByte(row, 9);
                    if (port != HarnessTarget.ServicePort || Marshal.ReadInt32(row) != 2) continue;
                    var address = new byte[4]; Marshal.Copy(IntPtr.Add(row, 4), address, 0, 4);
                    rows.Add(new(Marshal.ReadInt32(row, 20), new IPAddress(address).ToString(), port));
                }
                return rows;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new InvalidOperationException("Listener table changed repeatedly.");
    }
}
