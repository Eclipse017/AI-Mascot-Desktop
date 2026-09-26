using System;
using System.Collections.Generic;
using System.IO;

namespace AIMascot.Core;

public enum PresenceKind { Unknown, Stopped, Running }
public enum DisplayMode { Auto, Demo }

public sealed record TargetBinding(string PackageFamily, string Executable)
{
    // Stable MSIX family observed on this machine; no version-specific install path.
    public static TargetBinding CodexDesktop { get; } =
        new("OpenAI.Codex_2p2nqsd0c76g0", "ChatGPT.exe");
    public string ProcessName => Path.GetFileNameWithoutExtension(Executable);
}

public sealed record ProcessIdentity(int ProcessId, string Name, string? PackageFamily,
    string? ExecutableName, bool ReadFailed = false, bool Exited = false);

public sealed record PresenceSample(PresenceKind Kind, int MatchedProcesses, int UnreadableProcesses, int Candidates);

public static class PresenceMatcher
{
    public static PresenceSample Match(TargetBinding target, IEnumerable<ProcessIdentity> processes, bool enumerationFailed = false)
    {
        var matched = new HashSet<int>();
        var unreadable = new HashSet<int>();
        var candidates = new HashSet<int>();
        foreach (var process in processes)
        {
            if (process.Exited || !string.Equals(process.Name, target.ProcessName, StringComparison.OrdinalIgnoreCase)) continue;
            candidates.Add(process.ProcessId);
            if (process.ReadFailed) { unreadable.Add(process.ProcessId); continue; }
            if (string.Equals(process.PackageFamily, target.PackageFamily, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(process.ExecutableName, target.Executable, StringComparison.OrdinalIgnoreCase))
                matched.Add(process.ProcessId);
        }
        var kind = matched.Count > 0 ? PresenceKind.Running
            : enumerationFailed || unreadable.Count > 0 ? PresenceKind.Unknown : PresenceKind.Stopped;
        return new(kind, matched.Count, unreadable.Count, candidates.Count);
    }
}

/// <summary>Two consecutive known samples confirm a transition; Unknown expires by elapsed time.</summary>
public sealed class PresenceTracker
{
    private readonly int _confirmations;
    private readonly TimeSpan _unknownGrace;
    private PresenceKind _candidate = PresenceKind.Unknown;
    private int _count;
    private TimeSpan? _unknownSince;
    public PresenceKind Stable { get; private set; } = PresenceKind.Unknown;
    public PresenceKind Latest { get; private set; } = PresenceKind.Unknown;

    public PresenceTracker(int confirmations = 2, TimeSpan? unknownGrace = null)
    {
        if (confirmations < 1) throw new ArgumentOutOfRangeException(nameof(confirmations));
        _confirmations = confirmations;
        _unknownGrace = unknownGrace ?? TimeSpan.FromSeconds(5);
        if (_unknownGrace < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(unknownGrace));
    }

    public PresenceKind Observe(PresenceKind state, TimeSpan now)
    {
        Latest = state;
        if (state == PresenceKind.Unknown)
        {
            _candidate = PresenceKind.Unknown; _count = 0;
            _unknownSince ??= now;
            if (now - _unknownSince.Value >= _unknownGrace) Stable = PresenceKind.Unknown;
            return Stable;
        }
        _unknownSince = null;
        _count = state == _candidate ? Math.Min(_count + 1, _confirmations) : 1;
        _candidate = state;
        if (_count >= _confirmations) Stable = state;
        return Stable;
    }
    public void Reset()
    {
        Stable = Latest = _candidate = PresenceKind.Unknown;
        _count = 0; _unknownSince = null;
    }
}

public static class DisplayPolicy
{
    public static bool ShouldShow(DisplayMode mode, bool userHidden, PresenceKind presence,
        bool fullscreen = false, bool suspended = false) =>
        !userHidden && !fullscreen && !suspended && (mode == DisplayMode.Demo || presence == PresenceKind.Running);
}
