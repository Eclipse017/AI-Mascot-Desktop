using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AIMascot.Platform;

// Own-process status only; no AI app content, global input, or network activity.
internal sealed class RuntimeStatus
{
    private readonly string _path;
    private readonly Queue<object> _interactions = new();
    private string? _lastPayload;
    private DateTime _lastWrite;
    public RuntimeStatus(string role, string? verificationDirectory)
    {
        _path = Path.Combine(verificationDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI-Mascot-Desktop", role), "runtime-status.json");
    }
    public void Interaction(string kind, double left, double top)
    {
        if (_interactions.Count >= 32) _interactions.Dequeue();
        // Commands such as greet can arrive before the hidden WPF window has ever been positioned.
        _interactions.Enqueue(new { Utc = DateTime.UtcNow, Kind = kind,
            Left = double.IsFinite(left) ? (double?)left : null, Top = double.IsFinite(top) ? (double?)top : null });
        _lastPayload = null;
    }
    public void Write(object state)
    {
        try
        {
            string payload = JsonSerializer.Serialize(state);
            var now = DateTime.UtcNow;
            if (payload == _lastPayload && now - _lastWrite < TimeSpan.FromSeconds(3)) return;
            var value = JsonSerializer.Serialize(new { Version = 1, ProcessId = Environment.ProcessId, Utc = now, State = state, Interactions = _interactions.ToArray() });
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + ".tmp", value); File.Move(_path + ".tmp", _path, true);
            _lastPayload = payload; _lastWrite = now;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
