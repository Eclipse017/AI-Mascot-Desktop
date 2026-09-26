using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;
using Microsoft.Win32;

namespace AIMascot.ConsoleApp;

public sealed class RoleController
{
    public static readonly string[] Commands = { "start", "restore", "hide", "auto", "demo", "pause", "greet", "pet", "rest", "controls", "quit", "enable-startup", "disable-startup" };
    private static readonly int SessionId = Process.GetCurrentProcess().SessionId;
    private readonly SemaphoreSlim _commands = new(1, 1);
    private delegate bool WindowVisitor(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowVisitor visitor, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);

    public RoleState Read(Role role)
    {
        var ids = new List<int>(); bool other = false; string? error = null;
        try
        {
            foreach (var process in Process.GetProcessesByName(role.ProcessName))
            {
                using (process)
                {
                    try
                    {
                        if (process.SessionId != SessionId || process.HasExited) continue;
                        if (string.Equals(process.MainModule?.FileName, role.Executable, StringComparison.OrdinalIgnoreCase)) ids.Add(process.Id);
                        else other = true;
                    }
                    catch (Exception e) when (e is Win32Exception or InvalidOperationException) { other = true; error = "部分进程身份暂时无法确认。"; }
                }
            }
            // Detect another copy even while it is still starting.
            if (ids.Count == 0 && Mutex.TryOpenExisting(role.EventName("Instance"), out var mutex))
            { mutex.Dispose(); other = true; }
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or UnauthorizedAccessException) { error = "暂时无法读取宿主状态。"; }
        SavedOptions? preferences = ReadOptions(role.PreferencesPath);
        bool? startup = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            var value = key?.GetValue(role.StartupName) as string;
            startup = value == null ? false : string.Equals(value, role.StartupCommand, StringComparison.OrdinalIgnoreCase) ? true : null;
        }
        catch (Exception e) when (e is IOException or SecurityException or UnauthorizedAccessException) { }
        bool? visible = ids.Count > 0 ? VisibleSprite(ids) : null;
        string? reason = ReadReason(Path.Combine(Path.GetDirectoryName(role.PreferencesPath)!, "runtime-status.json"), ids);
        return new(role.Id, File.Exists(role.Executable), ids.Count > 0, other, visible, startup, preferences, error, ids.ToArray(), reason);
    }

    public static string? ReadReason(string path, IReadOnlyCollection<int> processIds)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) return null;
            using var json = JsonDocument.Parse(File.ReadAllText(path)); var root = json.RootElement;
            if (root.GetProperty("Version").GetInt32() != 1 || !processIds.Contains(root.GetProperty("ProcessId").GetInt32())) return null;
            var reason = root.GetProperty("State").GetProperty("Reason").GetString();
            // A suspended process intentionally stops polling. Its matching PID is still required.
            if (reason != "Suspended" && DateTime.UtcNow - root.GetProperty("Utc").GetDateTime() > TimeSpan.FromSeconds(12)) return null;
            return reason;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return null; }
    }

    public static SavedOptions? ReadOptions(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 32768) return null;
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var root = json.RootElement;
            if (root.GetProperty("Version").GetInt32() is not (1 or 2 or 3)) return null;
            int mode = root.GetProperty("Mode").GetInt32();
            if (mode is not (0 or 1)) return null;
            return new(mode, root.GetProperty("UserHidden").GetBoolean(), root.GetProperty("Paused").GetBoolean());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return null; }
    }

    private static bool? VisibleSprite(IReadOnlyCollection<int> ids)
    {
        bool visible = false;
        // Only inspect our own mascot HWND styles. Never inspect AI app window text.
        bool read = EnumWindows((hwnd, _) => {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (ids.Contains((int)pid) && IsWindowVisible(hwnd))
            {
                long style = GetWindowLongPtr(hwnd, -20).ToInt64();
                if ((style & 0x80000) != 0 && (style & 0x8) != 0) visible = true; // layered, topmost
            }
            return true;
        }, 0);
        return read ? visible : null;
    }

    public async Task<CommandResult> ExecuteAsync(Role role, string command)
    {
        if (!Commands.Contains(command, StringComparer.Ordinal)) return new(role.Id, command, false, "不支持的操作。");
        await _commands.WaitAsync().ConfigureAwait(false);
        try
        {
            var before = Read(role);
            if (!before.Installed) return Result(false, "尚未安装：" + role.Executable);
            if (before.OtherCopy) return Result(false, "检测到其他位置的宿主或身份无法确认；请先关闭该版本。");
            if (before.Error != null) return Result(false, before.Error);
            if (command is "enable-startup" or "disable-startup")
            {
                using var process = Launch(role, command);
                if (process == null) return Result(false, "启动设置程序未启动。");
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
                bool expected = command == "enable-startup";
                bool startupAccepted = process.ExitCode == 0 && Read(role).StartupEnabled == expected;
                return Result(startupAccepted, startupAccepted ? "登录启动设置已核验。" : "登录启动设置未能确认。");
            }
            if (command == "quit" && !before.Running) return Result(true, "宿主已停止。");
            if (!before.Running)
            {
                // Start silently first, then issue exactly one command, so cold and warm paths agree.
                using var launched = Launch(role, "background");
                if (launched == null) return Result(false, "宿主未启动。");
                if (!await Until(() => Read(role).Running && SignalExists(role, "hide"), 8000).ConfigureAwait(false))
                    return Result(false, "启动尚未确认，请查看状态后重试。");
                // WPF registers its wait callbacks after creating the named events. AutoResetEvent retains one signal.
            }
            if (command == "start") return Result(true, "宿主已启动；保留原显示偏好。");
            if (!TrySignal(role, command)) return Result(false, "宿主控制接口不可用，请刷新后重试。");
            Func<bool>? accepted = command switch {
                "hide" => () => ReadOptions(role.PreferencesPath)?.UserHidden == true,
                "restore" => () => ReadOptions(role.PreferencesPath)?.UserHidden == false,
                "auto" => () => ReadOptions(role.PreferencesPath)?.Mode == 0,
                "demo" => () => ReadOptions(role.PreferencesPath)?.Mode == 1,
                "pause" when before.Options != null => () => ReadOptions(role.PreferencesPath)?.Paused == !before.Options.Paused,
                "quit" => () => !Read(role).Running,
                _ => null
            };
            if (accepted != null && !await Until(accepted, 5000).ConfigureAwait(false)) return Result(false, "命令已发送，但未确认状态变化；请检查角色状态。");
            return Result(true, accepted == null ? "已发送到角色宿主。" : command == "restore" ? "已取消手动隐藏；是否显示仍取决于跟随模式及全屏状态。" : "已确认设置或宿主状态。");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException or OperationCanceledException or SecurityException)
        { return Result(false, "操作未完成：" + e.Message); }
        finally { _commands.Release(); }
        CommandResult Result(bool ok, string message) => new(role.Id, command, ok, ok ? message : "未完成 · " + message);
    }

    private static Process? Launch(Role role, string command)
    {
        var info = new ProcessStartInfo(role.Executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(role.Executable)!, CreateNoWindow = true };
        info.ArgumentList.Add("--" + command);
        return Process.Start(info);
    }
    public static bool TrySignal(Role role, string command)
    {
        if (!Commands.Contains(command) || command is "start" or "enable-startup" or "disable-startup") return false;
        if (!EventWaitHandle.TryOpenExisting(role.EventName(command), out var signal)) return false;
        using (signal) return signal.Set();
    }
    private static bool SignalExists(Role role, string command)
    {
        if (!EventWaitHandle.TryOpenExisting(role.EventName(command), out var signal)) return false;
        signal.Dispose(); return true;
    }
    private static async Task<bool> Until(Func<bool> condition, int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        do { if (condition()) return true; await Task.Delay(120).ConfigureAwait(false); } while (watch.ElapsedMilliseconds < milliseconds);
        return false;
    }
}
